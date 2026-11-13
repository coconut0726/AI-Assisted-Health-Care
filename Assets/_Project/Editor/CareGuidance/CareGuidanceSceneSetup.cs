using System.Linq;
using System.Text;
using HomeCare.Guidance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HomeCare.Guidance.EditorTools
{
    public static class CareGuidanceSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/VRGuidanceDemo_V2.unity";

        const string ProcedurePath = "Assets/_Project/Content/Procedures/burn_care_procedure.json";
        const string GhostPath = "Assets/_Project/Art/Ghost/Ghost.fbx";
        const string MaterialFolder = "Assets/_Project/Art/CareItems";

        struct ItemSpec
        {
            public string id, name, color;
            public string[] keywords;
            public PrimitiveType shape;
            public Vector3 position, scale, euler;
            public bool grabbable;
        }

        // Stand-in supplies for the burn-care procedure, laid out on a tray (metres; +z away from the user).
        static readonly ItemSpec[] Items =
        {
            Spec("burn", "Burn", "D9534F", PrimitiveType.Cylinder, new(-0.08f, 0.071f, -0.07f), new(0.06f, 0.002f, 0.045f), default, false, "burn", "burned skin", "blister"),
            Spec("dressing", "Old dressing", "EFE6C8", PrimitiveType.Cube, new(-0.08f, 0.076f, -0.07f), new(0.085f, 0.006f, 0.065f), default, true, "old dressing", "dressing"),
            Spec("soap", "Soap", "F4F1EA", PrimitiveType.Cube, new(-0.25f, 0.023f, 0.07f), new(0.07f, 0.035f, 0.05f), default, true, "soap"),
            Spec("water", "Clean water", "7FB8D6", PrimitiveType.Cylinder, new(-0.13f, 0.03f, 0.07f), new(0.13f, 0.025f, 0.13f), default, true, "water"),
            Spec("towel", "Clean towel", "E8DCC2", PrimitiveType.Cube, new(0.02f, 0.015f, 0.07f), new(0.15f, 0.02f, 0.1f), default, true, "towel"),
            Spec("gloves", "Gloves", "9FD3F5", PrimitiveType.Cube, new(0.17f, 0.015f, 0.07f), new(0.1f, 0.02f, 0.13f), default, true, "glove"),
            Spec("swab", "Cotton swab", "FFFFFF", PrimitiveType.Cylinder, new(-0.22f, 0.009f, 0.17f), new(0.008f, 0.04f, 0.008f), new(0, 0, 90), true, "cotton swab", "swab"),
            Spec("ointment", "Burn ointment", "F7C873", PrimitiveType.Cube, new(-0.12f, 0.02f, 0.17f), new(0.11f, 0.03f, 0.035f), default, true, "ointment"),
            Spec("pad", "Non-stick pad", "FFFFFF", PrimitiveType.Cube, new(0.0f, 0.008f, 0.17f), new(0.08f, 0.006f, 0.08f), default, true, "non-stick pad", "pad"),
            Spec("gauze", "Gauze roll", "FAFAFA", PrimitiveType.Cylinder, new(0.1f, 0.035f, 0.17f), new(0.06f, 0.03f, 0.06f), new(0, 0, 90), true, "gauze"),
            Spec("tape", "Medical tape", "B9C2CC", PrimitiveType.Cylinder, new(0.2f, 0.015f, 0.17f), new(0.05f, 0.01f, 0.05f), default, true, "medical tape", "tape"),
        };

        static ItemSpec Spec(string id, string name, string color, PrimitiveType shape, Vector3 position, Vector3 scale,
            Vector3 euler, bool grabbable, params string[] keywords) => new()
        {
            id = id, name = name, color = color, shape = shape, position = position, scale = scale, euler = euler,
            grabbable = grabbable, keywords = keywords,
        };

        /// <summary>
        /// Turns the copied demo scene into the V2 scene: removes the demo-only and template UI,
        /// turns off the controller rig from the MR template, and adds the hands-only care guidance
        /// with stand-in supplies. Safe to run again (it replaces what it created before).
        /// </summary>
        [MenuItem("Tools/Home Care/Set Up Care Guidance V2 Scene")]
        public static void SetUpV2Scene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath);

            foreach (var root in scene.GetRootGameObjects())
            {
                switch (root.name)
                {
                    case "AHC VR Demo":          // demo-only UI: role select, simulated nurse recording, fake "found" supplies
                    case "UI":                   // MR template UI: coaching card, tutorial player, tooltip, hand menu
                    case "Care Guidance":        // previous run of this setup
                    case "Care Items":
                        Object.DestroyImmediate(root);
                        break;
                    case "MR Interaction Setup": // template XRI controller rig; V2 uses the Meta camera rig and hand tracking only
                        root.SetActive(false);
                        break;
                }
            }

            var lineMaterial = LineMaterial();
            var itemsRoot = BuildCareItems(out var vocabulary);

            var guidance = new GameObject("Care Guidance");
            var controller = guidance.AddComponent<CareGuidanceController>(); // adds camera, hand tracking and grabber
            controller.procedureJson = AssetDatabase.LoadAssetAtPath<TextAsset>(ProcedurePath);
            controller.ghostModel = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPath);
            controller.itemsRoot = itemsRoot;
            controller.vocabulary = vocabulary;
            controller.lineMaterial = lineMaterial;
            if (controller.procedureJson == null) Debug.LogError("[HomeCare] Missing " + ProcedurePath);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = guidance;
            Debug.Log("[HomeCare] V2 scene set up: " + ScenePath);
        }

        /// <summary>Unlit material for highlight boxes, arrows and detection markers.</summary>
        public static Material LineMaterial() =>
            MaterialAsset("CareHighlight", Shader.Find("Universal Render Pipeline/Unlit"), Color.white);

        /// <summary>Creates "Care Items": a tray with a forearm, a burn and stand-in burn-care supplies.</summary>
        public static Transform BuildCareItems(out ItemVocabulary[] vocabulary)
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var itemsRoot = new GameObject("Care Items").transform;
            var tray = Prop(itemsRoot, "Tray", PrimitiveType.Cube, new Vector3(0, 0, 0.03f), new Vector3(0.62f, 0.01f, 0.4f), default,
                MaterialAsset("Tray", unlit, Hex("5B6B7A")));
            Object.DestroyImmediate(tray.GetComponent<Collider>());
            Prop(itemsRoot, "Forearm", PrimitiveType.Capsule, new Vector3(-0.1f, 0.035f, -0.07f), new Vector3(0.07f, 0.16f, 0.07f),
                new Vector3(0, 0, 90), MaterialAsset("Skin", unlit, Hex("E0AE84")));

            vocabulary = new ItemVocabulary[Items.Length];
            for (int i = 0; i < Items.Length; i++)
            {
                var spec = Items[i];
                var go = Prop(itemsRoot, spec.name, spec.shape, spec.position, spec.scale, spec.euler,
                    MaterialAsset("Item_" + spec.id, unlit, Hex(spec.color)));
                var item = go.AddComponent<CareItem>();
                item.itemId = spec.id;
                item.displayName = spec.name;
                item.keywords = spec.keywords;
                item.grabbable = spec.grabbable;
                vocabulary[i] = new ItemVocabulary { itemId = spec.id, displayName = spec.name, keywords = spec.keywords };
            }
            return itemsRoot;
        }

        static GameObject Prop(Transform parent, string name, PrimitiveType shape, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Material MaterialAsset(string name, Shader shader, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "CareItems");
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;

        public static string DumpHierarchy(int maxDepth = 2)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var sb = new StringBuilder(scene.path + (scene.isDirty ? " (unsaved)" : "") + "\n");
            void Walk(Transform t, int depth)
            {
                if (depth > maxDepth) return;
                var comps = string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name));
                sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]")
                  .Append("  {").Append(comps).AppendLine("}");
                foreach (Transform child in t) Walk(child, depth + 1);
            }
            foreach (var root in scene.GetRootGameObjects()) Walk(root.transform, 0);
            return sb.ToString();
        }
    }
}
