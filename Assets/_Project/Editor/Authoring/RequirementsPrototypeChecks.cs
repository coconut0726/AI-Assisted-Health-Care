using System;
using System.IO;
using HomeCare.Procedures;
using Newtonsoft.Json.Linq;

namespace HomeCare.Authoring
{
    // No API requests or Unity scene dependencies. Also run by Tools/Test-RequirementsPrototype.ps1.
    public static class RequirementsPrototypeChecks
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Home Care/Run Requirements Checks")]
        public static void RunInEditor()
        {
            int count = Run("Assets/_Project/Content/Procedures/requirements_example.json");
            UnityEngine.Debug.Log("Requirements prototype: " + count + " checks passed.");
        }
#endif
        public static int Run(string examplePath)
        {
            int count = 0;
            var procedure = RequirementsJson.Parse(File.ReadAllText(examplePath));
            Check(procedure.steps.Length == 3, "Example loads", ref count);
            var step = procedure.steps[2];
            var after = new[] { new EvidenceAttachment { scope = "after_step", path = "after.jpg" } };
            var full = new[] { after[0], new EvidenceAttachment { scope = "whole_step", path = "execution.mp4" } };
            var response = Answer(step, "satisfied", "satisfied");

            var results = RequirementVerification.Evaluate(step, response, after);
            Check(results[0].status == "unknown" && results[1].status == "satisfied",
                "After image cannot verify a continuous constraint", ref count);
            Check(RequirementVerification.Aggregate(results) == "unknown", "Unknown blocks a pass", ref count);
            results = RequirementVerification.Evaluate(step, response, full);
            Check(RequirementVerification.Aggregate(results) == "satisfied", "All supported checks pass", ref count);
            response.results[0].status = "not_satisfied";
            Check(RequirementVerification.Aggregate(RequirementVerification.Evaluate(step, response, full)) == "not_satisfied",
                "Observed failure fails the step", ref count);
            response.results[1].status = "unknown";
            Check(RequirementVerification.Aggregate(RequirementVerification.Evaluate(step, response, full)) == "not_satisfied",
                "Failure takes precedence over unknown", ref count);

            response = Answer(step, "satisfied", "satisfied");
            response.results = new[] { response.results[1] };
            results = RequirementVerification.Evaluate(step, response, full);
            Check(results.Length == 2 && results[0].status == "unknown", "Omitted requirement stays unknown", ref count);
            response = Answer(step, "satisfied", "satisfied");
            response.results[0].evidence = "";
            Check(RequirementVerification.Evaluate(step, response, full)[0].status == "unknown",
                "Unsupported claim stays unknown", ref count);
            response = Answer(step, "satisfied", "satisfied");
            response.results[0].reason = "";
            Check(RequirementVerification.Evaluate(step, response, full)[0].status == "unknown",
                "Unexplained claim stays unknown", ref count);
            Check(RequirementVerification.Aggregate(new RequirementResult[0]) == "unknown", "Empty result cannot pass", ref count);
            Check(RequirementVerification.Evaluate(step, Answer(step, "satisfied", "satisfied"), new EvidenceAttachment[0])[1].status == "unknown",
                "No attachment cannot pass", ref count);

            var nonvisual = RequirementsJson.Parse(RequirementsJson.Serialize(procedure)).steps[2];
            nonvisual.requirements[0].evidence_scope = "not_visual";
            Check(RequirementVerification.Evaluate(nonvisual, Answer(nonvisual, "satisfied", "satisfied"), full)[0].status == "unknown",
                "Vision cannot establish nonvisual conditions", ref count);
            var before = RequirementsJson.Parse(RequirementsJson.Serialize(procedure)).steps[2];
            before.requirements[0].evidence_scope = "before_step";
            Check(RequirementVerification.Evaluate(before, Answer(before, "satisfied", "satisfied"), full)[0].status == "unknown",
                "After evidence cannot substitute for before evidence", ref count);

            response = Answer(step, "satisfied", "satisfied");
            response.step_id = "s999";
            Reject(() => RequirementVerification.Evaluate(step, response, full), "Reject wrong step", ref count);
            response = Answer(step, "satisfied", "satisfied");
            response.results[1].requirement_id = response.results[0].requirement_id;
            Reject(() => RequirementVerification.Evaluate(step, response, full), "Reject duplicate result ID", ref count);
            response = Answer(step, "satisfied", "satisfied");
            response.results[0].requirement_id = "invented";
            Reject(() => RequirementVerification.Evaluate(step, response, full), "Reject foreign result ID", ref count);
            response = Answer(step, "yes", "satisfied");
            Reject(() => RequirementVerification.Evaluate(step, response, full), "Reject unknown status", ref count);

            string original = RequirementsJson.Serialize(procedure);
            procedure.steps[0].source_span = "An invented instruction";
            Reject(() => RequirementsJson.Validate(procedure), "Reject ungrounded source span", ref count);
            procedure = RequirementsJson.Parse(original);
            procedure.steps[1].requirements[0].id = procedure.steps[0].requirements[0].id;
            Reject(() => RequirementsJson.Validate(procedure), "Reject duplicate rubric ID", ref count);
            Reject(() => RequirementsJson.Parse("{}"), "Reject incomplete requirments", ref count);
            Reject(() => RequirementsJson.ParseResponse("{\"step_id\":\"s1\",\"step_id\":\"s2\",\"results\":[]}"),
                "Reject ambiguous duplicate JSON fields", ref count);
            Check((bool)JObject.Parse(RequirementsJson.ProcedureSchema)["additionalProperties"] == false,
                "Closed structured-output schema", ref count);
            return count;
        }

        static VerificationResponse Answer(ProcedureStep step, string first, string second)
        {
            return new VerificationResponse { step_id = step.id, results = new[] {
                new RequirementResult { requirement_id = step.requirements[0].id, status = first,
                    evidence = "execution.mp4 at 00:01: block remains on pad", reason = "Observed relation during wrapping." },
                new RequirementResult { requirement_id = step.requirements[1].id, status = second,
                    evidence = "after.jpg: strap surrounds block", reason = "The required placement is visible." }
            } };
        }

        static void Check(bool value, string name, ref int count)
        {
            if (!value) throw new Exception("FAILED: " + name);
            count++;
        }

        static void Reject(Action action, string name, ref int count)
        {
            try { action(); }
            catch (ArgumentException) { count++; return; }
            catch (Newtonsoft.Json.JsonException) { count++; return; }
            throw new Exception("FAILED: " + name);
        }
    }
}
