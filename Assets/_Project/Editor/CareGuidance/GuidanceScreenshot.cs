using System;
using System.Collections.Generic;
using System.IO;
using AHC.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace HomeCare.Guidance.EditorTools
{
    /// <summary>
    /// Sharp screenshots of the detection guidance in Play mode: a temporary camera frames the markers
    /// (outlines, pills, card, chip, arrow) and renders them at high resolution over the Figma passthrough
    /// placeholder colour. Panels, the ghost and the virtual-item highlights are hidden for the shot only.
    /// </summary>
    public static class GuidanceScreenshot
    {
        const string Folder = "Reports/visual_guidance";

        [MenuItem("Tools/Home Care/Capture Guidance Screenshot")]
        static void CaptureMenu() => Debug.Log("[HomeCare] " + Capture(Path.Combine(Folder, "guidance_" + DateTime.Now.ToString("HHmmss") + ".png")));

        public static string Capture(string path, int width = 2560, int height = 1600, float margin = 1.15f)
        {
            if (!Application.isPlaying) return "Enter Play mode first.";
            var root = GameObject.Find("Detected Objects");
            if (root == null || root.transform.childCount == 0) return "No detection markers to capture.";
            if (!TryBounds(root.transform, out var bounds)) return "No visible markers.";

            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
            var head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : null;
            Vector3 eye = head != null ? head.position : bounds.center - Vector3.forward;

            var hidden = new List<GameObject>();
            void Hide(GameObject go) { if (go != null && go.activeSelf) { go.SetActive(false); hidden.Add(go); } }
            var ghost = UnityEngine.Object.FindAnyObjectByType<GhostCompanion>();
            if (ghost != null) Hide(ghost.gameObject);
            foreach (var name in new[] { "Care Panel", "Detection Test Panel", "Item Highlights" }) Hide(GameObject.Find(name));

            var camGo = new GameObject("Guidance Screenshot Camera");
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            Texture2D image = null;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Theme.Hex("E6DCC8"); // Figma "Passthrough (placeholder)"
                cam.fieldOfView = 34f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                cam.allowMSAA = true;
                cam.targetTexture = rt;

                // Look at the markers from where the user stands (billboards face the user), close enough to fill the frame.
                var dir = bounds.center - eye;
                dir = dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;
                var rotation = Quaternion.LookRotation(dir, Vector3.up);
                float halfV = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float halfH = Mathf.Atan(Mathf.Tan(halfV) * width / height);
                // Fit the bounds' extent as seen from this direction.
                var local = Quaternion.Inverse(rotation);
                float ex = 0, ey = 0, ez = 0;
                foreach (var corner in Corners(bounds))
                {
                    var p = local * (corner - bounds.center);
                    ex = Mathf.Max(ex, Mathf.Abs(p.x)); ey = Mathf.Max(ey, Mathf.Abs(p.y)); ez = Mathf.Max(ez, Mathf.Abs(p.z));
                }
                float distance = Mathf.Max(ex * margin / Mathf.Tan(halfH), ey * margin / Mathf.Tan(halfV)) + ez;
                camGo.transform.SetPositionAndRotation(bounds.center - dir * distance, rotation);

                // World-space canvases need a fresh layout before rendering from a new camera.
                Canvas.ForceUpdateCanvases();
                cam.Render();

                var previous = RenderTexture.active;
                var resolved = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rt, resolved);
                RenderTexture.active = resolved;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(resolved);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllBytes(path, image.EncodeToPNG());
                return "Saved " + Path.GetFullPath(path);
            }
            finally
            {
                foreach (var go in hidden) if (go != null) go.SetActive(true);
                if (image != null) UnityEngine.Object.Destroy(image);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(camGo);
            }
        }

        static bool TryBounds(Transform root, out Bounds bounds)
        {
            var b = default(Bounds);
            bool any = false;
            void Add(Vector3 p) { if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.gameObject.activeInHierarchy) { Add(r.bounds.min); Add(r.bounds.max); }
            var corners = new Vector3[4];
            foreach (var g in root.GetComponentsInChildren<Graphic>())
            {
                if (!g.enabled || !g.gameObject.activeInHierarchy) continue;
                g.rectTransform.GetWorldCorners(corners);
                foreach (var c in corners) Add(c);
            }
            bounds = b;
            return any;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
        }
    }
}
