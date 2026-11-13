using System;
using System.Collections.Generic;

namespace HomeCare.Procedures
{
    [Serializable]
    public class ProcedureRequirements
    {
        //procedure title, original instruction and separated steps
        public string procedure_title;
        public string instruction;
        public ProcedureStep[] steps;
    }

    [Serializable]
    public class ProcedureStep
    {
        public string id;
        public string title;
        public string instruction;
        public string source_span;
        public Requirement[] requirements;
    }

    [Serializable]
    public class Requirement
    {
        public string id;
        //precondition/ postcondition /execution constraint
        public string category;
        public string description;
        public string source_span;
        // question for VLM
        public string vqa_question;
        // before_step / after_step / whole_step / not_visual
        public string evidence_scope;
        // what to focus on the image/video
        public string observation_hint;
    }

    [Serializable]
    public class RequirementResult
    {
        // the same as requirement
        public string requirement_id;
        // satisfied / not_satisfied / unknown
        public string status;
        // the description detected by VLM
        public string evidence;
        // the reason VLM provides
        public string reason;
    }

    [Serializable]
    public class VerificationResponse
    {
        public string step_id;
        public RequirementResult[] results;
    }

    [Serializable]
    public class EvidenceAttachment
    {
        //the same as evidence_scope, help to match requirement
        public string scope;
        // the path to image and video
        public string path;
    }

    [Serializable]
    public class VerificationReport
    {
        public string verified_at_utc;
        public string provider;
        public string model;
        public ProcedureRequirements procedure;
        public string step_id;
        public EvidenceAttachment[] attachments;
        public string overall_status;
        public RequirementResult[] results;
    }

    // Pure policy: visual reasoning belongs to the VQA provider, aggregation belongs here.
    public static class RequirementVerification
    {
        public const string Satisfied = "satisfied";
        public const string NotSatisfied = "not_satisfied";
        public const string Unknown = "unknown";

        public static RequirementResult[] Evaluate(ProcedureStep step, VerificationResponse response,
            EvidenceAttachment[] attachments)
        {
            if (step == null || step.requirements == null || step.requirements.Length == 0)
                throw new ArgumentException("The step must contain requirements.");
            if (response == null || response.step_id != step.id || response.results == null)
                throw new ArgumentException("The VQA response does not match the selected step.");

            var expected = new HashSet<string>();
            foreach (var requirement in step.requirements) expected.Add(requirement.id);
            var received = new Dictionary<string, RequirementResult>();
            foreach (var result in response.results)
            {
                if (result == null || string.IsNullOrWhiteSpace(result.requirement_id) ||
                    !expected.Contains(result.requirement_id) || received.ContainsKey(result.requirement_id))
                    throw new ArgumentException("The VQA response contains an unknown or duplicate requirement ID.");
                if (result.status != Satisfied && result.status != NotSatisfied && result.status != Unknown)
                    throw new ArgumentException("Invalid VQA status: " + result.status);
                received.Add(result.requirement_id, result);
            }

            var results = new List<RequirementResult>();
            foreach (var requirement in step.requirements)
            {
                RequirementResult candidate;
                string unavailable = EvidenceUnavailable(requirement.evidence_scope, attachments);
                if (unavailable != null)
                    results.Add(Uncertain(requirement.id, unavailable));
                else if (!received.TryGetValue(requirement.id, out candidate))
                    results.Add(Uncertain(requirement.id, "The model omitted this requirement."));
                else if (candidate.status != Unknown && (string.IsNullOrWhiteSpace(candidate.evidence) ||
                    string.IsNullOrWhiteSpace(candidate.reason)))
                    results.Add(Uncertain(requirement.id, "The model supplied no visual evidence or explanation."));
                else
                    results.Add(candidate);
            }
            return results.ToArray();
        }

        static RequirementResult Uncertain(string id, string reason)
        {
            return new RequirementResult { requirement_id = id, status = Unknown, evidence = "", reason = reason };
        }

        static string EvidenceUnavailable(string scope, EvidenceAttachment[] attachments)
        {
            if (scope == "not_visual") return "This condition cannot be established from visual evidence.";
            if (scope != "before_step" && scope != "after_step" && scope != "whole_step")
                return "Unrecognized evidence scope.";
            if (attachments != null)
                foreach (var attachment in attachments)
                    if (attachment != null && !string.IsNullOrWhiteSpace(attachment.path) && attachment.scope == scope)
                        return null;
            return "Required evidence was not supplied: " + scope + ".";
        }

        public static string Aggregate(RequirementResult[] results)
        {
            if (results == null || results.Length == 0) return Unknown;
            bool unknown = false;
            foreach (var result in results)
            {
                if (result != null && result.status == NotSatisfied) return NotSatisfied;
                if (result == null || result.status != Satisfied) unknown = true;
            }
            return unknown ? Unknown : Satisfied;
        }
    }
}
