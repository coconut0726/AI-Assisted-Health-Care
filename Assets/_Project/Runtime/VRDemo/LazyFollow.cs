using UnityEngine;

namespace AHC.VR
{
    /// <summary>When the head has a real pose to place UI from.</summary>
    static class HeadPose
    {
        const float Settle = 0.5f;    // let tracking start before trusting the pose
        const float GiveUpAfter = 2f; // Editor without a headset: the head never moves, place anyway

        public static bool Ready(Transform head, float since)
        {
            float waited = Time.time - since;
            if (waited < Settle) return false;
            bool untracked = head.localPosition == Vector3.zero && head.localRotation == Quaternion.identity;
            return !untracked || waited >= GiveUpAfter;
        }
    }

    /// <summary>
    /// World-locked panel: placed once in front of the user's first tracked view, then it stays where it is
    /// and does not follow the head. <see cref="Recenter"/> places it again in front of the user.
    /// </summary>
    public class LazyFollow : MonoBehaviour
    {
        public Transform head;
        public float distance = 1.2f;
        public float heightOffset = -0.12f;

        [Header("On the wall")]
        [Tooltip("Place the panel against the wall the user is facing (found with depth), parallel to it, so it stays " +
                 "well clear of the table and the items on it.")]
        public bool onWall;
        [Tooltip("Gap between the wall and the panel, metres.")]
        public float wallGap = 0.1f;
        [Tooltip("Nearest / farthest the panel is placed, metres. A wall farther than this: the panel floats at the limit.")]
        public Vector2 wallRange = new(1.3f, 3.5f);
        [Tooltip("How much the panel grows with distance (1: same apparent size as at 'distance'; 0: no growth).")]
        [Range(0, 1)] public float keepApparentSize = 0.8f;
        [Tooltip("Panel centre above the eyes, as a fraction of its height (keeps its bottom edge above the table).")]
        public float raise = 0.2f;
        [Tooltip("Panel height in metres at scale 1.")]
        public float panelHeight = 0.9f;
        [Tooltip("How long to look for a wall before using the default placement, seconds.")]
        public float findWallFor = 5f;
        [Tooltip("Default distance when no wall is found, metres.")]
        public float noWallDistance = 2f;

        float _since, _nextTry;
        bool _placed;
        Vector3 _scale;

        void Start()
        {
            _since = Time.time;
            _scale = transform.localScale;
            if (!_placed) transform.localScale = Vector3.zero; // hidden until placed
        }

        void LateUpdate()
        {
            if (_placed || head == null || !HeadPose.Ready(head, _since) || Time.time < _nextTry) return;
            Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            flatForward.Normalize();

            Vector3 position = head.position + flatForward * distance + Vector3.up * heightOffset;
            Quaternion rotation = Quaternion.LookRotation(position - head.position, Vector3.up);
            float scale = 1f;
            if (onWall)
            {
                float d;
                Vector3 facing = flatForward; // panel forward (away from the user)
                if (WallFinder.TryFind(head, flatForward, out var wall))
                {
                    d = Mathf.Clamp(wall.distance - wallGap, wallRange.x, wallRange.y);
                    facing = -wall.normal; // parallel to the wall
                    Debug.Log($"[AHC] Panel placed on the wall: wall {wall.distance:F2} m away, panel at {d:F2} m ({wall.samples} samples).");
                }
                else if (Time.time - _since < findWallFor)
                {
                    _nextTry = Time.time + 0.25f; // depth may not be ready yet; look again shortly
                    return;
                }
                else
                {
                    d = noWallDistance;
                    Debug.Log($"[AHC] No wall found in view; panel placed {d:F1} m ahead.");
                }
                // Larger with distance so the text stays readable; centre a little above the eyes so the bottom
                // edge stays above the table in view.
                scale = Mathf.Lerp(1f, d / distance, keepApparentSize);
                position = head.position + flatForward * d + Vector3.up * (panelHeight * scale * raise);
                rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = _scale * scale;
            _placed = true;
        }

        public void Recenter()
        {
            _placed = false;
            _since = Time.time - 10f; // the head is already tracked: place on the next frame
        }
    }

    /// <summary>
    /// Turns a world-space label to face the user once, when it appears (around the vertical axis only),
    /// then keeps that orientation; it does not keep turning as the user moves.
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        public Transform head;
        float _since;

        void Start() => _since = Time.time - (Time.time > 3f ? 10f : 0f); // late spawns face the user at once

        void LateUpdate()
        {
            if (head == null || !HeadPose.Ready(head, _since)) return;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            enabled = false;
        }
    }

    /// <summary>Slow pulse used for the hand-gesture rings.</summary>
    public class Pulse : MonoBehaviour
    {
        public float period = 1.4f;
        public float amount = 0.12f;
        Vector3 _base;

        void Awake() => _base = transform.localScale;

        void Update()
        {
            float s = 1f + Mathf.Sin(Time.time * Mathf.PI * 2f / period) * amount;
            transform.localScale = _base * s;
        }
    }
}
