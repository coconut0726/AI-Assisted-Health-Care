"""Care-plan server: stands in for the cloud between the headset and the mobile app.

Stores every version of each patient's plan (action_requirements_v1 JSON, stored as is) in SQLite.
See docs/SCHEMA_mobile.md for the API and the save rules.

    GET  /health
    GET  /api/patients/<id>/plan            latest PlanVersion
    PUT  /api/patients/<id>/plan            {base_version, plan} -> new PlanVersion, 409 if stale, 422 if invalid
    GET  /api/patients/<id>/plan/procedure  latest plan only (what the headset loads)

Uses only the Python standard library. New "Watch out" items get error_type / violation_condition from
Gemini when GEMINI_API_KEY is set; otherwise a plain fallback is used and the plan still saves.
"""
import argparse
import copy
import json
import os
import re
import sqlite3
import threading
import urllib.error
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_DB = HERE / "care_plan_data" / "care_plan.sqlite3"
SEED_JSON = HERE.parent / "MobileApp" / "src" / "data" / "burn_action_requirements.json"
GEMINI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/interactions"
DEFAULT_MODEL = "gemini-3.8-flash"
MAX_REQUEST_BYTES = 2 * 1024 * 1024
SCHEMA_VERSION = "action_requirements_v1"
ERROR_TYPES = ("wrong_object", "spatial_error", "technique_error", "measurement_error", "timing_parameter_error")
SOURCES = ("explicit_instruction", "graph_dependency", "expert_prior", "inferred", "author_edit")
PATIENT_ID = re.compile(r"^[A-Za-z0-9_-]{1,64}$")


class PlanError(ValueError):
    """The plan would be rejected by the headset (ActionProcedureJson.Validate)."""


# ---------------------------------------------------------------------------
# Validation: a port of ActionProcedureJson.Parse / Validate (branch feature/periodical-sampling-with-localVLM).
# Missing arrays default to empty like the C# field initializers; an explicit null is an error, as in C#.

_MISSING = object()


def _arr(obj, key, where):
    value = obj.get(key, _MISSING)
    if value is _MISSING:
        return []
    if not isinstance(value, list):
        raise PlanError(f"{where}: {key} must be an array.")
    return value


def _need(text, name):
    if not isinstance(text, str) or not text.strip():
        raise PlanError("Missing " + name)


def _quote(quote, source):
    _need(quote, "source quotation")
    if quote not in source:
        raise PlanError("Not an exact source quotation: " + quote)


def claim_key(text):
    return "".join(c.lower() for c in (text or "") if c.isalnum())


def _refs(refs, valid, self_id):
    seen = set()
    for ref in refs:
        if not isinstance(ref, str) or ref == self_id or ref not in valid or ref in seen:
            raise PlanError(f"Invalid reference: {ref}")
        seen.add(ref)


def _provenance(source, reference, review, instruction):
    _need(reference, "source_reference")
    if source == "explicit_instruction":
        _quote(reference, instruction)
    elif source not in SOURCES:
        raise PlanError(f"Invalid requirement source: {source}")
    if source == "inferred" and not review:
        raise PlanError("Inferred requirements need expert review.")


def _check_id(item_id, ids):
    _need(item_id, "requirement/criterion id")
    if item_id in ids:
        raise PlanError("Duplicate predicate ID: " + item_id)
    ids.add(item_id)


def _check_error(e, plan, ids, is_global, action_ids):
    if not isinstance(e, dict):
        raise PlanError("Null error criterion.")
    _check_id(e.get("criterion_id"), ids)
    _need(e.get("violation_condition"), "violation_condition")
    _need(e.get("compliance_condition"), "compliance_condition")
    if e.get("error_type") not in ERROR_TYPES:
        raise PlanError(f"Unsupported error_type: {e.get('error_type')}")
    _provenance(e.get("source"), e.get("source_reference"), bool(e.get("requires_expert_review")), plan["instruction"])
    relevant = _arr(e, "relevant_action_ids", e.get("criterion_id"))
    _arr(e, "related_requirement_ids", e.get("criterion_id"))
    if is_global and not relevant:
        raise PlanError("A global rule must identify relevant actions.")
    _refs(relevant, action_ids, None)


