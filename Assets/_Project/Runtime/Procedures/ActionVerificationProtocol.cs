using System;
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
                request_id = ticket.RequestId, run_id = session.RunId, verification_scope = "single_current_image",
                task_goal = string.IsNullOrWhiteSpace(session.Procedure.task_goal) ? session.Procedure.procedure_title : session.Procedure.task_goal,
                current_step = new { step_id = ticket.StepId, title = session.CurrentStep.step_name, description = session.CurrentStep.step_description },
                current_action = new { action_id = a.action_id, instruction = a.instruction, applicability_condition = a.applicability_condition },
                phase = ticket.Phase,
                established_action_applicability = session.Applicability,
                current_requirements = ticket.Checks,
                // History and graph gates remain in the session; they are not evidence for this image check.
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
