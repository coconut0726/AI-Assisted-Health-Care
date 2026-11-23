using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HomeCare.Procedures.Actions
{
    public static class ActionVerificationProtocol
    {
        public static string CreateRequest(ActionRequirementSession session, ActionVerificationTicket ticket,
            byte[] jpeg, string capturedAtUtc, string model = "project-backend")
        {
            if (session == null || ticket == null || jpeg == null || jpeg.Length == 0) throw new ArgumentException("A session, ticket and camera image are required.");
            if (ticket.Revision != session.Revision || ticket.ActionId != session.CurrentAction.action_id || ticket.Phase != session.Phase.ToString())
                throw new ArgumentException("The capture belongs to an obsolete action or phase.");
            var a = session.CurrentAction;
            var context = new
            {
                request_id = ticket.RequestId, run_id = session.RunId,
                task_goal = string.IsNullOrWhiteSpace(session.Procedure.task_goal) ? session.Procedure.procedure_title : session.Procedure.task_goal,
                current_step = new { step_id = ticket.StepId, title = session.CurrentStep.step_name, description = session.CurrentStep.step_description },
                current_action = new { action_id = a.action_id, instruction = a.instruction, applicability_condition = a.applicability_condition },
                phase = ticket.Phase,
                established_action_applicability = session.Applicability,
                current_requirements = ticket.Checks,
                dependency_history = session.DependencySummary(),
                confirmed_step_history = session.History.Where(h => h.run_id == session.RunId &&
                    (h.event_type == "action_finished" || h.event_type == "precondition_confirmed" || h.event_type == "requirement_confirmed" || h.event_type == "applicability_confirmed" || h.event_type == "rule_violation"))
                    .Reverse().Take(12).Reverse().ToArray(),
                task_outline = session.Procedure.steps.Select(s => new { step_id = s.step_id, title = s.step_name, action_ids = s.actions.Select(x => x.action_id).ToArray() }).ToArray(),
                current_frame = new { captured_at_utc = capturedAtUtc, kind = "single_current_rgb_frame" }
            };
            return new JObject
            {
                ["model"] = model, ["store"] = false,
                ["system_instruction"] = RequirementsPrompts.VerifyAction,
                ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = ActionProcedureJson.Serialize(context) },
                    new JObject { ["type"] = "image", ["mime_type"] = "image/jpeg", ["data"] = Convert.ToBase64String(jpeg) }),
                ["response_format"] = new JObject { ["type"] = "text", ["mime_type"] = "application/json", ["schema"] = JObject.Parse(ActionProcedureJson.ReplySchema(ticket)) }
            }.ToString(Formatting.None);
        }

        public static ActionVerificationReply Parse(string envelope)
        {
            var root = ActionProcedureJson.Object(envelope);
            var reply = ActionProcedureJson.Object(GeminiVerificationProtocol.ExtractModelText(envelope)).ToObject<ActionVerificationReply>();
            reply.provider = (string)root["provider"];
            reply.model = (string)root["model"];
            return reply;
        }
    }
}
