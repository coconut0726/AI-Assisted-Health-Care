using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeCare.Procedures.Actions
{
    /// <summary>Phase-aware action execution. No Unity, provider or camera dependency.</summary>
    public sealed class ActionRequirementSession
    {
        public const string Yes = "satisfied", No = "not_satisfied", Unknown = "unknown", NotApplicable = "not_applicable";
        public ActionProcedure Procedure { get; }
        public ActionCheckPhase Phase { get; private set; }
        public int Revision { get; private set; }
        public int ActionIndex { get; private set; }
        public bool Busy => _pending != null;
        public bool Completed => Phase == ActionCheckPhase.Completed;
        public ProcedureAction CurrentAction => _actions[ActionIndex];
        public ActionStep CurrentStep => _owners[CurrentAction.action_id];
        public int StepIndex => Array.IndexOf(Procedure.steps, CurrentStep);
        public int ActionCount => _actions.Length;
        public string RunId { get; private set; } = Guid.NewGuid().ToString("N");
        public IReadOnlyList<ActionHistoryEvent> History => _history;
        public string Applicability { get; private set; }
        public string OverallStatus => Completed || CanAdvance ? Yes : CurrentChecks.Any(q => Result(q.requirement_id)?.status == No) ? No : Unknown;
        public bool CanAdvance => !Completed && Phase == ActionCheckPhase.Completion &&
            _globalViolations.Count == 0 && (Applicability == NotApplicable || (DependenciesMet() && _localViolations.Count == 0 &&
             CurrentChecks.All(q => Accepted(Result(q.requirement_id)?.status))));

        readonly ProcedureAction[] _actions;
        readonly Dictionary<string, ActionStep> _owners;
        readonly Dictionary<string, string> _finished = new();
        readonly Dictionary<string, ActionCheckResult> _results = new();
        readonly HashSet<string> _manual = new(), _localViolations = new(), _globalViolations = new();
        readonly List<ActionHistoryEvent> _history = new();
        ActionVerificationTicket _pending;

        public ActionRequirementSession(ActionProcedure procedure)
        {
            Procedure = ActionProcedureJson.Parse(ActionProcedureJson.Serialize(procedure));
            _actions = Procedure.steps.SelectMany(s => s.actions).ToArray();
            _owners = Procedure.steps.SelectMany(s => s.actions.Select(a => new { a.action_id, Step = s })).ToDictionary(x => x.action_id, x => x.Step);
            StartAction();
        }

        public ActiveActionCheck[] CurrentChecks
        {
            get
            {
                if (Completed || Phase == ActionCheckPhase.Applicability || Applicability == NotApplicable) return Array.Empty<ActiveActionCheck>();
                var a = CurrentAction;
                if (Phase == ActionCheckPhase.Preconditions) return a.preconditions.Select(q => Check(q, "precondition")).ToArray();
                var checks = a.postconditions.Select(q => Check(q, "postcondition")).ToList();
                checks.AddRange(a.error_criteria.Select(e => Check(e, false)));
                checks.AddRange(Procedure.procedure_error_criteria.Where(e => e.relevant_action_ids.Contains(a.action_id)).Select(e => Check(e, true)));
                return checks.ToArray();
            }
        }

        static ActiveActionCheck Check(ActionRequirement q, string category) => new()
        { requirement_id = q.requirement_id, category = category, condition = q.condition, applicability_condition = "", requires_expert_review = q.requires_expert_review };
        static ActiveActionCheck Check(ActionErrorCriterion e, bool global) => new()
        { requirement_id = e.criterion_id, category = "error_compliance", condition = e.compliance_condition,
            applicability_condition = e.applicability_condition, requires_expert_review = e.requires_expert_review, is_global = global };
        static bool Accepted(string value) => value == Yes || value == NotApplicable;
        public ActionCheckResult Result(string id) => _results.TryGetValue(id, out var result) ? result : null;
        public bool IsHumanConfirmed(string id) => _manual.Contains(id);
        public bool HasLatchedViolation => _localViolations.Count > 0 || _globalViolations.Count > 0;
        public ProcedureAction[] ConditionalActions => _actions.Where(a => !string.IsNullOrWhiteSpace(a.applicability_condition) &&
            (!_finished.TryGetValue(a.action_id, out var state) || state == NotApplicable)).ToArray();

        public object[] DependencySummary() => CurrentAction.dependencies.Concat(CurrentAction.conditional_dependencies.Select(d => d.action_id))
            .Concat(CurrentStep.dependencies.SelectMany(id => Procedure.steps.First(s => s.step_id == id).actions).Select(a => a.action_id))
            .Distinct().Select(id => (object)new { action_id = id, instruction = _actions.First(a => a.action_id == id).instruction,
                status = _finished.TryGetValue(id, out var state) ? state : "unconfirmed" }).ToArray();

        public bool DependenciesMet()
        {
            // Conditional prerequisite actions recorded not_applicable do not block their dependents.
            // An unresolved prerequisite never becomes a successful completion by absence from history.
            var ids = CurrentAction.dependencies.Concat(CurrentAction.conditional_dependencies.Select(d => d.action_id))
                .Concat(CurrentStep.dependencies.SelectMany(id => Procedure.steps.First(s => s.step_id == id).actions).Select(a => a.action_id));
            return ids.All(id => _finished.ContainsKey(id));
        }

        public ActionVerificationTicket BeginVerification()
        {
            if (Completed || Busy || Applicability == NotApplicable) throw new InvalidOperationException("No current checks available.");
            _pending = new ActionVerificationTicket { RequestId = Guid.NewGuid().ToString("N"), StepId = CurrentStep.step_id,
                ActionId = CurrentAction.action_id, Phase = Phase.ToString(), Revision = Revision, Checks = CurrentChecks };
            return _pending;
        }

        public bool Accept(ActionVerificationTicket ticket, ActionVerificationReply reply)
        {
            if (!IsCurrent(ticket)) return false;
            if (reply == null || reply.request_id != ticket.RequestId || reply.step_id != ticket.StepId || reply.action_id != ticket.ActionId || reply.phase != ticket.Phase)
                throw new ArgumentException("The response does not match the active request, action and phase.");
            if (!new[] { "applies", NotApplicable, Unknown }.Contains(reply.action_applicability) || reply.results == null)
                throw new ArgumentException("Invalid action applicability or missing results.");
            var wanted = ticket.Checks.ToDictionary(q => q.requirement_id);
            var incoming = new Dictionary<string, ActionCheckResult>();
            foreach (var result in reply.results)
            {
                if (result == null || result.requirement_id == null || !wanted.ContainsKey(result.requirement_id) || incoming.ContainsKey(result.requirement_id) ||
                    !new[] { Yes, No, Unknown, NotApplicable }.Contains(result.status)) throw new ArgumentException("Unknown/duplicate check ID or invalid status.");
                incoming.Add(result.requirement_id, result);
            }
            _pending = null;
            if (Phase == ActionCheckPhase.Applicability)
            {
                SetApplicability(reply.action_applicability, "VLM", "Conditional action applicability checked.");
                return true;
            }
            // Applicability was established before entering a phase. A contrary/unresolved reply is
            // not an implicit skip; all active checks become unresolved until explicitly reassessed.
            bool scopeKnown = reply.action_applicability == "applies";
            foreach (var q in ticket.Checks)
            {
                if (_manual.Contains(q.requirement_id))
                {
                    if (!scopeKnown || !incoming.TryGetValue(q.requirement_id, out var later) || later.status != No || q.requires_expert_review) continue;
                    _manual.Remove(q.requirement_id); // A later direct contradiction cannot be hidden by an older confirmation.
                }
                string status = Unknown, reason = "The model omitted this requirement.";
                if (scopeKnown && incoming.TryGetValue(q.requirement_id, out var supplied))
                {
                    status = supplied.status; reason = supplied.reason;
                    if (string.IsNullOrWhiteSpace(reason)) { status = Unknown; reason = "No explanation was supplied."; }
                    if (status == NotApplicable && string.IsNullOrWhiteSpace(q.applicability_condition))
                    { status = Unknown; reason = "An unconditional requirement cannot be marked not applicable."; }
                    if (q.requires_expert_review && status != Unknown)
                    { status = Unknown; reason = "This criterion is marked for expert review; model certainty cannot resolve that flag."; }
                }
                else if (!scopeKnown) reason = "Action applicability was not confirmed for this observation.";
                _results[q.requirement_id] = new ActionCheckResult { requirement_id = q.requirement_id, status = status, reason = reason };
                if (q.category == "error_compliance" && status == No)
                {
                    (q.is_global ? _globalViolations : _localViolations).Add(q.requirement_id);
                    Add("rule_violation", q.requirement_id, No, "VLM", reason);
                }
            }
            ProgressPreconditions();
            return true;
        }

        bool IsCurrent(ActionVerificationTicket t) => t != null && _pending != null &&
            _pending.RequestId == t.RequestId && Revision == t.Revision && !Completed && t.ActionId == CurrentAction.action_id && t.Phase == Phase.ToString();

        public void Fail(ActionVerificationTicket ticket, string reason)
        {
            if (!IsCurrent(ticket)) return;
            _pending = null;
            foreach (var q in CurrentChecks.Where(q => !_manual.Contains(q.requirement_id)))
                _results[q.requirement_id] = new ActionCheckResult { requirement_id = q.requirement_id, status = Unknown, reason = reason };
        }

        public void Confirm(string id, bool notApplicable = false)
        {
            var check = CurrentChecks.FirstOrDefault(q => q.requirement_id == id) ?? throw new ArgumentException("Not an active requirement: " + id);
            if (notApplicable && string.IsNullOrWhiteSpace(check.applicability_condition)) throw new ArgumentException("This check is unconditional.");
            Invalidate();
            var status = notApplicable ? NotApplicable : Yes;
            _manual.Add(id);
            _results[id] = new ActionCheckResult { requirement_id = id, status = status, reason = "Explicit operator confirmation; not a model observation." };
            Add("requirement_confirmed", id, status, "human_confirmation", check.condition);
            ProgressPreconditions();
        }

        public void ConfirmApplicability(bool applies)
        {
            if (Phase != ActionCheckPhase.Applicability) throw new InvalidOperationException("This action is not awaiting applicability.");
            Invalidate(); SetApplicability(applies ? "applies" : NotApplicable, "human_confirmation", "Explicit operator assessment of the source condition.");
        }
        void SetApplicability(string value, string source, string reason)
        {
            if (value == Unknown) return;
            Applicability = value;
            Add("applicability_confirmed", "", value, source, reason);
            Phase = value == NotApplicable || CurrentAction.preconditions.Length == 0 ? ActionCheckPhase.Completion : ActionCheckPhase.Preconditions;
            Revision++;
        }
        void ProgressPreconditions()
        {
            if (Phase != ActionCheckPhase.Preconditions || !DependenciesMet() || !CurrentChecks.All(q => Accepted(Result(q.requirement_id)?.status))) return;
            foreach (var q in CurrentChecks) Add("precondition_confirmed", q.requirement_id, Result(q.requirement_id).status,
                _manual.Contains(q.requirement_id) ? "human_confirmation" : "VLM", Result(q.requirement_id).reason);
            Phase = ActionCheckPhase.Completion; Revision++; // A fresh subsequent frame is required for the new phase.
        }

        public void Advance()
        {
            if (!CanAdvance) throw new InvalidOperationException("All current checks and dependencies must be confirmed before advancing.");
            string status = Applicability == NotApplicable ? NotApplicable : "completed";
            foreach (var q in CurrentChecks.Where(q => !_manual.Contains(q.requirement_id)))
                Add("requirement_confirmed", q.requirement_id, Result(q.requirement_id).status, "VLM", Result(q.requirement_id).reason);
            _finished[CurrentAction.action_id] = status;
            Add("action_finished", "", status, "session", "Accepted current checks; this records progression, not persistent world state.");
            Invalidate();
            if (_finished.Count == _actions.Length) { Phase = ActionCheckPhase.Completed; return; }
            ActionIndex = Array.FindIndex(_actions, a => !_finished.ContainsKey(a.action_id)); StartAction();
        }

        public void SelectConditionalAction(string id)
        {
            var action = ConditionalActions.FirstOrDefault(a => a.action_id == id) ?? throw new ArgumentException("Not a pending conditional action.");
            if (_localViolations.Count > 0 || _globalViolations.Count > 0) throw new InvalidOperationException("Resolve or explicitly restart the failed attempt before switching.");
            Invalidate(); _finished.Remove(id); ActionIndex = Array.IndexOf(_actions, action); StartAction();
            Add("action_selected", "", Unknown, "human_confirmation", "Selection establishes context, not action completion.");
        }

        public void RetryCurrentAction()
        {
            if (Completed) throw new InvalidOperationException("Restart the completed procedure first.");
            Add("action_restarted", "", Unknown, "human_confirmation", "Previous failures remain in session history.");
            Invalidate(); StartAction(); // Local rule violations reset; global violations remain latched.
        }
        public void Restart()
        {
            Invalidate(); _finished.Clear(); _globalViolations.Clear(); ActionIndex = 0; RunId = Guid.NewGuid().ToString("N");
            Add("procedure_restarted", "", Unknown, "human_confirmation", "A new run was explicitly started.");
            StartAction();
        }
        public void Invalidate() { Revision++; _pending = null; }
        public void RefreshAfterPause()
        {
            Invalidate(); _results.Clear(); _manual.Clear();
            // Preserve phase and latched violations: returning to an earlier precondition after
            // performing an action (e.g. an empty tray) would require undoing the user's work.
        }
        void StartAction()
        {
            _results.Clear(); _manual.Clear(); _localViolations.Clear();
            Applicability = string.IsNullOrWhiteSpace(CurrentAction.applicability_condition) ? "applies" : Unknown;
            Phase = Applicability == Unknown ? ActionCheckPhase.Applicability :
                CurrentAction.preconditions.Length > 0 ? ActionCheckPhase.Preconditions : ActionCheckPhase.Completion;
        }
        void Add(string type, string id, string status, string source, string reason) => _history.Add(new ActionHistoryEvent
        { run_id = RunId, at_utc = DateTime.UtcNow.ToString("o"), event_type = type, step_id = CurrentStep.step_id, action_id = CurrentAction.action_id,
            requirement_id = id, status = status, source = source, reason = reason });
    }
}
