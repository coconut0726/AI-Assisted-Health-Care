using TMPro;
using UnityEngine;

namespace HomeCare.Guidance
{
    public enum HighlightState { None, Mentioned, Held, Target }

    /// <summary>A wireframe box and a name tag drawn around a <see cref="CareItem"/>; follows it as it moves.</summary>
    public sealed class ItemHighlight : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color_ = Shader.PropertyToID("_Color");
        static readonly Color Mentioned = new(0.25f, 0.85f, 1f);
        static readonly Color Held = new(0.3f, 1f, 0.45f);
        static readonly Color Target = new(1f, 0.72f, 0.2f);

        public CareItem Item { get; private set; }
        public HighlightState State { get; private set; }

        LineRenderer _line;
        TextMeshPro _label;
        Transform _head;
        MaterialPropertyBlock _block;
        readonly Vector3[] _points = new Vector3[16];

        public static ItemHighlight Create(CareItem item, Transform parent, Material lineMaterial, Transform head)
        {
            var go = new GameObject("Highlight " + item.Name);
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<ItemHighlight>();
            h.Item = item;
            h._head = head;
            h._block = new MaterialPropertyBlock();
            h._line = go.AddComponent<LineRenderer>();
            h._line.useWorldSpace = true;
            h._line.positionCount = 16;
            h._line.widthMultiplier = 0.004f;
            h._line.numCapVertices = 2;
            h._line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            h._line.receiveShadows = false;
            if (lineMaterial != null) h._line.sharedMaterial = lineMaterial;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            h._label = labelGo.AddComponent<TextMeshPro>();
            h._label.font = TMP_Settings.defaultFontAsset;
            h._label.fontSize = 0.22f;
            h._label.alignment = TextAlignmentOptions.Bottom;
            h._label.rectTransform.sizeDelta = new Vector2(0.4f, 0.06f);
            h._label.outlineWidth = 0.25f;
            h._label.outlineColor = new Color32(0, 0, 0, 220);
            h._label.richText = false;
            h._label.text = item.Name;
            h.SetState(HighlightState.None);
            return h;
        }

        public void SetState(HighlightState state)
        {
            State = state;
            bool visible = state != HighlightState.None && Item != null;
            _line.enabled = visible;
            _label.gameObject.SetActive(visible);
            if (!visible) return;
            var c = state == HighlightState.Held ? Held : state == HighlightState.Target ? Target : Mentioned;
            _line.startColor = _line.endColor = c;
            _block.SetColor(BaseColor, c);
            _block.SetColor(Color_, c);
            _line.SetPropertyBlock(_block);
            _label.color = c;
            _line.widthMultiplier = state == HighlightState.Mentioned ? 0.003f : 0.005f;
        }

        void LateUpdate()
        {
            if (Item == null) { Destroy(gameObject); return; }
            if (State == HighlightState.None) return;

            var b = Item.LocalBounds;
            var ext = b.extents + Vector3.one * 0.008f / Mathf.Max(0.0001f, Item.transform.lossyScale.x);
            Vector3 C(float x, float y, float z) => Item.transform.TransformPoint(b.center + Vector3.Scale(ext, new Vector3(x, y, z)));
            Vector3 b0 = C(-1, -1, -1), b1 = C(1, -1, -1), b2 = C(1, -1, 1), b3 = C(-1, -1, 1);
            Vector3 t0 = C(-1, 1, -1), t1 = C(1, 1, -1), t2 = C(1, 1, 1), t3 = C(-1, 1, 1);
            // One strip that walks all 12 edges (some edges are traced twice).
            _points[0] = b0; _points[1] = b1; _points[2] = b2; _points[3] = b3; _points[4] = b0;
            _points[5] = t0; _points[6] = t1; _points[7] = b1; _points[8] = t1; _points[9] = t2;
            _points[10] = b2; _points[11] = t2; _points[12] = t3; _points[13] = b3; _points[14] = t3; _points[15] = t0;
            _line.SetPositions(_points);

            if (State == HighlightState.Target)
            {
                float pulse = 0.004f + 0.003f * (0.5f + 0.5f * Mathf.Sin(Time.time * 6f));
                _line.widthMultiplier = pulse;
            }

            float top = Mathf.Max(t0.y, t1.y, t2.y, t3.y, b0.y, b1.y, b2.y, b3.y);
            var labelPos = new Vector3(Item.WorldCenter.x, top + 0.015f, Item.WorldCenter.z);
            _label.transform.position = labelPos;
            if (_head != null)
            {
                var away = labelPos - _head.position;
                if (away.sqrMagnitude > 1e-4f) _label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }
    }
}