def _forbidden_keys(node):
    if isinstance(node, dict):
        for key, value in node.items():
            if key.lower().startswith("evidence") or key == "execution_constraints":
                raise PlanError("This action-flow format does not contain " + key + ".")
            _forbidden_keys(value)
    elif isinstance(node, list):
        for value in node:
            _forbidden_keys(value)


def validate_plan(plan):
    """Raises PlanError with the headset's reason when the plan would not load on the headset."""
    if not isinstance(plan, dict) or plan.get("schema_version") != SCHEMA_VERSION:
        raise PlanError("Expected " + SCHEMA_VERSION + ".")
    _forbidden_keys(plan)
    _need(plan.get("procedure_title"), "procedure_title")
    _need(plan.get("instruction"), "instruction")
    instruction = plan["instruction"]
    steps = _arr(plan, "steps", "plan")
    if not steps or any(not isinstance(s, dict) or not _arr(s, "actions", s.get("step_id")) for s in steps):
        raise PlanError("A procedure must have steps containing actions.")
    step_ids = {}
    for s in steps:
        _need(s.get("step_id"), "step id")
        if s["step_id"] in step_ids:
            raise PlanError("Duplicate step ID: " + s["step_id"])
        step_ids[s["step_id"]] = s
    actions = [a for s in steps for a in s["actions"]]
    if any(not isinstance(a, dict) for a in actions):
        raise PlanError("Null action.")
    action_ids = set()
    for a in actions:
        _need(a.get("action_id"), "action id")
        if a["action_id"] in action_ids:
            raise PlanError("Duplicate action ID: " + a["action_id"])
        action_ids.add(a["action_id"])

    predicates, requirement_ids = set(), set()
    for s in steps:
        _need(s.get("step_name"), "step_name")
        _refs(_arr(s, "dependencies", s["step_id"]), step_ids, s["step_id"])
        for a in s["actions"]:
            aid = a["action_id"]
            _need(a.get("instruction"), aid + " instruction")
            _quote(a.get("original_text"), instruction)
            _refs(_arr(a, "dependencies", aid), action_ids, aid)
            for edge in _arr(a, "conditional_dependencies", aid):
                if not isinstance(edge, dict):
                    raise PlanError("Null conditional dependency.")
                _refs([edge.get("action_id")], action_ids, aid)
                _need(edge.get("condition"), "conditional dependency condition")
            for key in ("actor", "tools", "materials", "contact_media", "targets"):
                _arr(a, key, aid)
            for text in _arr(a, "associated_constraints", aid):
                _quote(text, instruction)
            pre, post = _arr(a, "preconditions", aid), _arr(a, "postconditions", aid)
            manual = bool(a.get("manual_completion_required"))
            if not post and not manual:
                raise PlanError(aid + " needs an image postcondition or explicit manual completion.")
            if manual and post:
                raise PlanError(aid + ": manual completion is reserved for Actions without image postconditions.")
            for q in pre + post:
                if not isinstance(q, dict):
                    raise PlanError("Null requirement.")
                _check_id(q.get("requirement_id"), predicates)
                requirement_ids.add(q["requirement_id"])
                _need(q.get("condition"), "condition")
                _provenance(q.get("source"), q.get("source_reference"), bool(q.get("requires_expert_review")), instruction)
            for e in _arr(a, "error_criteria", aid):
                _check_error(e, plan, predicates, False, action_ids)

    if plan.get("procedure_error_criteria", _MISSING) is None:
        raise PlanError("Null procedure_error_criteria.")
    rules = _arr(plan, "procedure_error_criteria", "plan")
    for e in rules:
        _check_error(e, plan, predicates, True, action_ids)
    for e in [e for a in actions for e in _arr(a, "error_criteria", a["action_id"])] + rules:
        _refs(_arr(e, "related_requirement_ids", e["criterion_id"]), requirement_ids, None)

    local = set()
    for a in actions:
        local.update(claim_key(q["condition"]) for q in _arr(a, "preconditions", "") + _arr(a, "postconditions", ""))
        local.update(claim_key(e["compliance_condition"]) for e in _arr(a, "error_criteria", ""))
    for e in rules:
        if claim_key(e["compliance_condition"]) in local:
            raise PlanError("A shared rule duplicates an existing local condition; keep the local check and omit this global copy: "
                            + e["criterion_id"])
    for a in actions:
        claims = {claim_key(q["condition"]) for q in _arr(a, "preconditions", "") + _arr(a, "postconditions", "")}
        if any(claim_key(e["compliance_condition"]) in claims for e in _arr(a, "error_criteria", "")):
            raise PlanError(a["action_id"] + ": an error criterion duplicates a requirement.")

    edges = {}
    for s in steps:
        step_dep_actions = [x["action_id"] for d in _arr(s, "dependencies", "") for x in step_ids[d]["actions"]]
        for a in s["actions"]:
            edges[a["action_id"]] = set(_arr(a, "dependencies", "")) | {d["action_id"] for d in _arr(a, "conditional_dependencies", "")} \
                | set(step_dep_actions)
    visiting, done = set(), set()

    def visit(node):
        if node in done:
            return
        if node in visiting:
            raise PlanError("Dependency cycle at " + node)
        visiting.add(node)
        for dep in edges[node]:
            visit(dep)
        visiting.discard(node)
        done.add(node)

    for node in sorted(action_ids):
        visit(node)


