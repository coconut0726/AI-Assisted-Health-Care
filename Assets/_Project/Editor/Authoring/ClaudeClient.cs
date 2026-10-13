// Assets/_Project/Editor/Authoring/ClaudeClient.cs
// Editor-only client for instruction decomposition. Runs in the Unity Editor, never on the headset.
// API key: set environment variable ANTHROPIC_API_KEY before launching Unity. Never commit the key.

using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HomeCare.Authoring
{
    // Provider-agnostic interface, so OpenAI or Gemini can be swapped in for comparison.
    public interface ILLMClient
    {
        // Returns the raw JSON text that conforms to the given JSON schema.
        Task<string> DecomposeAsync(string systemPrompt, string userContent, string jsonSchema);
    }

    public class ClaudeClient : ILLMClient
    {
        const string Endpoint = "https://api.anthropic.com/v1/messages";
        const string ApiVersion = "2023-06-01";

        readonly string _model;
        readonly int _maxTokens;

        public ClaudeClient(string model = "claude-sonnet-5-5", int maxTokens = 8000)
        {
            _model = model;
            _maxTokens = maxTokens;
        }

        public async Task<string> DecomposeAsync(string systemPrompt, string userContent, string jsonSchema)
        {
            string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");

            // Body built by hand so the schema can be injected as raw JSON.
            string body =
                "{" +
                $"\"model\":\"{_model}\"," +
                $"\"max_tokens\":{_maxTokens}," +
                $"\"system\":{JsonString(systemPrompt)}," +
                $"\"messages\":[{{\"role\":\"user\",\"content\":{JsonString(userContent)}}}]," +
                "\"output_config\":{\"format\":{\"type\":\"json_schema\",\"schema\":" + jsonSchema + "}}" +
                "}";

            using var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("content-type", "application/json");
            req.SetRequestHeader("x-api-key", apiKey);
            req.SetRequestHeader("anthropic-version", ApiVersion);
            req.timeout = 180;

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"Claude API error {req.responseCode}: {req.downloadHandler.text}");

            // Response: { "content": [ { "type": "text", "text": "<schema-conformant JSON>" } ], ... }
            var resp = JsonUtility.FromJson<MessageResponse>(req.downloadHandler.text);
            if (resp?.content == null || resp.content.Length == 0)
                throw new Exception("Empty response from Claude API.");
            return resp.content[0].text;
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

        [Serializable] class MessageResponse { public ContentBlock[] content; }
        [Serializable] class ContentBlock { public string type; public string text; }
    }
}
