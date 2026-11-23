"""Serve Unity's existing Interactions protocol with local Ollama (default) or explicit Gemini.

AHC > VLM Backend selects the provider and models. Start-LocalVlm.cmd
uses those saved settings; --provider optionally overrides the initial selection.
Gemini requires an explicitly selected route and a session API key.
USB: adb reverse tcp:8765 tcp:8765. Editor + Quest Link uses localhost directly.
Uses only the Python standard library. No images, prompts, keys or model answers are logged.
"""
import argparse
import base64
import hmac
import json
import os
from pathlib import Path
import secrets
import sys
import struct
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import zlib

ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/interactions"
MAX_REQUEST_BYTES = 8 * 1024 * 1024
CONFIG_PATH = Path(__file__).with_name("local-vlm.json")


class ProviderResponseError(Exception):
    """The provider did not produce a complete structured answer."""


def load_local_config(path=CONFIG_PATH):
    with open(path, encoding="utf-8-sig") as source:
        config = json.load(source)
    if not isinstance(config, dict) or not isinstance(config.get("model"), str) or not config["model"].strip():
        raise ValueError("Set model in Tools/local-vlm.json to the exact name shown by ollama list")
    if config["model"].endswith("-cloud"):
        raise ValueError("Use a downloaded local model, not a -cloud model")
    return config


def check_local_inference(config):
    """Startup readiness check: real vision + structured output, entirely local."""
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    width = height = 224
    pixels = (b'\x00' + b'\x80\x80\x80' * width) * height
    png = (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) +
           chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b''))
    payload = {"model": config["model"], "system_instruction": "Describe only what is visible. Return JSON.",
               "input": [{"type": "text", "text": "Describe this image in a few words."},
                         {"type": "image", "data": base64.b64encode(png).decode('ascii')}],
               "response_format": {"schema": {"type": "object", "properties": {"description": {"type": "string"}},
                                              "required": ["description"], "additionalProperties": False}}}
    started = time.monotonic()
    check_config = dict(config, num_predict=128)
    result = json.loads(make_ollama_forward(check_config)(json.dumps(payload).encode('utf-8'), ''))
    answer = json.loads(result['steps'][0]['content'][0]['text'])
    if not isinstance(answer.get('description'), str) or not answer['description'].strip():
        raise ProviderResponseError('Vision startup check returned no description')
    print(f"Local image inference passed in {time.monotonic() - started:.1f}s.", flush=True)


def ollama_request(payload, config):
    """Preserve text/image order as separate user messages, including evidence scope labels."""
    messages = [{"role": "system", "content": payload["system_instruction"]}]
    for part in payload["input"]:
        if not isinstance(part, dict):
            raise ValueError("Invalid input part")
        if part.get("type") == "text" and isinstance(part.get("text"), str):
            messages.append({"role": "user", "content": part["text"]})
        elif part.get("type") == "image" and isinstance(part.get("data"), str) and part["data"]:
            # Keep the preceding filename/scope text adjacent to its image.
            if len(messages) == 1 or "images" in messages[-1]:
                messages.append({"role": "user", "content": ""})
            messages[-1]["images"] = [part["data"]]
        else:
            raise ValueError("Local Ollama supports text and images here; video must not be silently dropped")
    schema = payload["response_format"].get("schema")
    if not isinstance(schema, dict):
        raise ValueError("A JSON schema is required")
    options = {"temperature": 0, "num_ctx": config.get("num_ctx", 8192),
               "num_predict": config.get("num_predict", 4096)}
    if "num_gpu" in config:
        options["num_gpu"] = config["num_gpu"]
    return {"model": config["model"], "messages": messages, "format": schema,
            "stream": False, "keep_alive": config.get("keep_alive", "30m"),
            "options": options}


