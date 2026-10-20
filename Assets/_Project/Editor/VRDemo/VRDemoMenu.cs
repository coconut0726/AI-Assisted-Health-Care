using AHC.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AHC.VR.EditorTools
{
    public static class VRDemoMenu
    {
        const string DataPath = "Assets/_Project/Data/BurnDressingDemo.json";

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

            Undo.RegisterCreatedObjectUndo(go, "Add AHC VR Demo");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[AHC] Added AHC VR Demo. Press Play (keyboard: → next, ← back, 1 patient, 2 nurse) or build to the headset (A next, B back).");
        }
    }
}
