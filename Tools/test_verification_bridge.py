import http.client
import json
import threading
import unittest
import urllib.error

from verification_bridge import MAX_REQUEST_BYTES, create_server


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


if __name__ == "__main__":
    unittest.main()
