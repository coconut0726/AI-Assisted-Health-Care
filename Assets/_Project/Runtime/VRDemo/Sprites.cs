using System.Collections.Generic;
using UnityEngine;

namespace AHC.VR
{
    /// <summary>
    /// Procedurally drawn, anti-aliased sprites (rounded rects, rings, the wrap arrow), so the
    /// UI needs no imported art. Cached per shape.
    /// </summary>
    public static class Sprites
    {
        static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>Filled rounded rectangle, 9-sliced so it scales to any size.</summary>
        public static Sprite Rounded(int radius) => Get($"round{radius}", () =>
        {
            int size = radius * 2 + 4;
            return Sliced(Draw(size, size, (x, y) => RoundRectCoverage(x, y, size, size, radius)), radius + 1);
        });

        /// <summary>Rounded rectangle outline, 9-sliced.</summary>
        public static Sprite RoundedOutline(int radius, float thickness) => Get($"outline{radius}_{thickness}", () =>
        {
            int size = radius * 2 + 4;
            return Sliced(Draw(size, size, (x, y) =>
            {
                float outer = RoundRectCoverage(x, y, size, size, radius);
                float inner = RoundRectCoverage(x, y, size, size, radius, thickness);
                return Mathf.Clamp01(outer - inner);
            }), radius + 1);
        });

        /// <summary>
        /// Dashed rounded-rectangle outline. Use with <c>Image.Type.Tiled</c>: the corners stay solid arcs and
        /// the straight edges repeat one dash + gap, so dashes keep their length at any size.
        /// </summary>
        public static Sprite DashedRoundedOutline(int radius, float thickness, int dash, int gap) => Get($"dashed{radius}_{thickness}_{dash}_{gap}", () =>
        {
            int border = radius + 2, period = dash + gap;
            int size = border * 2 + period;
            return Sliced(Draw(size, size, (x, y) =>
            {
                float outer = RoundRectCoverage(x, y, size, size, radius);
                float inner = RoundRectCoverage(x, y, size, size, radius, thickness);
                float ring = Mathf.Clamp01(outer - inner);
                // In the repeating middle strip of each edge, keep only the dash part.
                bool midX = x > border && x < size - border, midY = y > border && y < size - border;
                if (midX == midY) return ring; // corners (and the unused centre)
                float along = (midX ? x : y) - border;
                float edge = Mathf.Min(along - gap * 0.5f, gap * 0.5f + dash - along);
                return ring * Mathf.Clamp01(edge + 0.5f);
            }), border);
        });

        public static Sprite Circle() => Get("circle", () =>
        {
            const int size = 128;
            return Plain(Draw(size, size, (x, y) => Mathf.Clamp01(size / 2f - 1f - Dist(x, y, size / 2f, size / 2f) + 0.5f)));
        });

        public static Sprite Ring(float thicknessFraction) => Get($"ring{thicknessFraction}", () =>
        {
            const int size = 256;
            float r = size / 2f - 2f, t = r * thicknessFraction;
            return Plain(Draw(size, size, (x, y) =>
            {
                float d = Dist(x, y, size / 2f, size / 2f);
                return Mathf.Clamp01(Mathf.Min(r - d, d - (r - t)) + 0.5f);
            }));
        });

        public static Sprite Check() => Get("check", () =>
        {
            const int size = 64;
            Vector2 a = new(15, 33), b = new(27, 20), c = new(49, 45);
            return Plain(Draw(size, size, (x, y) =>
            {
                var p = new Vector2(x, y);
                return Mathf.Clamp01(4.5f - Mathf.Min(SegDist(p, a, b), SegDist(p, b, c)) + 0.5f);
            }));
        });

        /// <summary>Curved arrow that goes up and over, used for the "wrap" movement guidance.</summary>
        public static Sprite WrapArrow() => Get("wrap", () =>
        {
            const int w = 256, h = 320;
            var pts = new List<Vector2>();
            // Arc of an ellipse from lower-left, over the top, ending upper-right.
            for (float a = 215f; a >= -10f; a -= 3f)
            {
                float rad = a * Mathf.Deg2Rad;
                pts.Add(new Vector2(128 + Mathf.Cos(rad) * 92, 150 + Mathf.Sin(rad) * 140));
            }
            Vector2 tip = pts[pts.Count - 1], prev = pts[pts.Count - 3];
            Vector2 dir = (tip - prev).normalized, nrm = new Vector2(-dir.y, dir.x);
            Vector2 a1 = tip + dir * 26, b1 = tip + nrm * 20, c1 = tip - nrm * 20;
            return Plain(Draw(w, h, (x, y) =>
            {
                var p = new Vector2(x, y);
                float best = float.MaxValue;
                for (int i = 0; i < pts.Count - 1; i++) best = Mathf.Min(best, SegDist(p, pts[i], pts[i + 1]));
                float stroke = Mathf.Clamp01(7f - best + 0.5f);
                float head = InTriangle(p, a1, b1, c1) ? 1f : 0f;
                return Mathf.Max(stroke, head);
            }));
        });

        // ---------------------------------------------------------------- drawing helpers

        delegate float Coverage(float x, float y);

        static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (!Cache.TryGetValue(key, out var s) || s == null) Cache[key] = s = make();
            return s;
        }

        static Texture2D Draw(int w, int h, Coverage f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(f(x + 0.5f, y + 0.5f)) * 255));
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Sprite Sliced(Texture2D t, int border) =>
            Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));

        static Sprite Plain(Texture2D t) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);

        static float RoundRectCoverage(float x, float y, int w, int h, float r, float inset = 0f)
        {
            float left = 2 + inset, right = w - 2 - inset, bottom = 2 + inset, top = h - 2 - inset;
            float rr = Mathf.Max(0.5f, r - inset);
            float cx = Mathf.Clamp(x, left + rr, right - rr), cy = Mathf.Clamp(y, bottom + rr, top - rr);
            float d = Dist(x, y, cx, cy);
            return Mathf.Clamp01(rr - d + 0.5f);
        }

        static float Dist(float x, float y, float cx, float cy) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
            bool neg = s1 < 0 || s2 < 0 || s3 < 0, pos = s1 > 0 || s2 > 0 || s3 > 0;
            return !(neg && pos);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
