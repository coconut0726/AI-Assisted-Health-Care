using UnityEngine;

namespace HomeCare.Guidance
{
    /// <summary>An arrow drawn from the held item to where it should go (for example the burn).</summary>
    public sealed class GuidanceArrow : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color_ = Shader.PropertyToID("_Color");

        LineRenderer _shaft, _head;
        Transform _from, _to;
        CareItem _fromItem, _toItem;

        public static GuidanceArrow Create(Transform parent, Material material)
        {
            var go = new GameObject("Guidance Arrow");
            go.transform.SetParent(parent, false);
            var arrow = go.AddComponent<GuidanceArrow>();
            arrow._shaft = Line(go.transform, "Shaft", material, 20);
            arrow._head = Line(go.transform, "Head", material, 3);
            arrow.Hide();
            return arrow;
        }

        static LineRenderer Line(Transform parent, string name, Material material, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.widthMultiplier = 0.006f;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) line.sharedMaterial = material;
            var c = new Color(1f, 0.72f, 0.2f);
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
            _shaft.enabled = _head.enabled = from != null && to != null && from != to;
        }

        public void Hide()
        {
            _fromItem = _toItem = null;
            _shaft.enabled = _head.enabled = false;
        }

        public bool Visible => _shaft.enabled;

        void LateUpdate()
        {
            if (!_shaft.enabled) return;
            if (_fromItem == null || _toItem == null) { Hide(); return; }
            Vector3 a = _fromItem.WorldCenter;
            Vector3 b = _toItem.WorldCenter + Vector3.up * 0.03f;
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
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            float size = 0.025f;
            _head.enabled = true;
            _head.SetPosition(0, tip - dir * size + side * size * 0.6f);
            _head.SetPosition(1, tip);
            _head.SetPosition(2, tip - dir * size - side * size * 0.6f);
        }
    }
}