# ---------------------------------------------------------------------------
# Filling the fields the nurse never edits (docs/SCHEMA_mobile.md, "Decisions").

def gemini_fill(items, api_key, model):
    """Asks Gemini for error_type and violation_condition. items: [{key, compliance_condition, action}]."""
    schema = {"type": "object", "properties": {"items": {"type": "array", "items": {
        "type": "object", "properties": {
            "key": {"type": "string"}, "error_type": {"type": "string", "enum": list(ERROR_TYPES)},
            "violation_condition": {"type": "string"}},
        "required": ["key", "error_type", "violation_condition"], "additionalProperties": False}}},
        "required": ["items"], "additionalProperties": False}
    prompt = ("A nurse wrote safety rules for a home-care procedure. For each item, classify the mistake it guards against "
              "as one error_type and write violation_condition: one short sentence describing what a camera image would "
              "show when the rule is broken. Keep the same key.")
    body = json.dumps({
        "model": model, "store": False, "system_instruction": prompt,
        "input": [{"type": "text", "text": json.dumps({"items": items}, ensure_ascii=False)}],
        "response_format": {"type": "text", "mime_type": "application/json", "schema": schema},
    }).encode()
    request = urllib.request.Request(GEMINI_ENDPOINT, data=body, method="POST",
                                     headers={"Content-Type": "application/json", "x-goog-api-key": api_key})
    with urllib.request.urlopen(request, timeout=60) as response:
        interaction = json.loads(response.read())
    if interaction.get("status") != "completed":
        raise ValueError("The model interaction did not complete.")
    for step in reversed(interaction.get("steps") or []):
        if step.get("type") == "model_output":
            text = "".join(p.get("text", "") for p in step.get("content") or [] if p.get("type") == "text")
            if text.strip():
                return {x["key"]: x for x in json.loads(text)["items"]}
    raise ValueError("The model returned no output.")


def fallback_fill(item):
    return {"error_type": "technique_error", "violation_condition": "This is not followed: " + item["compliance_condition"]}


def item_id_for(name):
    return re.sub(r"^_|_$", "", re.sub(r"[^a-z0-9]+", "_", name.lower()))


