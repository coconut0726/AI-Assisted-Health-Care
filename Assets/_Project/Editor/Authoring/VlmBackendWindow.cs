using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace HomeCare.Authoring
{
    internal static class VlmBackendSettings
    {
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string ConfigPath => Path.Combine(Root, "Tools/local-vlm.json");
        public static string MachinePath => Path.Combine(Root, "UserSettings/local-vlm-machine.json");
        public static bool CpuOnly => File.Exists(MachinePath) && (bool?)JObject.Parse(File.ReadAllText(MachinePath))["cpu_only"] == true;
        public static JObject Read(bool includeMachineSettings = true)
        {
            var config = JObject.Parse(File.ReadAllText(ConfigPath));
            if (includeMachineSettings && CpuOnly) config["num_gpu"] = 0;
            return config;
        }
        public static string Provider(JObject config) => (string)config["provider"] ?? "ollama";
        public static string Model(JObject config, string provider) =>
            (string)config[provider == "gemini" ? "gemini_model" : "model"];
        public static string Endpoint(JObject config) => "http://127.0.0.1:" + (int)config["bridge_port"];

        public static async Task<JObject> GetAsync(string url, CancellationToken cancellation)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = 2;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellation.IsCancellationRequested) { request.Abort(); cancellation.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            if (request.result != UnityWebRequest.Result.Success) return null;
            try { return JObject.Parse(request.downloadHandler.text); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        public static async Task<JObject> ManageAsync(JObject config, JObject health, string action,
            JObject body, CancellationToken cancellation)
        {
            string path = Path.Combine(Root, "UserSettings/vlm-bridge-" + (int)config["bridge_port"] + ".json");
            JObject owner = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
            if (health == null || (bool?)health["managed"] != true || owner == null ||
                string.IsNullOrWhiteSpace((string)owner["token"]) ||
                (string)owner["instance_id"] != (string)health["instance_id"])
                throw new InvalidOperationException("This port belongs to an older or another project's bridge. Stop its terminal once, then use Apply & Start here.");
            using var request = new UnityWebRequest(Endpoint(config) + action, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + (string)owner["token"]);
            request.timeout = 5;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellation.IsCancellationRequested) { request.Abort(); cancellation.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException(request.responseCode == 409
                    ? "A request is still running. Wait for it to finish before switching."
                    : "Could not apply backend settings: " + request.downloadHandler.text);
            return JObject.Parse(request.downloadHandler.text);
        }
    }

    public sealed class VlmBackendWindow : EditorWindow
    {
        enum Backend { Qwen, Gemini }
        Backend _selected;
        string _qwenModel, _geminiModel;
        [NonSerialized] string _apiKey = "";
        string _status = "", _error = "";
        JObject _health;
        bool _busy, _refreshing, _cpuOnly;
        double _nextRefresh;
        CancellationTokenSource _lifetime;

        [MenuItem("Tools/Home Care/VLM Backend")]
        public static void Open() => GetWindow<VlmBackendWindow>("VLM Backend");

        void OnEnable()
        {
            _lifetime = new CancellationTokenSource();
            try
            {
                var config = VlmBackendSettings.Read();
                _selected = VlmBackendSettings.Provider(config) == "gemini" ? Backend.Gemini : Backend.Qwen;
                _qwenModel = (string)config["model"];
                _geminiModel = (string)config["gemini_model"] ?? "gemini-3.8-flash";
                _cpuOnly = VlmBackendSettings.CpuOnly;
            }
            catch (Exception e) { _error = e.Message; }
        }

        void OnDisable() { _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; }

        void OnInspectorUpdate()
        {
            if (!_busy && !_refreshing && EditorApplication.timeSinceStartup >= _nextRefresh) Refresh();
        }

        async void Refresh()
        {
            _refreshing = true;
            var token = _lifetime.Token;
            try { _health = await VlmBackendSettings.GetAsync(VlmBackendSettings.Endpoint(VlmBackendSettings.Read()) + "/health", token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this != null) _error = e.Message; }
            finally { _refreshing = false; _nextRefresh = EditorApplication.timeSinceStartup + 2; if (this != null) Repaint(); }
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Project VLM backend", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("The scene verifier and Editor tools in ProjectBackend mode follow this selection. Apply when the current request is finished.", MessageType.Info);
            using (new EditorGUI.DisabledScope(_busy))
            {
                _selected = (Backend)EditorGUILayout.EnumPopup("Backend", _selected);
                _qwenModel = EditorGUILayout.TextField("Qwen model", _qwenModel);
                _cpuOnly = EditorGUILayout.Toggle("Use CPU (slower)", _cpuOnly);
                EditorGUILayout.LabelField("CPU choice is saved only on this PC. First use downloads missing runtimes and model.", EditorStyles.wordWrappedMiniLabel);
                _geminiModel = EditorGUILayout.TextField("Gemini model ID", _geminiModel);
                _apiKey = EditorGUILayout.PasswordField("Gemini API key", _apiKey);
                bool keyAvailable = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")) ||
                    (bool?)_health?["gemini_key_configured"] == true;
                EditorGUILayout.LabelField(keyAvailable ? "Gemini key available for this session." : "Enter a key when using Gemini.", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Keys stay in memory; model choices are saved to Tools/local-vlm.json.", EditorStyles.wordWrappedMiniLabel);
                bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
                if (playing) EditorGUILayout.HelpBox("Exit Play mode before changing the backend or stopping the bridge.", MessageType.Info);
                using (new EditorGUI.DisabledScope(playing || EditorApplication.isCompiling))
                {
                    if (GUILayout.Button(_busy ? "Working…" : "Apply & Start", GUILayout.Height(30))) Apply();
                    using (new EditorGUI.DisabledScope(_health == null))
                        if (GUILayout.Button("Stop Bridge")) Stop();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Running", _health == null ? "Bridge not running" :
                (string)_health["provider"] + " / " + (string)_health["model"] + ((bool?)_health["cpu_only"] == true ? " (CPU)" : "") + ((bool?)_health["busy"] == true ? " (busy)" : ""));
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        }

        async void Apply()
        {
            _busy = true; _error = ""; _status = "Preparing backend…";
            var cancellation = _lifetime.Token;
            try
            {
                var savedConfig = VlmBackendSettings.Read(includeMachineSettings: false);
                var config = (JObject)savedConfig.DeepClone();
                string provider = _selected == Backend.Qwen ? "ollama" : "gemini";
                config["provider"] = provider;
                config["model"] = (_qwenModel ?? "").Trim();
                config["gemini_model"] = (_geminiModel ?? "").Trim();
                if (string.IsNullOrWhiteSpace(VlmBackendSettings.Model(config, provider)))
                    throw new InvalidOperationException("Enter the selected backend's model ID.");
                if (!string.IsNullOrWhiteSpace(_apiKey))
                    Environment.SetEnvironmentVariable("GEMINI_API_KEY", _apiKey.Trim(), EnvironmentVariableTarget.Process);
                string key = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
                var health = await VlmBackendSettings.GetAsync(VlmBackendSettings.Endpoint(config) + "/health", cancellation);
                if (provider == "gemini" && string.IsNullOrWhiteSpace(key) && (bool?)health?["gemini_key_configured"] != true)
                    throw new InvalidOperationException("Enter a Gemini API key for this session.");
                if (health != null && (bool?)health["busy"] == true)
                    throw new InvalidOperationException("Wait for the current request to finish before switching.");
                Directory.CreateDirectory(Path.GetDirectoryName(VlmBackendSettings.MachinePath));
                File.WriteAllText(VlmBackendSettings.MachinePath, new JObject { ["cpu_only"] = _cpuOnly }.ToString());
                if (_cpuOnly) config["num_gpu"] = 0;
                await PrepareDependencies(config, provider, cancellation);
                if (provider == "ollama") await EnsureOllama(config, cancellation);
                if (provider == "ollama" && (health == null || (string)health["provider"] != provider ||
                    (string)health["model"] != (string)config["model"] || ((bool?)health["cpu_only"] == true) != _cpuOnly))
                {
                    _status = "Checking actual local image inference. CPU and first loading can take a minute or more…"; Repaint();
                    string checkConfig = Path.Combine(VlmBackendSettings.Root, "UserSettings/vlm-check-config.json");
                    File.WriteAllText(checkConfig, config.ToString());
                    string python = ResolvePython(out string prefix);
                    await RunHidden(python, prefix + Quote(Path.Combine(VlmBackendSettings.Root, "Tools/verification_bridge.py")) +
                        " --config " + Quote(checkConfig) + " --check-local", "vlm-inference-check.log", cancellation);
                }
                if (health == null) health = await StartBridge(config, provider, cancellation);
                await VlmBackendSettings.ManageAsync(config, health, "/configure", new JObject {
                    ["provider"] = provider, ["config"] = config, ["api_key"] = key }, cancellation);
                savedConfig["provider"] = provider;
                savedConfig["model"] = config["model"];
                savedConfig["gemini_model"] = config["gemini_model"];
                File.WriteAllText(VlmBackendSettings.ConfigPath, savedConfig.ToString() + Environment.NewLine);
                _apiKey = "";
                _health = await VlmBackendSettings.GetAsync(VlmBackendSettings.Endpoint(config) + "/health", cancellation);
                _status = "Applied. Start a new procedure run; ProjectBackend authoring also uses this backend.";
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this != null) _error = e.Message; }
            finally { _busy = false; if (this != null) Repaint(); }
        }

        async void Stop()
        {
            _busy = true; _error = "";
            var cancellation = _lifetime.Token;
            try
            {
                var config = VlmBackendSettings.Read();
                var health = await VlmBackendSettings.GetAsync(VlmBackendSettings.Endpoint(config) + "/health", cancellation);
                if (health == null) { _health = null; _status = "Bridge is already stopped."; return; }
                await VlmBackendSettings.ManageAsync(config, health, "/shutdown", new JObject(), cancellation);
                double deadline = EditorApplication.timeSinceStartup + 5;
                while (await VlmBackendSettings.GetAsync(VlmBackendSettings.Endpoint(config) + "/health", cancellation) != null)
                {
                    if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException("Bridge is still stopping. Wait before starting it again.");
                    await Task.Delay(100, cancellation);
                }
                _health = null;
                _status = "Bridge stopped. Existing Ollama services may remain available for reuse.";
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this != null) _error = e.Message; }
            finally { _busy = false; if (this != null) Repaint(); }
        }

        static string FindOnPath(string filename)
        {
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string path;
                try { path = Path.Combine(folder.Trim('"'), filename); } catch (ArgumentException) { continue; }
                if (File.Exists(path) && path.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0) return path;
            }
            return null;
        }

        static string Quote(string value) => "\"" + value + "\"";

        async Task PrepareDependencies(JObject config, string provider, CancellationToken cancellation)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                throw new InvalidOperationException("Automatic preparation supports Windows x64. Use an x64 Windows PC for this demo.");
            if (!System.Text.RegularExpressions.Regex.IsMatch((string)config["model"] ?? "", @"^[a-zA-Z0-9][a-zA-Z0-9._:/-]*$"))
                throw new InvalidOperationException("Enter a valid Ollama model name.");
            _status = "Preparing Python, Ollama and model. Missing files download automatically; first use may download several GB…"; Repaint();
            string shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell/v1.0/powershell.exe");
            await RunHidden(shell, "-NoProfile -ExecutionPolicy Bypass -File " +
                Quote(Path.Combine(VlmBackendSettings.Root, "Tools/Setup-LocalVlm.ps1")) + " -Provider " + provider +
                " -Model " + Quote((string)config["model"]), "vlm-setup.log", cancellation);
        }

        static async Task RunHidden(string executable, string arguments, string logName, CancellationToken cancellation)
        {
            using var process = Process.Start(new ProcessStartInfo(executable, arguments) {
                WorkingDirectory = VlmBackendSettings.Root, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true });
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                while (!process.HasExited) await Task.Delay(200, cancellation);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill();
                throw;
            }
            string text = await output + Environment.NewLine + await errors;
            string logPath = Path.Combine(VlmBackendSettings.Root, "UserSettings", logName);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath, text);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(text.Substring(Math.Max(0, text.Length - 1600)) + "\nSee " + logPath);
        }

        static string ResolvePython(out string prefix)
        {
            prefix = "";
            string prepared = Path.Combine(VlmBackendSettings.Root, "UserSettings/local-vlm-python.json");
            if (File.Exists(prepared))
            {
                var runtime = JObject.Parse(File.ReadAllText(prepared));
                if (File.Exists((string)runtime["path"]))
                {
                    if (((JArray)runtime["arguments"])?.Count > 0) prefix = "-3 ";
                    return (string)runtime["path"];
                }
            }
            string python = Path.Combine(VlmBackendSettings.Root, "LocalVlm/runtime/python/python.exe");
            if (!File.Exists(python)) python = FindOnPath("python.exe");
            if (python == null) { python = FindOnPath("py.exe"); prefix = "-3 "; }
            return python ?? throw new InvalidOperationException("Python 3 is missing. Run Start-LocalVlm.cmd first.");
        }

        static async Task EnsureOllama(JObject config, CancellationToken cancellation)
        {
            string url = ((string)config["ollama_url"]).TrimEnd('/') + "/api/tags";
            var tags = await VlmBackendSettings.GetAsync(url, cancellation);
            if (tags == null)
            {
                string executable = Path.Combine(VlmBackendSettings.Root, "LocalVlm/runtime/ollama/ollama.exe");
                if (!File.Exists(executable)) executable = FindOnPath("ollama.exe");
                if (executable == null)
                    executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs/Ollama/ollama.exe");
                if (!File.Exists(executable)) throw new InvalidOperationException("Ollama is missing. Include the portable runtime or install Ollama.");
                var info = new ProcessStartInfo(executable, "serve") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                info.EnvironmentVariables["OLLAMA_HOST"] = new Uri((string)config["ollama_url"]).Authority;
                info.EnvironmentVariables["OLLAMA_NO_CLOUD"] = "1";
                info.EnvironmentVariables["OLLAMA_NUM_PARALLEL"] = "1";
                info.EnvironmentVariables.Remove("GEMINI_API_KEY");
                string models = Path.Combine(VlmBackendSettings.Root, "LocalVlm/models");
                if (Directory.Exists(models)) info.EnvironmentVariables["OLLAMA_MODELS"] = models;
                using var process = Process.Start(info);
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (tags == null && EditorApplication.timeSinceStartup < deadline)
                {
                    await Task.Delay(200, cancellation);
                    tags = await VlmBackendSettings.GetAsync(url, cancellation);
                    if (process.HasExited && tags == null) throw new InvalidOperationException("Ollama could not start.");
                }
            }
            if (tags == null || !((JArray)tags["models"]).Any(m => (string)m["name"] == (string)config["model"]))
                throw new InvalidOperationException("The selected Qwen model is not installed in the active Ollama store. No model was downloaded.");
        }

        static async Task<JObject> StartBridge(JObject config, string provider, CancellationToken cancellation)
        {
            string python = ResolvePython(out string prefix);
            string settings = Path.Combine(VlmBackendSettings.Root, "UserSettings");
            Directory.CreateDirectory(settings);
            string log = Path.Combine(settings, "vlm-bridge.log");
            string arguments = prefix + Quote(Path.Combine(VlmBackendSettings.Root, "Tools/verification_bridge.py")) +
                " --config " + Quote(VlmBackendSettings.ConfigPath) + " --provider " + provider +
                " --port " + (int)config["bridge_port"] + " --log-file " + Quote(log);
            using var process = Process.Start(new ProcessStartInfo(python, arguments) {
                WorkingDirectory = VlmBackendSettings.Root, UseShellExecute = false,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            double deadline = EditorApplication.timeSinceStartup + 15;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                var health = await VlmBackendSettings.GetAsync(VlmBackendSettings.Endpoint(config) + "/health", cancellation);
                if (health != null) return health;
                if (process.HasExited) throw new InvalidOperationException("Bridge could not start. See UserSettings/vlm-bridge.log.");
                await Task.Delay(150, cancellation);
            }
            throw new TimeoutException("Bridge did not start. See UserSettings/vlm-bridge.log.");
        }
    }
}