def wrap_ollama_response(answer):
    if not isinstance(answer, dict) or answer.get("done") is not True or answer.get("done_reason") != "stop":
        raise ProviderResponseError("Ollama did not finish normally; check output/context limits")
    message = answer.get("message")
    if not isinstance(message, dict):
        raise ProviderResponseError("Ollama response has no message")
    content = message.get("content")
    try:
        result = json.loads(content)
    except (TypeError, ValueError) as error:
        raise ProviderResponseError("Ollama did not return valid structured JSON") from error
    if not isinstance(result, dict):
        raise ProviderResponseError("Ollama must return a JSON object")
    # Keep the envelope Unity already parses; requirement evaluation still happens in Unity.
    return json.dumps({"status": "completed", "provider": "Ollama", "model": answer.get("model"),
                       "steps": [{"type": "model_output", "content": [{"type": "text", "text": content}]}],
                       "local_timings": {key: answer.get(key) for key in
                                         ("total_duration", "load_duration", "prompt_eval_duration", "eval_duration")}},
                      ensure_ascii=False).encode("utf-8")


def make_ollama_forward(config, open_url=urllib.request.urlopen):
    endpoint = config.get("ollama_url", "http://127.0.0.1:11434").rstrip("/") + "/api/chat"

    def forward(body, _unused_key):
        payload = ollama_request(json.loads(body), config)
        request = urllib.request.Request(endpoint, data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
                                         method="POST", headers={"Content-Type": "application/json"})
        with open_url(request, timeout=120) as response:
            try:
                answer = json.load(response)
            except (ValueError, UnicodeError) as error:
                raise ProviderResponseError("Ollama response is not JSON") from error
            return wrap_ollama_response(answer)
    return forward


def forward_to_gemini(body, api_key):
    request = urllib.request.Request(ENDPOINT, data=body, method="POST", headers={
        "Content-Type": "application/json", "x-goog-api-key": api_key,
    })
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


class BackendRouter:
    """One explicitly selected route. Configuration updates never occur during inference."""

    def __init__(self, config, api_key="", provider=None, gemini_forward=forward_to_gemini,
                 ollama_factory=make_ollama_forward):
        self.gemini_forward = gemini_forward
        self.ollama_factory = ollama_factory
        self._route = ("ollama", {}, api_key)
        self.configure(provider or config.get("provider", "ollama"), config, require_key=False)

    def configure(self, provider, config, api_key=None, require_key=True):
        if provider not in ("ollama", "gemini") or not isinstance(config, dict):
            raise ValueError("Choose ollama or gemini and provide model settings")
        config = dict(config)
        if not isinstance(config.get("model"), str) or not config["model"].strip() or config["model"].endswith("-cloud"):
            raise ValueError("Configure a downloaded Ollama model")
        if provider == "gemini" and require_key and not str(config.get("gemini_model", "")).strip():
            raise ValueError("Configure an available Gemini model ID")
        key = api_key.strip() if isinstance(api_key, str) and api_key.strip() else self._route[2]
        if provider == "gemini" and require_key and not key:
            raise ValueError("Enter a Gemini API key in VLM Backend")
        config["provider"] = provider
        # Replace one tuple so health cannot observe a partially updated route.
        self._route = (provider, config, key)

    def snapshot(self):
        provider, config, key = self._route
        return {"provider": provider, "config": config, "api_key": key,
                "model": config.get("model") if provider == "ollama" else config.get("gemini_model")}

    def forward(self, body, snapshot):
        if snapshot["provider"] == "ollama":
            return self.ollama_factory(snapshot["config"])(body, "")
        payload = json.loads(body)
        if snapshot["model"]:
            payload["model"] = snapshot["model"]
        answer = json.loads(self.gemini_forward(json.dumps(payload, ensure_ascii=False).encode("utf-8"), snapshot["api_key"]))
        if not isinstance(answer, dict):
            raise ProviderResponseError("Gemini returned an invalid response")
        answer["provider"] = "Gemini"
        answer["model"] = answer.get("model") or payload["model"]
        return json.dumps(answer, ensure_ascii=False).encode("utf-8")