def prepare_plan(plan, previous, fill):
    """Completes a plan the phone sent so it passes validate_plan. Returns (plan, notes)."""
    plan = copy.deepcopy(plan)
    notes = []
    actions = [a for s in plan.get("steps") or [] for a in s.get("actions") or []]
    action_ids = [a.get("action_id") for a in actions]

    # Ids must be unique across requirements and criteria. Keep the first, renumber the rest.
    taken = set()

    def unique(item, key, prefix):
        value = item.get(key)
        if not value or value in taken:
            n = 1
            while f"{prefix}{n}" in taken or any(_has_id(plan, f"{prefix}{n}", key)):
                n += 1
            item[key] = f"{prefix}{n}"
        taken.add(item[key])

    for a in actions:
        for q in (a.get("preconditions") or []) + (a.get("postconditions") or []):
            unique(q, "requirement_id", a["action_id"] + "_R")
        for e in a.get("error_criteria") or []:
            unique(e, "criterion_id", a["action_id"] + "_E")
    for e in plan.get("procedure_error_criteria") or []:
        unique(e, "criterion_id", "G")

    # Provenance: a nurse-approved inferred item becomes an author edit (the headset rejects unreviewed "inferred").
    for item in _all_items(plan):
        if item.get("source") == "inferred" and not item.get("requires_expert_review"):
            item["source"] = "author_edit"
        if item.get("source") == "author_edit" and not (item.get("source_reference") or "").strip():
            item["source_reference"] = item.get("condition") or item.get("compliance_condition") or "nurse edit"

    # AI fields on Watch-out items and shared rules.
    pending = []
    for a in actions:
        for e in a.get("error_criteria") or []:
            if e.get("error_type") not in ERROR_TYPES or not (e.get("violation_condition") or "").strip():
                pending.append((e, a.get("instruction", "")))
    for e in plan.get("procedure_error_criteria") or []:
        if e.get("error_type") not in ERROR_TYPES or not (e.get("violation_condition") or "").strip():
            pending.append((e, "whole session"))
        if not e.get("relevant_action_ids"):
            e["relevant_action_ids"] = [x for x in action_ids if x]
    if pending:
        items = [{"key": e["criterion_id"], "compliance_condition": e.get("compliance_condition", ""), "action": where}
                 for e, where in pending]
        answers = {}
        try:
            answers = fill(items) or {}
        except Exception as exc:  # noqa: BLE001 - any model failure falls back; the save must still work
            notes.append("AI fill failed, used fallback: " + type(exc).__name__)
        for (e, _), item in zip(pending, items):
            answer = answers.get(item["key"])
            if not answer or answer.get("error_type") not in ERROR_TYPES or not (answer.get("violation_condition") or "").strip():
                answer = fallback_fill(item)
            e["error_type"] = answer["error_type"]
            e["violation_condition"] = answer["violation_condition"].strip()
            e.setdefault("related_requirement_ids", [])
            e.setdefault("applicability_condition", "")

    # Supplies: the nurse edits displayName only.
    for supply in plan.get("supplies") or []:
        name = (supply.get("displayName") or "").strip()
        supply["displayName"] = name
        supply["itemId"] = supply.get("itemId") or item_id_for(name)
        if not supply.get("keywords"):
            supply["keywords"] = [name.lower()]

    # "Call the nurse if": keep the flag the phone sent; otherwise carry it over or find the action that says so.
    if not any(a.get("notify_nurse") for a in actions):
        before = {a.get("action_id") for s in (previous or {}).get("steps") or [] for a in s.get("actions") or [] if a.get("notify_nurse")}
        match = [a for a in actions if a.get("action_id") in before] or \
                [a for a in actions if re.search(r"\bcall\b.*\bnurse\b", a.get("instruction", ""), re.I)]
        if match:
            match[0]["notify_nurse"] = True
    return plan, notes


def _all_items(plan):
    for s in plan.get("steps") or []:
        for a in s.get("actions") or []:
            yield from (a.get("preconditions") or [])
            yield from (a.get("postconditions") or [])
            yield from (a.get("error_criteria") or [])
    yield from (plan.get("procedure_error_criteria") or [])


def _has_id(plan, value, key):
    for item in _all_items(plan):
        yield item.get(key) == value


def changed_step_ids(before, after):
    old = {s.get("step_id"): s for s in (before or {}).get("steps") or []}
    return [s.get("step_id") for s in after.get("steps") or [] if old.get(s.get("step_id")) != s]


# ---------------------------------------------------------------------------
# Demo seed: mirrors demoPlan() in MobileApp/src/data/demo.ts.

