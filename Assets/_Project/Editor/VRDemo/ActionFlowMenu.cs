using HomeCare.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace AHC.VR.EditorTools
{
    public static class ActionFlowMenu
    {
        [MenuItem("AHC/Open Action Requirements Demo", true)]
        public static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("AHC/Open Action Requirements Demo")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ActionRequirementsWindow.ScenePath);
        }
    }
}
