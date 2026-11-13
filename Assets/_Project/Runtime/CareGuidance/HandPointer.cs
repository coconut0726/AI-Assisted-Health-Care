using UnityEngine;

namespace HomeCare.Guidance
{
    /// <summary>
    /// Visual feedback and far interaction for tracked hands:
    /// a dot on each index fingertip (so you can see the hands are tracked), and a ray from the
    /// hand (aimed from the shoulder through the index knuckle, like the Quest system menus).
    /// When the ray is on a panel button, pinch to press it. Up close, touching the button works too.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public sealed class HandPointer : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color_ = Shader.PropertyToID("_Color");
        static readonly Color Idle = new(1f, 1f, 1f, 0.6f);
        static readonly Color Active = new(0.35f, 0.85f, 1f);

        public Transform head;
        public Material material;
        [Tooltip("Hide the ray when the hand is this close to the button it points at; touch it instead (metres).")]
        public float pokeRange = 0.1f;
        public float idleRayLength = 0.25f;

        sealed class Visual
        {
            public LineRenderer ray;
            public Transform tip, hit;
            public MaterialPropertyBlock block = new();
        }

        Visual _left, _right;
        HandGrabber _grabber;

        // Built on first use, once the controller has assigned the head and material.
        void EnsureBuilt()
        {
            if (_left != null) return;
            _grabber = GetComponent<HandGrabber>();
            _left = Build("Left");
            _right = Build("Right");
        }

        Visual Build(string side)
        {
            var root = new GameObject(side + " Hand Pointer").transform;
            root.SetParent(transform, false);
            var v = new Visual
            {
                tip = Dot(root, "Fingertip", 0.012f),
                hit = Dot(root, "Ray hit", 0.016f),
            };
            v.ray = new GameObject("Ray").AddComponent<LineRenderer>();
            v.ray.transform.SetParent(root, false);
            v.ray.useWorldSpace = true;
            v.ray.positionCount = 2;
            v.ray.widthCurve = AnimationCurve.Linear(0, 0.004f, 1, 0.0015f);
            v.ray.numCapVertices = 2;
            v.ray.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) v.ray.sharedMaterial = material;
            return v;
        }

        Transform Dot(Transform parent, string name, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (material != null) r.sharedMaterial = material;
            return go.transform;
        }

        void Paint(Visual v, Color c)
        {
            v.block.SetColor(BaseColor, c);
            v.block.SetColor(Color_, c);
            v.ray.startColor = v.ray.endColor = c;
            v.ray.SetPropertyBlock(v.block);
            v.tip.GetComponent<Renderer>().SetPropertyBlock(v.block);
            v.hit.GetComponent<Renderer>().SetPropertyBlock(v.block);
        }

        void Update()
        {
            foreach (var button in HandPokeButton.All) button.SetRayHover(false);
            var hands = HandTracking.Instance;
            if (hands == null || head == null) return;
            EnsureBuilt();
            Drive(hands.Left, _left);
            Drive(hands.Right, _right);
        }

        void Drive(TrackedHand hand, Visual v)
        {
            hand.PointingAtUi = false;
            bool tracked = hand.IsTracked && !hand.IsSimulated && head != null;
            v.tip.gameObject.SetActive(tracked);
            if (!tracked) { v.ray.enabled = false; v.hit.gameObject.SetActive(false); return; }
            v.tip.position = hand.IndexTip;

            bool holding = _grabber != null && _grabber.HeldBy(hand.Side) != null;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var shoulder = head.position + Vector3.down * 0.17f + right * (hand.Side == HandSide.Left ? -0.16f : 0.16f);
            var ray = new Ray(hand.IndexKnuckle, (hand.IndexKnuckle - shoulder).normalized);

            HandPokeButton best = null;
            float bestDistance = float.MaxValue;
            Vector3 bestPoint = default;
            if (!holding)
                foreach (var button in HandPokeButton.All)
                    if (button.Raycast(ray, out var d, out var p) && d < bestDistance)
                    { best = button; bestDistance = d; bestPoint = p; }

            bool closeEnoughToTouch = best != null && Vector3.Distance(hand.IndexTip, bestPoint) < pokeRange;
            if (best != null && !closeEnoughToTouch)
            {
                hand.PointingAtUi = true;
                best.SetRayHover(true);
                v.ray.enabled = true;
                v.ray.SetPosition(0, ray.origin);
                v.ray.SetPosition(1, bestPoint);
                v.hit.gameObject.SetActive(true);
                v.hit.position = bestPoint;
                Paint(v, Active);
                if (hand.PinchStarted) best.Press();
                return;
            }

            v.hit.gameObject.SetActive(false);
            v.ray.enabled = !holding && !closeEnoughToTouch;
            v.ray.SetPosition(0, ray.origin);
            v.ray.SetPosition(1, ray.GetPoint(idleRayLength));
            Paint(v, closeEnoughToTouch ? Active : Idle);
        }
    }
}
