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
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("Configure a valid verification bridge URL in the scene Inspector.");
            using var request = new UnityWebRequest(uri, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
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
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException("Verification bridge HTTP " + request.responseCode + ": " +
                    request.error + " " + request.downloadHandler.text);
            return GeminiVerificationProtocol.ParseInteraction(request.downloadHandler.text);
        }
    }
}
