using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HomeCare.AR;
using HomeCare.Guidance;
using HomeCare.Procedures;
using UnityEngine;

namespace AHC.VR
{
    /// <summary>One supply and whether the camera has seen it.</summary>
    public sealed class SupplyCheck
    {
        public ItemVocabulary Item;
        /// <summary>null: not looked for yet.</summary>
        public bool? Found;
    }

    /// <summary>
    /// The real caregiver flow behind <see cref="VRDemoController"/>: the nurse's reviewed procedure
    /// (6 steps with checks), real object detection in the passthrough view, after-step verification
    /// through the PC bridge, and a session report. The controller draws the Figma UI from this state.
    /// </summary>
    [RequireComponent(typeof(AfterStepCamera))]
    public sealed class RealCareSession : MonoBehaviour
    {
        [Tooltip("Reviewed procedure with checks (Assets/_Project/Content/Procedures/burn_care_procedure.json).")]
        public TextAsset procedureJson;

        [Header("PC bridge (USB: adb reverse tcp:8765 tcp:8765)")]
        public string bridgeEndpoint = "http://127.0.0.1:8765/verify";
        public string geminiModel = "gemini-3.8-flash";

        [Header("Object detection")]
        [Tooltip("Look for the step's supplies each time a step starts.")]
        public bool findOnNewStep = true;
        [Tooltip("Unlit material for detection boxes and arrows (Assets/_Project/Art/CareItems/CareHighlight).")]
        public Material lineMaterial;
        [Tooltip("Supplies a step can name. Matched against the step text to decide what to look for.")]
        public ItemVocabulary[] supplies =
        {
            Item("burn", "Burn", "burn", "burned skin", "blister"),
            Item("dressing", "Old dressing", "old dressing", "dressing"),
            Item("soap", "Soap", "soap"),
            Item("water", "Clean water", "water"),
            Item("towel", "Clean towel", "towel"),
            Item("gloves", "Gloves", "glove"),
            Item("swab", "Cotton swab", "cotton swab", "swab"),
            Item("ointment", "Burn ointment", "ointment"),
            Item("pad", "Non-stick pad", "non-stick pad", "pad"),
            Item("gauze", "Gauze roll", "gauze"),
            Item("tape", "Medical tape", "medical tape", "tape"),
        };

        static ItemVocabulary Item(string id, string name, params string[] keywords) =>
            new() { itemId = id, displayName = name, keywords = keywords };

        /// <summary>Things on the body rather than on the table; not part of the supply check.</summary>
        static readonly HashSet<string> OnTheArm = new() { "burn", "dressing" };

        /// <summary>Raised whenever anything shown on the HUD changes.</summary>
        public event Action Changed;
        /// <summary>A short line for the guide to say (detection guidance, verification result).</summary>
        public event Action<string> Spoke;

        AfterStepCamera _camera;
        ObjectDetectionGuidance _detector;
        ProcedureSession _session;
        SessionReportWriter _report;
        CancellationTokenSource _lifetime, _supplyCheck;
        int _stepRequest;

