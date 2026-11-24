using System;
using System.IO;
using System.Linq;
using System.Threading;
using HomeCare.AR;
using HomeCare.Procedures;
using HomeCare.Procedures.Actions;
using UnityEngine;

namespace AHC.VR
{
    [RequireComponent(typeof(AfterStepCamera))]
    public sealed class ActionGuidanceSession : MonoBehaviour
    {
        public TextAsset procedureJson;
        public TextAsset[] examples = Array.Empty<TextAsset>();
        public string bridgeEndpoint = "http://127.0.0.1:8765/verify";
        [Tooltip("Require the bridge to be routed to Qwen/Ollama; an accidental cloud selection is rejected before forwarding.")]
        public bool localOnly = true;
        public bool autoVerify = true;
        [Min(0.1f)] public float verificationInterval = 1f;
        [Tooltip("Automatically skip a conditional Action once it is explicitly not applicable and progression is allowed. Unknown never skips.")]
        public bool autoSkipNotApplicable;
        public Material lineMaterial;
        public ActionRequirementSession Session { get; private set; }
        public AfterStepCamera CameraSource { get; private set; }
        public string Status { get; private set; }
        public string LoadError { get; private set; }
        public string LastProvider { get; private set; }
        public string ReportPath { get; private set; }
        public string SkippedActionNotice { get; private set; }
        public bool Started { get; private set; }
        public bool Paused { get; private set; }
        public bool InFlight { get; private set; }
        public event Action Changed;
        readonly VerificationPollingSchedule _polling = new();
        CancellationTokenSource _cancellation;
        int _generation;
        bool _applicationPaused;

