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
            return count;
        }

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
