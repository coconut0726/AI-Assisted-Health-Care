using System;

namespace HomeCare_new.Procedures
{
    [Serializable]
    public class ProcedureRequirements_new
    {
        public string procedure_title;
        public string instruction;
        public ProcedureStep_new[] steps;
    }

    [Serializable]
    public class ProcedureStep_new
    {
        public string id;
        public string title;
        public string instruction;
        public string source_span;
        public Requirement_new[] requirements;
    }

    [Serializable]
    public class Requirement_new
    {
        public string id;
        public string category;
        public string description;
        public string source_span;
        public string vqa_question;
        public string evidence_scope;
        public string observation_hint;
    }
}