using UnityEngine;

namespace AHC.VR
{
    /// <summary>
    /// Keeps a panel in front of the user without glueing it to their head: it only re-centres
    /// once the user has turned away by more than <see cref="angleThreshold"/>, then eases back.
    /// </summary>
    public class LazyFollow : MonoBehaviour
    {
        public Transform head;
        public float distance = 1.2f;
        public float heightOffset = -0.12f;
        public float angleThreshold = 25f;
        public float smoothTime = 0.35f;

        Vector3 _velocity;
        Vector3 _targetForward;
        bool _snapped;

        void LateUpdate()
        {
            if (head == null) return;
            Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            flatForward.Normalize();

            if (!_snapped || Vector3.Angle(_targetForward, flatForward) > angleThreshold)
                _targetForward = flatForward;

            Vector3 target = head.position + _targetForward * distance + Vector3.up * heightOffset;
            if (!_snapped)
            {
                transform.position = target;
                _snapped = true;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, smoothTime);
            }
            transform.rotation = Quaternion.LookRotation(transform.position - head.position, Vector3.up);
        }

        public void Recenter() => _snapped = false;
    }

    /// <summary>Turns a world-space label to face the user, rotating only around the vertical axis.</summary>
    public class Billboard : MonoBehaviour
    {
        public Transform head;

        void LateUpdate()
        {
            if (head == null) return;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
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
