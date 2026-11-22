using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HomeCare.Procedures;
using UnityEngine.Networking;

namespace HomeCare.AR
{
    public static class VerificationBridgeClient
    {
        public static async Task<VerificationResponse> VerifyAsync(string endpoint, string requestJson, CancellationToken cancellation)
        {
            string raw = await PostAsync(endpoint, requestJson, cancellation);
            var result = GeminiVerificationProtocol.ParseInteraction(raw);
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(raw);
            result.provider = (string)envelope["provider"] ?? "Gemini";
            result.model = (string)envelope["model"];
            return result;
        }

        /// <summary>How long to keep retrying while the bridge is busy with another request (HTTP 429).</summary>
        const float BusyRetrySeconds = 60;

        // Sends any Interactions request through the PC bridge and returns the raw response JSON.
        // The bridge runs one request at a time and answers 429 meanwhile (also while an abandoned
        // request is still finishing), so wait for it instead of failing.
        public static async Task<string> PostAsync(string endpoint, string requestJson, CancellationToken cancellation,
            string expectedProvider = null, string expectedModel = null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("Configure a valid verification bridge URL in the scene Inspector.");
            var deadline = DateTime.UtcNow.AddSeconds(BusyRetrySeconds);
            while (true)
            {
                var (code, text, error) = await SendAsync(uri, requestJson, cancellation, expectedProvider, expectedModel);
                if (code == 429 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(750, cancellation);
                    continue;
                }
                if (error != null)
                    throw new InvalidOperationException("Verification bridge HTTP " + code + ": " + error + " " + text);
                return text;
            }
        }

        static async Task<(long code, string text, string error)> SendAsync(Uri uri, string requestJson, CancellationToken cancellation,
            string expectedProvider, string expectedModel)
        {
            using var request = new UnityWebRequest(uri, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (expectedProvider != null) request.SetRequestHeader("X-HomeCare-Provider", expectedProvider);
            if (expectedModel != null) request.SetRequestHeader("X-HomeCare-Model", expectedModel);
            request.timeout = 150;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellation.IsCancellationRequested)
                {
                    request.Abort();
                    cancellation.ThrowIfCancellationRequested();
                }
                await Task.Yield();
            }
            cancellation.ThrowIfCancellationRequested();
            bool ok = request.result == UnityWebRequest.Result.Success;
            return (request.responseCode, request.downloadHandler.text, ok ? null : request.error ?? "request failed");
        }
    }
}
