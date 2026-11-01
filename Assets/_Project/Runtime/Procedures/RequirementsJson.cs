using System;
using System.Collections.Generic;
using System.Linq;
using HomeCare.Procedures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HomeCare.Procedures
{
    public static class RequirementsJson
    {

        //Serialize procedure and then parse it enables duplicate a copy of procedure so we will not change the procedure itself in session part.
        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Formatting.Indented);

        public static ProcedureRequirements Parse(string json)
        {
            // Reject ambiguous duplicate properties instead of silently taking the last one.
            var token = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var procedure = token.ToObject<ProcedureRequirements>();
            Validate(procedure);
            return procedure;
        }

        //Check whether procedure and requirements of each step are valid
        public static void Validate(ProcedureRequirements procedure)
        {
            if (procedure == null)
                throw new ArgumentException("No procedure.");
            Require(procedure.procedure_title, "procedure_title");
            Require(procedure.instruction, "instruction");

            if (procedure.steps == null || procedure.steps.Length == 0)
                throw new ArgumentException("At least one step is required.");

            var stepIds = new HashSet<string>();
            var requirementIds = new HashSet<string>();

            foreach (var step in procedure.steps)
            {
                if (step == null) throw new ArgumentException("Null step.");
                Require(step.id, "step id");

                if (!stepIds.Add(step.id)) throw new ArgumentException("Duplicate step ID: " + step.id);

                Require(step.title, step.id + " title");
                Require(step.instruction, step.id + " instruction");

                Source(step.source_span, procedure.instruction);

                if (step.requirements == null || step.requirements.Length == 0)
                    throw new ArgumentException(step.id + " must have at least one requirement.");

                foreach (var requirement in step.requirements)
                {
                    if (requirement == null) throw new ArgumentException("Null requirement.");
                    Require(requirement.id, "requirement id");

                    if (!requirementIds.Add(requirement.id))
                        throw new ArgumentException("Duplicate requirement ID: " + requirement.id);

                    if (!new[] { "precondition", "postcondition", "execution_constraint" }.Contains(requirement.category))
                        throw new ArgumentException("Invalid requirement category: " + requirement.category);

                    if (!new[] { "before_step", "after_step", "whole_step", "not_visual" }.Contains(requirement.evidence_scope))
                        throw new ArgumentException("Invalid evidence scope: " + requirement.evidence_scope);

                    Require(requirement.description, requirement.id + " description");

                    Require(requirement.vqa_question, requirement.id + " vqa_question");

                    Require(requirement.observation_hint, requirement.id + " observation_hint");

                    Source(requirement.source_span, procedure.instruction);
                }
            }
        }

        static void Require(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing " + field + ".");
        }

        static void Source(string span, string instruction)
        {
            Require(span, "source_span");
            if (instruction.IndexOf(span, StringComparison.Ordinal) < 0)
                throw new ArgumentException("source_span must quote the original instruction exactly: " + span);
        }

        // Small, closed schemas supported by both existing provider clients.
        static JObject Text() => new JObject { ["type"] = "string" };
        static JObject Choice(params string[] values) => new JObject { ["type"] = "string", ["enum"] = new JArray(values) };
        static JObject ArrayOf(JObject item) => new JObject { ["type"] = "array", ["items"] = item };
        static JObject ObjectOf(params object[] pairs)
        {
            var properties = new JObject();
            for (int i = 0; i < pairs.Length; i += 2) properties[(string)pairs[i]] = (JToken)pairs[i + 1];
            return new JObject
            {
                ["type"] = "object", ["properties"] = properties,
                ["required"] = new JArray(properties.Properties().Select(p => p.Name)),
                ["additionalProperties"] = false
            };
        }

        public static string ProcedureSchema => ObjectOf(
            "procedure_title", Text(), "instruction", Text(),
            "steps", ArrayOf(ObjectOf(
                "id", Text(), "title", Text(), "instruction", Text(), "source_span", Text(),
                "requirements", ArrayOf(ObjectOf(
                    "id", Text(), "category", Choice("precondition", "postcondition", "execution_constraint"),
                    "description", Text(), "source_span", Text(), "vqa_question", Text(),
                    "evidence_scope", Choice("before_step", "after_step", "whole_step", "not_visual"),
                    "observation_hint", Text()))))).ToString(Formatting.None);

        public static string VerificationSchema => ObjectOf("step_id", Text(), "results", ArrayOf(ObjectOf(
            "requirement_id", Text(), "status", Choice("satisfied", "not_satisfied", "unknown"),
            "evidence", Text(), "reason", Text()))).ToString(Formatting.None);

        public static VerificationResponse ParseResponse(string json)
        {
            return JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error })
                .ToObject<VerificationResponse>();
        }
    }
}