def demo_plan():
    plan = json.loads(SEED_JSON.read_text(encoding="utf-8"))
    plan["procedure_title"] = "Burn Dressing Change"
    plan["supplies"] = [{"itemId": item_id_for(n), "displayName": n, "keywords": [n.lower()]}
                        for n in ["Mild soap", "Burn cream", "Q-tips", "Non-stick pad", "Gauze roll", "Tape"]]
    last = plan["steps"][-1]
    last["step_name"] = "If the Burn Gets Worse"
    last["actions"][0]["notify_nurse"] = True
    a4 = plan["steps"][1]["actions"][1]
    a4["preconditions"] = [{"requirement_id": "A4_PRE1", "condition": "Gloves are on.", "source": "graph_dependency",
                            "source_reference": "A2", "requires_expert_review": False}]
    a4["error_criteria"] = [{
        "criterion_id": "A4_E1", "error_type": "technique_error",
        "violation_condition": "The dressing is being ripped off quickly.",
        "compliance_condition": "Peel it off slowly — don’t rip it.", "applicability_condition": "",
        "related_requirement_ids": [], "source": "inferred", "source_reference": "Gently remove the old dressing.",
        "requires_expert_review": True}]

    def rule(cid, etype, violation, compliance, actions, source, reference):
        return {"criterion_id": cid, "error_type": etype, "violation_condition": violation, "compliance_condition": compliance,
                "applicability_condition": "", "related_requirement_ids": [], "relevant_action_ids": actions,
                "source": source, "source_reference": reference, "requires_expert_review": False}

    plan["procedure_error_criteria"] = [
        rule("G1", "wrong_object", "The pad or cotton swab touches a surface that is not clean.",
             "Clean pad and Q-tip never touch non-clean surfaces.", ["A7", "A8"], "expert_prior", "Burn unit dressing protocol"),
        rule("G2", "technique_error", "A blister is broken.", "Blisters stay intact — none are popped.",
             ["A5", "A6", "A7"], "explicit_instruction", "do not break any blisters"),
        rule("G3", "technique_error", "Bare hands touch the burn or the new dressing.",
             "Gloves stay on from removing the old dressing until the gauze is taped.",
             ["A4", "A5", "A6", "A7", "A8", "A9", "A10"], "expert_prior", "Burn unit dressing protocol"),
    ]
    return plan


# ---------------------------------------------------------------------------
# Storage

def now_iso():
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


class PlanStore:
    def __init__(self, path):
        self.path = str(path)
        if self.path != ":memory:":
            Path(self.path).parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(self.path, check_same_thread=False)
        self.lock = threading.Lock()
        self.db.execute("""CREATE TABLE IF NOT EXISTS plan_versions (
            patient_id TEXT NOT NULL, version INTEGER NOT NULL, updated_at TEXT NOT NULL, updated_by TEXT NOT NULL,
            plan_json TEXT NOT NULL, changed_step_ids TEXT NOT NULL, PRIMARY KEY (patient_id, version))""")
        self.db.commit()

    def latest(self, patient_id):
        with self.lock:
            return self._latest(patient_id)

    def _latest(self, patient_id):
        row = self.db.execute("SELECT patient_id, version, updated_at, updated_by, plan_json, changed_step_ids "
                              "FROM plan_versions WHERE patient_id = ? ORDER BY version DESC LIMIT 1", (patient_id,)).fetchone()
        if not row:
            return None
        return {"patient_id": row[0], "version": row[1], "updated_at": row[2], "updated_by": row[3],
                "plan": json.loads(row[4]), "changed_step_ids": json.loads(row[5])}

    def insert(self, patient_id, version, updated_by, plan, changed):
        record = {"patient_id": patient_id, "version": version, "updated_at": now_iso(), "updated_by": updated_by,
                  "plan": plan, "changed_step_ids": changed}
        self.db.execute("INSERT INTO plan_versions VALUES (?, ?, ?, ?, ?, ?)",
                        (patient_id, version, record["updated_at"], updated_by, json.dumps(plan, ensure_ascii=False), json.dumps(changed)))
        self.db.commit()
        return record

    def save(self, patient_id, base_version, plan, fill):
        """Returns (status, payload). status: 200, 404, 409 or 422."""
        with self.lock:
            current = self._latest(patient_id)
            if current is None:
                return 404, {"error": "Unknown patient: " + patient_id}
            if base_version != current["version"]:
                return 409, {"error": "The plan was changed somewhere else.", "latest_version": current["version"]}
            prepared, notes = prepare_plan(plan, current["plan"], fill)
            try:
                validate_plan(prepared)
            except PlanError as exc:
                return 422, {"error": str(exc)}
            record = self.insert(patient_id, current["version"] + 1, "nurse", prepared,
                                 changed_step_ids(current["plan"], prepared))
        if notes:
            print(f"{patient_id} v{record['version']}: " + "; ".join(notes), flush=True)
        return 200, record

    def seed(self, patient_id, plan, reset=False):
        validate_plan(plan)
        with self.lock:
            if reset:
                self.db.execute("DELETE FROM plan_versions WHERE patient_id = ?", (patient_id,))
                self.db.commit()
            exists = self.db.execute("SELECT 1 FROM plan_versions WHERE patient_id = ?", (patient_id,)).fetchone()
            if not exists:
                self.insert(patient_id, 1, "ai", plan, [])


