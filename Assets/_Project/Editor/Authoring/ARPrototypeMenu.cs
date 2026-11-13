using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using System.Linq;

namespace HomeCare.Authoring
{
    public sealed class ARPrototypeMenu : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        [MenuItem("Tools/Home Care/Open AR Prototype Scene")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/HomeCareAR.unity");
        }

        // Meta's build processor reads this flag when preparing camera capability metadata.
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == "Assets/_Project/Scenes/HomeCareAR.unity")) return;
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config == null) throw new BuildFailedException("Meta project configuration is unavailable.");
            config.isPassthroughCameraAccessEnabled = true;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.enableNSCConfig = true;
            config.securityXmlPath = "Assets/_Project/Config/verification_network_security.xml";
            OVRProjectConfig.CommitProjectConfig(config);
        }
    }
}