        void Awake() { CameraSource = GetComponent<AfterStepCamera>(); Load(procedureJson); }
        void OnDisable() { Cancel(); Session?.RefreshAfterPause(); _polling.Stop(); Save(); }
        void OnDestroy() { _cancellation?.Cancel(); _cancellation?.Dispose(); }
        void OnApplicationPause(bool paused)
        {
            _applicationPaused = paused;
            if (!Started || Session == null || Session.Completed) return;
            Cancel(); _polling.Stop();
            if (paused) { Session.RefreshAfterPause(); Status = "Application paused; current checks will be refreshed on return."; Save(); }
            else { Status = PhaseHint(); _polling.Start(Time.realtimeSinceStartupAsDouble, Interval); }
            Changed?.Invoke();
        }
        public void Load(TextAsset asset)
        {
            Cancel(); procedureJson = asset; Started = false; Paused = false; _polling.Stop();
            Session = null; LoadError = null; ReportPath = null; SkippedActionNotice = null;
            try
            {
                if (asset == null) throw new ArgumentException("Assign an Action-flow procedure JSON.");
                Session = new ActionRequirementSession(ActionProcedureJson.Parse(asset.text));
                Status = "Ready. Begin the procedure to start phase-aware checks.";
            }
            catch (Exception e) { LoadError = e.Message; Status = "Could not load the Action procedure: " + e.Message; }
            Changed?.Invoke();
        }
        public void Begin()
        {
            if (Session == null) return;
            Started = true; Paused = false;
            ReportPath = Path.Combine(Application.persistentDataPath, "ActionFlowReports", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Session.RunId + ".json");
            _polling.Start(Time.realtimeSinceStartupAsDouble, Interval);
            Status = PhaseHint(); Save(); Changed?.Invoke();
        }
        float Interval => float.IsNaN(verificationInterval) || float.IsInfinity(verificationInterval) ? 1f : Mathf.Max(0.1f, verificationInterval);
        bool HasImageChecks => Session != null && !Session.Completed &&
            (Session.Phase == ActionCheckPhase.Applicability || Session.CurrentChecks.Length != 0);
        void Update()
        {
            if (Started && !Paused && !_applicationPaused && !InFlight && autoSkipNotApplicable &&
                Session?.CanAdvance == true && Session.Applicability == ActionRequirementSession.NotApplicable)
            {
                Next();
                return;
            }
            if (!Started || Paused || _applicationPaused || !HasImageChecks || !autoVerify || Session.Applicability == ActionRequirementSession.NotApplicable)
            { _polling.Stop(); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_polling.Running) _polling.Start(now, Interval);
            if (_polling.TryConsumeTick(now, Interval, !InFlight)) VerifyNow();
        }
        public async void VerifyNow()
        {
            if (!Started || Paused || _applicationPaused || InFlight || !HasImageChecks || Session.Applicability == ActionRequirementSession.NotApplicable) return;
            var session = Session; int generation = _generation;
            _cancellation ??= new CancellationTokenSource();
            var cancellation = _cancellation.Token;
            var ticket = session.BeginVerification();
            InFlight = true; Status = "Checking " + ticket.Phase + "…"; Changed?.Invoke();
            try
            {
                byte[] jpeg = await CameraSource.CaptureJpegAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                string request = ActionVerificationProtocol.CreateRequest(session, ticket, jpeg, DateTime.UtcNow.ToString("o"));
                string envelope = await VerificationBridgeClient.PostAsync(bridgeEndpoint, request, cancellation, localOnly ? "ollama" : null);
                cancellation.ThrowIfCancellationRequested();
                if (generation != _generation || session != Session || !isActiveAndEnabled) return;
                var reply = ActionVerificationProtocol.Parse(envelope);
                if (session.Accept(ticket, reply))
                {
                    LastProvider = (reply.provider ?? "VLM") + " / " + reply.model;
                    Status = session.HasLatchedViolation ? "A rule violation was recorded. Retry this action; a global violation requires a new run."
                        : session.Applicability == ActionRequirementSession.NotApplicable ? PhaseHint()
                        : session.CanAdvance ? "Current checks confirmed. Next is available; monitoring continues until you advance." : PhaseHint();
                    if (session.Applicability == ActionRequirementSession.NotApplicable) Save();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (generation == _generation && session == Session)
                {
                    session.Fail(ticket, e.Message);
                    Status = "Check unavailable: " + e.Message + (localOnly ? " Select Qwen in AHC > VLM Backend." : "");
                    Debug.LogWarning("[ActionFlow] " + Status, this);
                }
            }
            finally { InFlight = false; if (this != null && generation == _generation) Changed?.Invoke(); }
        }
        string PhaseHint() => Session.HasLatchedViolation ? "A rule violation is recorded. Retry the Action; a global violation requires restarting the procedure."
            : Session.Applicability == ActionRequirementSession.NotApplicable ? autoSkipNotApplicable
                ? "This Action does not apply. Skipping it automatically."
                : "This Action does not apply. Select Skip Action to continue."
            : Session.Phase == ActionCheckPhase.Applicability ? "Confirm whether this Action's condition applies. Unknown does not mean skip."
            : Session.Phase == ActionCheckPhase.Preconditions ? "Before starting the Action: waiting for its preconditions."
            : Session.CurrentAction.manual_completion_required ? "This Action has no reliable single-image completion check. Perform it and use Confirm Action completed."
            : "Perform the Action. Current visible results and spatial rules are checked together. Unknown does not pass.";
        void Cancel()
        {
            _generation++; _cancellation?.Cancel(); _cancellation?.Dispose(); _cancellation = new CancellationTokenSource();
            Session?.Invalidate();
        }
        public void Confirm(string id, bool notApplicable = false)
        { Cancel(); Session.Confirm(id, notApplicable); Status = PhaseHint(); Save(); Changed?.Invoke(); }
        public void ConfirmApplicability(bool applies)
        { Cancel(); Session.ConfirmApplicability(applies); Status = PhaseHint(); Save(); Changed?.Invoke(); }
        public void ConfirmActionCompletion()
        {
            if (Paused || _applicationPaused || Session == null) return;
            Cancel(); Session.ConfirmActionCompletion();
            Status = Session.CanAdvance ? "Action completed by operator confirmation. Next is available." : PhaseHint();
            Save(); Changed?.Invoke();
        }
        public void Next()
        {
            if (Session == null || !Session.CanAdvance || Paused) return;
            var skipped = Session.Applicability == ActionRequirementSession.NotApplicable ? Session.CurrentAction : null;
            Cancel(); Session.Advance();
            SkippedActionNotice = skipped == null ? null : $"Skipped {skipped.action_id}: {skipped.instruction} — its condition does not apply.";
            Status = Session.Completed ? "All Actions finished. Session saved." : PhaseHint();
            _polling.Start(Time.realtimeSinceStartupAsDouble, Interval); Save(); Changed?.Invoke();
        }
        public void Retry()
        { Cancel(); Session.RetryCurrentAction(); SkippedActionNotice = null; Status = PhaseHint(); Save(); Changed?.Invoke(); }
        public void SelectConditionalAction(string id)
        { Cancel(); Session.SelectConditionalAction(id); SkippedActionNotice = null; Status = PhaseHint(); Save(); Changed?.Invoke(); }
        public void Restart()
        { Save(); Cancel(); Session.Restart(); Started = false; Paused = false; ReportPath = null; SkippedActionNotice = null; _polling.Stop(); Status = "New run ready."; Changed?.Invoke(); }
        public void TogglePause()
        { Cancel(); Paused = !Paused; if (Paused) Session.RefreshAfterPause(); _polling.Stop(); Status = Paused ? "Paused." : PhaseHint(); Changed?.Invoke(); }
        public void Save()
        {
            if (Session == null || string.IsNullOrEmpty(ReportPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, ActionProcedureJson.Serialize(new { procedure = Session.Procedure,
                    run_id = Session.RunId, phase = Session.Phase.ToString(), current_action_id = Session.CurrentAction.action_id,
                    manual_completion_confirmed = Session.ActionCompletionConfirmed,
                    provider = LastProvider, history = Session.History.Where(h => h.run_id == Session.RunId).ToArray(),
                    current_results = Session.CurrentChecks.Select(q => new { q.requirement_id, q.category, q.condition,
                        result = Session.Result(q.requirement_id), human_confirmed = Session.IsHumanConfirmed(q.requirement_id) }).ToArray() }));
            }
            catch (IOException e) { Debug.LogWarning("[ActionFlow] Could not save session: " + e.Message, this); }
        }
    }
}