# ---------------------------------------------------------------------------
# HTTP

def create_server(host, port, store, fill):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, format, *args):  # keep plans out of the console
            pass

        def reply(self, status, payload):
            data = json.dumps(payload, ensure_ascii=False).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.cors()
            self.end_headers()
            self.wfile.write(data)

        def cors(self):
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Methods", "GET, PUT, OPTIONS")
            self.send_header("Access-Control-Allow-Headers", "Content-Type")

        def route(self):
            parts = [p for p in self.path.split("?")[0].split("/") if p]
            if len(parts) >= 4 and parts[0] == "api" and parts[1] == "patients" and parts[3] == "plan" and PATIENT_ID.match(parts[2]):
                return parts[2], parts[4:]
            return None, None

        def do_OPTIONS(self):
            self.send_response(204)
            self.cors()
            self.end_headers()

        def do_GET(self):
            if self.path.split("?")[0] == "/health":
                return self.reply(200, {"ok": True})
            patient_id, rest = self.route()
            if patient_id is None or rest not in ([], ["procedure"]):
                return self.reply(404, {"error": "Not found"})
            record = store.latest(patient_id)
            if record is None:
                return self.reply(404, {"error": "Unknown patient: " + patient_id})
            self.reply(200, record["plan"] if rest else record)

        def do_PUT(self):
            patient_id, rest = self.route()
            if patient_id is None or rest:
                return self.reply(404, {"error": "Not found"})
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                length = -1
            if length <= 0 or length > MAX_REQUEST_BYTES:
                return self.reply(413 if length > MAX_REQUEST_BYTES else 400, {"error": "Bad request size"})
            try:
                body = json.loads(self.rfile.read(length))
                base_version, plan = body["base_version"], body["plan"]
                if not isinstance(base_version, int) or not isinstance(plan, dict):
                    raise ValueError
            except (ValueError, KeyError, TypeError):
                return self.reply(400, {"error": "Expected {base_version, plan}"})
            status, payload = store.save(patient_id, base_version, plan, fill)
            self.reply(status, payload)

    return ThreadingHTTPServer((host, port), Handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="0.0.0.0", help="0.0.0.0 lets the iPhone reach it on the LAN")
    parser.add_argument("--port", type=int, default=8770)
    parser.add_argument("--db", default=str(DEFAULT_DB))
    parser.add_argument("--reset-demo", action="store_true", help="delete all versions of 'james' and seed the burn demo again")
    args = parser.parse_args()

    store = PlanStore(args.db)
    store.seed("james", demo_plan(), reset=args.reset_demo)
    api_key = os.environ.get("GEMINI_API_KEY", "").strip()
    model = os.environ.get("CARE_PLAN_MODEL", DEFAULT_MODEL).strip()
    fill = (lambda items: gemini_fill(items, api_key, model)) if api_key else (lambda items: {})
    server = create_server(args.host, args.port, store, fill)
    print(f"Care-plan server: http://{args.host}:{args.port}/api/patients/james/plan", flush=True)
    print("AI fill for Watch-out items: " + (model if api_key else "off (GEMINI_API_KEY not set, plain fallback)"), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
