using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

namespace AHC.VR
{
    /// <summary>
    /// Finds the wall the user is facing with depth rays (on the headset) or physics rays (Editor), so panels can be
    /// placed against it: far enough to stay clear of the table and the items on it.
    /// </summary>
    public static class WallFinder
    {
        public struct Wall
        {
            /// <summary>Horizontal distance from the head to the wall, along the look direction.</summary>
            public float distance;
            /// <summary>The wall's surface normal (horizontal, pointing toward the user).</summary>
            public Vector3 normal;
            public int samples;
        }

        const float MaxDistance = 6f;
        static EnvironmentRaycastManager _depth;

        /// <summary>
        /// Casts a fan of near-horizontal rays from <paramref name="head"/> along <paramref name="forward"/> (flat) and
        /// keeps hits on vertical surfaces. False until depth is ready or when no wall is in view.
        /// </summary>
        public static bool TryFind(Transform head, Vector3 forward, out Wall wall)
        {
            wall = default;
            var distances = new List<float>();
            var normals = new List<Vector3>();
            foreach (float yaw in new[] { -20f, -10f, 0f, 10f, 20f })
            {
                Vector3 flat = Quaternion.AngleAxis(yaw, Vector3.up) * forward;
                foreach (float pitch in new[] { -8f, -3f, 2f, 7f, 12f, 17f }) // above the table, mostly
                {
                    Vector3 dir = Vector3.RotateTowards(flat, pitch < 0 ? Vector3.down : Vector3.up, Mathf.Abs(pitch) * Mathf.Deg2Rad, 0f);
                    if (!Cast(new Ray(head.position, dir), out var point, out var normal)) continue;
                    if (Mathf.Abs(normal.y) > 0.35f) continue; // floor, table or ceiling
                    float along = Vector3.Dot(point - head.position, forward);
                    if (along < 0.6f) continue; // too close to be a wall (a person, a monitor on the table…)
                    distances.Add(along);
                    normals.Add(Vector3.ProjectOnPlane(normal, Vector3.up).normalized);
                }
            }
            if (distances.Count < 5) return false;

            // The wall is the bulk of the hits: take the median distance and the average normal of the hits near it.
            var sorted = new List<float>(distances);
            sorted.Sort();
            float median = sorted[sorted.Count / 2];
            Vector3 n = Vector3.zero;
            int count = 0;
            for (int i = 0; i < distances.Count; i++)
            {
                if (Mathf.Abs(distances[i] - median) > 0.3f) continue;
                n += normals[i];
                count++;
            }
            if (count < 5 || n.sqrMagnitude < 1e-4f) return false;
            n.Normalize();
            if (Vector3.Dot(n, -forward) < 0.5f) n = -forward; // a wall seen at a steep angle: face the user instead
            wall = new Wall { distance = median, normal = n, samples = count };
            return true;
        }

        static bool Cast(Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
#if UNITY_EDITOR
            if (Physics.Raycast(ray, out var ph, MaxDistance, ~LayerMask.GetMask("UI"), QueryTriggerInteraction.Ignore))
            {
                point = ph.point;
                normal = ph.normal;
                return true;
            }
            return false;
#else
            if (_depth == null) _depth = Object.FindAnyObjectByType<EnvironmentRaycastManager>();
            if (_depth == null || !_depth.isActiveAndEnabled) return false;
            if (!_depth.Raycast(ray, out var hit, MaxDistance) || hit.normalConfidence < 0.3f) return false;
            point = hit.point;
            normal = hit.normal;
            return true;
#endif
        }
    }
}
