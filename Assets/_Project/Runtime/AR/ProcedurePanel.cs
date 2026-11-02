using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace HomeCare.AR
{
    // Creates a world-space panel compatible with the scene's existing XR UI interactors.
    public sealed class ProcedurePanel
    {
        readonly RectTransform _root;
        readonly Canvas _canvas;
        readonly TMP_Text _progress;
        readonly TMP_Text _instruction;
        readonly TMP_Text _feedback;
        readonly Button _verify;
        readonly Button _next;
        readonly Button _restart;
        readonly TMP_Text _verifyLabel;
        readonly TMP_Text _nextLabel;

        public ProcedurePanel(Transform parent, Action verify, Action next, Action restart, Action recenter)
        {
            var panel = new GameObject("Procedure Panel", typeof(RectTransform), typeof(Canvas), typeof(Image),
                typeof(TrackedDeviceGraphicRaycaster), typeof(GraphicRaycaster));
            _root = panel.GetComponent<RectTransform>();
            _root.SetParent(parent, false);
            _root.sizeDelta = new Vector2(920, 780);
            _root.localScale = Vector3.one * 0.0009f;
            _canvas = panel.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            panel.GetComponent<Image>().color = new Color(0.025f, 0.045f, 0.075f, 0.96f);
            _progress = Label("Progress", new Vector2(35, -28), new Vector2(850, 45), 26);
            _instruction = ScrollLabel("Instruction", new Vector2(35, -88), new Vector2(850, 185), 34);
            _feedback = ScrollLabel("Results", new Vector2(35, -290), new Vector2(850, 300), 27);

            _verify = Button("Complete & Verify", new Vector2(35, -610), new Vector2(540, 72), verify, out _verifyLabel);
            _next = Button("Next step", new Vector2(595, -610), new Vector2(290, 72), next, out _nextLabel);
            _restart = Button("Restart", new Vector2(35, -705), new Vector2(280, 48), restart, out _);
            Button("Recenter panel", new Vector2(595, -705), new Vector2(290, 48), recenter, out _);

            if (EventSystem.current == null)
                new GameObject("Home Care EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        }

        TMP_Text ScrollLabel(string name, Vector2 position, Vector2 size, float fontSize)
        {
            var scrollObject = new GameObject(name + " Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var scrollRect = scrollObject.GetComponent<RectTransform>();
            scrollRect.SetParent(_root, false);
            Position(scrollRect, position, size);
            scrollObject.GetComponent<Image>().color = new Color(1, 1, 1, 0.025f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(scrollRect, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            var label = MakeLabel(name, viewportRect, fontSize);
            var content = label.rectTransform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(-16, 0);
            var fitter = label.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            return label;
        }

        TMP_Text Label(string name, Vector2 position, Vector2 size, float fontSize)
        {
            var label = MakeLabel(name, _root, fontSize);
            Position(label.rectTransform, position, size);
            return label;
        }

        static TMP_Text MakeLabel(string name, Transform parent, float fontSize)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.richText = false;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        Button Button(string label, Vector2 position, Vector2 size, Action action, out TMP_Text caption)
        {
            var obj = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = obj.GetComponent<RectTransform>();
            rect.SetParent(_root, false);
            Position(rect, position, size);
            var image = obj.GetComponent<Image>();
            image.color = new Color(0.07f, 0.35f, 0.48f, 1);
            var button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.65f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            caption = MakeLabel("Label", rect, 27);
            caption.text = label;
            caption.alignment = TextAlignmentOptions.Center;
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
            var scroll = _feedback.GetComponentInParent<ScrollRect>();
            scroll.verticalNormalizedPosition = 1;
            _instruction.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1;
        }

        public void Recenter(Camera camera)
        {
            if (camera == null) return;
            _canvas.worldCamera = camera;
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            _root.position = camera.transform.position + forward * 1.3f + rotation * new Vector3(0.35f, -0.08f, 0);
            _root.rotation = rotation;
        }
    }
}
