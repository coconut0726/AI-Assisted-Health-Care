using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HomeCare.Guidance
{
    /// <summary>
    /// World-space instruction panel sized for touching with a fingertip. It sits within arm's
    /// reach, slightly below eye level. Buttons respond to hand pokes (and mouse clicks in the Editor).
    /// </summary>
    public sealed class CarePanel
    {
        const float Scale = 0.0006f; // metres per canvas pixel: 900 x 1060 px ≈ 0.54 m x 0.64 m

        readonly RectTransform _root;
        readonly Canvas _canvas;
        readonly TMP_Text _progress, _status, _instruction, _items, _guidance, _feedback;
        readonly Image _guidanceBox;
        readonly Button _verify, _next, _restart, _find;
        readonly TMP_Text _verifyLabel, _nextLabel, _findLabel;

        public Transform Root => _root;

        public CarePanel(Transform parent, Action verify, Action next, Action restart, Action recenter, Action saveReport, Action findItems)
        {
            var panel = new GameObject("Care Panel", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(GraphicRaycaster));
            _root = panel.GetComponent<RectTransform>();
            _root.SetParent(parent, false);
            _root.sizeDelta = new Vector2(900, 1060);
            _root.localScale = Vector3.one * Scale;
            _canvas = panel.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 10;
            panel.GetComponent<Image>().color = new Color(0.025f, 0.045f, 0.075f, 0.94f);

            _progress = Label("Progress", _root, new Vector2(32, -22), new Vector2(560, 40), 24, new Color(0.55f, 0.85f, 1f));
            _status = Label("Hand status", _root, new Vector2(600, -26), new Vector2(268, 34), 17, new Color(0.7f, 0.78f, 0.85f));
            _status.alignment = TextAlignmentOptions.TopRight;
            _instruction = ScrollLabel("Instruction", new Vector2(32, -70), new Vector2(836, 200), 30, out _);
            _items = Label("Items", _root, new Vector2(32, -282), new Vector2(836, 80), 22, Color.white);
            _items.richText = true;

            _guidanceBox = Box("Guidance", new Vector2(32, -372), new Vector2(836, 170), new Color(0.12f, 0.3f, 0.2f, 0.85f));
            _guidance = Label("Guidance text", _guidanceBox.rectTransform, new Vector2(18, -12), new Vector2(800, 146), 24, Color.white);
            _guidance.richText = true;

            _feedback = ScrollLabel("Results", new Vector2(32, -556), new Vector2(836, 250), 22, out _);

            _verify = Button("Complete & Verify", new Vector2(32, -822), new Vector2(520, 100), verify, out _verifyLabel, 30);
            _next = Button("Next step", new Vector2(568, -822), new Vector2(300, 100), next, out _nextLabel, 30);
            _find = Button("Find items", new Vector2(32, -942), new Vector2(200, 90), findItems, out _findLabel, 24);
            _find.GetComponent<Image>().color = new Color(0.1f, 0.45f, 0.25f, 1);
            _restart = Button("Restart", new Vector2(244, -942), new Vector2(200, 90), restart, out _, 22);
            Button("Move panel", new Vector2(456, -942), new Vector2(200, 90), recenter, out _, 22);
            Button("Save report", new Vector2(668, -942), new Vector2(200, 90), saveReport, out _, 22);

            ObjectDetectionTestRig.SetLayer(_root, LayerMask.NameToLayer("UI"));

            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("Care EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        Image Box(string name, Vector2 position, Vector2 size, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = obj.GetComponent<RectTransform>();
            rect.SetParent(_root, false);
            Position(rect, position, size);
            var image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        TMP_Text ScrollLabel(string name, Vector2 position, Vector2 size, float fontSize, out ScrollRect scroll)
        {
            var scrollObject = new GameObject(name + " Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var scrollRect = scrollObject.GetComponent<RectTransform>();
            scrollRect.SetParent(_root, false);
            Position(scrollRect, position, size);
            scrollObject.GetComponent<Image>().color = new Color(1, 1, 1, 0.03f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(scrollRect, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(12, 8);
            viewportRect.offsetMax = new Vector2(-12, -8);
            var label = MakeLabel(name, viewportRect, fontSize, Color.white);
            var content = label.rectTransform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            label.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return label;
        }

        static TMP_Text Label(string name, RectTransform parent, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var label = MakeLabel(name, parent, fontSize, color);
            Position(label.rectTransform, position, size);
            return label;
        }

        static TMP_Text MakeLabel(string name, Transform parent, float fontSize, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = color;
            text.richText = false;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        Button Button(string label, Vector2 position, Vector2 size, Action action, out TMP_Text caption, float fontSize)
        {
            var obj = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = obj.GetComponent<RectTransform>();
            rect.SetParent(_root, false);
            Position(rect, position, size);
            var image = obj.GetComponent<Image>();
            image.color = new Color(0.07f, 0.4f, 0.55f, 1);
            var button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            obj.AddComponent<HandPokeButton>();
            caption = MakeLabel("Label", rect, fontSize, Color.white);
            caption.text = label;
            caption.alignment = TextAlignmentOptions.Center;
            caption.overflowMode = TextOverflowModes.Overflow;
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        static void Position(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public void Set(string progress, string instruction, string feedback, bool busy, bool canVerify,
            bool canAdvance, bool completed, bool lastStep)
        {
            _progress.text = progress;
            _instruction.text = instruction;
            _feedback.text = feedback;
            _verify.interactable = canVerify && !busy;
            _next.interactable = canAdvance && !busy;
            _restart.interactable = !busy;
            _verifyLabel.text = busy ? "Verifying..." : "Complete & Verify";
            _nextLabel.text = lastStep ? "Finish" : "Next step";
            _verify.gameObject.SetActive(!completed);
            _next.gameObject.SetActive(!completed);
            _feedback.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1;
            _instruction.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1;
        }

        /// <summary>Find items button: shows "Looking..." while a detection runs.</summary>
        public void SetFinding(bool finding, bool available)
        {
            _find.interactable = available && !finding;
            _findLabel.text = finding ? "Looking..." : "Find items";
        }

        public void SetStatus(string text)
        {
            if (_status.text != text) _status.text = text;
        }

        /// <summary>Rich text, e.g. a list of items with found / not found marks.</summary>
        public void SetItems(string richText) => _items.text = richText;

        public void SetGuidance(string richText, bool warning = false)
        {
            _guidance.text = richText;
            _guidanceBox.color = warning ? new Color(0.35f, 0.22f, 0.05f, 0.9f) : new Color(0.12f, 0.3f, 0.2f, 0.85f);
        }

        /// <summary>Place the panel within arm's reach: in front, a little low, tilted toward the eyes.</summary>
        public void Recenter(Camera camera, float distance, float drop, float side)
        {
            if (camera == null) return;
            _canvas.worldCamera = camera;
            var head = camera.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var position = head.position + forward * distance - Vector3.up * drop + right * side;
            _root.position = position;
            _root.rotation = Quaternion.LookRotation(position - head.position, Vector3.up);
        }
    }
}
