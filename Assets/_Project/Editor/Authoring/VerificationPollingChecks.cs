using System;
using System.IO;
using HomeCare.Procedures;

namespace HomeCare.Authoring
{
    /// <summary>Uses a fake clock and model replies; no camera, network or API key is needed.</summary>
    public static class VerificationPollingChecks
    {
#if UNITY_EDITOR
        // [UnityEditor.MenuItem("Tools/Home Care/Run Verification Polling Checks")]
        public static void RunInEditor() => UnityEngine.Debug.Log("Verification polling: " +
            Run("Assets/_Project/Content/Procedures/ar_example.json") + " checks passed.");
#endif

        public static int Run(string path)
        {
            int count = 0;
            var clock = new VerificationPollingSchedule();
            clock.Start(0, 5);
            Check(!clock.TryConsumeTick(4.999, 5, true), "First capture waits five seconds", ref count);
            Check(clock.TryConsumeTick(5, 5, true), "First capture is due at five seconds", ref count);
            Check(!clock.TryConsumeTick(5, 5, true), "Cannot dispatch twice in one slot", ref count);
            Check(!clock.TryConsumeTick(10, 5, false), "Busy detector or verifier suppresses a capture", ref count);
            Check(!clock.TryConsumeTick(12, 5, true), "Finishing a long request does not replay a busy slot", ref count);
            Check(clock.TryConsumeTick(15, 5, true), "Polling continues at the next free slot", ref count);
            Check(clock.TryConsumeTick(100, 5, true), "A long frame stall permits one fresh capture", ref count);
            Check(!clock.TryConsumeTick(100.001, 5, true), "Missed slots cannot create a request burst", ref count);
            clock.Stop();
            Check(!clock.TryConsumeTick(200, 5, true), "Home, pause or completion stops polling", ref count);
            clock.Start(201, 5);
            Check(!clock.TryConsumeTick(205, 5, true) && clock.TryConsumeTick(206, 5, true),
                "Resuming waits a fresh interval instead of catching up", ref count);
            clock.Start(208, 5); // a manual check reschedules the next automatic slot
            Check(!clock.TryConsumeTick(211, 5, true) && clock.TryConsumeTick(213, 5, true),
                "Manual checks postpone the next automatic capture", ref count);
            clock.Start(220, 2);
            Check(!clock.TryConsumeTick(221.9, 2, true) && clock.TryConsumeTick(222, 2, true),
                "Polling interval is configurable", ref count);

            var session = new ProcedureSession(RequirementsJson.Parse(File.ReadAllText(path)));
            var image = new[] { new EvidenceAttachment { scope = "after_step", path = "current_frame.jpg" } };
            clock.Start(0, 5);
            double now = 5;
            foreach (string result in new[] { "not_satisfied", "unknown", "satisfied" })
            {
                Check(clock.TryConsumeTick(now, 5, true), "Active steps can be checked repeatedly", ref count);
                session.BeginVerification();
                Check(!clock.TryConsumeTick(now + 5, 5, session.State != ProcedureSessionState.Verifying),
                    "A response taking longer than five seconds cannot overlap another check", ref count);
                session.AcceptVerification(Answer(session, result), image);
                Check(session.StepIndex == 0 && session.CanAdvance == (result == "satisfied"),
                    "Unmet/unknown stay on the step; pass only unlocks Next", ref count);
                now += 10;
            }
            clock.Stop();
            Check(!clock.TryConsumeTick(100, 5, true), "Passed steps stop being checked", ref count);
            session.Advance();
            clock.Start(100, 5);
            Check(session.StepIndex == 1 && session.Results == null && !clock.TryConsumeTick(104.99, 5, true),
                "Next clears old results and starts a fresh polling interval", ref count);

            var procedure = RequirementsJson.Parse(File.ReadAllText(path));
            procedure.steps[0].requirements[0].evidence_scope = "whole_step";
            session = new ProcedureSession(procedure);
            for (int i = 0; i < 3; i++)
            {
                session.BeginVerification();
                session.AcceptVerification(Answer(session, "satisfied"), image);
            }
            Check(!session.CanAdvance && session.Results[0].status == "unknown",
                "Repeated after images cannot establish a whole-step execution constraint", ref count);

            procedure = RequirementsJson.Parse(File.ReadAllText(path));
            var original = procedure.steps[0].requirements[0];
            procedure.steps[0].requirements = new[]
            {
                original, Copy(original, "_second", "postcondition", "after_step"),
                Copy(original, "_pre", "precondition", "before_step"),
                Copy(original, "_constraint", "execution_constraint", "whole_step")
            };
            session = new ProcedureSession(procedure, postconditionsOnly: true);
            Check(session.CurrentStep.requirements.Length == 2 && procedure.steps[0].requirements.Length == 4,
                "Postcondition-only sessions filter their snapshot without changing the author's procedure", ref count);
            Check(new ProcedureSession(procedure).CurrentStep.requirements.Length == 4,
                "Other demos retain all requirements by default", ref count);
            foreach (string incomplete in new[] { "not_satisfied", "unknown" })
            {
                session.BeginVerification();
                var partial = Answer(session, "satisfied");
                partial.results[1].status = incomplete;
                session.AcceptVerification(partial, image);
                Check(!session.CanAdvance && session.StepIndex == 0,
                    "One passed postcondition cannot advance while another is " + incomplete, ref count);
            }
            session.BeginVerification();
            var omitted = Answer(session, "satisfied");
            omitted.results = new[] { omitted.results[0] };
            session.AcceptVerification(omitted, image);
            Check(!session.CanAdvance, "An omitted postcondition cannot cause automatic advancement", ref count);

            // Drive the same accept/advance sequence as RealCareSession, with one fresh polling period per step.
            clock.Start(0, 1);
            double completedAt = 0;
            int advanced = 0;
            while (session.State != ProcedureSessionState.Completed)
            {
                int index = session.StepIndex;
                Check(!clock.TryConsumeTick(completedAt + 0.999, 1, true) &&
                      clock.TryConsumeTick(completedAt + 1, 1, true),
                    "Each new step waits its own one-second polling interval", ref count);
                session.BeginVerification();
                Check(!clock.TryConsumeTick(completedAt + 2, 1, false),
                    "Slow inference skips one-second slots instead of overlapping requests", ref count);
                session.AcceptVerification(Answer(session, "satisfied"), image);
                Check(session.CanAdvance, "All current postconditions must pass together", ref count);
                session.Advance();
                advanced++;
                completedAt += 2;
                if (session.State == ProcedureSessionState.Completed) clock.Stop();
                else
                {
                    Check(session.StepIndex == index + 1 && session.Results == null && !session.CanAdvance,
                        "Advancement clears the old pass rather than carrying it into the next step", ref count);
                    clock.Start(completedAt, 1);
                }
            }
            Check(advanced == procedure.steps.Length && !clock.TryConsumeTick(completedAt + 100, 1, true),
                "The final pass completes the procedure once and stops polling", ref count);
            bool blocked = false;
            try { session.Advance(); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "A completed procedure cannot advance a second time", ref count);

            procedure.steps[0].requirements = new[] { Copy(original, "_only_pre", "precondition", "before_step") };
            blocked = false;
            try { _ = new ProcedureSession(procedure, postconditionsOnly: true); }
            catch (ArgumentException) { blocked = true; }
            Check(blocked, "A step without postconditions is rejected instead of passing an empty list", ref count);
            return count;
        }

        static Requirement Copy(Requirement source, string suffix, string category, string scope) => new()
        {
            id = source.id + suffix, category = category, evidence_scope = scope,
            description = source.description, source_span = source.source_span,
            vqa_question = source.vqa_question, observation_hint = source.observation_hint
        };

        static VerificationResponse Answer(ProcedureSession session, string status) => new()
        {
            step_id = session.CurrentStep.id,
            results = Array.ConvertAll(session.CurrentStep.requirements, r => new RequirementResult
            {
                requirement_id = r.id, status = status, evidence = "current_frame.jpg: visible state",
                reason = "Mock observation for the required state."
            })
        };

        static void Check(bool condition, string name, ref int count)
        {
            if (!condition) throw new Exception("FAILED: " + name);
            count++;
        }
    }
}