        public string LoadError { get; private set; }
        /// <summary>The procedure file's "completion_note", or null to use the demo's red flags.</summary>
        public string CompletionNote { get; private set; }
        public ProcedureRequirements Procedure => _session?.Procedure;
        public int StepIndex => _session?.StepIndex ?? 0;
        public int StepCount => _session?.Procedure.steps.Length ?? 0;
        public ProcedureStep CurrentStep => _session?.CurrentStep;
        public ProcedureSessionState State => _session?.State ?? ProcedureSessionState.Ready;
        public bool Completed => State == ProcedureSessionState.Completed;
        public bool CanAdvance => _session != null && _session.CanAdvance;
        public bool Verifying => State == ProcedureSessionState.Verifying;
        public bool CanVerify => _session != null && (State == ProcedureSessionState.Ready || State == ProcedureSessionState.NeedsRetry);
        public RequirementResult[] Results => _session?.Results;
        public bool Detecting => _detector != null && _detector.Busy;
        public bool CheckingSupplies { get; private set; }
        public readonly List<SupplyCheck> SupplyChecks = new();
        /// <summary>Supplies the current step names, with what the last detection saw.</summary>
        public readonly List<SupplyCheck> StepItems = new();
        /// <summary>Status line under the step: what the system is doing, or what it found.</summary>
        public string Status { get; private set; } = "";
        public string Guidance { get; private set; } = "";
        public string Caution { get; private set; } = "";
        public string ReportPath => _report?.MarkdownPath;
        public float StartedAt { get; private set; }

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            _lifetime = new CancellationTokenSource();
            _camera = GetComponent<AfterStepCamera>();
            _detector = TryGetComponent<ObjectDetectionGuidance>(out var d) ? d : gameObject.AddComponent<ObjectDetectionGuidance>();
        }

        public void Init(Transform head)
        {
            _detector.Init(_camera, lineMaterial, head);
            try
            {
                if (procedureJson == null) throw new InvalidOperationException("Assign the reviewed procedure JSON on RealCareSession.");
                _session = new ProcedureSession(RequirementsJson.Parse(procedureJson.text));
                ReadCaseExtras(procedureJson.text);
            }
            catch (Exception e)
            {
                LoadError = "The procedure could not be loaded: " + e.Message;
                Debug.LogException(e, this);
            }
            foreach (var s in supplies)
                if (s != null && !OnTheArm.Contains(s.itemId)) SupplyChecks.Add(new SupplyCheck { Item = s });
        }

        /// <summary>
        /// A procedure file may bring its own "supplies" (same fields as <see cref="ItemVocabulary"/>) and a
        /// "completion_note" for the last screen; otherwise the Inspector list and the burn-care note are used.
        /// These fields are not part of the procedure sent to Gemini.
        /// </summary>
        void ReadCaseExtras(string json)
        {
            var root = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (root["supplies"] is Newtonsoft.Json.Linq.JArray list && list.Count > 0)
                supplies = list.ToObject<ItemVocabulary[]>();
            CompletionNote = (string)root["completion_note"];
        }

        void OnDestroy()
        {
            _report?.Save();
            _lifetime?.Cancel();
            _lifetime?.Dispose();
        }

        void OnApplicationPause(bool paused) { if (paused) _report?.Save(); }

        void Notify() { if (this != null) Changed?.Invoke(); }

        // ------------------------------------------------------------------ session

