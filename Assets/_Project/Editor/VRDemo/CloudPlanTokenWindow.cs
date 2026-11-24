using System;
using System.Diagnostics;
using System.IO;
using HomeCare.Procedures.Actions;
using UnityEditor;
using UnityEngine;

namespace AHC.VR.EditorTools
{
    /// <summary>
    /// Stores the care-plan server's access token in a git-ignored Resources file so the headset build can send it.
    /// The token is the Secret Manager secret "care-plan-token" (see CarePlanServer/README.md).
    /// </summary>
    public sealed class CloudPlanTokenWindow : EditorWindow
    {
        const string Folder = "Assets/_Project/Config/Resources";
        static string FilePath => Folder + "/" + CarePlanCloud.TokenResource + ".txt";
        string _token = "";
        string _message = "";

        [MenuItem("AHC/Cloud Plan/Set Access Token…")]
        static void Open()
        {
            var window = GetWindow<CloudPlanTokenWindow>(true, "Care-plan cloud token");
            window.minSize = new Vector2(460, 170);
            window._message = File.Exists(FilePath) ? "A token is saved." : "No token saved yet.";
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("The headset sends this token to the care-plan server. It is saved in " + FilePath +
                                    ", which git ignores. Get it with: gcloud secrets versions access latest --secret=care-plan-token", MessageType.Info);
            _token = EditorGUILayout.PasswordField("Access token", _token);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Read with gcloud")) ReadWithGcloud();
                GUI.enabled = !string.IsNullOrWhiteSpace(_token);
                if (GUILayout.Button("Save")) Save();
                GUI.enabled = true;
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedMiniLabel);
        }

        void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, _token.Trim());
            AssetDatabase.ImportAsset(FilePath);
            _token = "";
            _message = "Saved. Rebuild the APK so the headset gets it.";
        }

        void ReadWithGcloud()
        {
            string gcloud = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Cloud SDK", "google-cloud-sdk", "bin", "gcloud.cmd");
            if (!File.Exists(gcloud)) gcloud = "gcloud";
            try
            {
                var info = new ProcessStartInfo(gcloud, "secrets versions access latest --secret=care-plan-token")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var process = Process.Start(info);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(30000);
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)) { _token = output.Trim(); _message = "Read from Secret Manager. Select Save."; }
                else _message = "gcloud failed: " + error.Trim();
            }
            catch (Exception e) { _message = "Could not run gcloud: " + e.Message; }
        }
    }
}
