using System;
using System.IO;
using HomeCare.Procedures;
using Newtonsoft.Json.Linq;

namespace HomeCare.Authoring
{
    public static class ARPrototypeChecks
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Home Care/Run AR Prototype Checks")]
        public static void RunInEditor()
        {
            int count = Run("Assets/_Project/Content/Procedures/ar_example.json");
            UnityEngine.Debug.Log("AR prototype: " + count + " checks passed.");
        }
#endif
        public static int Run(string path)
        {
            int count = 0;
            var procedure = RequirementsJson.Parse(File.ReadAllText(path));
            var session = new ProcedureSession(procedure);
            var evidence = new[] { new EvidenceAttachment { scope = "after_step", path = "headset.jpg" } };
            Reject(() => session.Advance(), "Cannot skip an unverified step", ref count);
            session.BeginVerification();
            Reject(() => session.BeginVerification(), "Cannot submit twice", ref count);
            Reject(() => session.Restart(), "Cannot restart while awaiting a response", ref count);
            Reject(() => session.AcceptVerification(Answer(session, "satisfied", "wrong_step"), evidence), "Reject stale step result", ref count);
            session.FailVerification();
            Check(!session.CanAdvance && session.StepIndex == 0, "Network failure holds current step", ref count);
            session.BeginVerification();
            session.AcceptVerification(Answer(session, "not_satisfied"), evidence);
            Check(!session.CanAdvance && session.StepIndex == 0, "Failed result holds current step", ref count);
            session.BeginVerification();
            session.AcceptVerification(Answer(session, "unknown"), evidence);
            Check(!session.CanAdvance, "Unknown result cannot unlock next", ref count);
            session.BeginVerification();
            session.AcceptVerification(new VerificationResponse { step_id = session.CurrentStep.id, results = new RequirementResult[0] }, evidence);
            Check(!session.CanAdvance, "Missing result cannot unlock next", ref count);

            session.BeginVerification();
            session.AcceptVerification(Answer(session, "satisfied"), evidence);
            Check(session.CanAdvance, "Supported satisfied result unlocks next", ref count);
            session.Advance();
            Check(session.StepIndex == 1 && session.Results == null && !session.CanAdvance, "Advancing clears the previous result", ref count);
            while (session.State != ProcedureSessionState.Completed)
            {
                session.BeginVerification();
                session.AcceptVerification(Answer(session, "satisfied"), evidence);
                session.Advance();
            }
            Check(session.StepIndex == 2 && !session.CanAdvance, "Finish stops at the final step", ref count);
            Reject(() => session.BeginVerification(), "Cannot verify a finished run", ref count);
            session.Restart();
            Check(session.StepIndex == 0 && session.State == ProcedureSessionState.Ready, "Restart creates a clean first step", ref count);
            procedure.steps[0].title = "Edited externally";
            Check(session.CurrentStep.title != procedure.steps[0].title, "Active rubric is a snapshot", ref count);

            string request = GeminiVerificationProtocol.CreateAfterImageRequest(session.Procedure, session.CurrentStep,
                new byte[] { 1, 2, 3 }, "headset.jpg", "test-model");
            var body = JObject.Parse(request);
            Check((bool)body["store"] == false && body["x-goog-api-key"] == null, "Request stores no credentials or conversation", ref count);
            Check((string)body["input"][2]["type"] == "image" &&
                ((string)body["input"][1]["text"]).Contains("after_step"), "Photo is labeled only as after evidence", ref count);
            var response = Answer(session, "satisfied");
            var interaction = new JObject { ["status"] = "completed", ["steps"] = new JArray(new JObject {
                ["type"] = "model_output", ["content"] = new JArray(new JObject { ["type"] = "text",
                    ["text"] = RequirementsJson.Serialize(response) }) }) };
            Check(GeminiVerificationProtocol.ParseInteraction(interaction.ToString()).step_id == session.CurrentStep.id,
                "Provider response parses into the same step", ref count);
            interaction["status"] = "failed";
            Reject(() => GeminiVerificationProtocol.ParseInteraction(interaction.ToString()), "Incomplete model response cannot pass", ref count);

            var processRequired = RequirementsJson.Parse(RequirementsJson.Serialize(session.Procedure));
            processRequired.steps[0].requirements[0].evidence_scope = "whole_step";
            session = new ProcedureSession(processRequired);
            session.BeginVerification();
            session.AcceptVerification(Answer(session, "satisfied"), evidence);
            Check(!session.CanAdvance && session.Results[0].status == "unknown", "After-only capture cannot pass a process requirement", ref count);
            return count;
        }

        static VerificationResponse Answer(ProcedureSession session, string status, string stepId = null)
        {
            return new VerificationResponse { step_id = stepId ?? session.CurrentStep.id,
                results = Array.ConvertAll(session.CurrentStep.requirements, r => new RequirementResult {
                    requirement_id = r.id, status = status, evidence = "headset.jpg: observed object relation",
                    reason = "Test observation for the required state." }) };
        }

        static void Check(bool condition, string name, ref int count)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            count++;
        }

        static void Reject(Action action, string name, ref int count)
        {
            try { action(); }
            catch (ArgumentException) { count++; return; }
            catch (InvalidOperationException) { count++; return; }
            throw new Exception("FAILED: " + name);
        }
    }
}
