using System;
using UnityEngine;
using System.Collections.Generic;

namespace HomeCare_new.Procedures
{
    public static class RequirementsJson_new
    {
        public static ProcedureRequirements_new Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("json is missing");

            ProcedureRequirements_new procedures = JsonUtility.FromJson<ProcedureRequirements_new>(json);

            Validate(procedures);

            return procedures;
        }

        public static void Validate(ProcedureRequirements_new procedures)
        {
            if(procedures == null)
            {
                throw new ArgumentException("no valid procedure");
            }

            if (string.IsNullOrWhiteSpace(procedures.procedure_title))
                throw new ArgumentException("Procedure title is missing.");
            
            if (string.IsNullOrWhiteSpace(procedures.instruction))
                throw new ArgumentException("Original instruction is missing.");

            if(procedures.steps == null || procedures.steps.Length == 0)
            {
                throw new ArgumentException("procedure must contain steps");
            }

            var stepIds = new HashSet<string>();
            var requirementIds = new HashSet<string>();



            foreach (var step in procedures.steps)
            {
                if (step == null)
                    throw new ArgumentException("A step is null");

                if (string.IsNullOrWhiteSpace(step.id))
                    throw new ArgumentException("Step ID is missing");

                if (!stepIds.Add(step.id))
                    throw new ArgumentException("Duplicate step ID: " + step.id);

                if (string.IsNullOrWhiteSpace(step.title))
                    throw new ArgumentException("step title is missing");

                if (string.IsNullOrWhiteSpace(step.instruction))
                    throw new ArgumentException("Step instruction is missing");
                               
                if (step.requirements == null || step.requirements.Length == 0)
                    throw new ArgumentException("step has no requirements: " + step.id);

                foreach (var req in step.requirements)
                {
                    if (req == null)
                        throw new ArgumentException("Requirement is null.");

                    if (string.IsNullOrWhiteSpace(req.id))
                        throw new ArgumentException("Requirement ID is missing.");

                    if (!requirementIds.Add(req.id))
                        throw new ArgumentException(
                            "Duplicate requirement ID: " + req.id);

                    if (string.IsNullOrWhiteSpace(req.description))
                        throw new ArgumentException(
                            "Requirement description is missing.");

                    if (req.category != "precondition" &&
                        req.category != "postcondition" &&
                        req.category != "execution_constraint")
                        throw new ArgumentException(
                            "Invalid requirement category: " + req.category);

                    if (req.evidence_scope != "before_step" &&
                        req.evidence_scope != "after_step" &&
                        req.evidence_scope != "whole_step" &&
                        req.evidence_scope != "not_visual")
                        throw new ArgumentException(
                            "Invalid evidence scope: " + req.evidence_scope);

                    if (req.evidence_scope != "not_visual" &&
                        string.IsNullOrWhiteSpace(req.vqa_question))
                        throw new ArgumentException(
                            "Visual requirement has no VQA question: " + req.id);
                }
            }
        }

        public static string Serialize<T>(T value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            return JsonUtility.ToJson(value, true);
        }
    }

}