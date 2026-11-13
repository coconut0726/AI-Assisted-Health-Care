using UnityEngine;

namespace HomeCare.Guidance
{
    /// <summary>
    /// An arrow drawn from an item to where it should go (for example the burn). Styled like the
    /// Figma XR "Movement path": a thick sage arc with a filled arrowhead.
    /// </summary>
    public sealed class GuidanceArrow : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color_ = Shader.PropertyToID("_Color");

        LineRenderer _shaft, _head;
        Transform _from, _to;
        CareItem _fromItem, _toItem;
        Vector3? _fromPoint, _toPoint;

        public static GuidanceArrow Create(Transform parent, Material material)
        {
            var go = new GameObject("Guidance Arrow");
            go.transform.SetParent(parent, false);
            var arrow = go.AddComponent<GuidanceArrow>();
            int ui = LayerMask.NameToLayer("UI"); // left out of Editor camera captures
            if (ui >= 0) go.layer = ui;
            arrow._shaft = Line(go.transform, "Shaft", material, 24, 0.007f);
            // Filled triangle: a two-point line whose width tapers from the base to the tip.
            arrow._head = Line(go.transform, "Head", material, 2, 0.032f);
            arrow._head.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            arrow._head.numCapVertices = 0;
            arrow.Hide();
            return arrow;
        }

        static LineRenderer Line(Transform parent, string name, Material material, int points, float width)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) line.sharedMaterial = material;
            var c = AHC.VR.Theme.Primary;
            line.startColor = line.endColor = c;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, c);
            block.SetColor(Color_, c);
            line.SetPropertyBlock(block);
            return line;
        }

        public void Show(CareItem from, CareItem to)
        {
            _fromItem = from;
            _toItem = to;
            _fromPoint = _toPoint = null;
            _shaft.enabled = _head.enabled = from != null && to != null && from != to;
        }

        /// <summary>Arrow between two fixed points in the room (for example two detected real objects).</summary>
        public void Show(Vector3 from, Vector3 to)
        {
            _fromItem = _toItem = null;
            _fromPoint = from;
            _toPoint = to;
            _shaft.enabled = _head.enabled = (to - from).sqrMagnitude > 1e-4f;
        }

        public void Hide()
        {
            _fromItem = _toItem = null;
            _fromPoint = _toPoint = null;
            _shaft.enabled = _head.enabled = false;
        }

        public bool Visible => _shaft.enabled;

        void LateUpdate()
        {
            if (!_shaft.enabled) return;
            Vector3 a, b;
            if (_fromPoint is Vector3 fromPoint && _toPoint is Vector3 toPoint) { a = fromPoint; b = toPoint + Vector3.up * 0.03f; }
            else if (_fromItem != null && _toItem != null) { a = _fromItem.WorldCenter; b = _toItem.WorldCenter + Vector3.up * 0.03f; }
            else { Hide(); return; }
            float length = Vector3.Distance(a, b);
            // A gentle arc, so the arrow stays visible above the table.
            Vector3 lift = Vector3.up * Mathf.Min(0.12f, length * 0.35f);
            int n = _shaft.positionCount;
            Vector3 prev = a;
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Lerp(0.12f, 0.92f, i / (float)(n - 1));
                Vector3 p = Vector3.Lerp(a, b, t) + lift * (4f * t * (1 - t));
                _shaft.SetPosition(i, p);
                if (i == n - 2) prev = p;
            }
            Vector3 tip = _shaft.GetPosition(n - 1);
            Vector3 dir = (tip - prev).normalized;
            const float size = 0.03f;
            _head.enabled = true;
            _head.SetPosition(0, tip);
            _head.SetPosition(1, tip + dir * size);
        }
    }
}
