using System;

namespace HomeCare.Procedures
{
    public enum ProcedureSessionState { Ready, Verifying, Passed, NeedsRetry, Completed }

    // Step progression is independent of the headset, UI and provider.
    public sealed class ProcedureSession
    {
        public ProcedureRequirements Procedure { get; }
        public int StepIndex { get; private set; }
        public ProcedureSessionState State { get; private set; }
        public RequirementResult[] Results { get; private set; }
        public string OverallStatus { get; private set; }
        public ProcedureStep CurrentStep => Procedure.steps[StepIndex];
        public bool CanAdvance => State == ProcedureSessionState.Passed;

        public ProcedureSession(ProcedureRequirements procedure)
        {
            RequirementsJson.Validate(procedure);
            // Keep a snapshot, so later authoring edits cannot change an active session.
            Procedure = RequirementsJson.Parse(RequirementsJson.Serialize(procedure));
            Restart();
        }

        public void BeginVerification()
        {
            if (State != ProcedureSessionState.Ready && State != ProcedureSessionState.NeedsRetry)
                throw new InvalidOperationException("This step cannot start another verification now.");
            Results = null;
            OverallStatus = null;
            State = ProcedureSessionState.Verifying;
        }

        public void AcceptVerification(VerificationResponse response, EvidenceAttachment[] evidence)
        {
            if (State != ProcedureSessionState.Verifying)
                throw new InvalidOperationException("No verification is in progress.");
            Results = RequirementVerification.Evaluate(CurrentStep, response, evidence);
            OverallStatus = RequirementVerification.Aggregate(Results);
            State = OverallStatus == RequirementVerification.Satisfied
                ? ProcedureSessionState.Passed : ProcedureSessionState.NeedsRetry;
        }

        public void FailVerification()
        {
            if (State != ProcedureSessionState.Verifying) return;
            Results = null;
            OverallStatus = RequirementVerification.Unknown;
            State = ProcedureSessionState.NeedsRetry;
        }

        public void Advance()
        {
            if (!CanAdvance) throw new InvalidOperationException("Verify this step successfully before continuing.");
            if (StepIndex == Procedure.steps.Length - 1)
            {
                State = ProcedureSessionState.Completed;
                return;
            }
            StepIndex++;
            State = ProcedureSessionState.Ready;
            Results = null;
            OverallStatus = null;
        }

        public void Restart()
        {
            if (State == ProcedureSessionState.Verifying)
                throw new InvalidOperationException("Wait for the current verification before restarting.");
            StepIndex = 0;
            State = ProcedureSessionState.Ready;
            Results = null;
            OverallStatus = null;
        }
    }
}
