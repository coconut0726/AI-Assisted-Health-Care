"""Forward the headset's existing Gemini Interactions request; keep GEMINI_API_KEY on the PC.

USB: adb reverse tcp:8765 tcp:8765, then python Tools/verification_bridge.py
LAN: python Tools/verification_bridge.py --host 0.0.0.0 (set the PC URL in the scene).
Uses only the Python standard library. No images, prompts, keys or model answers are logged.
"""
import argparse
import json
import os
import threading
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/interactions"
MAX_REQUEST_BYTES = 8 * 1024 * 1024


def forward_to_gemini(body, api_key):
    request = urllib.request.Request(ENDPOINT, data=body, method="POST", headers={
        "Content-Type": "application/json", "x-goog-api-key": api_key,
    })
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def create_server(host, port, api_key, forward=forward_to_gemini):
    active_request = threading.BoundedSemaphore(1)

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
                self.reply(200, {"ok": True, "key_configured": bool(api_key)})
            else:
                self.reply(404, {"error": "Not found"})

        def do_POST(self):
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
            if not api_key:
                self.reply(503, {"error": "Set GEMINI_API_KEY in the PC process before starting the bridge"})
                return
            if not active_request.acquire(blocking=False):
                self.reply(429, {"error": "A verification is already in progress"})
                return
            try:
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
                answer = forward(upstream_body, api_key)
                # Never send an HTML upstream failure as if it were a successful model response.
                parsed = json.loads(answer)
                if not isinstance(parsed, dict):
                    raise ValueError("Invalid provider response")
                self.reply(200, answer)
            except (ValueError, UnicodeError):
                self.reply(400, {"error": "Malformed verification request or provider JSON"})
            except urllib.error.HTTPError as error:
                self.reply(502, {"error": "Gemini rejected the request", "provider_status": error.code})
            except (OSError, urllib.error.URLError):
                self.reply(502, {"error": "Gemini could not be reached or timed out"})
            finally:
                active_request.release()

    return ThreadingHTTPServer((host, port), Handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    api_key = os.environ.get("GEMINI_API_KEY", "").strip()
    server = create_server(args.host, args.port, api_key)
    print(f"Verification bridge: http://{args.host}:{args.port}/verify", flush=True)
    print("GEMINI_API_KEY configured: " + ("yes" if api_key else "no"), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
