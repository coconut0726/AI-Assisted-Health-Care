import copy
import http.client
import json
import threading
import unittest

from care_plan_server import PlanError, PlanStore, create_server, demo_plan, prepare_plan, validate_plan


def find_action(plan, action_id):
    return next(a for s in plan["steps"] for a in s["actions"] if a["action_id"] == action_id)


class ValidationTests(unittest.TestCase):
    def test_demo_plan_is_valid(self):
        validate_plan(demo_plan())

    def test_rules_match_the_headset(self):
        cases = {
            "manual with postconditions": lambda p: find_action(p, "A2").update(manual_completion_required=True),
            "no postconditions, not manual": lambda p: find_action(p, "A4").update(postconditions=[]),
            "unreviewed inferred": lambda p: find_action(p, "A4")["error_criteria"][0].update(requires_expert_review=False),
            "bad error type": lambda p: find_action(p, "A4")["error_criteria"][0].update(error_type="order"),
            "duplicate id": lambda p: find_action(p, "A4")["preconditions"][0].update(requirement_id="A4_E1"),
            "shared rule repeats a local check": lambda p: p["procedure_error_criteria"][0].update(compliance_condition="Gloves are on."),
            "edited original_text": lambda p: find_action(p, "A4").update(original_text="Take it off"),
            "explicit source not quoted": lambda p: p["procedure_error_criteria"][1].update(source_reference="never pop blisters"),
            "dependency cycle": lambda p: find_action(p, "A1").update(dependencies=["A2"]),  # A2 already depends on A1
            "global rule without actions": lambda p: p["procedure_error_criteria"][0].update(relevant_action_ids=[]),
            "evidence field": lambda p: find_action(p, "A4").update(evidence_scope="after_step"),
            "null array": lambda p: find_action(p, "A4").update(tools=None),
        }
        for name, change in cases.items():
            with self.subTest(name):
                plan = demo_plan()
                change(plan)
                with self.assertRaises(PlanError):
                    validate_plan(plan)


class PrepareTests(unittest.TestCase):
    def test_nurse_watch_out_gets_ai_fields(self):
        plan = demo_plan()
        find_action(plan, "A9")["error_criteria"].append({
            "criterion_id": "A9_E1", "error_type": "", "violation_condition": "", "compliance_condition": "Wrap it loosely.",
            "applicability_condition": "", "related_requirement_ids": [], "source": "author_edit",
            "source_reference": "Wrap it loosely.", "requires_expert_review": False})
        seen = []

        def fill(items):
            seen.extend(items)
            return {"A9_E1": {"key": "A9_E1", "error_type": "measurement_error", "violation_condition": "The gauze is tight."}}

        out, notes = prepare_plan(plan, demo_plan(), fill)
        e = find_action(out, "A9")["error_criteria"][0]
        self.assertEqual((e["error_type"], e["violation_condition"]), ("measurement_error", "The gauze is tight."))
        self.assertEqual([i["key"] for i in seen], ["A9_E1"])
        self.assertEqual(notes, [])
        validate_plan(out)

    def test_fallback_when_ai_fails(self):
        plan = demo_plan()
        plan["procedure_error_criteria"].append({"criterion_id": "", "error_type": "", "violation_condition": "",
                                                 "compliance_condition": "Keep pets out of the room.", "source": "author_edit",
                                                 "source_reference": "Keep pets out of the room.", "requires_expert_review": False})

        def fill(items):
            raise TimeoutError

        out, notes = prepare_plan(plan, demo_plan(), fill)
        rule = out["procedure_error_criteria"][-1]
        self.assertEqual(rule["criterion_id"], "G4")
        self.assertIn(rule["error_type"], ("technique_error",))
        self.assertTrue(rule["violation_condition"] and rule["relevant_action_ids"])
        self.assertTrue(notes)
        validate_plan(out)

    def test_supplies_flag_and_provenance(self):
        plan = demo_plan()
        plan["supplies"].append({"itemId": "", "displayName": "Clean towel ", "keywords": []})
        for a in (a for s in plan["steps"] for a in s["actions"]):
            a.pop("notify_nurse", None)
        find_action(plan, "A4")["error_criteria"][0]["requires_expert_review"] = False  # nurse approved it
        out, _ = prepare_plan(plan, demo_plan(), lambda items: {})
        self.assertEqual(out["supplies"][-1], {"itemId": "clean_towel", "displayName": "Clean towel", "keywords": ["clean towel"]})
        self.assertTrue(find_action(out, "A11").get("notify_nurse"))
        self.assertEqual(find_action(out, "A4")["error_criteria"][0]["source"], "author_edit")
        validate_plan(out)


class ServerTests(unittest.TestCase):
    def setUp(self):
        self.store = PlanStore(":memory:")
        self.store.seed("james", demo_plan())
        self.server = create_server("127.0.0.1", 0, self.store, lambda items: {})
        self.thread = threading.Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(1)
        self.store.db.close()

    def call(self, method, path, body=None):
        conn = http.client.HTTPConnection("127.0.0.1", self.server.server_address[1], timeout=5)
        data = None if body is None else json.dumps(body).encode()
        conn.request(method, path, body=data, headers={"Content-Type": "application/json"} if data else {})
        res = conn.getresponse()
        raw = res.read()
        headers = dict(res.getheaders())
        conn.close()
        return res.status, (json.loads(raw) if raw else None), headers

    def test_get_save_and_conflict(self):
        status, v1, headers = self.call("GET", "/api/patients/james/plan")
        self.assertEqual((status, v1["version"], v1["updated_by"]), (200, 1, "ai"))
        self.assertEqual(headers.get("Access-Control-Allow-Origin"), "*")

        plan = copy.deepcopy(v1["plan"])
        find_action(plan, "A4")["instruction"] = "Slowly remove the old dressing."
        status, v2, _ = self.call("PUT", "/api/patients/james/plan", {"base_version": 1, "plan": plan})
        self.assertEqual((status, v2["version"], v2["updated_by"], v2["changed_step_ids"]), (200, 2, "nurse", ["S2"]))

        status, again, _ = self.call("PUT", "/api/patients/james/plan", {"base_version": 1, "plan": plan})
        self.assertEqual((status, again["latest_version"]), (409, 2))

        status, procedure, _ = self.call("GET", "/api/patients/james/plan/procedure")
        self.assertEqual((status, find_action(procedure, "A4")["instruction"]), (200, "Slowly remove the old dressing."))

    def test_invalid_plan_is_rejected_and_not_stored(self):
        _, v1, _ = self.call("GET", "/api/patients/james/plan")
        plan = v1["plan"]
        find_action(plan, "A2")["manual_completion_required"] = True
        status, body, _ = self.call("PUT", "/api/patients/james/plan", {"base_version": 1, "plan": plan})
        self.assertEqual(status, 422)
        self.assertIn("manual completion", body["error"])
        self.assertEqual(self.store.latest("james")["version"], 1)

    def test_bad_requests(self):
        self.assertEqual(self.call("GET", "/api/patients/nobody/plan")[0], 404)
        self.assertEqual(self.call("GET", "/api/patients/../plan")[0], 404)
        self.assertEqual(self.call("PUT", "/api/patients/james/plan", {"plan": {}})[0], 400)
        self.assertEqual(self.call("OPTIONS", "/api/patients/james/plan")[0], 204)
        self.assertEqual(self.call("GET", "/health")[1], {"ok": True})


if __name__ == "__main__":
    unittest.main()
