using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HomeCare.AR;
using HomeCare.Procedures;
using Newtonsoft.Json.Linq;

namespace HomeCare.Authoring
{
    /// <summary>Calls the local PC bridge, following the project backend or requiring Ollama explicitly.</summary>
    public sealed class LocalVlmClient : ILLMClient
    {
        readonly string _endpoint;
        public string Model { get; }
        public string Provider { get; }

        public LocalVlmClient(bool followProjectBackend = false)
        {
            var config = VlmBackendSettings.Read();
            Provider = followProjectBackend ? VlmBackendSettings.Provider(config) : "ollama";
            Model = VlmBackendSettings.Model(config, Provider);
            _endpoint = VlmBackendSettings.Endpoint(config) + "/verify";
        }

        public Task<string> DecomposeAsync(string systemPrompt, string userContent, string jsonSchema) =>
            SendAsync(systemPrompt, new JArray(new JObject { ["type"] = "text", ["text"] = userContent }), jsonSchema);

        public Task<string> GenerateWithEvidenceAsync(string systemPrompt, string userContent,
            EvidenceAttachment[] attachments, string jsonSchema)
        {
            GeminiClient.ValidateEvidence(attachments);
            var parts = new JArray(new JObject { ["type"] = "text", ["text"] = userContent });
            foreach (var attachment in attachments)
            {
                if (Provider == "ollama" && attachment.scope == "whole_step")
                    throw new ArgumentException("The local prototype accepts images only. Whole-step video is not supported and cannot be replaced by after-step evidence.");
                parts.Add(new JObject { ["type"] = "text", ["text"] = "Evidence scope: " + attachment.scope +
                    "; filename: " + Path.GetFileName(attachment.path) });
                bool video = attachment.scope == "whole_step";
                parts.Add(new JObject { ["type"] = video ? "video" : "image", ["data"] = Convert.ToBase64String(File.ReadAllBytes(attachment.path)),
                    ["mime_type"] = video ? "video/mp4" : Path.GetExtension(attachment.path).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg" });
            }
            return SendAsync(systemPrompt, parts, jsonSchema);
        }

        async Task<string> SendAsync(string systemPrompt, JArray input, string schema)
        {
            var health = await VlmBackendSettings.GetAsync(_endpoint.Replace("/verify", "/health"), CancellationToken.None);
            if (health == null || (bool?)health["managed"] != true || (string)health["provider"] != Provider || (string)health["model"] != Model)
                throw new InvalidOperationException("The bridge does not match this request. Open Tools > Home Care > VLM Backend and use Apply & Start.");
            var request = new JObject { ["model"] = Model, ["store"] = false, ["system_instruction"] = systemPrompt,
                ["input"] = input, ["response_format"] = new JObject { ["type"] = "text", ["mime_type"] = "application/json",
                    ["schema"] = JObject.Parse(schema) } };
            string body = request.ToString();
            if (System.Text.Encoding.UTF8.GetByteCount(body) > 8 * 1024 * 1024)
                throw new ArgumentException("Bridge upload exceeds 8 MiB after encoding. Use smaller images or a shorter video.");
            string response = await VerificationBridgeClient.PostAsync(_endpoint, body, CancellationToken.None, Provider, Model);
            return GeminiVerificationProtocol.ExtractModelText(response);
        }
    }
}
