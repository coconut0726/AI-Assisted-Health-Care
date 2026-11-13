using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HomeCare.Guidance.EditorTools
{
    /// <summary>Builds the stand-alone object detection test scene. Safe to run again.</summary>
    public static class DetectionTestSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/3DGuidanceTestScence.unity";
        const string CameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        [MenuItem("Tools/Home Care/Set Up 3D Guidance Test Scene")]
        public static void SetUp()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            foreach (var root in scene.GetRootGameObjects())
                if (root.name is "Detection Test" or "Care Items" || (root.name == "Main Camera" && root.GetComponentInChildren<OVRCameraRig>() == null))
                    Object.DestroyImmediate(root);

            // Meta camera rig: passthrough, hand tracking and the passthrough camera need it.
            if (Object.FindAnyObjectByType<OVRCameraRig>() == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefab);
                if (prefab == null) { Debug.LogError("[HomeCare] Missing " + CameraRigPrefab); return; }
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                rig.name = "OVRCameraRig";
            }

            var items = CareGuidanceSceneSetup.BuildCareItems(out _);
            items.name = "Care Items";
            var test = new GameObject("Detection Test");
            var rigComponent = test.AddComponent<ObjectDetectionTestRig>(); // adds camera, hand tracking and the detector
            rigComponent.lineMaterial = CareGuidanceSceneSetup.LineMaterial();
            rigComponent.standInItems = items;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = test;
            Debug.Log("[HomeCare] 3D guidance test scene set up: " + ScenePath);
        }

        public const string ApkPath = "test_detection.apk";

        /// <summary>
        /// Builds an APK with only the test scene, without changing the project's Build Settings
        /// (the V2 scene stays the default build). Returns a one-line result.
        /// </summary>
        [MenuItem("Tools/Home Care/Build 3D Guidance Test APK")]
        public static string BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                return "Switch the build target to Android first.";
            var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            string result = $"{s.result}: {ApkPath} ({s.totalSize / (1024f * 1024f):F1} MB, {s.totalTime.TotalSeconds:F0} s, {s.totalErrors} errors)";
            Debug.Log("[HomeCare] Test APK " + result);
            return result;
        }
    }
}
