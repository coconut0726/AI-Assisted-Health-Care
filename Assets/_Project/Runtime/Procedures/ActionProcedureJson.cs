using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HomeCare.Procedures.Actions
{
    public static class ActionProcedureJson
    {
        public const string Version = "action_requirements_v1";
        static readonly string[] ErrorTypes = { "wrong_object", "spatial_error", "technique_error", "measurement_error", "timing_parameter_error" };
        public static string[] SupportedErrorTypes => ErrorTypes.ToArray();
        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Formatting.Indented);
        public static JObject Object(string json) => JObject.Parse(json,
            new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });

        public static ActionProcedure Parse(string json, bool requireRequirements = true)
        {
            var root = Object(json);
            foreach (var property in root.Descendants().OfType<JProperty>())
                if (property.Name.StartsWith("evidence", StringComparison.OrdinalIgnoreCase) || property.Name == "execution_constraints")
                    throw new ArgumentException("This action-flow format does not contain " + property.Name + ".");
            var procedure = root.ToObject<ActionProcedure>();
            Validate(procedure, requireRequirements);
            return procedure;
        }

        public static void Validate(ActionProcedure p, bool requirements = true)
        {
            if (p == null || p.schema_version != Version) throw new ArgumentException("Expected " + Version + ".");
            Need(p.procedure_title, "procedure_title"); Need(p.instruction, "instruction");
            if (p.steps == null || p.steps.Length == 0 || p.steps.Any(s => s == null || s.actions == null || s.actions.Length == 0))
                throw new ArgumentException("A procedure must have steps containing actions.");
            var steps = Unique(p.steps, s => s.step_id, "step");
            var all = p.steps.SelectMany(s => s.actions).ToArray();
            if (all.Any(a => a == null)) throw new ArgumentException("Null action.");
            var actions = Unique(all, a => a.action_id, "action");
            var predicates = new HashSet<string>();
            var requirementsById = new HashSet<string>();
            foreach (var step in p.steps)
            {
                Need(step.step_name, "step_name"); Refs(step.dependencies, steps.Keys, step.step_id);
                foreach (var a in step.actions)
                {
                    Need(a.instruction, a.action_id + " instruction"); Quote(a.original_text, p.instruction);
                    Refs(a.dependencies, actions.Keys, a.action_id);
                    if (a.conditional_dependencies == null || a.preconditions == null || a.postconditions == null || a.error_criteria == null ||
                        a.associated_constraints == null || a.actor == null || a.tools == null || a.materials == null || a.contact_media == null || a.targets == null)
                        throw new ArgumentException(a.action_id + ": arrays cannot be null.");
                    foreach (var edge in a.conditional_dependencies)
                    {
                        if (edge == null) throw new ArgumentException("Null conditional dependency.");
                        Refs(new[] { edge.action_id }, actions.Keys, a.action_id); Need(edge.condition, "conditional dependency condition");
                    }
                    foreach (var text in a.associated_constraints) Quote(text, p.instruction);
                    if (requirements && a.postconditions.Length == 0 && !a.manual_completion_required)
                        throw new ArgumentException(a.action_id + " needs an image postcondition or explicit manual completion.");
                    if (a.manual_completion_required && a.postconditions.Length != 0)
                        throw new ArgumentException(a.action_id + ": manual completion is reserved for Actions without image postconditions.");
                    foreach (var q in a.preconditions.Concat(a.postconditions))
                    {
                        if (q == null) throw new ArgumentException("Null requirement.");
                        Id(q.requirement_id, predicates); requirementsById.Add(q.requirement_id);
                        Need(q.condition, "condition"); Provenance(q.source, q.source_reference, q.requires_expert_review, p.instruction);
                    }
                    foreach (var e in a.error_criteria) CheckError(e, p, predicates, false, actions.Keys);
                }
            }
            if (p.procedure_error_criteria == null) throw new ArgumentException("Null procedure_error_criteria.");
            foreach (var e in p.procedure_error_criteria) CheckError(e, p, predicates, true, actions.Keys);
            foreach (var e in all.SelectMany(a => a.error_criteria).Concat(p.procedure_error_criteria))
                Refs(e.related_requirement_ids, requirementsById, null);
            var localClaims = new HashSet<string>(all.SelectMany(a => a.preconditions.Concat(a.postconditions)).Select(q => ClaimKey(q.condition))
                .Concat(all.SelectMany(a => a.error_criteria).Select(e => ClaimKey(e.compliance_condition))));
            foreach (var error in p.procedure_error_criteria)
                if (localClaims.Contains(ClaimKey(error.compliance_condition)))
                    throw new ArgumentException("A shared rule duplicates an existing local condition; keep the local check and omit this global copy: " + error.criterion_id);
            foreach (var action in all)
            {
                var claims = new HashSet<string>(action.preconditions.Concat(action.postconditions).Select(q => ClaimKey(q.condition)));
                if (action.error_criteria.Any(e => claims.Contains(ClaimKey(e.compliance_condition))))
                    throw new ArgumentException(action.action_id + ": an error criterion duplicates a requirement.");
            }
            var edges = new Dictionary<string, HashSet<string>>();
            foreach (var step in p.steps)
                foreach (var a in step.actions)
                    edges[a.action_id] = new HashSet<string>(a.dependencies.Concat(a.conditional_dependencies.Select(d => d.action_id))
                        .Concat(step.dependencies.SelectMany(s => steps[s].actions).Select(x => x.action_id)));
            var visiting = new HashSet<string>(); var done = new HashSet<string>();
            foreach (var id in actions.Keys) Visit(id, edges, visiting, done);
        }

        static void Visit(string id, Dictionary<string, HashSet<string>> edges, HashSet<string> visiting, HashSet<string> done)
        {
            if (done.Contains(id)) return;
            if (!visiting.Add(id)) throw new ArgumentException("Dependency cycle at " + id);
            foreach (var dep in edges[id]) Visit(dep, edges, visiting, done);
            visiting.Remove(id); done.Add(id);
        }

        static void CheckError(ActionErrorCriterion e, ActionProcedure p, HashSet<string> ids, bool global, IEnumerable<string> actions)
        {
            if (e == null) throw new ArgumentException("Null error criterion.");
            Id(e.criterion_id, ids); Need(e.violation_condition, "violation_condition"); Need(e.compliance_condition, "compliance_condition");
            if (!ErrorTypes.Contains(e.error_type)) throw new ArgumentException("Order/Missing are checked by the session, not generated VQA rules: " + e.error_type);
            Provenance(e.source, e.source_reference, e.requires_expert_review, p.instruction);
            if (e.related_requirement_ids == null || e.relevant_action_ids == null) throw new ArgumentException("Null criterion references.");
            if (global && e.relevant_action_ids.Length == 0) throw new ArgumentException("A global rule must identify relevant actions.");
            Refs(e.relevant_action_ids, actions, null);
        }

        static Dictionary<string, T> Unique<T>(IEnumerable<T> values, Func<T, string> id, string kind)
        {
            var result = new Dictionary<string, T>();
            foreach (var value in values) { var key = id(value); Need(key, kind + " id"); if (result.ContainsKey(key)) throw new ArgumentException("Duplicate " + kind + " ID: " + key); result.Add(key, value); }
            return result;
        }
        static void Id(string id, HashSet<string> ids) { Need(id, "requirement/criterion id"); if (!ids.Add(id)) throw new ArgumentException("Duplicate predicate ID: " + id); }
        static void Refs(IEnumerable<string> refs, IEnumerable<string> valid, string self)
        {
            if (refs == null) throw new ArgumentException("Null dependencies/references.");
            var allowed = new HashSet<string>(valid); var seen = new HashSet<string>();
            foreach (var id in refs) if (id == null || id == self || !allowed.Contains(id) || !seen.Add(id)) throw new ArgumentException("Invalid reference: " + id);
        }
        static void Need(string text, string name) { if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Missing " + name); }
        static string ClaimKey(string text) => new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        static void Quote(string quote, string source) { Need(quote, "source quotation"); if (!source.Contains(quote)) throw new ArgumentException("Not an exact source quotation: " + quote); }
        static void Provenance(string source, string reference, bool review, string instruction)
        {
            Need(reference, "source_reference");
            if (source == "explicit_instruction") Quote(reference, instruction);
            else if (source != "graph_dependency" && source != "expert_prior" && source != "inferred" && source != "author_edit") throw new ArgumentException("Invalid requirement source: " + source);
            if (source == "inferred" && !review) throw new ArgumentException("Inferred requirements need expert review.");
        }

        public static JObject TaskGraph(ActionProcedure p)
        {
            var root = JObject.FromObject(p); root.Remove("schema_version"); root.Remove("task_goal"); root.Remove("procedure_error_criteria");
            foreach (var action in root["steps"].SelectMany(s => s["actions"]))
                foreach (var key in new[] { "preconditions", "postconditions", "error_criteria", "manual_completion_required" }) ((JObject)action).Remove(key);
            return root;
        }

        public static ActionProcedure Merge(ActionProcedure graph, IEnumerable<string> batches)
        {
            var copy = Parse(Serialize(graph), false);
            var byId = copy.steps.SelectMany(s => s.actions).ToDictionary(a => a.action_id);
            var seen = new HashSet<string>(); var globals = new List<ActionErrorCriterion>();
            var notes = new List<string>(copy.review_notes ?? Array.Empty<string>());
            var aliases = new Dictionary<string, HashSet<string>>();
            foreach (var json in batches)
            {
                var batch = Object(json);
                foreach (var token in (JArray)batch["action_updates"])
                {
                    string id = (string)token["action_id"];
                    if (id == null || !byId.ContainsKey(id) || !seen.Add(id)) throw new ArgumentException("Unknown or duplicate Action update: " + id);
                    if (((JObject)token).Properties().Any(x => !new[] { "action_id", "manual_completion_required", "preconditions", "postconditions", "error_criteria", "review_notes" }.Contains(x.Name)))
                        throw new ArgumentException("An update tried to change the task graph.");
                    if (token["manual_completion_required"]?.Type != JTokenType.Boolean)
                        throw new ArgumentException(id + " needs an explicit manual_completion_required flag.");
                    byId[id].manual_completion_required = (bool)token["manual_completion_required"];
                    var normalized = NamespaceRequirementIds((JObject)token, aliases);
                    byId[id].preconditions = normalized["preconditions"].ToObject<ActionRequirement[]>();
                    byId[id].postconditions = normalized["postconditions"].ToObject<ActionRequirement[]>();
                    byId[id].error_criteria = normalized["error_criteria"].ToObject<ActionErrorCriterion[]>();
                    if (token["review_notes"] is JArray localNotes)
                        notes.AddRange(localNotes.Values<string>().Select(n => id + ": " + n));
                }
                globals.AddRange(batch["procedure_error_criteria"].ToObject<ActionErrorCriterion[]>());
                if (batch["review_notes"] is JArray batchNotes) notes.AddRange(batchNotes.Values<string>());
            }
            if (seen.Count != byId.Count) throw new ArgumentException("Not every Action was enriched.");
            var known = new HashSet<string>(byId.Values.SelectMany(a => a.preconditions.Concat(a.postconditions)).Select(q => q.requirement_id));
            foreach (var error in byId.Values.SelectMany(a => a.error_criteria).Concat(globals))
            {
                if (error == null || error.related_requirement_ids == null) throw new ArgumentException("Null error/reference array.");
                error.related_requirement_ids = error.related_requirement_ids.Select(id =>
                {
                    if (known.Contains(id)) return id;
                    if (aliases.TryGetValue(id, out var targets) && targets.Count == 1) return targets.Single();
                    throw new ArgumentException("Unknown or ambiguous requirement reference: " + id);
                }).ToArray();
            }
            copy.procedure_error_criteria = globals.ToArray(); copy.review_notes = notes.Distinct().ToArray(); Validate(copy); return copy;
        }

        // Each request sees one Action, so model-local IDs such as R1 need an Action namespace.
        // Only identifiers/references change; source conditions and the task graph remain untouched.
        static JObject NamespaceRequirementIds(JObject input, Dictionary<string, HashSet<string>> aliases)
        {
            var update = (JObject)input.DeepClone();
            string prefix = (string)update["action_id"] + "_";
            var local = new Dictionary<string, string>();
            foreach (var q in ((JArray)update["preconditions"]).Concat((JArray)update["postconditions"]))
            {
                string old = (string)q["requirement_id"]; Need(old, "requirement_id");
                if (local.ContainsKey(old)) throw new ArgumentException("Duplicate requirement ID within one Action: " + old);
                string id = old.StartsWith(prefix, StringComparison.Ordinal) ? old : prefix + old;
                local.Add(old, id); q["requirement_id"] = id;
                if (!aliases.TryGetValue(old, out var targets)) aliases[old] = targets = new HashSet<string>();
                targets.Add(id);
            }
            foreach (var error in (JArray)update["error_criteria"])
            {
                string id = (string)error["criterion_id"]; Need(id, "criterion_id");
                error["criterion_id"] = id.StartsWith(prefix, StringComparison.Ordinal) ? id : prefix + id;
                error["related_requirement_ids"] = new JArray(((JArray)error["related_requirement_ids"]).Values<string>()
                    .Select(reference => local.TryGetValue(reference, out var target) ? target : reference));
            }
            return update;
        }

        static JObject Text() => new JObject { ["type"] = "string" };
        static JObject Bool() => new JObject { ["type"] = "boolean" };
        static JObject Choice(params string[] values) => new JObject { ["type"] = "string", ["enum"] = new JArray(values) };
        static JObject List(JObject item) => new JObject { ["type"] = "array", ["items"] = item };
        static JObject Strings() => List(Text());
        static JObject NoStepDependencies() { var schema = Strings(); schema["maxItems"] = 0; return schema; }
        static JObject Obj(params object[] pairs)
        {
            var props = new JObject(); for (int i = 0; i < pairs.Length; i += 2) props[(string)pairs[i]] = (JToken)pairs[i + 1];
            return new JObject { ["type"] = "object", ["properties"] = props, ["required"] = new JArray(props.Properties().Select(x => x.Name)), ["additionalProperties"] = false };
        }
        static JObject Requirement() => Obj("requirement_id", Text(), "condition", Text(), "source", Choice("explicit_instruction", "graph_dependency", "expert_prior", "inferred"),
            "source_reference", Text(), "requires_expert_review", Bool());
        static JObject Error(bool global)
        {
            var schema = Obj("criterion_id", Text(), "error_type", Choice(ErrorTypes), "violation_condition", Text(), "compliance_condition", Text(),
                "applicability_condition", Text(), "related_requirement_ids", Strings(), "source", Choice("explicit_instruction", "graph_dependency", "expert_prior", "inferred"),
                "source_reference", Text(), "requires_expert_review", Bool());
            if (global) { schema["properties"]["relevant_action_ids"] = Strings(); ((JArray)schema["required"]).Add("relevant_action_ids"); }
            return schema;
        }
        public static string GraphSchema => Obj("procedure_title", Text(), "instruction", Text(), "steps", List(Obj(
            "step_id", Text(), "step_name", Text(), "step_description", Text(), "dependencies", NoStepDependencies(), "actions", List(Obj(
                "action_id", Text(), "instruction", Text(), "original_text", Text(), "dependencies", Strings(),
                "conditional_dependencies", List(Obj("action_id", Text(), "condition", Text())), "applicability_condition", Text(),
                "actor", Strings(), "tools", Strings(), "materials", Strings(), "contact_media", Strings(), "targets", Strings(), "associated_constraints", Strings())))),
            "context_notes", Strings(), "review_notes", Strings()).ToString(Formatting.None);
        public static string EnrichmentSchema => Obj("action_updates", List(Obj("action_id", Text(), "manual_completion_required", Bool(), "preconditions", List(Requirement()),
            "postconditions", List(Requirement()), "error_criteria", List(Error(false)), "review_notes", Strings())),
            "procedure_error_criteria", List(Error(true)), "review_notes", Strings()).ToString(Formatting.None);

        public static string EnrichmentSchemaFor(string[] requestedIds, bool includeGlobal)
        {
            var schema = Object(EnrichmentSchema);
            var updates = schema["properties"]["action_updates"];
            updates["minItems"] = requestedIds.Length; updates["maxItems"] = requestedIds.Length;
            if (requestedIds.Length > 0) updates["items"]["properties"]["action_id"] = Choice(requestedIds);
            if (!includeGlobal) schema["properties"]["procedure_error_criteria"]["maxItems"] = 0;
            else schema["properties"]["procedure_error_criteria"]["items"]["properties"]["relevant_action_ids"]["minItems"] = 2;
            return schema.ToString(Formatting.None);
        }

        public static string ReplySchema(ActionVerificationTicket ticket) => Obj("request_id", Choice(ticket.RequestId), "step_id", Choice(ticket.StepId),
            "action_id", Choice(ticket.ActionId), "phase", Choice(ticket.Phase), "action_applicability", Choice("applies", "not_applicable", "unknown"),
            "results", List(Obj("requirement_id", ticket.Checks.Length == 0 ? Text() : Choice(ticket.Checks.Select(c => c.requirement_id).ToArray()),
                "status", Choice("satisfied", "not_satisfied", "unknown", "not_applicable"), "reason", Text()))).ToString(Formatting.None);
    }
}
