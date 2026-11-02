using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AHC.VR;
using HomeCare.AR;
using HomeCare.Procedures;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace HomeCare.Guidance
{
    /// <summary>
    /// V2 care guidance, hands only (no controllers):
    /// <list type="bullet">
    /// <item>Shows the reviewed nurse procedure step by step on a panel you press with your fingertip.</item>
    /// <item>Finds the items the current step mentions among the scene's <see cref="CareItem"/>s and draws a box around them.</item>
    /// <item>When you pick an item up (pinch or fist), asks Gemini through the PC bridge how to use it for this step
    ///       and shows the answer as text plus an arrow to where it goes.</item>
    /// <item>Complete &amp; Verify captures the passthrough camera and checks the step's requirements (verification-test flow).</item>
    /// <item>Writes a session report (JSON + Markdown) after every change.</item>
    /// </list>
    /// Gestures: poke a button to press it · pinch or make a fist next to an item to hold it ·
    /// pinch with the left hand in empty air for about a second to bring the panel in front of you.
    /// Editor: mouse clicks the buttons and drags items; Enter verify · N next · R restart · C recenter · S save report.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(AfterStepCamera), typeof(HandTracking), typeof(HandGrabber))]
    public sealed class CareGuidanceController : MonoBehaviour
    {
        [Header("Procedure (nurse instruction reviewed in the Requirements Prototype)")]
        public TextAsset procedureJson;
        [TextArea(2, 4)]
        public string completionNote = "Call your nurse if the burn becomes more red or swollen, starts to smell or leak pus, or if you develop a fever.";

        [Header("PC bridge (USB: adb reverse tcp:8765 tcp:8765)")]
        public string bridgeEndpoint = "http://127.0.0.1:8765/verify";
        public string geminiModel = "gemini-3.8-flash";
        [Tooltip("Attach the current passthrough camera frame when asking for guidance about a held item.")]
        public bool sendCameraFrameWithGuidance = true;

        [Header("Items")]
        [Tooltip("Parent of the scene's CareItems. Moved in front of the user at start when enabled below.")]
        public Transform itemsRoot;
        public bool placeItemsInFront = true;
        [Tooltip("Where the items start relative to the eyes: right, up, forward (metres).")]
        public Vector3 itemsOffset = new(0.15f, -0.55f, 0.45f);
        [Tooltip("Supplies an instruction may name. Used to report items that are mentioned but missing from the scene.")]
        public ItemVocabulary[] vocabulary;
        [Tooltip("Unlit material for highlight boxes and arrows.")]
        public Material lineMaterial;

        [Header("Panel placement (metres)")]
        public float panelDistance = 0.45f;
        public float panelDrop = 0.2f;
        public float panelSide = -0.32f;
        public float recenterPinchSeconds = 1.2f;

        [Header("Optional")]
        [Tooltip("The AI character (Assets/_Project/Art/Ghost/Ghost.fbx). Speaks each step and each piece of guidance.")]
        public GameObject ghostModel;

        ProcedureSession _session;
        CarePanel _panel;
        AfterStepCamera _camera;
        HandTracking _hands;
        HandGrabber _grabber;
        HandPointer _pointer;
        GhostCompanion _ghost;
        bool _flatScreen;
        Transform _head;
        Camera _view;
        CancellationTokenSource _lifetime;
        string _sessionDirectory;
        SessionReportWriter _report;
        string _feedback = "Perform the instruction, then press Complete & Verify.";
        bool _verifying, _recenterLatched;

        Transform _highlightRoot;
        readonly Dictionary<CareItem, ItemHighlight> _highlights = new();
        readonly List<CareItem> _mentioned = new();
        GuidanceArrow _arrow;
        CareItem _guidanceTarget;
        readonly Dictionary<string, GuidanceResponse> _guidanceCache = new();
        int _guidanceRequest;
        int _renderedStep = -1;

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            _lifetime = new CancellationTokenSource();
            _camera = GetComponent<AfterStepCamera>();
            _hands = GetComponent<HandTracking>();
            _grabber = GetComponent<HandGrabber>();
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
            // The XR Interaction Toolkit auto-spawns its device simulator in the Editor; it takes over
            // the mouse and keyboard, and this scene does not use XRI.
            var simulator = GameObject.Find("XR Interaction Simulator");
            if (simulator != null) Destroy(simulator);
            _flatScreen = !UnityEngine.XR.XRSettings.isDeviceActive;
#endif
            if (_view != null)
            {
                _view.clearFlags = CameraClearFlags.SolidColor;
                _view.backgroundColor = Color.clear;
            }

            _panel = new CarePanel(transform, CompleteAndVerify, NextStep, RestartProcedure, RecenterPanel, SaveReport);
            _highlightRoot = new GameObject("Item Highlights").transform;
            _highlightRoot.SetParent(transform, false);
            _arrow = GuidanceArrow.Create(transform, lineMaterial);
            _grabber.Grabbed += OnGrabbed;
            _grabber.Released += OnReleased;
            if (ghostModel != null) _ghost = GhostCompanion.Spawn(ghostModel, transform, _head);

            try
            {
                if (procedureJson == null) throw new InvalidOperationException("Assign a reviewed procedure JSON in the Inspector.");
                _session = new ProcedureSession(RequirementsJson.Parse(procedureJson.text));
                NewSessionDirectory();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _feedback = "The procedure could not be loaded. Check the procedure setup before starting.";
            }

            Render();
            // Let head tracking settle before placing things relative to the eyes.
            yield return new WaitForSeconds(0.5f);
            PlaceItems();
            RecenterPanel();
        }

        void PlaceItems()
        {
            if (!placeItemsInFront || itemsRoot == null) return;
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            // Without a headset you cannot look down, so keep the tray inside the Game view.
            var offset = _flatScreen ? new Vector3(0.12f, -0.3f, 0.75f) : itemsOffset;
            itemsRoot.SetPositionAndRotation(
                _head.position + right * offset.x + Vector3.up * offset.y + forward * offset.z,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        public void RecenterPanel()
        {
            if (_flatScreen) _panel?.Recenter(_view, 0.85f, 0.02f, -0.3f); // whole panel visible in the Game view
            else _panel?.Recenter(_view, panelDistance, panelDrop, panelSide);
        }

        void NewSessionDirectory()
        {
            _sessionDirectory = Path.Combine(Application.persistentDataPath, "HomeCareSessions",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_sessionDirectory);
            _report = new SessionReportWriter(_sessionDirectory, _session.Procedure, geminiModel);
            _report.Save();
            _guidanceCache.Clear();
            Debug.Log("[HomeCare] Session report: " + _report.MarkdownPath);
        }

        // ------------------------------------------------------------------ gestures and keys

        void Update()
        {
            _panel?.SetStatus(_hands.Status);

            // Pinch with the left hand in empty air and hold: the panel comes to you.
            var left = _hands.Left;
            if (left.IsTracked && !left.IsSimulated && left.IsPinching && _grabber.HeldBy(HandSide.Left) == null)
            {
                if (!_recenterLatched && left.PinchHeldFor >= recenterPinchSeconds)
                {
                    _recenterLatched = true;
                    RecenterPanel();
                }
            }
            else _recenterLatched = false;

#if UNITY_EDITOR
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.enterKey.wasPressedThisFrame) CompleteAndVerify();
            if (keyboard.nKey.wasPressedThisFrame) NextStep();
            if (keyboard.rKey.wasPressedThisFrame) RestartProcedure();
            if (keyboard.cKey.wasPressedThisFrame) { RecenterPanel(); PlaceItems(); }
            if (keyboard.sKey.wasPressedThisFrame) SaveReport();
#endif
        }

        // ------------------------------------------------------------------ items mentioned by the step

        static string StepText(ProcedureStep step) => step.title + "\n" + step.instruction;

        void RefreshStepItems()
        {
            _mentioned.Clear();
            _guidanceTarget = null;
            _arrow.Hide();
            var presence = new List<ItemPresence>();
            bool completed = _session == null || _session.State == ProcedureSessionState.Completed;
            if (!completed)
            {
                string text = StepText(_session.CurrentStep);
                var sceneItems = CareItem.FindAllInScene();
                foreach (var item in sceneItems)
                    if (item.IsMentionedIn(text)) _mentioned.Add(item);

                var seen = new HashSet<string>();
                if (vocabulary != null)
                    foreach (var word in vocabulary)
                    {
                        if (word == null || !KeywordMatcher.Matches(text, word.keywords) || !seen.Add(word.itemId)) continue;
                        bool found = sceneItems.Any(i => i.itemId == word.itemId);
                        presence.Add(new ItemPresence { item_id = word.itemId, name = word.displayName, found_in_scene = found });
                    }
                foreach (var item in _mentioned)
                    if (seen.Add(item.itemId))
                        presence.Add(new ItemPresence { item_id = item.itemId, name = item.Name, found_in_scene = true });
            }

            if (presence.Count == 0) _panel.SetItems(completed ? "" : "<color=#9BB0C0>No supplies named in this step.</color>");
            else
            {
                var line = new StringBuilder("<color=#8FD8FF>Items for this step:</color>  ");
                line.Append(string.Join("   ", presence.Select(p => p.found_in_scene
                    ? $"<color=#7CFFA0><noparse>{p.name}</noparse></color>"
                    : $"<color=#FFC857><noparse>{p.name}</noparse> (not found)</color>")));
                _panel.SetItems(line.ToString());
            }

            if (_report != null && !completed)
            {
                var step = _report.Step(_session.CurrentStep.id);
                step.mentioned_items = presence;
                step.started_at_utc ??= SessionReportWriter.Now();
                if (step.final_status == "not_started") step.final_status = "in_progress";
                _report.Save();
            }
            _panel.SetGuidance(completed ? "" : "<color=#C8D6E0>Pick up an item (pinch or grab it) to get guidance on how to use it.</color>");
            UpdateHighlights();
        }

        void UpdateHighlights()
        {
            foreach (var item in CareItem.FindAllInScene())
            {
                if (!_highlights.TryGetValue(item, out var highlight) || highlight == null)
                    _highlights[item] = highlight = ItemHighlight.Create(item, _highlightRoot, lineMaterial, _head);
                var state = _grabber.IsHeld(item) ? HighlightState.Held
                    : item == _guidanceTarget ? HighlightState.Target
                    : _mentioned.Contains(item) ? HighlightState.Mentioned
                    : HighlightState.None;
                highlight.SetState(state);
            }
        }

        // ------------------------------------------------------------------ guidance for a held item

        void OnGrabbed(CareItem item, TrackedHand hand)
        {
            UpdateHighlights();
            RequestGuidance(item);
        }

        void OnReleased(CareItem item, TrackedHand hand)
        {
            if (_arrow.Visible) _arrow.Hide();
            UpdateHighlights();
        }

        async void RequestGuidance(CareItem item)
        {
            if (_session == null || _session.State == ProcedureSessionState.Completed) return;
            var step = _session.CurrentStep;
            bool mentioned = _mentioned.Contains(item);
            string key = step.id + "/" + item.itemId;
            int request = ++_guidanceRequest;
            string source = "gemini";

            if (!_guidanceCache.TryGetValue(key, out var guidance))
            {
                _panel.SetGuidance($"Holding <b><noparse>{item.Name}</noparse></b>. Getting guidance…");
                try
                {
                    byte[] jpeg = null;
                    if (sendCameraFrameWithGuidance && !_verifying)
                    {
                        using var captureTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                        captureTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                        try { jpeg = await _camera.CaptureJpegAsync(captureTimeout.Token); }
                        catch (Exception e) when (!_lifetime.IsCancellationRequested)
                        {
                            Debug.Log("[HomeCare] Guidance without a camera frame: " + e.Message);
                        }
                    }
                    string body = GuidanceProtocol.CreateRequest(_session.Procedure, step, item, CareItem.FindAllInScene(), jpeg, geminiModel);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    guidance = GuidanceProtocol.Parse(await VerificationBridgeClient.PostAsync(bridgeEndpoint, body, timeout.Token));
                    _guidanceCache[key] = guidance;
                }
                catch (Exception e) when (!_lifetime.IsCancellationRequested)
                {
                    Debug.LogWarning("[HomeCare] AI guidance unavailable, using the step text instead: " + e.Message, this);
                    guidance = GuidanceProtocol.Offline(step, item, mentioned, FindItem("burn"));
                    source = "offline";
                }
                catch (OperationCanceledException) { return; }
            }
            else source = "gemini (cached)";

            if (this == null || _session == null) return;
            var stepReport = _report?.Step(step.id);
            stepReport?.guidance.Add(new GuidanceEvent
            {
                at_utc = SessionReportWriter.Now(), held_item_id = item.itemId, held_item_name = item.Name, source = source,
                item_used_in_step = guidance.item_used_in_step, guidance_text = guidance.guidance_text, caution = guidance.caution,
                target_item_id = guidance.target_item_id, visual_cue = guidance.visual_cue,
            });
            _report?.Save();

            // Ignore answers that arrive after the user moved on.
            if (request != _guidanceRequest || _session.State == ProcedureSessionState.Completed || _session.CurrentStep != step) return;
            ShowGuidance(item, guidance, source);
        }

        void ShowGuidance(CareItem item, GuidanceResponse g, string source)
        {
            var text = new StringBuilder();
            text.Append($"<b><noparse>{item.Name}</noparse></b>  <size=70%><color=#9BB0C0>({source})</color></size>\n");
            if (!g.item_used_in_step) text.Append("<color=#FFC857>Not needed for this step.</color> ");
            text.Append("<noparse>").Append(g.guidance_text).Append("</noparse>");
            if (!string.IsNullOrWhiteSpace(g.caution))
                text.Append("\n<color=#FF9A8A>Caution: <noparse>").Append(g.caution).Append("</noparse></color>");
            _panel.SetGuidance(text.ToString(), !g.item_used_in_step);

            var target = g.target_item_id == "none" ? null : FindItem(g.target_item_id);
            _guidanceTarget = g.visual_cue != "none" && target != item ? target : null;
            if (_guidanceTarget != null && g.visual_cue == "arrow_to_target" && _grabber.IsHeld(item)) _arrow.Show(item, _guidanceTarget);
            else _arrow.Hide();
            UpdateHighlights();
            if (_ghost != null) _ghost.Say(g.guidance_text);
        }

        static CareItem FindItem(string id) =>
            string.IsNullOrEmpty(id) ? null : CareItem.FindAllInScene().FirstOrDefault(i => i.itemId == id);

        // ------------------------------------------------------------------ verification (verification-test flow)

        public async void CompleteAndVerify()
        {
            if (_session == null || (_session.State != ProcedureSessionState.Ready && _session.State != ProcedureSessionState.NeedsRetry)) return;
            _session.BeginVerification();
            _verifying = true;
            _feedback = "Capturing the result. Keep the relevant objects in view.";
            Render();
            var step = _session.CurrentStep;
            var attempt = new AttemptReport { at_utc = SessionReportWriter.Now() };
            try
            {
                var cancellation = _lifetime.Token;
                byte[] jpeg = await _camera.CaptureJpegAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                string basename = (_camera.IsEditorTest ? "editor_test" : "headset") + "_step" + (_session.StepIndex + 1) + "_" +
                                  Guid.NewGuid().ToString("N").Substring(0, 10);
                string imagePath = Path.Combine(_sessionDirectory, basename + ".jpg");
                File.WriteAllBytes(imagePath, jpeg);
                attempt.image_file = Path.GetFileName(imagePath);
                var attachments = new[] { new EvidenceAttachment { scope = "after_step", path = imagePath } };
                string request = GeminiVerificationProtocol.CreateAfterImageRequest(_session.Procedure, step, jpeg,
                    Path.GetFileName(imagePath), geminiModel);
                _feedback = "Checking the result...";
                Render();
                var response = await VerificationBridgeClient.VerifyAsync(bridgeEndpoint, request, cancellation);
                cancellation.ThrowIfCancellationRequested();
                _session.AcceptVerification(response, attachments);
                _feedback = ResultText();
                attempt.overall_status = _session.OverallStatus;
                for (int i = 0; i < _session.Results.Length; i++)
                {
                    var r = _session.Results[i];
                    var q = step.requirements[i];
                    attempt.results.Add(new RequirementReport
                    {
                        requirement_id = r.requirement_id, description = q.description, evidence_scope = q.evidence_scope,
                        status = r.status, evidence = r.evidence, reason = r.reason,
                    });
                }
                // Same per-attempt file the verification-test branch writes.
                var verification = new VerificationReport
                {
                    verified_at_utc = DateTime.UtcNow.ToString("o"), provider = "Gemini", model = geminiModel,
                    procedure = _session.Procedure, step_id = step.id, attachments = attachments,
                    overall_status = _session.OverallStatus, results = _session.Results,
                };
                try { File.WriteAllText(Path.Combine(_sessionDirectory, basename + ".json"), RequirementsJson.Serialize(verification)); }
                catch (IOException e) { Debug.LogWarning("Verification succeeded but its report could not be saved: " + e.Message, this); }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                _session.FailVerification();
                attempt.overall_status = RequirementVerification.Unknown;
                attempt.error = exception.Message;
                _feedback = "Could not complete verification. Check the camera and the PC bridge connection, then try again.";
                Debug.LogException(exception, this);
            }
            finally { _verifying = false; }

            if (this == null) return;
            var stepReport = _report.Step(step.id);
            stepReport.attempts.Add(attempt);
            stepReport.final_status = StatusName(attempt.overall_status);
            _report.Save();
            Render();
        }

        static string StatusName(string overall) => overall switch
        {
            RequirementVerification.Satisfied => "passed",
            RequirementVerification.NotSatisfied => "not_met",
            _ => "not_verified",
        };

        string ResultText()
        {
            var text = new StringBuilder();
            text.AppendLine(_session.CanAdvance ? "Passed. You can continue." :
                _session.OverallStatus == RequirementVerification.NotSatisfied ? "Some checks were not satisfied. Review them and try again." :
                "More evidence is needed. This step has not passed.");
            for (int i = 0; i < _session.Results.Length; i++)
            {
                var result = _session.Results[i];
                var requirement = _session.CurrentStep.requirements[i];
                string state = result.status == RequirementVerification.Satisfied ? "PASS" :
                    result.status == RequirementVerification.NotSatisfied ? "NOT MET" : "NOT VERIFIED";
                text.Append("\n").Append(state).Append(": ").AppendLine(requirement.description);
                if (requirement.evidence_scope != "after_step")
                    text.AppendLine("This check needs evidence from before or during the action, or another observation method.");
                text.AppendLine(result.reason);
            }
            return text.ToString();
        }

        public void NextStep()
        {
            if (_session == null || !_session.CanAdvance) return;
            var stepReport = _report.Step(_session.CurrentStep.id);
            stepReport.passed_at_utc = SessionReportWriter.Now();
            _session.Advance();
            bool completed = _session.State == ProcedureSessionState.Completed;
            if (completed)
            {
                _report.Report.completed = true;
                _report.Report.finished_at_utc = SessionReportWriter.Now();
            }
            _report.Save();
            _feedback = completed
                ? "All steps have passed. Procedure complete.\n\n" + completionNote + "\n\nReport saved:\n" + _report.MarkdownPath
                : "Perform the instruction, then press Complete & Verify.";
            Render();
        }

        public void RestartProcedure()
        {
            if (_session == null || _session.State == ProcedureSessionState.Verifying) return;
            _session.Restart();
            NewSessionDirectory();
            _renderedStep = -1;
            _feedback = "New run. Perform the instruction, then press Complete & Verify.";
            Render();
        }

        public void SaveReport()
        {
            if (_report == null) return;
            _report.Save();
            Debug.Log("[HomeCare] Session report saved: " + _report.MarkdownPath);
            if (_session != null && _session.State != ProcedureSessionState.Verifying)
            {
                _feedback = "Report saved:\n" + _report.MarkdownPath + "\n\n" + _feedback;
                Render();
            }
        }

        void Render()
        {
            if (_panel == null) return;
            if (_session == null)
            {
                _panel.Set("Home Care", "Procedure unavailable", _feedback, false, false, false, false, false);
                return;
            }
            bool completed = _session.State == ProcedureSessionState.Completed;
            bool busy = _session.State == ProcedureSessionState.Verifying;
            string progress = _session.Procedure.procedure_title + "  |  Step " + (_session.StepIndex + 1) + " of " + _session.Procedure.steps.Length;
            string instruction = completed ? "Procedure complete" : _session.CurrentStep.title + "\n" + _session.CurrentStep.instruction;
            _panel.Set(progress, instruction, _feedback, busy, !completed && !_session.CanAdvance,
                _session.CanAdvance, completed, _session.StepIndex == _session.Procedure.steps.Length - 1);

            int stepKey = completed ? -2 : _session.StepIndex;
            if (stepKey != _renderedStep)
            {
                _renderedStep = stepKey;
                RefreshStepItems();
                if (_ghost != null) _ghost.Say(completed ? "All done, great job! " + completionNote : _session.CurrentStep.instruction);
            }
        }

        void OnApplicationPause(bool paused) { if (paused) _report?.Save(); }

        void OnDestroy()
        {
            _report?.Save();
            _lifetime?.Cancel();
            _lifetime?.Dispose();
        }
    }
}
