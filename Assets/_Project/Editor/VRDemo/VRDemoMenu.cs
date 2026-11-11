using AHC.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AHC.VR.EditorTools
{
    public static class VRDemoMenu
    {
        const string DataPath = "Assets/_Project/Data/BurnDressingDemo.json";
        const string GhostPath = "Assets/_Project/Art/Ghost/Ghost.fbx";
        const string ScenePath = "Assets/_Project/Scenes/VRGuidanceDemo.unity";
        const string ProcedurePath = "Assets/_Project/Content/Procedures/burn_care_procedure.json";

        /// <summary>
        /// Makes the caregiver flow of VRGuidanceDemo real: adds <see cref="RealCareSession"/> (detection +
        /// after-step verification + session report) to the demo object and saves the scene. Safe to run again.
        /// </summary>
        [MenuItem("AHC/Set Up Real Care Flow (VRGuidanceDemo)")]
        public static void SetUpRealCareFlow()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = Object.FindAnyObjectByType<VRDemoController>();
            if (controller == null)
            {
                Debug.LogError("[AHC] No AHC VR Demo object in " + ScenePath + ". Run AHC ▸ Add VR Demo to Scene first.");
                return;
            }

            // GetComponent returns a fake null in the Editor, so ?? would not work here.
            if (!controller.TryGetComponent<RealCareSession>(out var care)) care = controller.gameObject.AddComponent<RealCareSession>();
            care.procedureJson = AssetDatabase.LoadAssetAtPath<TextAsset>(ProcedurePath);
            care.lineMaterial = HomeCare.Guidance.EditorTools.CareGuidanceSceneSetup.LineMaterial();
            if (care.procedureJson == null) Debug.LogError("[AHC] Missing " + ProcedurePath);
            EditorUtility.SetDirty(care);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("[AHC] Real care flow set up in " + ScenePath + ". Start the PC bridge, then Play or build to the headset.");
        }

        /// <summary>
        /// Gives the care guide a voice with Meta Voice SDK: a "Voice" child with Wit TTS + speaker + dictation,
        /// and <see cref="VoiceGuide"/> on the demo object. Needs a Wit configuration asset first
        /// (Meta ▸ Voice SDK ▸ Get Started, paste your Wit.ai app's Server Access Token). Safe to run again.
        /// </summary>
        const string GuideVoice = "wit$Rebecca";

        [MenuItem("AHC/Set Up Voice (Meta Voice SDK)")]
        public static string SetUpVoice()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = Object.FindAnyObjectByType<VRDemoController>();
            if (controller == null) return Log("No AHC VR Demo object in " + ScenePath + ".", true);

            var guids = AssetDatabase.FindAssets("t:WitConfiguration");
            if (guids.Length == 0)
                return Log("No Wit configuration yet. Open Meta ▸ Voice SDK ▸ Get Started, paste your Wit.ai app's " +
                           "Server Access Token, then run AHC ▸ Set Up Voice again.", true);
            var config = AssetDatabase.LoadAssetAtPath<Meta.WitAi.Data.Configuration.WitConfiguration>(AssetDatabase.GUIDToAssetPath(guids[0]));

            var voiceRoot = controller.transform.Find("Voice");
            if (voiceRoot == null)
            {
                voiceRoot = new GameObject("Voice").transform;
                voiceRoot.SetParent(controller.transform, false);
            }
            var go = voiceRoot.gameObject;
            var tts = go.TryGetComponent<Meta.WitAi.TTS.Integrations.TTSWit>(out var t) ? t : go.AddComponent<Meta.WitAi.TTS.Integrations.TTSWit>();
            tts.Configuration = config;
            // The guide's voice: Wit's "Rebecca" (female, en_US). Other female voices: Railey, Remi, Rubie, Rosie (en_CA).
            tts.SetVoiceSettings(new[]
            {
                new Meta.WitAi.TTS.Integrations.TTSWitVoiceSettings { SettingsId = "Guide", voice = GuideVoice },
            });
            EditorUtility.SetDirty(tts);

            // The speaker plays through an AudioSource on this object; without one it loads speech but stays silent.
            var source = go.TryGetComponent<AudioSource>(out var a) ? a : go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // the guide's voice, heard the same wherever the user looks
            EditorUtility.SetDirty(source);

            var speaker = go.TryGetComponent<Meta.WitAi.TTS.Utilities.TTSSpeaker>(out var s) ? s : go.AddComponent<Meta.WitAi.TTS.Utilities.TTSSpeaker>();
            var speakerSo = new SerializedObject(speaker); // TTSService is read-only; it also falls back to this object
            var service = speakerSo.FindProperty("_ttsService");
            if (service != null) { service.objectReferenceValue = tts; speakerSo.ApplyModifiedPropertiesWithoutUndo(); }
            // The speaker uses its own preset id (or its own custom settings, "Charlie" by default), not the service default.
            speaker.VoiceID = "Guide";
            speaker.customWitVoiceSettings = new Meta.WitAi.TTS.Integrations.TTSWitVoiceSettings { SettingsId = "Guide", voice = GuideVoice };
            EditorUtility.SetDirty(speaker);

            var dictation = go.TryGetComponent<Oculus.Voice.Dictation.AppDictationExperience>(out var d) ? d : go.AddComponent<Oculus.Voice.Dictation.AppDictationExperience>();
            var so = new SerializedObject(dictation);
            var cfg = so.FindProperty("runtimeConfiguration.witConfiguration");
            if (cfg != null) { cfg.objectReferenceValue = config; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorUtility.SetDirty(dictation);

            if (!controller.TryGetComponent<VoiceGuide>(out var guide)) guide = controller.gameObject.AddComponent<VoiceGuide>();
            guide.speaker = speaker;
            guide.dictation = dictation;
            EditorUtility.SetDirty(guide);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = go;
            return Log("Voice set up with " + AssetDatabase.GetAssetPath(config) +
                       (cfg == null ? " (set the dictation's Wit configuration by hand: field not found)." : "."), cfg == null);
        }

        static string Log(string message, bool problem)
        {
            if (problem) Debug.LogWarning("[AHC] " + message); else Debug.Log("[AHC] " + message);
            return message;
        }

        const string WoundCasePath = "Assets/_Project/Content/Procedures/wound_dressing_procedure.json";

        [MenuItem("AHC/Care Case/Simple Wound Dressing (prop test)")]
        static void UseWoundCase() => UseCase(WoundCasePath);

        [MenuItem("AHC/Care Case/Burn Dressing")]
        static void UseBurnCase() => UseCase(ProcedurePath);

        /// <summary>Points the real care flow in VRGuidanceDemo at another procedure file and saves the scene.</summary>
        public static string UseCase(string procedurePath)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var care = Object.FindAnyObjectByType<RealCareSession>();
            if (care == null) return "No RealCareSession in " + ScenePath + ". Run AHC ▸ Set Up Real Care Flow first.";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(procedurePath);
            if (json == null) return "Missing " + procedurePath;
            care.procedureJson = json;
            EditorUtility.SetDirty(care);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            string result = "Care case: " + procedurePath;
            Debug.Log("[AHC] " + result);
            return result;
        }

        const string ApkPath = "build/full_demo.apk";

        /// <summary>
        /// Builds an APK with only VRGuidanceDemo, without changing the project's Build Settings.
        /// Returns a one-line result.
        /// </summary>
        [MenuItem("AHC/Build Full Demo APK")]
        public static string BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                return "Switch the build target to Android first.";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            string result = $"{s.result}: {ApkPath} ({s.totalSize / (1024f * 1024f):F1} MB, {s.totalTime.TotalSeconds:F0} s, {s.totalErrors} errors)";
            Debug.Log("[AHC] Full demo APK " + result);
            return result;
        }

        [MenuItem("AHC/Add VR Demo to Scene")]
        static void AddToScene()
        {
            var existing = Object.FindAnyObjectByType<VRDemoController>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[AHC] The scene already has an AHC VR Demo object.", existing);
                return;
            }

            if (Object.FindAnyObjectByType<OVRCameraRig>() == null)
                Debug.LogWarning("[AHC] No OVRCameraRig in this scene. Add the Camera Rig building block (and passthrough) first, or use the scene you build to the headset.");

            var go = new GameObject("AHC VR Demo");
            var controller = go.AddComponent<VRDemoController>();
            controller.demoJson = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (controller.demoJson == null) Debug.LogError("[AHC] Could not find " + DataPath);
            controller.ghostModel = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPath);

            Undo.RegisterCreatedObjectUndo(go, "Add AHC VR Demo");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[AHC] Added AHC VR Demo. Press Play (keyboard: → next, ← back, 1 patient, 2 nurse) or build to the headset (A next, B back).");
        }
    }
}
