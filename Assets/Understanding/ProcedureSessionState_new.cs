using System;

namespace HomeCare_new.Procedures
{
    public sealed class ProcedureSession
    {
        public HomeCare.Procedures.ProcedureRequirements procedure {get;}

        public int stepIndex {get; private set;}

        public HomeCare.Procedures.ProcedureSessionState state {get; private set;}

        public HomeCare.Procedures.RequirementResult[] results {get; private set;}

        public string overRallState {get; private set;}

        public HomeCare.Procedures.ProcedureStep currentStep
            => procedure.steps[stepIndex];

        public bool canAdvance
            => state == HomeCare.Procedures.ProcedureSessionState.Passed;

        public ProcedureSession(HomeCare.Procedures.ProcedureRequirements pro)
        {
            HomeCare.Procedures.RequirementsJson.Validate(pro);

            procedure = HomeCare.Procedures.RequirementsJson.Parse(HomeCare.Procedures.RequirementsJson.Serialize(pro));

            Restart();
        }

        public void Restart()
        {
            if (state == HomeCare.Procedures.ProcedureSessionState.Verifying)
                throw new InvalidOperationException("Wait for current step verfied before restarting");

            stepIndex = 0;
            state = HomeCare.Procedures.ProcedureSessionState.Ready;
            results= null;
            overRallState = null;
        }

        public void BeginVerification()
        {
            if (state != HomeCare.Procedures.ProcedureSessionState.Ready || state != HomeCare.Procedures.ProcedureSessionState.NeedsRetry)
                throw new InvalidOperationException("This step cannot start verification right now");

            results = null;
            overRallState = null;

            state = HomeCare.Procedures.ProcedureSessionState.Verifying;
        }

        public void AcceptVerification(HomeCare.Procedures.VerificationResponse response, HomeCare.Procedures.EvidenceAttachment[] evidence)
        {
            if (state != HomeCare.Procedures.ProcedureSessionState.Verifying)
                throw new InvalidOperationException("No step is verifying");

            results = HomeCare.Procedures.RequirementVerification.Evaluate(currentStep, response, evidence);

            overRallState = HomeCare.Procedures.RequirementVerification.Aggregate(results);

            state = overRallState == HomeCare.Procedures.RequirementVerification.Satisfied
            ? HomeCare.Procedures.ProcedureSessionState.Passed : HomeCare.Procedures.ProcedureSessionState.NeedsRetry;
        }

        public void FailVerification()
        {
            if (state != HomeCare.Procedures.ProcedureSessionState.Verifying)
                return;

            results = null;
            overRallState = HomeCare.Procedures.RequirementVerification.Unknown;

            state = HomeCare.Procedures.ProcedureSessionState.NeedsRetry;
        }

        public void Advance()
        {
            if (!canAdvance)
            {
                throw new InvalidOperationException(
                    "Verify this step successfully before continuing."
                );
            }

            if (stepIndex == procedure.steps.Length - 1)
            {
                state = HomeCare.Procedures.ProcedureSessionState.Completed;
                return;
            }

            stepIndex++;

            state = HomeCare.Procedures.ProcedureSessionState.Ready;
            results = null;
            overRallState = null;
        }
    }
}