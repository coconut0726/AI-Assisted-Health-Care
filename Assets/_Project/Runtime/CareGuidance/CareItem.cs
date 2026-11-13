using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HomeCare.Guidance
{
    /// <summary>
    /// Marks an object in the scene as a care supply (or the burn itself) that instructions can
    /// refer to. Keywords are matched against the current step's text to decide whether the
    /// step mentions it.
    /// </summary>
    public sealed class CareItem : MonoBehaviour
    {
        public string itemId;
        public string displayName;
        [Tooltip("Words or phrases that refer to this item in an instruction (case-insensitive).")]
        public string[] keywords;
        [Tooltip("Can be picked up with a pinch or a fist.")]
        public bool grabbable = true;

        Bounds _localBounds;
        bool _hasBounds;

        public string Name => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        public bool IsMentionedIn(string text) => KeywordMatcher.Matches(text, keywords);

        /// <summary>Bounds of all child renderers, in this object's local space (so a box follows its rotation).</summary>
        public Bounds LocalBounds
        {
            get
            {
                if (_hasBounds) return _localBounds;
                bool any = false;
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (r is LineRenderer || r.GetComponentInParent<ItemHighlight>() != null) continue;
                    var b = r.localBounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                        var p = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                        if (!any) { _localBounds = new Bounds(p, Vector3.zero); any = true; }
                        else _localBounds.Encapsulate(p);
                    }
                }
                if (!any) _localBounds = new Bounds(Vector3.zero, Vector3.one * 0.05f);
                _hasBounds = true;
                return _localBounds;
            }
        }

        /// <summary>Distance from a world point to this item's box (0 inside).</summary>
        public float DistanceTo(Vector3 worldPoint)
        {
            var b = LocalBounds;
            var local = transform.InverseTransformPoint(worldPoint);
            var closest = b.ClosestPoint(local);
            return Vector3.Distance(transform.TransformPoint(closest), worldPoint);
        }

        public Vector3 WorldCenter => transform.TransformPoint(LocalBounds.center);

        public static List<CareItem> FindAllInScene()
        {
            return new List<CareItem>(FindObjectsByType<CareItem>(FindObjectsSortMode.None));
        }
    }

    /// <summary>A supply the instruction may name, used to report items that are mentioned but not found in the scene.</summary>
    [Serializable]
    public class ItemVocabulary
    {
        public string itemId;
        public string displayName;
        public string[] keywords;
    }

    public static class KeywordMatcher
    {
        public static bool Matches(string text, string[] keywords)
        {
            if (string.IsNullOrEmpty(text) || keywords == null) return false;
            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                string k = keyword.Trim();
                bool ascii = Regex.IsMatch(k, @"^[\x00-\x7F]+$");
                if (ascii)
                {
                    // Whole words only ("pad" must not match "padding"); allow simple plurals.
                    if (Regex.IsMatch(text, @"\b" + Regex.Escape(k) + @"(s|es)?\b", RegexOptions.IgnoreCase)) return true;
                }
                else if (text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
