using UnityEngine;

namespace AHC.VR
{
    /// <summary>
    /// Simple stand-ins for the arm and supplies, so the guidance has something to sit on when
    /// testing in the Editor. On the headset the real objects are seen through passthrough,
    /// so these are off by default there.
    /// </summary>
    public static class PlaceholderProps
    {
        public static GameObject Build(Transform workspace, GuidanceData data)
        {
            var root = new GameObject("Placeholder props");
            root.transform.SetParent(workspace, false);

            Box(root.transform, "Table", new Vector3(0.05f, -0.01f, -0.02f), new Vector3(1.3f, 0.02f, 0.7f), "D9CBB3");
            var arm = Shape(root.transform, PrimitiveType.Capsule, "Arm", new Vector3(-0.05f, 0.045f, 0f), new Vector3(0.09f, 0.32f, 0.09f), "E0AE84");
            arm.transform.localRotation = Quaternion.Euler(0, 0, 90);

            foreach (var o in data.objects)
            {
                Vector3 p = o.position;
                switch (o.id)
                {
                    case "wound": Box(root.transform, "Dressing", p + new Vector3(0, 0.088f, 0), new Vector3(0.13f, 0.01f, 0.1f), "F3EFE6"); break;
                    case "soap": Shape(root.transform, PrimitiveType.Cylinder, "Soap", p + Vector3.up * 0.06f, new Vector3(0.06f, 0.06f, 0.06f), "F4F1EA"); break;
                    case "water": Shape(root.transform, PrimitiveType.Cylinder, "Water bowl", p + Vector3.up * 0.025f, new Vector3(0.16f, 0.025f, 0.16f), "DDEBEF"); break;
                    case "towel": Box(root.transform, "Towel", p + Vector3.up * 0.01f, new Vector3(0.2f, 0.02f, 0.12f), "F6F3EC"); break;
                    case "cream": Box(root.transform, "Cream tube", p + Vector3.up * 0.015f, new Vector3(0.15f, 0.03f, 0.04f), "FFFFFF"); break;
                    case "qtip": Box(root.transform, "Q-tip", p + Vector3.up * 0.005f, new Vector3(0.08f, 0.006f, 0.006f), "FFFFFF"); break;
                    case "pad": Box(root.transform, "Non-stick pad", p + Vector3.up * 0.003f, new Vector3(0.1f, 0.006f, 0.1f), "FFFFFF"); break;
                    case "gauze":
                        var g = Shape(root.transform, PrimitiveType.Cylinder, "Gauze roll", p + Vector3.up * 0.04f, new Vector3(0.08f, 0.05f, 0.08f), "FFFFFF");
                        g.transform.localRotation = Quaternion.Euler(0, 0, 90);
                        break;
                    case "tape": Shape(root.transform, PrimitiveType.Cylinder, "Tape roll", p + Vector3.up * 0.012f, new Vector3(0.07f, 0.012f, 0.07f), "FFFFFF"); break;
                }
            }
            return root;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, string color) =>
            Shape(parent, PrimitiveType.Cube, name, pos, size, color);

        static GameObject Shape(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, string color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            // Unlit so the stand-ins keep their colours under the template's blue sky lighting.
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null) r.material = new Material(unlit);
            r.material.color = Theme.Hex(color);
            return go;
        }
    }
}
