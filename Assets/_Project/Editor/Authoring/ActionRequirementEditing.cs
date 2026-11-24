using System;
using System.Collections.Generic;
using System.Linq;
using HomeCare.Procedures.Actions;

namespace HomeCare.Authoring
{
    // Data operations shared by the form and checks; no Unity or provider dependency.
    public static class ActionRequirementEditing
    {
        public const string AuthorSource = "author_edit";

        public static ActionRequirement AddCondition(ActionProcedure p, ProcedureAction action, bool before)
        {
            var q = new ActionRequirement
            {
                requirement_id = NextId(p, action.action_id + (before ? "_PRE" : "_POST")),
                condition = "", source = AuthorSource, source_reference = "Author-defined condition for " + action.action_id + "."
            };
            if (before) action.preconditions = action.preconditions.Append(q).ToArray();
            else action.postconditions = action.postconditions.Append(q).ToArray();
            NoteEdited(p); SyncCompletionModes(p); return q;
        }

        public static void RemoveCondition(ActionProcedure p, ProcedureAction action, ActionRequirement q, bool before)
        {
            if (before) action.preconditions = action.preconditions.Where(x => x != q).ToArray();
            else action.postconditions = action.postconditions.Where(x => x != q).ToArray();
            foreach (var error in Errors(p))
                error.related_requirement_ids = error.related_requirement_ids.Where(id => id != q.requirement_id).ToArray();
            NoteEdited(p); SyncCompletionModes(p);
        }

        public static ActionErrorCriterion AddError(ActionProcedure p, ProcedureAction action, string type, bool global = false)
        {
            if (!ActionProcedureJson.SupportedErrorTypes.Contains(type)) throw new ArgumentException("Choose one of the five supported error types.");
            var error = new ActionErrorCriterion
            {
                criterion_id = NextId(p, global ? "P_ERR" : action.action_id + "_ERR"), error_type = type,
                violation_condition = "", compliance_condition = "", source = AuthorSource,
                source_reference = "Author-defined rule for " + (global ? "the procedure" : action.action_id) + ".",
                relevant_action_ids = global ? new[] { action.action_id } : Array.Empty<string>()
            };
            if (global) p.procedure_error_criteria = p.procedure_error_criteria.Append(error).ToArray();
            else action.error_criteria = action.error_criteria.Append(error).ToArray();
            NoteEdited(p); return error;
        }

        public static void RemoveError(ActionProcedure p, ProcedureAction action, ActionErrorCriterion error, bool global)
        {
            if (global) p.procedure_error_criteria = p.procedure_error_criteria.Where(x => x != error).ToArray();
            else action.error_criteria = action.error_criteria.Where(x => x != error).ToArray();
            NoteEdited(p);
        }

        public static void MarkEdited(ActionRequirement q, string owner)
        {
            if (q.source != AuthorSource) q.source_reference = EditReference(owner, q.source_reference);
            q.source = AuthorSource;
        }

        public static void MarkEdited(ActionErrorCriterion q, string owner)
        {
            if (q.source != AuthorSource) q.source_reference = EditReference(owner, q.source_reference);
            q.source = AuthorSource;
        }

        static string EditReference(string owner, string previous) => "Author edit for " + owner +
            (string.IsNullOrWhiteSpace(previous) ? "." : "; previous source: " + previous);

        public static void NoteEdited(ActionProcedure p)
        {
            const string note = "The author edited this rubric after generation. Original generation notes may describe the earlier version; author_edit identifies manually supplied conditions, not execution evidence.";
            p.review_notes = (p.review_notes ?? Array.Empty<string>()).Append(note).Distinct().ToArray();
        }

        public static void SyncCompletionModes(ActionProcedure p)
        {
            foreach (var a in Actions(p)) a.manual_completion_required = a.postconditions.Length == 0;
        }

        public static ActionProcedure ReadyToSave(ActionProcedure p)
        {
            var copy = ActionProcedureJson.Object(ActionProcedureJson.Serialize(p)).ToObject<ActionProcedure>();
            if (Actions(copy).SelectMany(a => a.preconditions.Concat(a.postconditions)).Any(q => q.source == AuthorSource) ||
                Errors(copy).Any(e => e.source == AuthorSource)) NoteEdited(copy);
            SyncCompletionModes(copy); ActionProcedureJson.Validate(copy); return copy;
        }

        public static string[] Issues(ActionProcedure p)
        {
            var issues = new List<string>();
            foreach (var a in Actions(p))
            {
                foreach (var q in a.preconditions.Concat(a.postconditions))
                    if (string.IsNullOrWhiteSpace(q.condition)) issues.Add(a.action_id + ": fill in the empty condition, or remove that row.");
                foreach (var e in a.error_criteria) ErrorIssues(e, a.action_id, issues);
            }
            foreach (var e in p.procedure_error_criteria)
            {
                ErrorIssues(e, "Shared rule", issues);
                if (e.relevant_action_ids.Length == 0) issues.Add("Shared rule: select at least one Action to check.");
            }
            if (issues.Count == 0)
                try { ReadyToSave(p); }
                catch (ArgumentException e) { issues.Add(FriendlyError(e.Message)); }
            return issues.ToArray();
        }

        static void ErrorIssues(ActionErrorCriterion e, string owner, List<string> issues)
        {
            if (string.IsNullOrWhiteSpace(e.violation_condition)) issues.Add(owner + ": describe what the error looks like.");
            if (string.IsNullOrWhiteSpace(e.compliance_condition)) issues.Add(owner + ": describe the correct visible state for that error rule.");
        }

        public static string FriendlyError(string message)
        {
            if (message.IndexOf("duplicates", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The same check appears more than once. Keep one copy of a condition instead of repeating it as an error or shared rule. " + message;
            if (message.IndexOf("source quotation", StringComparison.OrdinalIgnoreCase) >= 0)
                return "An original source quote no longer matches. Edit through the form to record an author edit, or restore the quote in Advanced JSON. " + message;
            return message;
        }

        public static ProcedureAction[] Actions(ActionProcedure p) => p.steps.SelectMany(s => s.actions).ToArray();
        static IEnumerable<ActionErrorCriterion> Errors(ActionProcedure p) => Actions(p).SelectMany(a => a.error_criteria).Concat(p.procedure_error_criteria);

        static string NextId(ActionProcedure p, string prefix)
        {
            var used = new HashSet<string>(Actions(p).SelectMany(a => a.preconditions.Concat(a.postconditions)).Select(q => q.requirement_id)
                .Concat(Errors(p).Select(e => e.criterion_id)));
            for (int i = 1; ; i++) if (!used.Contains(prefix + i)) return prefix + i;
        }
    }
}
