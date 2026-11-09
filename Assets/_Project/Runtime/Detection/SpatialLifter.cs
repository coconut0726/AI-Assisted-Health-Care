using System;
using System.Collections.Generic;
using HomeCare.AR;
using Meta.XR;
using UnityEngine;

namespace HomeCare.Guidance
{
    /// <summary>A detected object placed in the room: a box facing the camera that took the picture.</summary>
    public struct WorldBox
    {
        public Vector3 center;
        public Quaternion rotation;
        /// <summary>Width, height, depth in metres.</summary>
        public Vector3 size;
        /// <summary>depth | live_depth | default_distance</summary>
        public string depthSource;
        public Vector3 FrontCenter => center - rotation * Vector3.forward * (size.z * 0.5f);
    }

    /// <summary>
    /// Turns a 2D box from the camera image into a <see cref="WorldBox"/>. Distances come from the Quest
    /// depth sensor (<see cref="EnvironmentRaycastManager"/>), sampled on a grid right after the image is
    /// taken so the user can move while Gemini works. In the Editor, the virtual items stand in for depth.
    /// </summary>
    public sealed class SpatialLifter
    {
        readonly CapturedFrame _frame;
        readonly Func<Ray, float> _distance;
        readonly int _columns, _rows;
        readonly float[] _grid;
        readonly float _defaultDistance;

        const float MaxDistance = 4f;

        SpatialLifter(CapturedFrame frame, Func<Ray, float> distance, int columns, int rows, float defaultDistance)
        {
            _frame = frame;
            _distance = distance;
            _columns = columns;
            _rows = rows;
            _defaultDistance = defaultDistance;
            _grid = new float[columns * rows];
        }

        /// <summary>Samples depth over the whole image now. Call right after the frame is captured.</summary>
        public static SpatialLifter Capture(CapturedFrame frame, EnvironmentRaycastManager environment,
            int columns, int rows, float defaultDistance)
        {
            Func<Ray, float> distance = frame.MatchesScene ? DistanceProvider(environment) : null;
            var lifter = new SpatialLifter(frame, distance, columns, rows, defaultDistance);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                lifter._grid[r * columns + c] = distance == null ? float.NaN
                    : distance(frame.ViewportToRay(new Vector2((c + 0.5f) / columns, (r + 0.5f) / rows)));
            return lifter;
        }

        public int DepthSamples
        {
            get { int n = 0; foreach (var d in _grid) if (!float.IsNaN(d)) n++; return n; }
        }

        static Func<Ray, float> DistanceProvider(EnvironmentRaycastManager environment)
        {
#if UNITY_EDITOR
            return EditorDistance;
#else
            if (environment == null || !EnvironmentRaycastManager.IsSupported) return null;
            return ray => environment.Raycast(ray, out var hit, MaxDistance) && hit.status == EnvironmentRaycastHitStatus.Hit
                ? Vector3.Distance(ray.origin, hit.point) : float.NaN;
#endif
        }

#if UNITY_EDITOR
        /// <summary>Editor stand-in for the depth sensor: physics colliders and the virtual care items.</summary>
        static float EditorDistance(Ray ray)
        {
            float best = float.NaN;
            if (Physics.Raycast(ray, out var hit, MaxDistance, ~0, QueryTriggerInteraction.Ignore)) best = hit.distance;
            foreach (var item in CareItem.FindAllInScene())
            {
                var t = item.transform;
                var local = new Ray(t.InverseTransformPoint(ray.origin), t.InverseTransformDirection(ray.direction));
                if (!item.LocalBounds.IntersectRay(local, out float localDistance)) continue;
                float d = Vector3.Distance(ray.origin, t.TransformPoint(local.GetPoint(localDistance)));
                if (d <= MaxDistance && (float.IsNaN(best) || d < best)) best = d;
            }
            return best;
        }
#endif

        /// <summary>Places the detected box in the room.</summary>
        public WorldBox Lift(Rect viewport)
        {
            var pose = _frame.CameraPose;
            var forward = pose.rotation * Vector3.forward;
            var up = pose.rotation * Vector3.up;

            float distance = MeasureDistance(viewport, out string source);
            var centerRay = _frame.ViewportToRay(viewport.center);
            // Distance along the centre ray -> distance along the camera axis, so all corners share one plane.
            float planeZ = distance * Mathf.Max(0.05f, Vector3.Dot(centerRay.direction, forward));
            Vector3 OnPlane(Vector2 v)
            {
                var ray = _frame.ViewportToRay(v);
                return ray.GetPoint(planeZ / Mathf.Max(0.05f, Vector3.Dot(ray.direction, forward)));
            }
            Vector3 bl = OnPlane(new Vector2(viewport.xMin, viewport.yMin)), br = OnPlane(new Vector2(viewport.xMax, viewport.yMin));
            Vector3 tl = OnPlane(new Vector2(viewport.xMin, viewport.yMax));
            float width = Vector3.Distance(bl, br), height = Vector3.Distance(bl, tl);
            // Depth hits the front surface; give the box some thickness behind it.
            float depth = Mathf.Clamp(Mathf.Min(width, height), 0.01f, 0.3f);
            var front = OnPlane(viewport.center);
            var rotation = Quaternion.LookRotation(front - pose.position, up);
            return new WorldBox
            {
                center = front + rotation * Vector3.forward * (depth * 0.5f),
                rotation = rotation,
                size = new Vector3(width, height, depth),
                depthSource = source,
            };
        }

        float MeasureDistance(Rect viewport, out string source)
        {
            var samples = new List<float>();
            // 1) Depth sampled when the picture was taken, inside the middle of the box.
            var inner = Shrink(viewport, 0.6f);
            for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _columns; c++)
            {
                var d = _grid[r * _columns + c];
                if (!float.IsNaN(d) && inner.Contains(new Vector2((c + 0.5f) / _columns, (r + 0.5f) / _rows))) samples.Add(d);
            }
            if (samples.Count > 0) { source = "depth"; return Nearish(samples); }

            // 2) Small objects can fall between grid cells: cast a few rays into the box now.
            if (_distance != null)
            {
                foreach (var p in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.3f), new Vector2(0.3f, 0.7f), new Vector2(0.7f, 0.7f) })
                {
                    var d = _distance(_frame.ViewportToRay(new Vector2(viewport.x + viewport.width * p.x, viewport.y + viewport.height * p.y)));
                    if (!float.IsNaN(d)) samples.Add(d);
                }
                if (samples.Count > 0) { source = "live_depth"; return Nearish(samples); }
            }

            // 3) No depth (Editor test photo, depth unsupported or not ready yet).
            source = "default_distance";
            return _defaultDistance;
        }

        static Rect Shrink(Rect r, float keep) =>
            new(r.center.x - r.width * keep * 0.5f, r.center.y - r.height * keep * 0.5f, r.width * keep, r.height * keep);

        /// <summary>30th percentile: the object sits in front of whatever shows around it inside its box.</summary>
        static float Nearish(List<float> values)
        {
            values.Sort();
            return values[Mathf.Clamp(Mathf.FloorToInt((values.Count - 1) * 0.3f), 0, values.Count - 1)];
        }
    }
}
