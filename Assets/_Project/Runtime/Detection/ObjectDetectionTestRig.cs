using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HomeCare.AR;
using HomeCare.Procedures;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace HomeCare.Guidance
{
    /// <summary>
    /// Stand-alone test for real-object detection (3DGuidanceTestScene), without the care procedure flow.
    /// A small panel has one button per test prompt: pressing one captures the camera, asks Gemini for
    /// boxes and guidance for <see cref="stepInstruction"/>, and places the 3D cues in the room.
    /// Headset: poke a button or aim the hand ray and pinch. Editor: click, or keys 1-4 (prompts),
    /// F (<see cref="ObjectDetectionGuidance.testPrompt"/>), X clear, C move panel.
    /// Every request is saved (image + JSON) to persistentDataPath/HomeCareDetectionTests/.
    /// </summary>
    [RequireComponent(typeof(AfterStepCamera), typeof(HandTracking), typeof(ObjectDetectionGuidance))]
    public sealed class ObjectDetectionTestRig : MonoBehaviour
    {
        [Header("PC bridge (USB: adb reverse tcp:8765 tcp:8765)")]
        public string bridgeEndpoint = "http://127.0.0.1:8765/verify";
        public string geminiModel = "gemini-3.8-flash";

        [Header("Test")]
        [Tooltip("Step text sent with each request, so the guidance and the visual type follow it.")]
        [TextArea(2, 4)]
        public string stepInstruction = "Cover the burn with a non-stick pad, then wrap it loosely with the gauze roll.";
        [Tooltip("One panel button per prompt (first 4 also on keys 1-4). Mix plain names and vague requests.")]
        public string[] prompts = { "gauze roll", "non-stick pad", "the thing I cover the burn with", "medical tape" };
        public Material lineMaterial;

        [Header("Stand-in items (Editor)")]
        [Tooltip("Virtual supplies for testing in the Editor. Moved in front of the user at start.")]
        public Transform standInItems;
        [Tooltip("Show the virtual supplies on the headset too. Off: only real objects are in view.")]
        public bool standInItemsOnHeadset;

        ObjectDetectionGuidance _detector;
        AfterStepCamera _camera;
        HandTracking _hands;
        HandPointer _pointer;
        Transform _head;
        Camera _view;
        bool _flatScreen;
        CancellationTokenSource _lifetime;
        string _directory;
        ProcedureRequirements _procedure;
        ProcedureStep _step;

        RectTransform _panel;
        TMP_Text _status, _result, _hand;
        Button[] _promptButtons = Array.Empty<Button>();

        void Awake()
        {
            _lifetime = new CancellationTokenSource();
            _camera = GetComponent<AfterStepCamera>();
            _hands = GetComponent<HandTracking>();
            _detector = GetComponent<ObjectDetectionGuidance>();
            _pointer = TryGetComponent<HandPointer>(out var pointer) ? pointer : gameObject.AddComponent<HandPointer>();
            var manager = FindAnyObjectByType<OVRManager>();
            if (manager != null) manager.isInsightPassthroughEnabled = true;
        }

        IEnumerator Start()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            _head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor
                  : Camera.main != null ? Camera.main.transform : transform;
            _view = _head.TryGetComponent<Camera>(out var headCamera) ? headCamera : Camera.main;
            if (rig != null)
            {
                _hands.trackingSpace = rig.trackingSpace;
                if (FindAnyObjectByType<OVRPassthroughLayer>() == null) rig.gameObject.AddComponent<OVRPassthroughLayer>();
            }
            _hands.viewCamera = _view;
            _pointer.head = _head;
            _pointer.material = lineMaterial;
#if UNITY_EDITOR
            var simulator = GameObject.Find("XR Interaction Simulator");
            if (simulator != null) Destroy(simulator);
            _flatScreen = !UnityEngine.XR.XRSettings.isDeviceActive;
#endif
            if (_view != null)
            {
                _view.clearFlags = CameraClearFlags.SolidColor;
                _view.backgroundColor = Color.clear;
            }
            if (standInItems != null && !Application.isEditor && !standInItemsOnHeadset) standInItems.gameObject.SetActive(false);

            _detector.Init(_camera, lineMaterial, _head);
            _procedure = new ProcedureRequirements { procedure_title = "3D guidance test", instruction = stepInstruction };
            _step = new ProcedureStep { id = "test", title = "Test step", instruction = stepInstruction, requirements = Array.Empty<Requirement>() };
            _procedure.steps = new[] { _step };
            _directory = Path.Combine(Application.persistentDataPath, "HomeCareDetectionTests", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(_directory);
            Debug.Log("[HomeCare] Detection test results: " + _directory);

            BuildPanel();
            SetStatus("Press a prompt to find it in the camera view.", "Step: " + stepInstruction);
            yield return new WaitForSeconds(0.5f); // let head tracking settle
            PlaceStandInItems();
            Recenter();
        }

        void PlaceStandInItems()
        {
            if (standInItems == null || !standInItems.gameObject.activeSelf) return;
            var forward = Flat(_head.forward);
            var right = Vector3.Cross(Vector3.up, forward);
            var offset = _flatScreen ? new Vector3(0.1f, -0.3f, 0.75f) : new Vector3(0.15f, -0.55f, 0.45f);
            standInItems.SetPositionAndRotation(_head.position + right * offset.x + Vector3.up * offset.y + forward * offset.z,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        static Vector3 Flat(Vector3 v)
        {
            v = Vector3.ProjectOnPlane(v, Vector3.up);
            return v.sqrMagnitude < 0.01f ? Vector3.forward : v.normalized;
        }

        void Update()
        {
            if (_hand != null && _hand.text != _hands.Status) _hand.text = _hands.Status;
#if UNITY_EDITOR
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var number = new[] { keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key };
            for (int i = 0; i < number.Length && i < prompts.Length; i++)
                if (number[i].wasPressedThisFrame) Find(prompts[i]);
            if (keyboard.fKey.wasPressedThisFrame && !string.IsNullOrWhiteSpace(_detector.testPrompt)) Find(_detector.testPrompt);
            if (keyboard.xKey.wasPressedThisFrame) ClearAll();
            if (keyboard.cKey.wasPressedThisFrame) { Recenter(); PlaceStandInItems(); }
#endif
        }

        // ------------------------------------------------------------------ detection

        public async void Find(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !Application.isPlaying || _detector == null || _procedure == null) return;
            prompt = prompt.Trim();
            _step.instruction = _procedure.instruction = stepInstruction; // editable in Play mode
            SetBusy(true);
            SetStatus($"Looking for \"{prompt}\"... keep it in view.", "");
            var started = DateTime.UtcNow;
            var record = new DetectionEvent { at_utc = SessionReportWriter.Now(), request = prompt, source = "gemini" };
            DetectionOutcome outcome = null;
            try
            {
                outcome = await _detector.FindAsync(prompt, _procedure, _step, Array.Empty<string>(), bridgeEndpoint, geminiModel, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                record.error = e is OperationCanceledException ? "Timed out waiting for the camera or the PC bridge." : e.Message;
                Debug.LogWarning("[HomeCare] Detection test failed: " + record.error, this);
            }
            if (this == null || outcome is { Superseded: true }) return;
            SetBusy(false);
            float seconds = (float)(DateTime.UtcNow - started).TotalSeconds;
            Save(record, outcome, seconds);

            if (outcome == null)
            {
                SetStatus("Failed: " + record.error, "Is the PC bridge running (and adb reverse set on the headset)?");
                return;
            }
            var r = outcome.Result;
            var text = new StringBuilder();
            foreach (var o in r.objects)
            {
                var marker = outcome.Placed.FirstOrDefault(p => p.detection == o).marker;
                text.Append(o.found ? (o.role == "target" ? "<color=#FFC857>" : "<color=#7CFFA0>") : "<color=#FF9A8A>")
                    .Append("<noparse>").Append(o.label).Append("</noparse></color>")
                    .Append($"  <size=75%><color=#9BB0C0>\"<noparse>{o.query}</noparse>\" · {o.role}");
                if (marker != null)
                {
                    var b = marker.Box;
                    float distance = Vector3.Distance(outcome.CameraPosition, b.FrontCenter);
                    text.Append($" · {distance:F2} m · {b.size.x * 100:F0}x{b.size.y * 100:F0} cm · {b.depthSource}");
                }
                else if (!o.found) text.Append(" · not seen");
                text.Append("</color></size>\n");
            }
            if (r.objects.Count == 0) text.Append("<color=#FFC857>Nothing returned.</color>\n");
            text.Append($"\n<b>{r.visual_type}</b>{(r.timer_seconds > 0 ? $" ({r.timer_seconds} s)" : "")}: <noparse>{r.guidance_text}</noparse>");
            if (!string.IsNullOrWhiteSpace(r.caution)) text.Append($"\n<color=#FF9A8A>Caution: <noparse>{r.caution}</noparse></color>");
            SetStatus($"\"{prompt}\" · {seconds:F1} s · {outcome.FrameSource} · {outcome.DepthSamples} depth samples", text.ToString());
        }

        void Save(DetectionEvent record, DetectionOutcome outcome, float seconds)
        {
            string name = DateTime.UtcNow.ToString("HHmmss") + "_" + new string(record.request.Where(char.IsLetterOrDigit).Take(24).ToArray());
            try
            {
                if (outcome?.Jpeg != null)
                {
                    File.WriteAllBytes(Path.Combine(_directory, name + ".jpg"), outcome.Jpeg);
                    record.image_file = name + ".jpg";
                }
                if (outcome?.Result != null)
                {
                    record.frame_source = outcome.FrameSource;
                    record.depth_samples = outcome.DepthSamples;
                    record.visual_type = outcome.Result.visual_type;
                    record.guidance_text = outcome.Result.guidance_text;
                    record.caution = outcome.Result.caution;
                    foreach (var o in outcome.Result.objects)
                    {
                        var marker = outcome.Placed.FirstOrDefault(p => p.detection == o).marker;
                        record.objects.Add(new DetectionObjectReport
                        {
                            query = o.query, label = o.label, role = o.role, found = o.found, box_2d = o.box_2d,
                            depth_source = marker != null ? marker.Box.depthSource : "",
                            world_center = marker != null ? new[] { marker.Box.center.x, marker.Box.center.y, marker.Box.center.z } : null,
                            world_size = marker != null ? new[] { marker.Box.size.x, marker.Box.size.y, marker.Box.size.z } : null,
                        });
                    }
                }
                File.WriteAllText(Path.Combine(_directory, name + ".json"), JsonConvert.SerializeObject(new
                {
                    step_instruction = stepInstruction, model = geminiModel, seconds, detection = record,
                }, Formatting.Indented));
            }
            catch (IOException e) { Debug.LogWarning("[HomeCare] Could not save the detection test: " + e.Message, this); }
        }

        public void ClearAll()
        {
            _detector.Clear();
            SetBusy(false);
            SetStatus("Cleared. Press a prompt to find it in the camera view.", "Step: " + stepInstruction);
        }

        void SetBusy(bool busy)
        {
            foreach (var b in _promptButtons) if (b != null) b.interactable = !busy;
        }

        void SetStatus(string status, string result)
        {
            if (_status != null) _status.text = status;
            if (_result != null) _result.text = result;
        }

        void OnDestroy()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
        }

        // ------------------------------------------------------------------ panel

        const float Scale = 0.0006f; // metres per canvas pixel

        void BuildPanel()
        {
            var go = new GameObject("Detection Test Panel", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(GraphicRaycaster));
            _panel = go.GetComponent<RectTransform>();
            _panel.SetParent(transform, false);
            _panel.sizeDelta = new Vector2(760, 720);
            _panel.localScale = Vector3.one * Scale;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _view;
            go.GetComponent<Image>().color = new Color(0.025f, 0.045f, 0.075f, 0.94f);

            Label("Title", new Vector2(28, -20), new Vector2(480, 40), 26, new Color(0.55f, 0.85f, 1f)).text = "Object detection test";
            _hand = Label("Hand", new Vector2(500, -24), new Vector2(232, 34), 17, new Color(0.7f, 0.78f, 0.85f));
            _hand.alignment = TextAlignmentOptions.TopRight;
            _status = Label("Status", new Vector2(28, -68), new Vector2(704, 64), 20, Color.white);
            _result = Label("Result", new Vector2(28, -138), new Vector2(704, 300), 21, Color.white);
            _result.richText = true;

            int count = Mathf.Min(prompts.Length, 6);
            _promptButtons = new Button[count];
            for (int i = 0; i < count; i++)
            {
                string prompt = prompts[i];
                _promptButtons[i] = Button($"{i + 1}. {prompt}", new Vector2(28 + (i % 2) * 358, -452 - (i / 2) * 82), new Vector2(346, 72),
                    () => Find(prompt), new Color(0.1f, 0.45f, 0.25f, 1));
            }
            float y = -452 - ((count + 1) / 2) * 82 - 6;
            Button("Clear", new Vector2(28, y), new Vector2(346, 72), ClearAll, new Color(0.07f, 0.4f, 0.55f, 1));
            Button("Move panel here", new Vector2(386, y), new Vector2(346, 72), () => { Recenter(); PlaceStandInItems(); }, new Color(0.07f, 0.4f, 0.55f, 1));
            _panel.sizeDelta = new Vector2(760, -y + 72 + 24);
            SetLayer(_panel, LayerMask.NameToLayer("UI"));

            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("Detection EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        TMP_Text Label(string name, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var text = obj.GetComponent<TextMeshProUGUI>();
            Place(text.rectTransform, _panel, position, size);
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = color;
            text.richText = false;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        Button Button(string caption, Vector2 position, Vector2 size, Action action, Color color)
        {
            var obj = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
            Place((RectTransform)obj.transform, _panel, position, size);
            var image = obj.GetComponent<Image>();
            image.color = color;
            var button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            obj.AddComponent<HandPokeButton>();
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(obj.transform, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(10, 0);
            label.rectTransform.offsetMax = new Vector2(-10, 0);
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 22;
            label.color = Color.white;
            label.richText = false;
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.text = caption;
            return button;
        }

        /// <summary>UI layer: left out of Editor camera captures, like the headset camera which never sees it.</summary>
        internal static void SetLayer(Transform root, int layer)
        {
            if (layer < 0) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static void Place(RectTransform rect, Transform parent, Vector2 position, Vector2 size)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>Panel to the left in front, a little low, so the work area stays visible.</summary>
        void Recenter()
        {
            if (_panel == null || _head == null) return;
            var forward = Flat(_head.forward);
            var right = Vector3.Cross(Vector3.up, forward);
            var position = _flatScreen
                ? _head.position + forward * 0.9f + right * -0.32f + Vector3.up * 0.02f
                : _head.position + forward * 0.5f + right * -0.35f - Vector3.up * 0.15f;
            _panel.position = position;
            _panel.rotation = Quaternion.LookRotation(position - _head.position, Vector3.up);
        }
    }
}
