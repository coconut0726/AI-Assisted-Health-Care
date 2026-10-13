// Assets/_Project/Editor/Authoring/GeminiClient.cs
// Editor-only Gemini client (Interactions API). Runs in the Unity Editor, never on the headset.
// API key: set environment variable GEMINI_API_KEY before launching Unity Hub. Never commit the key.
// Uses the ILLMClient interface defined in ClaudeClient.cs.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HomeCare.Authoring
{
    public class GeminiClient : ILLMClient
    {
        const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";
        const long MaxInlineVideoBytes = 90L * 1024 * 1024; // API limit is under 100 MB; keep a margin

        readonly string _model;

        public GeminiClient(string model = "gemini-3.8-flash")
        {
            _model = model;
        }

        // Text only: nurse verbal or text instruction -> schema-conformant JSON.
        public Task<string> DecomposeAsync(string systemPrompt, string userContent, string jsonSchema)
        {
            string input = $"[{{\"type\":\"text\",\"text\":{JsonString(userContent)}}}]";
            return SendAsync(systemPrompt, input, jsonSchema);
        }

        // Text + short video (inline base64). For videos over ~90 MB, use the Files API instead.
        public Task<string> DecomposeWithVideoAsync(string systemPrompt, string userContent, string videoPath, string jsonSchema)
        {
            var info = new FileInfo(videoPath);
            if (!info.Exists) throw new FileNotFoundException("Video not found", videoPath);
            if (info.Length > MaxInlineVideoBytes)
                throw new InvalidOperationException("Video too large for inline upload; use the Files API.");

            string b64 = Convert.ToBase64String(File.ReadAllBytes(videoPath));
            string input =
                "[" +
                $"{{\"type\":\"video\",\"data\":\"{b64}\",\"mime_type\":\"video/mp4\"}}," +
                $"{{\"type\":\"text\",\"text\":{JsonString(userContent)}}}" +
                "]";
            return SendAsync(systemPrompt, input, jsonSchema);
        }

        async Task<string> SendAsync(string systemPrompt, string inputJson, string jsonSchema)
        {
            string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("GEMINI_API_KEY is not set. Restart Unity Hub after setting it.");

            string body =
                "{" +
                $"\"model\":\"{_model}\"," +
                $"\"system_instruction\":{JsonString(systemPrompt)}," +
                $"\"input\":{inputJson}," +
                "\"response_format\":{\"type\":\"text\",\"mime_type\":\"application/json\",\"schema\":" + jsonSchema + "}" +
                "}";

            using var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-goog-api-key", apiKey);
            req.timeout = 300;

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"Gemini API error {req.responseCode}: {req.downloadHandler.text}");

            // Response: { "steps": [ { "type": "model_output", "content": [ { "type": "text", "text": "..." } ] } ] }
            var resp = JsonUtility.FromJson<InteractionResponse>(req.downloadHandler.text);
            if (resp?.steps != null)
            {
                for (int i = resp.steps.Length - 1; i >= 0; i--)
                {
                    var s = resp.steps[i];
                    if (s.type != "model_output" || s.content == null) continue;
                    var sb = new StringBuilder();
                    foreach (var c in s.content)
                        if (c.type == "text") sb.Append(c.text);
                    if (sb.Length > 0) return sb.ToString();
                }
            }
            throw new Exception("No model output found. Raw response: " + req.downloadHandler.text);
        }

        static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        [Serializable] class InteractionResponse { public Step[] steps; }
        [Serializable] class Step { public string type; public ContentPart[] content; }
        [Serializable] class ContentPart { public string type; public string text; }
    }
}
