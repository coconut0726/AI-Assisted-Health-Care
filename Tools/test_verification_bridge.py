import http.client
import json
import threading
import unittest
import urllib.error

from verification_bridge import MAX_REQUEST_BYTES, BackendRouter, create_server


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        self.upstream_error = None
        self.started = threading.Event()
        self.release = threading.Event()
        self.release.set()

        def forward(body, key):
            self.calls.append((json.loads(body), key))
            self.started.set()
            self.release.wait(3)
            if self.upstream_error:
                raise self.upstream_error
            return json.dumps({"status": "completed", "steps": [{
                "type": "model_output", "content": [{"type": "text", "text": json.dumps({
                    "step_id": "s1", "results": [{"requirement_id": "s1_r1", "status": "unknown",
                    "evidence": "", "reason": "No sufficient observation in this test fixture"}]
                })}]
            }]}).encode()

        self.server = create_server("127.0.0.1", 0, "test-key-not-a-real-credential", forward)
        self.thread = threading.Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(1)

    def request(self, method="POST", path="/verify", payload=None, raw=None, headers=None):
        connection = http.client.HTTPConnection(*self.server.server_address, timeout=5)
        body = raw if raw is not None else json.dumps(payload or self.valid_payload())
        connection.request(method, path, body=None if method == "GET" else body, headers=headers or {"Content-Type": "application/json"})
        response = connection.getresponse()
        answer = json.loads(response.read())
        status = response.status
        connection.close()
        return status, answer

    @staticmethod
    def valid_payload():
        return {"model": "test-model", "store": True, "system_instruction": "Test only", "input": [
            {"type": "image", "mime_type": "image/jpeg", "data": "test-image"}
        ], "response_format": {"type": "text", "mime_type": "application/json", "schema": {"type": "object"}}}

    def test_health_without_sending_media(self):
        status, answer = self.request("GET", "/health")
        self.assertEqual(status, 200)
        self.assertTrue(answer["key_configured"])
        self.assertEqual(self.calls, [])

    def test_round_trip_preserves_structured_result_and_disables_storage(self):
        status, answer = self.request()
        self.assertEqual(status, 200)
        result = json.loads(answer["steps"][0]["content"][0]["text"])
        self.assertEqual(result["results"][0]["status"], "unknown")
        self.assertFalse(self.calls[0][0]["store"])
        self.assertNotIn("x-goog-api-key", self.calls[0][0])

    def test_malformed_json_does_not_call_provider(self):
        self.assertEqual(self.request(raw="{")[0], 400)
        self.assertEqual(self.calls, [])

    def test_unexpected_provider_fields_rejected(self):
        payload = self.valid_payload()
        payload["tools"] = [{"type": "unwanted"}]
        self.assertEqual(self.request(payload=payload)[0], 400)
        self.assertEqual(self.calls, [])

    def test_oversized_upload_rejected_before_reading_body(self):
        self.assertEqual(self.request(raw="", headers={"Content-Length": str(MAX_REQUEST_BYTES + 1)})[0], 413)
        self.assertEqual(self.calls, [])

    def test_provider_rejection_is_not_a_successful_verdict(self):
        self.upstream_error = urllib.error.HTTPError("https://provider.invalid", 429, "limited", {}, None)
        status, answer = self.request()
        self.assertEqual(status, 502)
        self.assertEqual(answer["provider_status"], 429)

    def test_unknown_route_does_not_call_provider(self):
        self.assertEqual(self.request(path="/other")[0], 404)
        self.assertEqual(self.calls, [])

    def test_concurrent_submission_is_rejected(self):
        self.release.clear()
        first = threading.Thread(target=self.request, daemon=True)
        first.start()
        self.assertTrue(self.started.wait(2))
        self.assertEqual(self.request()[0], 429)
        self.release.set()
        first.join(2)
        self.assertEqual(len(self.calls), 1)


class MissingKeyTests(unittest.TestCase):
    def test_missing_key_is_an_actionable_error(self):
        server = create_server("127.0.0.1", 0, "", lambda *_: self.fail("Provider should not be called"))
        thread = threading.Thread(target=server.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True)
        thread.start()
        try:
            connection = http.client.HTTPConnection(*server.server_address, timeout=3)
            connection.request("POST", "/verify", json.dumps(BridgeTests.valid_payload()))
            response = connection.getresponse()
            self.assertEqual(response.status, 503)
            self.assertIn("GEMINI_API_KEY", json.loads(response.read())["error"])
            connection.close()
        finally:
            server.shutdown()
            server.server_close()
            thread.join(1)


class BackendSwitchTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        self.started, self.release = threading.Event(), threading.Event()
        self.release.set()
        self.config = {"provider": "ollama", "model": "qwen-test:4b", "gemini_model": "gemini-test"}

        def answer():
            return {"status": "completed", "steps": [{"type": "model_output", "content": [
                {"type": "text", "text": '{"step_id":"s1","results":[]}'}]}]}

        def gemini(body, key):
            self.calls.append(("gemini", json.loads(body), key))
            return json.dumps(answer()).encode()

        def ollama_factory(config):
            def forward(body, _key):
                self.calls.append(("ollama", config["model"], None))
                self.started.set()
                self.release.wait(3)
                return json.dumps(dict(answer(), provider="Ollama", model=config["model"])).encode()
            return forward

        self.router = BackendRouter(self.config, gemini_forward=gemini, ollama_factory=ollama_factory)
        self.server = create_server("127.0.0.1", 0, "", backend=self.router,
                                    control_token="test-control-token", instance_id="test-instance")
        self.thread = threading.Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(1)

    def request(self, path="/verify", payload=None, authorized=False, headers=None):
        connection = http.client.HTTPConnection(*self.server.server_address, timeout=5)
        request_headers = {"Content-Type": "application/json"}
        if authorized:
            request_headers["Authorization"] = "Bearer test-control-token"
        request_headers.update(headers or {})
        connection.request("GET" if path == "/health" else "POST", path,
                           None if path == "/health" else json.dumps(payload if payload is not None else BridgeTests.valid_payload()),
                           request_headers)
        response = connection.getresponse()
        result = response.status, json.loads(response.read())
        connection.close()
        return result

    def configure(self, provider, key=None):
        payload = {"provider": provider, "config": self.config}
        if key is not None:
            payload["api_key"] = key
        return self.request("/configure", payload, authorized=True)

    def test_switches_both_routes_and_overrides_the_legacy_scene_model(self):
        self.assertEqual(self.request()[1]["model"], "qwen-test:4b")
        self.assertEqual(self.configure("gemini", "fake-session-key")[0], 200)
        status, answer = self.request()
        self.assertEqual(status, 200)
        self.assertEqual(answer["provider"], "Gemini")
        self.assertEqual(answer["model"], "gemini-test")
        self.assertEqual(self.calls[-1][1]["model"], "gemini-test")
        self.assertEqual(self.calls[-1][2], "fake-session-key")
        self.assertEqual(self.configure("ollama")[0], 200)
        self.assertEqual(self.request()[1]["provider"], "Ollama")
        self.assertEqual(self.configure("gemini")[0], 200)  # reuse the in-memory session key
        self.assertEqual(len(self.calls), 3)  # selection/health never runs an inference

    def test_missing_gemini_key_leaves_the_working_qwen_route_unchanged(self):
        self.assertEqual(self.configure("gemini")[0], 400)
        self.assertEqual(self.request("/health")[1]["provider"], "ollama")
        self.assertEqual(self.request()[1]["provider"], "Ollama")

    def test_settings_and_shutdown_require_the_project_control_token(self):
        payload = {"provider": "gemini", "config": self.config, "api_key": "fake-session-key"}
        self.assertEqual(self.request("/configure", payload)[0], 403)
        self.assertEqual(self.request("/shutdown", {})[0], 403)
        self.assertEqual(self.request("/health")[1]["provider"], "ollama")

    def test_active_inference_blocks_switch_and_shutdown(self):
        self.release.clear()
        worker = threading.Thread(target=self.request, daemon=True)
        worker.start()
        self.assertTrue(self.started.wait(2))
        self.assertTrue(self.request("/health")[1]["busy"])
        self.assertEqual(self.configure("gemini", "fake-session-key")[0], 409)
        self.assertEqual(self.request("/shutdown", {}, authorized=True)[0], 409)
        self.release.set()
        worker.join(2)
        self.assertEqual(self.request("/health")[1]["provider"], "ollama")

    def test_editor_backend_guard_prevents_an_unintended_cloud_request(self):
        self.assertEqual(self.configure("gemini", "fake-session-key")[0], 200)
        self.assertEqual(self.request(headers={"X-HomeCare-Provider": "ollama"})[0], 409)
        self.assertEqual(self.request(headers={"X-HomeCare-Provider": "gemini", "X-HomeCare-Model": "wrong-model"})[0], 409)
        self.assertEqual(self.calls, [])
        self.assertEqual(self.request(headers={"X-HomeCare-Provider": "gemini", "X-HomeCare-Model": "gemini-test"})[0], 200)

    def test_health_does_not_disclose_api_or_control_keys(self):
        self.configure("gemini", "fake-session-key")
        _, health = self.request("/health")
        self.assertTrue(health["gemini_key_configured"])
        self.assertTrue(health["managed"])
        self.assertEqual(health["instance_id"], "test-instance")
        self.assertNotIn("api_key", health)
        self.assertNotIn("token", health)
        self.assertNotIn("fake-session-key", json.dumps(health))
        self.assertNotIn("test-control-token", json.dumps(health))

    def test_bad_provider_does_not_replace_the_existing_route(self):
        self.assertEqual(self.configure("other")[0], 400)
        self.assertEqual(self.request("/health")[1]["provider"], "ollama")

    def test_managed_shutdown_stops_the_server(self):
        self.assertEqual(self.request("/shutdown", {}, authorized=True)[0], 200)
        self.thread.join(2)
        self.assertFalse(self.thread.is_alive())


if __name__ == "__main__":
    unittest.main()
