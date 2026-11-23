using System.Collections.Generic;
using System.Linq;
using HomeCare.Guidance;
using HomeCare.Procedures.Actions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AHC.VR
{
    [RequireComponent(typeof(ActionGuidanceSession))]
    public sealed class ActionGuidanceController : MonoBehaviour
    {
        public float hudDistance = 1f, hudHeight = 0.08f;
        ActionGuidanceSession _care;
        Transform _head;
        RectTransform _hud;
        TextMeshProUGUI _status;
        Button _next;
        string _layoutKey;
        bool _dirty = true;
        GameObject _preview;
        Transform _pad, _block, _strap;
        float _previewSince;
        readonly Dictionary<string, TextMeshProUGUI> _labels = new();
        readonly List<Button> _confirmationButtons = new();
        readonly List<Button> _conditionalButtons = new();

        void Start()
        {
            _care = GetComponent<ActionGuidanceSession>(); _care.Changed += MarkDirty;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            _head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : transform;
#if UNITY_EDITOR
            if (_care.CameraSource.captureViewCameraInEditor && !UnityEngine.XR.XRSettings.isDeviceActive && _head.position.y < 0.5f)
            {
                // No headset pose: keep the virtual tabletop above the copied room's floor.
                var origin = rig != null ? rig.transform : _head;
                origin.position += Vector3.up * (1.6f - _head.position.y);
            }
#endif
            var camera = _head.TryGetComponent<Camera>(out var headCamera) ? headCamera : Camera.main;
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var hands = TryGetComponent<HandTracking>(out var trackedHands) ? trackedHands : gameObject.AddComponent<HandTracking>();
            if (rig != null) hands.trackingSpace = rig.trackingSpace;
            hands.viewCamera = camera;
            var pointer = TryGetComponent<HandPointer>(out var handPointer) ? handPointer : gameObject.AddComponent<HandPointer>();
            pointer.head = _head; pointer.material = _care.lineMaterial;
            var follow = new GameObject("Action Flow HUD").AddComponent<LazyFollow>();
            follow.transform.SetParent(transform, false); follow.head = _head; follow.distance = hudDistance; follow.heightOffset = hudHeight;
            follow.onWall = true; follow.panelHeight = 900 * 0.001f;
            follow.gameObject.layer = LayerMask.NameToLayer("UI");
            var canvas = Ui.WorldCanvas("Action Requirements", follow.transform, new Vector2(1440, 1000), 0.001f, camera);
            canvas.gameObject.AddComponent<GraphicRaycaster>(); _hud = (RectTransform)canvas.transform;
            Refresh();
        }
        void OnDestroy() { if (_care != null) _care.Changed -= MarkDirty; }
        void MarkDirty() => _dirty = true;
        void Update()
        {
            if (_care == null) return;
            var kb = Keyboard.current;
            if (OVRInput.GetDown(OVRInput.Button.One) || (kb != null && kb.spaceKey.wasPressedThisFrame))
            { if (!_care.Started) _care.Begin(); else if (_care.Session?.CanAdvance == true) _care.Next(); else _care.VerifyNow(); }
            if (kb != null && kb.pKey.wasPressedThisFrame && _care.Started) _care.TogglePause();
            if (kb != null && kb.rKey.wasPressedThisFrame && _care.Started && _care.Session?.Completed == false) _care.Retry();
            if (Application.isEditor && _care.CameraSource.captureViewCameraInEditor && _care.Session?.Procedure.procedure_title == "Tabletop requirement flow")
            {
                if (_preview == null && HeadPose.Ready(_head, _previewSince)) BuildPreview();
            }
            else if (_preview != null) { Destroy(_preview); _preview = null; }
            if (_dirty) Refresh();
        }
        void Refresh()
        {
            _dirty = false;
            var session = _care.Session;
            string key = session == null ? _care.LoadError : session.RunId + ":" + session.ActionIndex + ":" + session.Phase + ":" + _care.Started + ":" + session.Applicability;
            if (key != _layoutKey || _status == null) { _layoutKey = key; Build(); }
            if (_status == null) return;
            _status.text = (_care.Paused ? "PAUSED · " : "") + _care.Status + "\n" + (_care.LastProvider ?? "Qwen / selected project backend") +
                (Application.isEditor && _care.CameraSource.editorTestImage != null ? " · EDITOR TEST IMAGE" :
                 _care.CameraSource.captureViewCameraInEditor && Application.isEditor ? " · EDITOR VIRTUAL CAMERA" : " · HEADSET RGB");
            if (_next != null) _next.interactable = !_care.Paused && session?.CanAdvance == true;
            foreach (var button in _conditionalButtons) button.interactable = !session.HasLatchedViolation;
            foreach (var pair in _labels)
            {
                var result = session.Result(pair.Key);
                pair.Value.text = (result?.status ?? "unknown") + (_care.Session.IsHumanConfirmed(pair.Key) ? " · operator confirmed" : "") +
                    (string.IsNullOrEmpty(result?.reason) ? "" : " — " + result.reason);
                pair.Value.color = result?.status == ActionRequirementSession.Yes ? Theme.PrimaryDark : result?.status == ActionRequirementSession.No ? Theme.Red : Theme.Muted;
            }
        }
        void Build()
        {
            Ui.Clear(_hud); _labels.Clear(); _confirmationButtons.Clear(); _conditionalButtons.Clear(); _next = null;
            var panel = Ui.Card(_hud, "Phase-aware guidance", 1020, Theme.Panel, new RectOffset(28, 28, 24, 24), 10);
            Ui.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, 1020);
            Ui.PillRow(panel.transform, "ACTION REQUIREMENTS · 1 s polling slots", Theme.PrimaryDark, Theme.PrimaryBg);
            if (_care.Session == null) { _status = Ui.Text(panel.transform, _care.LoadError, 20, Theme.Red); return; }
            var s = _care.Session;
            Ui.Text(panel.transform, s.Procedure.procedure_title, 26, Theme.Text, true);
            if (!_care.Started)
            {
                Ui.Text(panel.transform, "Task goal: " + s.Procedure.task_goal, 19, Theme.Text);
                Ui.Text(panel.transform, "Preconditions → Action completion + execution rules. Unknown never unlocks Next. Operator confirmations are recorded separately from VLM results.", 17, Theme.Muted);
                var cases = Ui.Row(panel.transform, "Examples", 8);
                foreach (var asset in _care.examples.Where(a => a != null))
                {
                    var local = asset;
                    Ui.Button(cases.transform, asset.name.Replace("_action_requirements", "").Replace("_", " "), false, () => _care.Load(local));
                }
                Ui.Button(panel.transform, "Begin", true, _care.Begin, "A");
            }
            else if (s.Completed)
            {
                Ui.Note(panel.transform, "All Actions finished. Model confirmations and operator confirmations remain distinguishable in the history.", Theme.PrimaryDark, Theme.PrimaryBg);
                Ui.Text(panel.transform, _care.ReportPath ?? "", 14, Theme.Muted);
                Ui.Button(panel.transform, "New run", true, _care.Restart);
            }
            else
            {
                Ui.Text(panel.transform, $"Step {s.StepIndex + 1}/{s.Procedure.steps.Length}: {s.CurrentStep.step_name} · Action {s.ActionIndex + 1}/{s.ActionCount}", 17, Theme.Muted);
                Ui.Text(panel.transform, s.CurrentAction.instruction, 23, Theme.Text, true);
                Ui.PillRow(panel.transform, s.Phase == ActionCheckPhase.Preconditions ? "BEFORE THE ACTION" : s.Phase == ActionCheckPhase.Applicability ? "DOES THE CONDITION APPLY?" : "COMPLETION + EXECUTION RULES", Theme.Lav, Theme.LavBg);
                if (s.ConditionalActions.Any(a => a.action_id != s.CurrentAction.action_id))
                {
                    var conditional = Ui.Row(panel.transform, "Conditional Actions", 8);
                    foreach (var a in s.ConditionalActions.Where(a => a.action_id != s.CurrentAction.action_id))
                    {
                        string id = a.action_id;
                        _conditionalButtons.Add(Ui.Button(conditional.transform, id + " · " + a.instruction.Substring(0, Mathf.Min(26, a.instruction.Length)), false,
                            () => _care.SelectConditionalAction(id)));
                    }
                }
                if (s.Phase == ActionCheckPhase.Applicability)
                {
                    Ui.Text(panel.transform, s.CurrentAction.applicability_condition, 19, Theme.Text);
                    var row = Ui.Row(panel.transform, "Applicability", 12);
                    Ui.Button(row.transform, "Condition applies", false, () => _care.ConfirmApplicability(true));
                    Ui.Button(row.transform, "Does not apply", false, () => _care.ConfirmApplicability(false));
                }
                Transform checksParent = RequirementsArea(panel.transform, s.CurrentChecks.Length);
                foreach (var q in s.CurrentChecks)
                {
                    var row = Ui.Card(checksParent, q.requirement_id, 0, Theme.Surface, new RectOffset(12, 12, 8, 8), 4, 12);
                    Ui.Text(row.transform, q.category + " · " + q.requirement_id + (q.requires_expert_review ? " · author review required" : ""), 13, Theme.Muted);
                    Ui.Text(row.transform, q.condition, 18, Theme.Text);
                    _labels[q.requirement_id] = Ui.Text(row.transform, "unknown", 14, Theme.Muted);
                    string id = q.requirement_id;
                    var buttons = Ui.Row(row.transform, "Operator confirmation", 10);
                    _confirmationButtons.Add(Ui.Button(buttons.transform, "Confirm myself", false, () => _care.Confirm(id)));
                    if (!string.IsNullOrWhiteSpace(q.applicability_condition))
                        _confirmationButtons.Add(Ui.Button(buttons.transform, "Not applicable", false, () => _care.Confirm(id, true)));
                }
                if (!s.DependenciesMet() && s.Applicability != ActionRequirementSession.NotApplicable)
                    Ui.Note(panel.transform, "A graph prerequisite has not been confirmed. The session will not infer it from the goal or a missing observation.", Theme.Amber, Theme.AmberBg);
                var controls = Ui.Row(panel.transform, "Controls", 10);
                Ui.Button(controls.transform, "Check now", false, _care.VerifyNow);
                Ui.Button(controls.transform, "Retry Action", false, _care.Retry, "R");
                Ui.Button(controls.transform, "Pause / resume", false, _care.TogglePause, "P");
                _next = Ui.Button(controls.transform, "Next Action", true, _care.Next, "A");
                Ui.Button(panel.transform, "Restart procedure", false, _care.Restart);
                if (Application.isEditor && _care.CameraSource.captureViewCameraInEditor && s.Procedure.procedure_title == "Tabletop requirement flow")
                    Ui.Button(panel.transform, "Editor: simulate current Action (camera must still verify)", false, Simulate);
            }
            _status = Ui.Text(panel.transform, "", 15, Theme.Muted);
        }

        Transform RequirementsArea(Transform parent, int count)
        {
            if (count <= 2) return parent;
            var navigation = Ui.Row(parent, "Scroll requirements", 12);
            var viewport = Ui.Rect("Requirements viewport", parent);
            Ui.Fixed(viewport.gameObject, -1, 400);
            var background = viewport.gameObject.AddComponent<Image>(); background.color = Theme.Surface;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Requirements content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            Ui.VStack(content.gameObject, new RectOffset(), 8);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            Ui.Button(navigation.transform, "Requirements ↑", false, () => scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + 0.4f));
            Ui.Button(navigation.transform, "Requirements ↓", false, () => scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition - 0.4f));
            return content;
        }

        void BuildPreview()
        {
            _preview = new GameObject("Action Flow Editor Props");
            Vector3 forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            _preview.transform.SetPositionAndRotation(_head.position + forward * 0.75f + Vector3.down * 0.22f, Quaternion.LookRotation(forward));
            Make("Tray", new Vector3(0, 0, 0), new Vector3(0.36f, 0.02f, 0.25f), new Color(0.72f, 0.72f, 0.72f));
            _pad = Make("Blue pad", new Vector3(-0.27f, 0.02f, 0), new Vector3(0.16f, 0.012f, 0.14f), Color.blue);
            _block = Make("Red block", new Vector3(0.27f, 0.04f, 0), new Vector3(0.07f, 0.07f, 0.07f), Color.red);
            _strap = new GameObject("Yellow strap").transform; _strap.SetParent(_preview.transform, false);
            _strap.localPosition = new Vector3(0, 0.03f, 0.2f);
            var band = Make("Strap length", Vector3.zero, new Vector3(0.1f, 0.005f, 0.016f), Color.yellow);
            band.SetParent(_strap, false);
        }
        Transform Make(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(_preview.transform, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().material.color = color; return go.transform;
        }
        void Simulate()
        {
            if (_preview == null) BuildPreview();
            switch (_care.Session.CurrentAction.action_id)
            {
                case "A1": _pad.localPosition = new Vector3(0, 0.016f, 0); break;
                case "A2": _block.localPosition = new Vector3(0, 0.057f, 0); break;
                case "A3":
                    _strap.localPosition = Vector3.zero;
                    foreach (Transform child in _strap) Destroy(child.gameObject);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var band = Make("Wrapped strap", new Vector3(side * 0.037f, 0.057f, 0), new Vector3(0.006f, 0.08f, 0.018f), Color.yellow);
                        band.SetParent(_strap, true);
                    }
                    var top = Make("Strap over block", new Vector3(0, 0.096f, 0), new Vector3(0.08f, 0.006f, 0.018f), Color.yellow);
                    top.SetParent(_strap, true);
                    break;
            }
            // Simulation changes only the rendered objects. It never writes a verification result.
        }
    }
}
