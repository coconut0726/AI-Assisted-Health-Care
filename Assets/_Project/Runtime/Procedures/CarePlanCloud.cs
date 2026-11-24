using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace HomeCare.Procedures.Actions
{
    /// <summary>A care plan fetched from the care-plan server (CarePlanServer/), or the copy cached on the device.</summary>
    public sealed class CloudPlan
    {
        public string PatientId;
        public int Version;
        public string UpdatedAt;
        public string UpdatedBy;
        /// <summary>The action_requirements_v1 JSON, exactly as the server stores it.</summary>
        public string ProcedureJson;
        public bool FromCache;

        public string Label => $"Cloud plan v{Version}" + (UpdatedBy == "nurse" ? " · edited by nurse" : "") + (FromCache ? " · offline copy" : "");
    }

    /// <summary>
    /// Reads the latest care plan the nurse saved on the phone (GET /api/patients/:id/plan, see docs/SCHEMA_mobile.md).
    /// The last good plan is cached under persistentDataPath so a session can still start offline.
    /// </summary>
    public static class CarePlanCloud
    {
        /// <summary>Resources path of the access token file. The file is git-ignored; set it with AHC → Cloud Plan → Set Access Token.</summary>
        public const string TokenResource = "CarePlanCloudToken";

        public static string LoadToken()
        {
            var asset = Resources.Load<TextAsset>(TokenResource);
            return asset == null ? "" : asset.text.Trim();
        }

        static string CachePath(string patientId) =>
            Path.Combine(Application.persistentDataPath, "CarePlanCache", patientId + ".json");

        /// <summary>Fetches the latest plan, checks it the way the headset will run it, and caches it.
        /// When the server cannot be reached, returns the cached plan (FromCache) if there is one; otherwise throws.</summary>
        public static async Task<CloudPlan> FetchAsync(string baseUrl, string patientId, string token, CancellationToken cancellation)
        {
            try
            {
                string raw = await GetAsync(baseUrl.TrimEnd('/') + "/api/patients/" + Uri.EscapeDataString(patientId) + "/plan", token, cancellation);
                var plan = Parse(raw, false);
                ActionProcedureJson.Parse(plan.ProcedureJson); // reject a plan the session could not run, keep the cache
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath(patientId)));
                File.WriteAllText(CachePath(patientId), raw);
                return plan;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                var cached = LoadCached(patientId);
                if (cached != null) { Debug.LogWarning("Care-plan server unavailable, using the offline copy: " + e.Message); return cached; }
                throw;
            }
        }

        public static CloudPlan LoadCached(string patientId)
        {
            try { return File.Exists(CachePath(patientId)) ? Parse(File.ReadAllText(CachePath(patientId)), true) : null; }
            catch (Exception) { return null; }
        }

        static CloudPlan Parse(string raw, bool fromCache)
        {
            var record = JObject.Parse(raw);
            if (record["plan"] is not JObject plan) throw new ArgumentException("The server reply has no plan.");
            return new CloudPlan
            {
                PatientId = (string)record["patient_id"],
                Version = (int?)record["version"] ?? 0,
                UpdatedAt = (string)record["updated_at"],
                UpdatedBy = (string)record["updated_by"],
                ProcedureJson = plan.ToString(Newtonsoft.Json.Formatting.None),
                FromCache = fromCache,
            };
        }

        static async Task<string> GetAsync(string url, string token, CancellationToken cancellation)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("Configure a valid care-plan server URL in the Inspector.");
            using var request = UnityWebRequest.Get(uri);
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 20;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellation.IsCancellationRequested) { request.Abort(); cancellation.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            if (request.result == UnityWebRequest.Result.Success) return request.downloadHandler.text;
            if (request.responseCode == 401) throw new InvalidOperationException("The care-plan server refused the access token (AHC → Cloud Plan → Set Access Token).");
            throw new InvalidOperationException($"Care-plan server HTTP {request.responseCode}: {request.error}");
        }
    }
}
