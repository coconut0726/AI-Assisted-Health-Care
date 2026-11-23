using System;

namespace HomeCare.Procedures.Actions
{
    // Independent of the legacy step-level ProcedureRequirements format.
    [Serializable] public sealed class ActionProcedure
    {
        public string schema_version = "action_requirements_v1";
        public string procedure_title;
        public string task_goal;
        public string instruction;
        public ActionStep[] steps = Array.Empty<ActionStep>();
        public ActionErrorCriterion[] procedure_error_criteria = Array.Empty<ActionErrorCriterion>();
        public string[] context_notes = Array.Empty<string>();
        public string[] review_notes = Array.Empty<string>();
    }

    [Serializable] public sealed class ActionStep
    {
        public string step_id, step_name, step_description;
        public string[] dependencies = Array.Empty<string>();
        public ProcedureAction[] actions = Array.Empty<ProcedureAction>();
    }

    [Serializable] public sealed class ProcedureAction
    {
        public string action_id, instruction, original_text;
        public string[] dependencies = Array.Empty<string>();
        public ConditionalActionDependency[] conditional_dependencies = Array.Empty<ConditionalActionDependency>();
        public string applicability_condition = "";
        public string[] actor = Array.Empty<string>();
        public string[] tools = Array.Empty<string>();
        public string[] materials = Array.Empty<string>();
        public string[] contact_media = Array.Empty<string>();
        public string[] targets = Array.Empty<string>();
        public string[] associated_constraints = Array.Empty<string>();
        public ActionRequirement[] preconditions = Array.Empty<ActionRequirement>();
        public ActionRequirement[] postconditions = Array.Empty<ActionRequirement>();
        public ActionErrorCriterion[] error_criteria = Array.Empty<ActionErrorCriterion>();
    }

    [Serializable] public sealed class ConditionalActionDependency { public string action_id, condition; }

    [Serializable] public sealed class ActionRequirement
    {
        public string requirement_id, condition, source, source_reference;
        public bool requires_expert_review;
    }

    [Serializable] public sealed class ActionErrorCriterion
    {
        public string criterion_id, error_type, violation_condition, compliance_condition;
        public string applicability_condition = "";
        public string[] related_requirement_ids = Array.Empty<string>();
        public string[] relevant_action_ids = Array.Empty<string>(); // Global rules only.
        public string source, source_reference;
        public bool requires_expert_review;
    }

    public enum ActionCheckPhase { Applicability, Preconditions, Completion, Completed }

    [Serializable] public sealed class ActiveActionCheck
    {
        public string requirement_id, category, condition, applicability_condition;
        public bool requires_expert_review;
        public bool is_global;
    }

    [Serializable] public sealed class ActionCheckResult
    {
        public string requirement_id, status, reason;
    }

    [Serializable] public sealed class ActionVerificationReply
    {
        public string request_id, step_id, action_id, phase;
        public string action_applicability;
        public ActionCheckResult[] results;
        [Newtonsoft.Json.JsonIgnore] public string provider, model;
    }

    [Serializable] public sealed class ActionHistoryEvent
    {
        public string run_id, at_utc, event_type, step_id, action_id, requirement_id, status, source, reason;
    }

    // Frozen request context; a reply can never mutate a different action or phase.
    public sealed class ActionVerificationTicket
    {
        public string RequestId, StepId, ActionId, Phase;
        public int Revision;
        public ActiveActionCheck[] Checks;
    }
}
