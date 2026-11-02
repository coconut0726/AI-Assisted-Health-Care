using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AHC.VR
{
    /// <summary>
    /// Small uGUI builder. Sizes are in canvas pixels and match the Figma frames 1:1;
    /// the canvas scale turns pixels into metres.
    /// </summary>
    public static class Ui
    {
        public const int CardRadius = 28;
        public const int PillRadius = 22;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 0;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static Canvas WorldCanvas(string name, Transform parent, Vector2 size, float metresPerPixel, Camera eventCamera)
        {
            var rt = Rect(name, parent);
            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            rt.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * metresPerPixel;
            return canvas;
        }

        /// <summary>Places a rect by its pivot corner, like an absolutely positioned Figma frame.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchorAndPivot, Vector2 position, float width = -1)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchorAndPivot;
            rt.anchoredPosition = position;
            if (width > 0) rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            }
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A rounded panel whose children stack vertically and whose height hugs its content.</summary>
        public static Image Card(Transform parent, string name, float width, Color color, RectOffset padding, float spacing, int radius = CardRadius)
        {
            var img = Image(parent, name, color, Sprites.Rounded(radius));
            img.pixelsPerUnitMultiplier = 1f;
            VStack(img.gameObject, padding, spacing);
            var fit = img.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (width > 0) img.rectTransform.sizeDelta = new Vector2(width, 100);
            else fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return img;
        }

        public static VerticalLayoutGroup VStack(GameObject go, RectOffset padding, float spacing)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = padding;
            v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        /// <summary>A horizontal row. Children keep their preferred width unless given flexible width.</summary>
        public static HorizontalLayoutGroup Row(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.MiddleLeft, RectOffset padding = null)
        {
            var rt = Rect(name, parent);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            if (padding != null) h.padding = padding;
            return h;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, bool bold = false, bool wrap = true)
        {
            var t = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.lineSpacing = -8f;
            t.raycastTarget = false;
            if (wrap) Flexible(t.gameObject);
            return t;
        }

        static LayoutElement LayoutOf(GameObject go) =>
            go.TryGetComponent<LayoutElement>(out var le) ? le : go.AddComponent<LayoutElement>();

        public static LayoutElement Flexible(GameObject go)
        {
            var le = LayoutOf(go);
            le.flexibleWidth = 1;
            return le;
        }

        public static LayoutElement Fixed(GameObject go, float w, float h)
        {
            var le = LayoutOf(go);
            if (w >= 0) le.minWidth = le.preferredWidth = w;
            if (h >= 0) le.minHeight = le.preferredHeight = h;
            return le;
        }

        public static Image Pill(Transform parent, string text, Color fg, Color bg, float size = 14)
        {
            var img = Image(parent, "Pill", bg, Sprites.Rounded(PillRadius));
            var h = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(14, 14, 6, 6);
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            Text(img.transform, text, size, fg, bold: true, wrap: false);
            return img;
        }

        /// <summary>Adds a pill without stretching it to the parent's width.</summary>
        public static Image PillRow(Transform parent, string text, Color fg, Color bg, float size = 14)
        {
            var row = Row(parent, "PillRow", 0);
            return Pill(row.transform, text, fg, bg, size);
        }

        public static Image Divider(Transform parent)
        {
            var img = Image(parent, "Divider", Theme.Border);
            Fixed(img.gameObject, -1, 2);
            return img;
        }

        public static Button Button(Transform parent, string label, bool primary, UnityAction onClick, string hint = null, bool stretch = false)
        {
            var img = Image(parent, label + " Button", primary ? Theme.Primary : Theme.White, Sprites.Rounded(PillRadius));
            img.raycastTarget = true;
            var h = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(28, 28, 16, 16);
            h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            Text(img.transform, label, 20, primary ? Theme.White : Theme.Text, bold: true, wrap: false);
            if (!string.IsNullOrEmpty(hint))
            {
                var key = Image(img.transform, "Hint", primary ? Theme.WithAlpha(Theme.White, 0.25f) : Theme.Surface, Sprites.Circle());
                Fixed(key.gameObject, 28, 28);
                var k = Text(key.transform, hint, 14, primary ? Theme.White : Theme.Muted, bold: true, wrap: false);
                k.alignment = TextAlignmentOptions.Center;
                Stretch(k.rectTransform);
            }
            if (stretch) Flexible(img.gameObject);
            var btn = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.92f, 0.95f, 0.92f);
            colors.pressedColor = new Color(0.8f, 0.86f, 0.8f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Grey rounded box with key/value rows, as in the Figma centre panels.</summary>
        public static Image Details(Transform parent, IEnumerable<(string key, string value, Color? color)> rows)
        {
            var box = Image(parent, "Details", Theme.Surface, Sprites.Rounded(22));
            VStack(box.gameObject, new RectOffset(20, 20, 16, 16), 12);
            foreach (var (k, v, c) in rows)
            {
                var r = Row(box.transform, k, 12);
                Text(r.transform, k, 17, Theme.Muted);
                var val = Text(r.transform, v, 17, c ?? Theme.Text, bold: true, wrap: false);
                val.alignment = TextAlignmentOptions.Right;
                Flexible(val.gameObject);
            }
            return box;
        }

        public static Image Note(Transform parent, string text, Color fg, Color bg)
        {
            var box = Image(parent, "Note", bg, Sprites.Rounded(18));
            VStack(box.gameObject, new RectOffset(18, 18, 14, 14), 0);
            Text(box.transform, text, 16, fg, bold: true);
            return box;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    /// <summary>Vertical gradient for any uGUI graphic (used for the voice bar and hero cards).</summary>
    public class VerticalGradient : BaseMeshEffect
    {
        public Color top = Theme.HeroTop;
        public Color bottom = Theme.HeroBottom;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Mathf.Min(min, v.position.y);
                max = Mathf.Max(max, v.position.y);
            }
            float span = Mathf.Max(0.0001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Color c = Color.Lerp(bottom, top, (v.position.y - min) / span);
                c.a *= v.color.a / 255f;
                v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
