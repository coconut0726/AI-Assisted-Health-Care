using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HomeCare.Procedures;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace HomeCare.Authoring
{
    public class GeminiClient : ILLMClient
    {
        const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";
        const long MaxInlineVideoBytes = 90L * 1024 * 1024;
        // Conservative local budget for inline uploads (before base64 expansion).
        public const long MaxInlineMediaBytes = 18L * 1024 * 1024;

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

        // Text + short video (inline base64). Larger files need a separate Files API workflow.
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

        // Same structured-output transport for requirement-guided VQA.
        public Task<string> GenerateWithEvidenceAsync(string systemPrompt, string userContent,
            EvidenceAttachment[] attachments, string jsonSchema)
        {
            ValidateEvidence(attachments);
            var parts = new List<object> { new { type = "text", text = userContent } };
            foreach (var attachment in attachments)
            {
                parts.Add(new { type = "text", text = "Evidence scope: " + attachment.scope +
                    "; filename: " + Path.GetFileName(attachment.path) });
                bool video = attachment.scope == "whole_step";
                parts.Add(new { type = video ? "video" : "image",
                    data = Convert.ToBase64String(File.ReadAllBytes(attachment.path)),
                    mime_type = video ? "video/mp4" :
                        (Path.GetExtension(attachment.path).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg") });
            }
            return SendAsync(systemPrompt, JsonConvert.SerializeObject(parts), jsonSchema);
        }

        public static void ValidateEvidence(EvidenceAttachment[] attachments)
        {
            if (attachments == null || attachments.Length == 0)
                throw new ArgumentException("Attach at least one execution image or video.");
            long total = 0;
            var scopes = new HashSet<string>();
            foreach (var attachment in attachments)
            {
                if (attachment == null || string.IsNullOrWhiteSpace(attachment.path))
                    throw new ArgumentException("An evidence attachment has no path.");
                if (!scopes.Add(attachment.scope ?? ""))
                    throw new ArgumentException("Only one attachment per evidence scope is supported.");
                string extension = Path.GetExtension(attachment.path).ToLowerInvariant();
                if (attachment.scope == "whole_step")
                {
                    if (extension != ".mp4") throw new ArgumentException("Whole-step evidence must be an MP4 video.");
                }
                else if (attachment.scope == "before_step" || attachment.scope == "after_step")
                {
                    if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                        throw new ArgumentException("Before/after evidence must be a PNG or JPEG image.");
                }
                else throw new ArgumentException("Invalid evidence scope: " + attachment.scope);
                var file = new FileInfo(attachment.path);
                if (!file.Exists) throw new FileNotFoundException("Evidence file not found.", attachment.path);
                if (file.Length == 0) throw new ArgumentException("Evidence file is empty: " + file.Name);
                total += file.Length;
            }
            if (total > MaxInlineMediaBytes)
                throw new ArgumentException("Evidence exceeds the prototype's 18 MiB total inline upload budget. Use smaller images or a shorter/compressed video.");
        }

        async Task<string> SendAsync(string systemPrompt, string inputJson, string jsonSchema)
        {
            string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("GEMINI_API_KEY is not set. Restart Unity Hub after setting it.");

            string body =
                "{" +
                $"\"model\":{JsonString(_model)}," +
                "\"store\":false," +
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
            if (resp == null || resp.status != "completed")
                throw new Exception("Gemini interaction did not complete: " + (resp?.status ?? "missing status"));
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

        [Serializable] class InteractionResponse { public string status; public Step[] steps; }
        [Serializable] class Step { public string type; public ContentPart[] content; }
        [Serializable] class ContentPart { public string type; public string text; }
    }
}
