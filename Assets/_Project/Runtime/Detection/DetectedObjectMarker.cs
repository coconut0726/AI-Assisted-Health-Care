using AHC.VR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HomeCare.Guidance
{
    /// <summary>How a detected object is marked (Figma "XR" page, VP2 / VP4).</summary>
    public enum MarkerStyle
    {
        /// <summary>Used in this step: solid sage outline, sage pill with white text.</summary>
        Active,
        /// <summary>Found but not used now (or only context): dashed white outline, white pill with dark text.</summary>
        Secondary,
    }

    /// <summary>
    /// Marks a real object found by the detector, styled like the Figma XR screens and
    /// <see cref="WorkspaceOverlays"/>: a rounded outline around the object with a name pill on top,
    /// and optionally a contextual widget card above it (header pill, guidance, caution) joined by a
    /// connector line. The outline stays in the plane of the photo it was found in, so it lines up with
    /// the object; the card turns to face the user.
    /// </summary>
    public sealed class DetectedObjectMarker : MonoBehaviour
    {
        /// <summary>Metres per canvas pixel: same as the workspace labels, so Figma sizes carry over 1:1.</summary>
        public const float Scale = 0.0006f;
        const float Padding = 0.008f;   // space between the object and its outline
        const float CardLift = 0.09f;   // connector length between the outline and the card

        public WorldBox Box { get; private set; }
        public DetectedObject Detection { get; private set; }
        public MarkerStyle Style { get; private set; }

        Transform _head;
        Vector2 _frameSize; // metres, including padding
        TextMeshProUGUI _cardPill;
        string _cardHeader;
        float _timerEnd = -1;

        static float Px(float metres) => metres / Scale;

        public static DetectedObjectMarker Create(DetectedObject detection, WorldBox box, Transform parent, Transform head,
            MarkerStyle style, float minSize)
        {
            var go = new GameObject("Detected " + detection.label);
            int ui = LayerMask.NameToLayer("UI"); // left out of Editor camera captures; children inherit it
            if (ui >= 0) go.layer = ui;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(box.FrontCenter, box.rotation);
            var m = go.AddComponent<DetectedObjectMarker>();
            m.Detection = detection;
            m.Box = box;
            m.Style = style;
            m._head = head;
            m._frameSize = new Vector2(Mathf.Max(box.size.x, minSize), Mathf.Max(box.size.y, minSize)) + Vector2.one * (2 * Padding);
            m.BuildFrame();
            return m;
        }

        void BuildFrame()
        {
            bool active = Style == MarkerStyle.Active;
            var canvas = Ui.WorldCanvas("Frame", transform, new Vector2(Px(_frameSize.x), Px(_frameSize.y)), Scale, null);
            canvas.sortingOrder = 5;

            // VP2/VP4: 3 px border, 16 px corners; sage when active, dashed white when secondary.
            var frame = Ui.Image(canvas.transform, "Outline", active ? Theme.Primary : Theme.WithAlpha(Theme.White, 0.9f),
                active ? Sprites.RoundedOutline(16, 4f) : Sprites.DashedRoundedOutline(16, 4f, 12, 9));
            if (!active) frame.type = Image.Type.Tiled;
            Ui.Stretch(frame.rectTransform);

            // Name pill on top of the outline, left-aligned (below it, the table would hide it).
            var pill = Ui.Pill(canvas.transform, Detection.label, active ? Theme.White : Theme.Text,
                active ? Theme.Primary : Theme.WithAlpha(Theme.White, 0.92f), 15);
            var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rt = pill.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(0, 8);
        }

        /// <summary>Top centre of the outline, in the room.</summary>
        public Vector3 TopPoint => transform.position + transform.up * (_frameSize.y * 0.5f);

        /// <summary>Middle of the outline's side that faces <paramref name="point"/>, so an arrow leaving it clears the name pill.</summary>
        public Vector3 SideToward(Vector3 point)
        {
            float side = Vector3.Dot(point - transform.position, transform.right) >= 0 ? 1f : -1f;
            return transform.position + transform.right * (side * _frameSize.x * 0.5f);
        }

        /// <summary>Just right of the outline's top-right corner (where VP4 puts the hint chip).</summary>
        public Vector3 RightOfTop => transform.position + transform.right * (_frameSize.x * 0.5f + 0.02f) + transform.up * (_frameSize.y * 0.5f);

        /// <summary>Where an arrow should start or end on this object.</summary>
        public Vector3 AnchorPoint => Box.FrontCenter;

        /// <summary>
        /// VP2 "Contextual widget": a white card above the object with an amber header pill, the guidance
        /// and a red caution, joined to the outline by a thin white connector.
        /// </summary>
        public void ShowCard(string header, string body, string caution, int timerSeconds)
        {
            var holder = new GameObject("Contextual widget").transform;
            holder.gameObject.layer = gameObject.layer;
            holder.SetParent(transform, false);
            // Above the pill: the card's bottom edge sits CardLift above the outline's top.
            holder.position = TopPoint + Vector3.up * CardLift;
            holder.gameObject.AddComponent<Billboard>().head = _head;

            var canvas = Ui.WorldCanvas("Card canvas", holder, new Vector2(10, 10), Scale, null);
            canvas.sortingOrder = 6;
            var card = Ui.Card(canvas.transform, "Card", 380, Theme.WithAlpha(Theme.White, 0.97f), new RectOffset(22, 22, 20, 20), 10, 24);
            Ui.Place(card.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, 380);
            _cardHeader = header;
            _timerEnd = timerSeconds > 0 ? Time.time + timerSeconds : -1;
            if (!string.IsNullOrWhiteSpace(header) || _timerEnd > 0)
                _cardPill = Ui.PillRow(card.transform, HeaderText(), Theme.Amber, Theme.AmberBg, 15).GetComponentInChildren<TextMeshProUGUI>();
            if (!string.IsNullOrWhiteSpace(body)) Ui.Text(card.transform, body, 22, Theme.Text, bold: true);
            if (!string.IsNullOrWhiteSpace(caution)) Ui.Text(card.transform, caution, 17, Theme.Red, bold: true);

            // Connector: straight down from the card to just above the name pill.
            var line = Ui.Image(canvas.transform, "Connector", Theme.WithAlpha(Theme.White, 0.9f));
            Ui.Place(line.rectTransform, new Vector2(0.5f, 1f), Vector2.zero);
            line.rectTransform.sizeDelta = new Vector2(3, Px(CardLift - 0.025f));
        }

        string HeaderText()
        {
            if (_timerEnd < 0) return _cardHeader;
            float left = Mathf.Max(0, _timerEnd - Time.time);
            string time = left > 0 ? $"{Mathf.FloorToInt(left / 60)}:{Mathf.FloorToInt(left % 60):00}" : "Time is up";
            return string.IsNullOrWhiteSpace(_cardHeader) ? time : _cardHeader + " · " + time;
        }

        void Update()
        {
            if (_cardPill == null || _timerEnd < 0) return;
            string text = HeaderText();
            if (_cardPill.text != text) _cardPill.text = text;
        }

        /// <summary>
        /// VP3/VP4 "Hint chip": a dark sage pill with a short action. Its left edge sits at <paramref name="position"/>.
        /// </summary>
        public static GameObject Chip(Transform parent, Vector3 position, string text, Transform head)
        {
            var holder = new GameObject("Hint chip").transform;
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) holder.gameObject.layer = ui;
            holder.SetParent(parent, false);
            holder.position = position;
            holder.gameObject.AddComponent<Billboard>().head = head;
            var canvas = Ui.WorldCanvas("Chip canvas", holder, new Vector2(10, 10), Scale, null);
            canvas.sortingOrder = 6;
            var pill = Ui.Pill(canvas.transform, text, Theme.White, Theme.WithAlpha(Theme.ChipDark, 0.94f), 20);
            var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pill.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(22, 22, 12, 12);
            Ui.Place(pill.rectTransform, new Vector2(0f, 0.5f), Vector2.zero);
            return holder.gameObject;
        }
    }
}