def create_server(host, port, api_key, forward=forward_to_gemini, *, provider="gemini", model=None,
                  backend=None, control_token=None, instance_id=None):
    active_request = threading.BoundedSemaphore(1)
    busy = threading.Event()
    stopping = threading.Event()

    def route_snapshot():
        return backend.snapshot() if backend else {"provider": provider, "model": model, "api_key": api_key}

    class Handler(BaseHTTPRequestHandler):
        def setup(self):
            super().setup()
            self.connection.settimeout(15)

        def log_message(self, format, *args):
            # Avoid writing request paths or bodies supplied by clients to the log.
            pass

        def reply(self, status, payload):
            body = payload if isinstance(payload, bytes) else json.dumps(payload).encode("utf-8")
            try:
                self.send_response(status)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                self.wfile.write(body)
            except (BrokenPipeError, ConnectionResetError):
                pass

        def do_GET(self):
            if self.path == "/health":
                route = route_snapshot()
                self.reply(200, {"ok": True, "provider": route["provider"], "model": route["model"],
                                 "cpu_only": route["provider"] == "ollama" and route.get("config", {}).get("num_gpu") == 0,
                                 "key_configured": bool(route["api_key"]) if route["provider"] == "gemini" else False,
                                 "gemini_key_configured": bool(route["api_key"]), "busy": busy.is_set(),
                                 "instance_id": instance_id, "managed": bool(control_token and backend)})
            else:
                self.reply(404, {"error": "Not found"})

        def do_POST(self):
            if self.path in ("/configure", "/shutdown"):
                self.manage()
                return
            if self.path != "/verify":
                self.reply(404, {"error": "Not found"})
                return
            try:
                size = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                size = 0
            if not 0 < size <= MAX_REQUEST_BYTES:
                self.reply(413, {"error": "Expected a JSON request of at most 8 MiB"})
                return
            if stopping.is_set():
                self.reply(503, {"error": "Bridge is stopping"})
                return
            if not active_request.acquire(blocking=False):
                self.reply(429, {"error": "A verification is already in progress"})
                return
            try:
                busy.set()
                route = route_snapshot()
                request_provider = route["provider"]
                if ((self.headers.get("X-HomeCare-Provider") and self.headers["X-HomeCare-Provider"] != request_provider) or
                        (self.headers.get("X-HomeCare-Model") and self.headers["X-HomeCare-Model"] != route["model"])):
                    self.reply(409, {"error": "Backend changed before this request started. Check the selected backend and try again."})
                    return
                if request_provider == "gemini" and not route["api_key"]:
                    self.reply(503, {"error": "Set GEMINI_API_KEY or enter a key in VLM Backend"})
                    return
                body = self.rfile.read(size)
                payload = json.loads(body)
                if not isinstance(payload, dict) or not isinstance(payload.get("model"), str) or not payload["model"].strip():
                    raise ValueError("Missing model")
                allowed = {"model", "store", "system_instruction", "input", "response_format"}
                if set(payload) - allowed or not isinstance(payload.get("input"), list) or not payload["input"]:
                    raise ValueError("Invalid verification request")
                if not isinstance(payload.get("system_instruction"), str) or not isinstance(payload.get("response_format"), dict):
                    raise ValueError("Missing verification prompt or structured-output format")
                payload["store"] = False
                upstream_body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
                answer = backend.forward(upstream_body, route) if backend else forward(upstream_body, route["api_key"])
                # Never send an HTML upstream failure as if it were a successful model response.
                parsed = json.loads(answer)
                if not isinstance(parsed, dict):
                    raise ValueError("Invalid provider response")
                self.reply(200, answer)
            except (ValueError, UnicodeError):
                self.reply(400, {"error": "Malformed request: expected structured text/images; local video is unsupported"})
            except ProviderResponseError as error:
                self.reply(502, {"error": str(error)})
            except urllib.error.HTTPError as error:
                failure = {"error": request_provider + " rejected the request", "provider_status": error.code}
                if request_provider == "ollama":
                    try:
                        detail = json.loads(error.read(4096)).get("error")
                        if isinstance(detail, str):
                            failure["provider_error"] = detail[:1000]
                    except (ValueError, AttributeError, UnicodeError):
                        pass
                self.reply(502, failure)
            except (OSError, urllib.error.URLError):
                self.reply(502, {"error": request_provider + " could not be reached or timed out"})
            finally:
                busy.clear()
                active_request.release()

        def manage(self):
            # A per-instance token in the project's ignored UserSettings directory lets
            # the Editor manage its own bridge without controlling another project's server.
            expected = "Bearer " + (control_token or "")
            if (not control_token or backend is None or self.client_address[0] not in ("127.0.0.1", "::1") or
                    not hmac.compare_digest(self.headers.get("Authorization", "").encode("utf-8"), expected.encode("utf-8"))):
                self.reply(403, {"error": "This bridge is not managed by this Editor session"})
                return
            if stopping.is_set():
                self.reply(503, {"error": "Bridge is stopping; try again after it closes"})
                return
            if not active_request.acquire(blocking=False):
                self.reply(409, {"error": "Wait for the current request to finish before switching"})
                return
            try:
                if self.path == "/shutdown":
                    stopping.set()
                    self.reply(200, {"ok": True})
                    threading.Thread(target=self.server.shutdown, daemon=True).start()
                    return
                size = int(self.headers.get("Content-Length", "0"))
                if not 0 < size <= 16384:
                    raise ValueError("Invalid settings size")
                settings = json.loads(self.rfile.read(size))
                backend.configure(settings.get("provider"), settings.get("config"), settings.get("api_key"))
                route = backend.snapshot()
                self.reply(200, {"ok": True, "provider": route["provider"], "model": route["model"]})
            except (ValueError, UnicodeError, AttributeError) as error:
                self.reply(400, {"error": str(error)})
            finally:
                active_request.release()

    return ThreadingHTTPServer((host, port), Handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--provider", choices=("ollama", "gemini"), help="Override the saved provider at startup")
    parser.add_argument("--config", type=Path, default=CONFIG_PATH)
    parser.add_argument("--check-local", action="store_true", help="Run real local image inference and exit; no cloud calls")
    parser.add_argument("--log-file", type=Path, help="Optional local diagnostic log (never logs request bodies or keys)")
    args = parser.parse_args()
    if args.log_file:
        args.log_file.parent.mkdir(parents=True, exist_ok=True)
        sys.stdout = sys.stderr = open(args.log_file, "a", encoding="utf-8", buffering=1)
    api_key = os.environ.get("GEMINI_API_KEY", "").strip()
    config = load_local_config(args.config)
    if args.check_local:
        try:
            check_local_inference(config)
        except (OSError, ValueError, ProviderResponseError) as error:
            detail = str(error)
            if isinstance(error, urllib.error.HTTPError):
                try:
                    detail = str(json.loads(error.read(4096)).get("error", detail))
                except (ValueError, OSError):
                    pass
            print("Local inference failed: " + detail[:1000], file=sys.stderr)
            print("For CUDA/GPU failures, update the graphics driver or use Start-LocalVlm.cmd -Cpu. "
                  "CPU mode is slower. No cloud fallback was attempted.", file=sys.stderr)
            return 1
        return 0
    backend = BackendRouter(config, api_key, args.provider)
    control_token = secrets.token_urlsafe(32)
    instance_id = secrets.token_hex(16)
    server = create_server(args.host, args.port, api_key, backend=backend, control_token=control_token, instance_id=instance_id)
    control_file = Path(__file__).resolve().parent.parent / "UserSettings" / ("vlm-bridge-" + str(args.port) + ".json")
    control_file.parent.mkdir(parents=True, exist_ok=True)
    control_file.write_text(json.dumps({"instance_id": instance_id, "token": control_token}), encoding="utf-8")
    print(f"Verification bridge: http://{args.host}:{args.port}/verify", flush=True)
    print("Provider: " + backend.snapshot()["provider"] + " / " + str(backend.snapshot()["model"]), flush=True)
    try:
        server.serve_forever(poll_interval=0.1)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        try:
            if json.loads(control_file.read_text(encoding="utf-8")).get("instance_id") == instance_id:
                control_file.unlink()
        except (OSError, ValueError):
            pass


if __name__ == "__main__":
    sys.exit(main())