        /// <summary>Starts a new run at step 1, with a new report folder.</summary>
        public void BeginSession()
        {
            if (_session == null || Verifying) return;
            StopSupplyCheck();
            _session.Restart();
            string dir = Path.Combine(Application.persistentDataPath, "HomeCareSessions",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            _report = new SessionReportWriter(dir, _session.Procedure, geminiModel);
            _report.Save();
            StartedAt = Time.time;
            Started = true;
            Debug.Log("[HomeCare] Session report: " + _report.MarkdownPath);
            EnterStep();
        }

        /// <summary>The user pressed Start: steps (and their guidance) are running. False on the supply screen.</summary>
        public bool Started { get; private set; }

        /// <summary>Leaves the flow (Home): clears boxes in the room and stops any request.</summary>
        public void Leave()
        {
            StopSupplyCheck();
            Started = false;
            _stepRequest++;
            _detector.Clear();
            _touchStarted = _releasedAt = -1;
            _report?.Save();
            Status = Guidance = Caution = "";
        }

        void EnterStep()
        {
            _stepRequest++;
            _detector.Clear();
            _lastRequest = null;
            _touchStarted = _releasedAt = -1;
            Guidance = Caution = "";
            StepItems.Clear();
            if (Completed) { Status = ""; Notify(); return; }

            string text = CurrentStep.title + "\n" + CurrentStep.instruction;
            foreach (var s in supplies)
                if (s != null && KeywordMatcher.Matches(text, s.keywords)) StepItems.Add(new SupplyCheck { Item = s });
            var step = _report?.Step(CurrentStep.id);
            if (step != null)
            {
                step.started_at_utc ??= SessionReportWriter.Now();
                if (step.final_status == "not_started") step.final_status = "in_progress";
                step.mentioned_items = StepItems.Select(i => new ItemPresence { item_id = i.Item.itemId, name = i.Item.displayName }).ToList();
                _report.Save();
            }
            Status = "Do this step, then press Check step.";
            Notify();
            if (findOnNewStep) FindItems();
        }

        public void Next()
        {
            if (!CanAdvance) return;
            var step = _report?.Step(CurrentStep.id);
            if (step != null) step.passed_at_utc = SessionReportWriter.Now();
            _session.Advance();
            FinishIfDone();
            EnterStep();
        }

        /// <summary>Presenter override: move on although the check did not pass (recorded as skipped).</summary>
        public void Skip()
        {
            if (_session == null || Verifying || Completed) return;
            var step = _report?.Step(CurrentStep.id);
            if (step != null) step.final_status = "skipped";
            _session.Skip();
            FinishIfDone();
            EnterStep();
        }

        void FinishIfDone()
        {
            if (!Completed || _report == null) return;
            _report.Report.completed = true;
            _report.Report.finished_at_utc = SessionReportWriter.Now();
            _report.Save();
        }

        // ------------------------------------------------------------------ supplies at the start

        /// <summary>Looks for every supply on the table, four at a time (the most one detection returns).</summary>
        public async void CheckSupplies()
        {
            if (_session == null || CheckingSupplies) return;
            StopSupplyCheck();
            _supplyCheck = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var token = _supplyCheck.Token;
            foreach (var s in SupplyChecks) s.Found = null;
            _detector.Clear(); // supply boxes from an earlier look
            CheckingSupplies = true;
            Status = "Looking for your supplies. Keep the table in view.";
            Notify();
            try
            {
                var procedure = _session.Procedure;
                foreach (var batch in SupplyChecks.Select((s, i) => (s, i)).GroupBy(x => x.i / DetectionProtocol.MaxObjects, x => x.s))
                {
                    var names = batch.Select(s => s.Item.displayName).ToList();
                    DetectionOutcome outcome = null;
                    try
                    {
                        // Boxes only: no step guidance before the user presses Start.
                        outcome = await _detector.FindAsync(string.Join(", ", names), procedure, procedure.steps[0], names,
                            bridgeEndpoint, geminiModel, token, boxesOnly: true);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[HomeCare] Supply check failed: " + e.Message, this);
                        Status = "Could not look for supplies. Check the camera and the PC bridge, then press Look again.";
                        return;
                    }
                    if (token.IsCancellationRequested || outcome == null || outcome.Superseded) return;
                    foreach (var s in batch) s.Found = Seen(outcome.Result, s.Item);
                    Notify();
                }
                int missing = SupplyChecks.Count(s => s.Found == false);
                Status = missing == 0 ? "All supplies found." : $"{missing} not found yet. Put them on the table, then press Look again.";
            }
            finally
            {
                if (_supplyCheck != null && _supplyCheck.Token == token) CheckingSupplies = false;
                Notify();
            }
        }

        void StopSupplyCheck()
        {
            _supplyCheck?.Cancel();
            _supplyCheck = null;
            CheckingSupplies = false;
        }

        static bool Seen(DetectionResult result, ItemVocabulary item) =>
            result.objects.Any(o => o.found && (KeywordMatcher.Matches(o.query + " " + o.label, item.keywords) ||
                                                KeywordMatcher.Matches(item.displayName, new[] { o.label })));

        // ------------------------------------------------------------------ objects for the current step

        /// <summary>
        /// Boxes the current step's supplies (or <paramref name="request"/>) in the room, with step guidance.
        /// <paramref name="refresh"/>: only update where the boxes are (same request, no speech, status kept).
        /// </summary>
        public async void FindItems(string request = null, bool refresh = false)
        {
            if (_session == null || !Started || Completed || Verifying) return; // no step guidance before Start
            StopSupplyCheck();
            var step = CurrentStep;
            int stepRequest = _stepRequest;
            var names = StepItems.Select(i => i.Item.displayName).ToList();
            if (string.IsNullOrWhiteSpace(request))
                request = names.Count > 0 ? string.Join(", ", names) : "the supplies needed for this step: " + step.title;
            _lastRequest = request;
            string statusBefore = Status;
            Status = refresh ? "Updating where the items are…" : "Looking for " + request + "… Keep the work area in view.";
            Notify();

            var detection = new DetectionEvent { at_utc = SessionReportWriter.Now(), request = request, source = refresh ? "gemini_refresh" : "gemini" };
            DetectionOutcome outcome = null;
            try
            {
                outcome = await _detector.FindAsync(request, _session.Procedure, step, names, bridgeEndpoint, geminiModel, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                detection.error = e is OperationCanceledException ? "Timed out waiting for the camera or the PC bridge." : e.Message;
                Debug.LogWarning("[HomeCare] Object detection failed: " + detection.error, this);
            }
            if (this == null || outcome is { Superseded: true }) return;
            if (outcome != null) Record(detection, outcome);
            _report?.Step(step.id)?.detections.Add(detection);
            _report?.Save();
            if (stepRequest != _stepRequest) return; // the user moved on

            if (outcome == null)
            {
                Status = refresh ? statusBefore : "Could not look for items. Check the camera and the PC bridge, then press Find items.";
                Notify();
                return;
            }
            var r = outcome.Result;
            if (refresh)
            {
                // Only the boxes moved: keep the guidance and status the user was reading, and say nothing.
                foreach (var item in StepItems) item.Found = Seen(r, item.Item) || item.Found == true;
                Status = statusBefore;
                Notify();
                return;
            }
            foreach (var item in StepItems) item.Found = Seen(r, item.Item);
            // The report's "found" means seen by the camera here (V2 used virtual stand-ins in the scene).
            var presence = _report?.Step(step.id)?.mentioned_items;
            if (presence != null)
            {
                foreach (var p in presence)
                    p.found_in_scene = StepItems.Any(i => i.Item.itemId == p.item_id && i.Found == true);
                _report.Save();
            }
            Guidance = r.guidance_text ?? "";
            Caution = r.caution ?? "";
            Status = r.objects.Count == 0 ? "Nothing found. Look at the table and press Find items." : "Do this step, then press Check step.";
            if (outcome.Placed.Count > 0 && outcome.Placed.All(p => p.marker.Box.depthSource == "default_distance"))
                Status += " (No depth reading: boxes are at an estimated distance.)";
            Notify();
            if (!string.IsNullOrWhiteSpace(Guidance)) Spoke?.Invoke(Guidance);
        }

        void Record(DetectionEvent detection, DetectionOutcome outcome)
        {
            detection.frame_source = outcome.FrameSource;
            detection.depth_samples = outcome.DepthSamples;
            if (outcome.Jpeg != null && _report != null)
            {
                string file = "detect_step" + (StepIndex + 1) + "_" + Guid.NewGuid().ToString("N").Substring(0, 10) + ".jpg";
                try { File.WriteAllBytes(Path.Combine(_report.Directory, file), outcome.Jpeg); detection.image_file = file; }
                catch (IOException e) { Debug.LogWarning("[HomeCare] Could not save the detection image: " + e.Message, this); }
            }
            var r = outcome.Result;
            detection.visual_type = r.visual_type;
            detection.guidance_text = r.guidance_text;
            detection.caution = r.caution;
            foreach (var o in r.objects)
            {
                var marker = outcome.Placed.FirstOrDefault(p => p.detection == o).marker;
                detection.objects.Add(new DetectionObjectReport
                {
                    query = o.query, label = o.label, role = o.role, found = o.found, box_2d = o.box_2d,
                    depth_source = marker != null ? marker.Box.depthSource : "",
                    world_center = marker != null ? new[] { marker.Box.center.x, marker.Box.center.y, marker.Box.center.z } : null,
                    world_size = marker != null ? new[] { marker.Box.size.x, marker.Box.size.y, marker.Box.size.z } : null,
                });
            }
        }

        // ------------------------------------------------------------------ after-step check

        /// <summary>Captures the passthrough camera and checks the current step's requirements.</summary>
        public async void Verify()
        {
            if (!CanVerify || _report == null) return;
            var step = CurrentStep;
            _session.BeginVerification();
            Status = "Checking your result. Keep the area in view.";
            Notify();
            var attempt = new AttemptReport { at_utc = SessionReportWriter.Now() };
            try
            {
                var cancellation = _lifetime.Token;
                await CountDownToPhoto(step, cancellation);
                byte[] jpeg = await _camera.CaptureJpegAsync(cancellation);
                string basename = (_camera.IsEditorTest ? "editor_test" : "headset") + "_step" + (StepIndex + 1) + "_" +
                                  Guid.NewGuid().ToString("N").Substring(0, 10);
                string imagePath = Path.Combine(_report.Directory, basename + ".jpg");
                File.WriteAllBytes(imagePath, jpeg);
                attempt.image_file = Path.GetFileName(imagePath);
                var attachments = new[] { new EvidenceAttachment { scope = "after_step", path = imagePath } };
                string request = GeminiVerificationProtocol.CreateAfterImageRequest(_session.Procedure, step, jpeg,
                    Path.GetFileName(imagePath), geminiModel);
                var response = await VerificationBridgeClient.VerifyAsync(bridgeEndpoint, request, cancellation);
                cancellation.ThrowIfCancellationRequested();
                _session.AcceptVerification(response, attachments);
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
                var verification = new VerificationReport
                {
                    verified_at_utc = DateTime.UtcNow.ToString("o"), provider = "Gemini", model = geminiModel,
                    procedure = _session.Procedure, step_id = step.id, attachments = attachments,
                    overall_status = _session.OverallStatus, results = _session.Results,
                };
                try { File.WriteAllText(Path.Combine(_report.Directory, basename + ".json"), RequirementsJson.Serialize(verification)); }
                catch (IOException e) { Debug.LogWarning("[HomeCare] Verification report could not be saved: " + e.Message, this); }
                Status = CanAdvance ? "Passed. You can continue."
                    : _session.OverallStatus == RequirementVerification.NotSatisfied ? "Not done yet. Fix what is marked, then check again."
                    : "The camera could not confirm this. Show the area clearly, then check again.";
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                _session.FailVerification();
                attempt.overall_status = RequirementVerification.Unknown;
                attempt.error = e.Message;
                Status = "Could not check this step. Check the camera and the PC bridge connection, then try again.";
                Debug.LogException(e, this);
            }

            if (this == null) return;
            var stepReport = _report.Step(step.id);
            stepReport.attempts.Add(attempt);
            stepReport.final_status = attempt.overall_status switch
            {
                RequirementVerification.Satisfied => "passed",
                RequirementVerification.NotSatisfied => "not_met",
                _ => "not_verified",
            };
            _report.Save();
            Notify();
            Spoke?.Invoke(CanAdvance ? "Well done. That step is complete." : FirstProblem() ?? Status);
            // Items were handled during the step: update where their boxes are (once, quietly).
            if (_detector.Markers.Count > 0) FindItems(_lastRequest, refresh: true);
        }

        // ------------------------------------------------------------------ boxes follow handled items

        [Header("Update boxes after the hand touches an item")]
        [Tooltip("Look again for the items once a hand has touched a boxed item and then moved away from it.")]
        public bool refreshAfterTouch = true;
        [Tooltip("A hand closer than this to a box counts as touching it, metres.")]
        public float touchMargin = 0.04f;
        [Tooltip("Ignore touches shorter than this, seconds (a hand passing by).")]
        public float minTouchSeconds = 0.3f;
        [Tooltip("Wait this long after the hand leaves before looking again, seconds.")]
        public float releaseDelay = 0.6f;

        string _lastRequest;
        float _touchStarted = -1, _releasedAt = -1;

        void Update()
        {
            // Only during the steps: supply boxes on the start screen are not followed.
            if (!refreshAfterTouch || _session == null || !Started || Completed || CheckingSupplies) return;
            bool touching = HandTouchesABox();
            if (touching)
            {
                if (_touchStarted < 0) _touchStarted = Time.time;
                _releasedAt = -1;
                return;
            }
            if (_touchStarted >= 0)
            {
                // The hand just left a box: if it was a real touch, the item has probably moved.
                if (Time.time - _touchStarted >= minTouchSeconds && _releasedAt < 0) _releasedAt = Time.time;
                _touchStarted = -1;
            }
            if (_releasedAt >= 0 && Time.time - _releasedAt >= releaseDelay)
            {
                if (Detecting || Verifying) return; // try again when the bridge is free
                _releasedAt = -1;
                Debug.Log("[AHC] Hand touched a boxed item; updating the boxes.");
                FindItems(_lastRequest, refresh: true);
            }
        }

        bool HandTouchesABox()
        {
            var hands = HandTracking.Instance;
            var markers = _detector.Markers;
            if (hands == null || markers.Count == 0) return false;
            foreach (var hand in hands.Hands)
            {
                if (!hand.IsTracked || hand.IsSimulated) continue;
                foreach (var m in markers)
                    if (m != null && (Near(m.Box, hand.IndexTip) || Near(m.Box, hand.ThumbTip) || Near(m.Box, hand.Palm))) return true;
            }
            return false;
        }

        bool Near(WorldBox box, Vector3 point)
        {
            // Box depth from a single photo is a guess: allow at least 10 cm front to back.
            Vector3 local = Quaternion.Inverse(box.rotation) * (point - box.center);
            Vector3 half = new Vector3(box.size.x, box.size.y, Mathf.Max(box.size.z, 0.1f)) * 0.5f + Vector3.one * touchMargin;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        [Tooltip("Seconds between pressing Check step and taking the photo, so the hand that pressed the button can " +
                 "get back into view (a check of both hands otherwise sees only one).")]
        public int photoCountdown = 3;

        /// <summary>Says what to show (the step's photo hints), then counts down on the panel before the photo.</summary>
        async System.Threading.Tasks.Task CountDownToPhoto(ProcedureStep step, CancellationToken cancellation)
        {
            if (photoCountdown <= 0) return;
            string show = string.Join(" ", step.requirements.Select(r => r.observation_hint)
                .Where(h => !string.IsNullOrWhiteSpace(h)).Distinct());
            if (!string.IsNullOrWhiteSpace(show)) Spoke?.Invoke(show);
            for (int s = photoCountdown; s > 0; s--)
            {
                Status = $"Taking the photo in {s}… {show}".Trim();
                Notify();
                await System.Threading.Tasks.Task.Delay(1000, cancellation);
            }
            Status = "Checking your result. Keep the area in view.";
            Notify();
        }

        string FirstProblem()
        {
            if (Results == null) return null;
            var failed = Results.FirstOrDefault(r => r.status != RequirementVerification.Satisfied);
            return failed == null || string.IsNullOrWhiteSpace(failed.reason) ? null : failed.reason;
        }
    }
}
