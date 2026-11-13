using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AHC.VR
{
    /// <summary>
    /// Draws the per-action guidance on the workspace: detection boxes and labels, hint chips,
    /// contextual widgets, gesture rings and the wrap arrow. Positions come from the demo data,
    /// in metres relative to this transform (place it on the burn; +X along the arm, +Z away from you).
    /// For the demo, objects are not detected — they sit where the data says, and the user aligns
    /// the whole workspace to the real table once (hold the right trigger).
    /// </summary>
    public class WorkspaceOverlays : MonoBehaviour
    {
        const float LabelScale = 0.0006f; // metres per canvas pixel for labels near the table

        public Transform head;
        GuidanceData _data;
        Transform _root;

        public void Init(GuidanceData data, Transform headTransform)
        {
            _data = data;
            head = headTransform;
        }

        public void Clear()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        public void Show(OverlayData[] overlays)
        {
            Clear();
            _root = new GameObject("Overlays").transform;
            _root.SetParent(transform, false);
            if (overlays == null) return;

            // Stack chips/widgets on the same target upwards so they don't overlap.
            var stackHeights = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var o in overlays)
            {
                var obj = _data.FindObject(o.target);
                if (obj == null) continue;
                float stack = stackHeights.TryGetValue(o.target, out var h) ? h : 0f;

                switch (o.type)
                {
                    case "box": Box(obj, o.text, false); break;
                    case "next": Box(obj, o.text, true); break;
                    case "chip": stackHeights[o.target] = stack + Chip(obj, o.text, Theme.ChipDark, stack); break;
                    case "warn": stackHeights[o.target] = stack + Chip(obj, o.text, Theme.Red, stack); break;
                    case "widget": stackHeights[o.target] = stack + Widget(obj, o, stack); break;
                    case "gesture": Gesture(obj); break;
                    case "arc": stackHeights[o.target] = stack + Arc(obj); break;
                }
            }
        }

        Canvas LabelCanvas(string name, Vector3 localPos, Vector2 size, bool billboard = true)
        {
            var canvas = Ui.WorldCanvas(name, _root, size, LabelScale, null);
            canvas.transform.localPosition = localPos;
            canvas.sortingOrder = 5;
            if (billboard) canvas.gameObject.AddComponent<Billboard>().head = head;
            return canvas;
        }

        static float Px(float metres) => metres / LabelScale;

        void Box(TrackedObject obj, string label, bool next)
        {
            float w = Px(obj.size.x), h = Px(obj.size.y);
            var canvas = LabelCanvas("Box " + obj.id, obj.position + Vector3.up * (obj.size.y * 0.5f), new Vector2(w, h));
            var frame = Ui.Image(canvas.transform, "Frame", next ? Theme.WithAlpha(Theme.White, 0.9f) : Theme.Primary, Sprites.RoundedOutline(16, next ? 3f : 4f));
            Ui.Stretch(frame.rectTransform);

            string text = next ? "Next: " + label : label;
            var pill = Ui.Pill(canvas.transform, text, next ? Theme.Text : Theme.White, next ? Theme.Panel : Theme.Primary, 15);
            var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Label sits on top of the box: below it the table would hide it.
            var rt = pill.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(0, 10);
        }

        float Chip(TrackedObject obj, string text, Color bg, float stack)
        {
            const float height = 0.03f;
            Vector3 pos = obj.position + Vector3.up * (obj.size.y + 0.07f + stack);
            var canvas = LabelCanvas("Chip", pos, new Vector2(10, 10));
            var pill = Ui.Pill(canvas.transform, text, Theme.White, Theme.WithAlpha(bg, 0.95f), 20);
            var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pill.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(22, 22, 12, 12);
            Ui.Place(pill.rectTransform, new Vector2(0.5f, 0f), Vector2.zero);
            return height;
        }

        float Widget(TrackedObject obj, OverlayData o, float stack)
        {
            // Card floats above the object; a thin connector line points down to it.
            float lift = obj.size.y + 0.12f + stack;
            var canvas = LabelCanvas("Widget", obj.position + Vector3.up * lift, new Vector2(10, 10));
            var card = Ui.Card(canvas.transform, "Contextual widget", 380, Theme.Panel, new RectOffset(22, 22, 20, 20), 10, 24);
            Ui.Place(card.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, 380);
            if (!string.IsNullOrEmpty(o.pill)) Ui.PillRow(card.transform, o.pill, Theme.Amber, Theme.AmberBg, 15);
            Ui.Text(card.transform, o.title, 22, Theme.Text, bold: true);
            if (!string.IsNullOrEmpty(o.note)) Ui.Text(card.transform, o.note, 17, Theme.Red, bold: true);

            var line = Ui.Image(canvas.transform, "Connector", Theme.WithAlpha(Theme.White, 0.9f));
            Ui.Place(line.rectTransform, new Vector2(0.5f, 1f), Vector2.zero);
            line.rectTransform.sizeDelta = new Vector2(3, Px(lift - obj.size.y));
            return 0.14f;
        }

        void Gesture(TrackedObject obj)
        {
            // Two rings lying flat on the object, pulsing like a press-and-lift.
            var canvas = Ui.WorldCanvas("Gesture", _root, new Vector2(Px(obj.size.x * 1.6f), Px(obj.size.x * 1.6f)), LabelScale, null);
            canvas.transform.localPosition = obj.position + Vector3.up * 0.005f;
            canvas.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var outer = Ui.Image(canvas.transform, "Outer ring", Theme.WithAlpha(Theme.White, 0.55f), Sprites.Ring(0.04f));
            Ui.Stretch(outer.rectTransform);
            var inner = Ui.Image(canvas.transform, "Inner ring", Theme.WithAlpha(Theme.White, 0.95f), Sprites.Ring(0.06f));
            Ui.Stretch(inner.rectTransform);
            inner.rectTransform.offsetMin = inner.rectTransform.offsetMax = Vector2.zero;
            inner.transform.localScale = Vector3.one * 0.78f;
            inner.gameObject.AddComponent<Pulse>();
        }

        /// <summary>Curved arrow around the arm at the wound, facing the user. Returns the height it takes up.</summary>
        float Arc(TrackedObject obj)
        {
            float h = obj.size.x * 1.5f, w = h * 0.8f;
            var canvas = LabelCanvas("Wrap arrow", obj.position + Vector3.up * (h * 0.4f), new Vector2(Px(w), Px(h)));
            var arrow = Ui.Image(canvas.transform, "Arrow", Theme.PrimaryDark, Sprites.WrapArrow());
            Ui.Stretch(arrow.rectTransform);
            arrow.gameObject.AddComponent<Pulse>().amount = 0.04f;
            return h * 0.9f - obj.size.y;
        }
    }
}
