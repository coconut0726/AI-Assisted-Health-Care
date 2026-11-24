using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HomeCare.Procedures;
using HomeCare.Procedures.Actions;
using Newtonsoft.Json.Linq;

namespace HomeCare.Authoring
{
    // Invoked by the existing command-line checks, without adding another Tools menu.
    public static class ActionFlowChecks
    {
        public static int Run(string directory)
        {
            int count = 0;
            Action<bool, string> check = (value, name) => { if (!value) throw new Exception("Action flow: " + name); count++; };
            Action<System.Action, string> rejects = (body, name) => { bool failed = false; try { body(); } catch (ArgumentException) { failed = true; } check(failed, name); };
            Func<ActionProcedure> example = () => ActionProcedureJson.Parse(File.ReadAllText(Path.Combine(directory, "tabletop_action_requirements.json")));
            foreach (var name in new[] { "tabletop", "burn", "wound" })
            {
                var p = ActionProcedureJson.Parse(File.ReadAllText(Path.Combine(directory, name + "_action_requirements.json")));
                check(p.steps.SelectMany(s => s.actions).All(a => a.postconditions.Length > 0 || a.manual_completion_required), name + " explicit image or manual completions");
                check(!JObject.FromObject(p).Descendants().OfType<JProperty>().Any(v => v.Name.StartsWith("evidence")), name + " no observation specification");
            }
            foreach (var name in new[] { "burn", "wound" })
                foreach (bool applies in new[] { false, true })
                {
                    var walkthrough = new ActionRequirementSession(ActionProcedureJson.Parse(File.ReadAllText(Path.Combine(directory, name + "_action_requirements.json"))));
                    for (int guard = 0; guard < 300 && !walkthrough.Completed; guard++)
                    {
                        if (walkthrough.Phase == ActionCheckPhase.Applicability) walkthrough.ConfirmApplicability(applies);
                        foreach (var q in walkthrough.CurrentChecks.ToArray()) walkthrough.Confirm(q.requirement_id);
                        if (walkthrough.Phase == ActionCheckPhase.Completion && walkthrough.Applicability != ActionRequirementSession.NotApplicable &&
                            walkthrough.CurrentAction.manual_completion_required && !walkthrough.ActionCompletionConfirmed)
                            walkthrough.ConfirmActionCompletion();
                        if (walkthrough.CanAdvance) walkthrough.Advance();
                    }
                    check(walkthrough.Completed && walkthrough.History.Count(h => h.event_type == "action_finished") == walkthrough.ActionCount,
                        name + " operator-confirmation walkthrough completes, conditional applicability=" + applies);
                }
            var duplicate = example(); duplicate.steps[1].actions[0].action_id = "A1";

            var editable = example();
            var editAction = editable.steps.SelectMany(s => s.actions).First(a => a.action_id == "A2");
            var added = ActionRequirementEditing.AddCondition(editable, editAction, true);
            added.condition = "The author-specified work surface is visible.";
            check(added.source == ActionRequirementEditing.AuthorSource && !string.IsNullOrEmpty(added.source_reference), "form additions have honest author provenance without fabricated source quotes");
            var editedExisting = editAction.postconditions[0]; string originalQuote = editedExisting.source_reference;
            editedExisting.condition = "The red block rests in the author-specified central region.";
            ActionRequirementEditing.MarkEdited(editedExisting, editAction.action_id);
            check(editedExisting.source == ActionRequirementEditing.AuthorSource && editedExisting.source_reference.Contains(originalQuote), "editing a generated condition retains its earlier source as history");
            string once = editedExisting.source_reference; ActionRequirementEditing.MarkEdited(editedExisting, editAction.action_id);
            check(once == editedExisting.source_reference, "repeated typing does not grow provenance endlessly");
            var editableSaved = ActionRequirementEditing.ReadyToSave(editable);
            check(ActionProcedureJson.Parse(ActionProcedureJson.Serialize(editableSaved)).steps.Length == editable.steps.Length, "form edits outside exact source wording remain loadable as author edits");
            var authorSession = new ActionRequirementSession(editableSaved);
            check(!authorSession.CanAdvance && !authorSession.History.Any(h => h.source == "human_confirmation"), "authoring a condition is never mistaken for confirming execution");
            check(!ActionProcedureJson.EnrichmentSchema.Contains(ActionRequirementEditing.AuthorSource), "model schema cannot claim author-edit provenance");

            var references = example();
            var referencedAction = references.steps.SelectMany(s => s.actions).First(a => a.action_id == "A2");
            var ruleOwner = references.steps.SelectMany(s => s.actions).First(a => a.action_id == "A3");
            var removed = referencedAction.postconditions[0];
            ruleOwner.error_criteria[0].related_requirement_ids = new[] { removed.requirement_id };
            var sharedEdit = ActionRequirementEditing.AddError(references, ruleOwner, "spatial_error", true);
            sharedEdit.violation_condition = "The author-specified spare object is inside the tray.";
            sharedEdit.compliance_condition = "The author-specified spare object is outside the tray.";
            sharedEdit.related_requirement_ids = new[] { removed.requirement_id };
            ActionRequirementEditing.RemoveCondition(references, referencedAction, removed, false);
            check(referencedAction.manual_completion_required, "removing the last image postcondition automatically enables manual completion");
            check(ruleOwner.error_criteria[0].related_requirement_ids.Length == 0 && sharedEdit.related_requirement_ids.Length == 0, "condition deletion repairs local and shared references");
            var newPost = ActionRequirementEditing.AddCondition(references, referencedAction, false); newPost.condition = "The author-specified result is visible.";
            check(!referencedAction.manual_completion_required, "adding an image postcondition automatically disables manual-only mode");
            ActionRequirementEditing.ReadyToSave(references);
            check(ActionRequirementEditing.Issues(references).Length == 0, "reference cleanup and completion mode produce a valid runtime file");
            var typesEdit = example(); var typesOwner = typesEdit.steps[0].actions[0];
            foreach (var type in ActionProcedureJson.SupportedErrorTypes)
            {
                var error = ActionRequirementEditing.AddError(typesEdit, typesOwner, type);
                error.violation_condition = "Author-defined wrong state for " + type;
                error.compliance_condition = "Author-defined correct visible state for " + type;
            }
            var typeSnapshot = ActionRequirementEditing.ReadyToSave(typesEdit);
            check(typeSnapshot.steps[0].actions[0].error_criteria.Select(e => e.error_type).Distinct().Count() == 5, "form supports all five error types and valid serialization");
            var emptyRow = ActionRequirementEditing.AddCondition(typesEdit, typesOwner, false);
            check(ActionRequirementEditing.Issues(typesEdit).Any(message => message.Contains("empty condition")), "unfinished rows get a plain-language prompt before save");
            ActionRequirementEditing.RemoveCondition(typesEdit, typesOwner, emptyRow, false);
            sharedEdit.relevant_action_ids = Array.Empty<string>();
            check(ActionRequirementEditing.Issues(references).Any(message => message.Contains("select at least one Action")), "missing shared scope gets an actionable field hint");
            rejects(() => ActionProcedureJson.Validate(duplicate), "duplicate Action IDs");
            var cycle = example(); cycle.steps[0].actions[0].dependencies = new[] { "A3" };
            rejects(() => ActionProcedureJson.Validate(cycle), "combined dependency cycle");
            var unknown = example(); unknown.steps[0].actions[0].dependencies = new[] { "missing" };
            rejects(() => ActionProcedureJson.Validate(unknown), "dangling dependency");
            var badQuote = example(); badQuote.steps[0].actions[0].postconditions[0].source_reference = "unsupported invented phrase";
            rejects(() => ActionProcedureJson.Validate(badQuote), "source quotation");
            var observed = JObject.FromObject(example()); observed["evidence_specification"] = new JObject();
            rejects(() => ActionProcedureJson.Parse(observed.ToString()), "removed field rejected");
            rejects(() => ActionProcedureJson.Parse(File.ReadAllText(Path.Combine(directory, "wound_dressing_procedure.json"))), "legacy JSON not silently reinterpreted");
            rejects(() => RequirementsJson.Parse(ActionProcedureJson.Serialize(example())), "legacy parser remains independent");

            var emptyRubric = example(); emptyRubric.steps[0].actions[0].postconditions = Array.Empty<ActionRequirement>();
            rejects(() => ActionProcedureJson.Validate(emptyRubric), "empty rubric never silently becomes complete");
            emptyRubric.steps[0].actions[0].manual_completion_required = true;
            ActionProcedureJson.Validate(emptyRubric);
            var manual = new ActionRequirementSession(emptyRubric);
            bool prematureManual = false; try { manual.ConfirmActionCompletion(); } catch (InvalidOperationException) { prematureManual = true; }
            check(prematureManual, "manual completion cannot bypass preconditions");
            Pass(manual);
            check(manual.Phase == ActionCheckPhase.Completion && manual.CurrentChecks.Length == 0 && !manual.CanAdvance, "manual action waits after preconditions");
            bool noEmptyRequest = false; try { manual.BeginVerification(); } catch (InvalidOperationException) { noEmptyRequest = true; }
            check(noEmptyRequest, "no empty VLM request for manual-only completion");
            manual.ConfirmActionCompletion();
            check(manual.CanAdvance && manual.ActionCompletionConfirmed, "explicit manual completion unlocks Next");
            check(manual.History.Any(h => h.event_type == "action_completion_confirmed" && h.source == "human_confirmation"), "manual completion never labeled VLM");
            manual.RefreshAfterPause(); check(!manual.CanAdvance, "pause refreshes pending operator completion");
            manual.ConfirmActionCompletion(); manual.RetryCurrentAction();
            check(!manual.CanAdvance && !manual.ActionCompletionConfirmed, "retry invalidates operator completion");
            Pass(manual); manual.ConfirmActionCompletion(); manual.Advance();
            check(manual.CurrentAction.action_id == "A2" && manual.DependenciesMet(), "manual completion resolves actual dependency history");
            manual.Restart(); check(!manual.ActionCompletionConfirmed && !manual.CanAdvance, "manual completion does not leak to new run");
            var contradictory = example(); contradictory.steps[0].actions[0].manual_completion_required = true;
            rejects(() => ActionProcedureJson.Validate(contradictory), "manual-only flag cannot hide existing image postconditions");

            var source = example(); var session = new ActionRequirementSession(source);
            source.steps[0].actions[0].preconditions[0].condition = "mutated outside session";
            check(session.CurrentChecks[0].condition != "mutated outside session", "snapshot isolation");
            check(session.Phase == ActionCheckPhase.Preconditions && !session.CanAdvance, "starts before action");
            var pre = session.BeginVerification(); check(session.Busy, "one request in flight");
            bool busyRejected = false; try { session.BeginVerification(); } catch (InvalidOperationException) { busyRejected = true; }
            check(busyRejected, "overlapping request rejected");
            check(session.Accept(pre, Reply(pre, ActionRequirementSession.Yes)), "matching response accepted");
            check(session.Phase == ActionCheckPhase.Completion && !session.CanAdvance, "pre success requires fresh completion check");
            check(session.CurrentChecks.All(q => q.category != "precondition"), "active phase selection");
            var completion = session.BeginVerification(); session.Accept(completion, Reply(completion, ActionRequirementSession.Unknown));
            check(!session.CanAdvance, "unknown cannot advance");
            completion = session.BeginVerification(); session.Accept(completion, Reply(completion, ActionRequirementSession.Yes));
            check(session.CanAdvance, "completion confirms action");
            session.RefreshAfterPause();
            check(!session.CanAdvance && session.Phase == ActionCheckPhase.Completion, "resume refreshes current phase without rewinding preconditions");
            Pass(session);
            completion = session.BeginVerification(); session.Fail(completion, "network failure");
            check(!session.CanAdvance, "failed fresh request clears old model pass");
            Pass(session); var refresh = session.BeginVerification();
            check(session.CanAdvance, "background refresh cannot starve manual Next");
            session.Advance(); check(session.CurrentAction.action_id == "A2", "next action in same procedure");
            check(!session.Accept(refresh, Reply(refresh, ActionRequirementSession.No)), "cancelled refresh cannot overwrite next action");
            check(session.History.Any(h => h.event_type == "action_finished" && h.action_id == "A1"), "confirmed progression recorded");
            var stale = session.BeginVerification(); session.RetryCurrentAction();
            check(!session.Accept(stale, Reply(stale, ActionRequirementSession.Yes)), "old response ignored after retry");
            var ticket = session.BeginVerification(); var wrong = Reply(ticket, ActionRequirementSession.Yes); wrong.action_id = "wrong";
            rejects(() => session.Accept(ticket, wrong), "wrong action response rejected"); session.Fail(ticket, "bad response");
            ticket = session.BeginVerification(); var doubled = Reply(ticket, ActionRequirementSession.Yes); doubled.results = new[] { doubled.results[0], doubled.results[0] };
            rejects(() => session.Accept(ticket, doubled), "duplicate result rejected"); session.Fail(ticket, "duplicate");
            ticket = session.BeginVerification(); var omitted = Reply(ticket, ActionRequirementSession.Yes); omitted.results = Array.Empty<ActionCheckResult>(); session.Accept(ticket, omitted);
            check(!session.CanAdvance && session.Result("A2_POST1").status == ActionRequirementSession.Unknown, "omitted result becomes unknown");
            Pass(session); session.Advance();
            check(session.CurrentChecks.Any(q => q.category == "error_compliance"), "execution rules checked with completion");
            ticket = session.BeginVerification(); var fail = Reply(ticket, ActionRequirementSession.Yes); fail.results.Last().status = ActionRequirementSession.No; session.Accept(ticket, fail);
            check(session.HasLatchedViolation && !session.CanAdvance, "rule violation blocks completion");
            Pass(session); check(!session.CanAdvance, "later clean frame cannot erase violation");
            session.RetryCurrentAction(); Pass(session); check(session.CanAdvance, "explicit local retry permits a new attempt");
            session.Advance(); check(session.Completed, "procedure completes only after all actions");

            var review = example(); review.steps[0].actions[0].preconditions[0].requires_expert_review = true;
            var reviewSession = new ActionRequirementSession(review); Pass(reviewSession);
            check(reviewSession.Phase == ActionCheckPhase.Preconditions, "author review flag cannot be self-approved by model");
            reviewSession.Confirm("A1_PRE1"); check(reviewSession.Phase == ActionCheckPhase.Completion, "explicit human confirmation tracked separately");
            check(reviewSession.History.Any(h => h.source == "human_confirmation"), "human provenance");
            string oldRun = reviewSession.RunId; reviewSession.Restart();
            var requestTicket = reviewSession.BeginVerification();
            var request = JObject.Parse(ActionVerificationProtocol.CreateRequest(reviewSession, requestTicket, new byte[] { 1, 2, 3 }, "2026-11-23T00:00:00Z"));
            var context = JObject.Parse((string)request["input"][0]["text"]);
            check(context["task_goal"] != null && context["current_requirements"] != null && context["dependency_history"] != null, "request includes goal/current checks/dependencies");
            check(!context["confirmed_step_history"].Any(h => (string)h["run_id"] == oldRun), "previous run facts not sent as current history");
            check((string)request["input"][1]["type"] == "image", "current frame attached");
            check(!context.Descendants().OfType<JProperty>().Any(p => p.Name.StartsWith("evidence")), "no removed fields in request context");
            reviewSession.Fail(requestTicket, "end test");

            var conditional = example(); conditional.steps[0].actions[0].applicability_condition = "The test condition holds.";
            var c = new ActionRequirementSession(conditional);
            check(c.Phase == ActionCheckPhase.Applicability, "conditional applicability checked first");
            ticket = c.BeginVerification(); var unresolved = Reply(ticket, ActionRequirementSession.Unknown); unresolved.action_applicability = ActionRequirementSession.Unknown; c.Accept(ticket, unresolved);
            check(!c.CanAdvance && c.Phase == ActionCheckPhase.Applicability, "unknown condition never skips");
            c.ConfirmApplicability(false); check(c.CanAdvance, "explicit inapplicable branch can advance");
            c.Advance(); check(c.DependenciesMet(), "inapplicable predecessor does not block dependent");

            var contingency = example();
            var notification = contingency.steps[1].actions[1];
            notification.applicability_condition = "The supplied conditional trigger holds.";
            notification.dependencies = Array.Empty<string>(); notification.error_criteria = Array.Empty<ActionErrorCriterion>();
            var early = new ActionRequirementSession(contingency);
            early.SelectConditionalAction("A3"); early.ConfirmApplicability(true); Pass(early); early.Advance();
            check(!early.Completed && early.CurrentAction.action_id == "A1", "early conditional completion returns to pending work");
            check(!early.Accept(stale, Reply(stale, ActionRequirementSession.Yes)), "selection invalidates old action ticket");
            var recheck = new ActionRequirementSession(contingency);
            recheck.SelectConditionalAction("A3"); recheck.ConfirmApplicability(false); recheck.Advance();
            recheck.SelectConditionalAction("A3");
            check(recheck.Phase == ActionCheckPhase.Applicability, "conditional false can be explicitly reassessed later");

            var global = example(); var rule = global.steps[1].actions[1].error_criteria[0];
            global.steps[1].actions[1].error_criteria = Array.Empty<ActionErrorCriterion>();
            rule.criterion_id = "P_KEEP"; rule.relevant_action_ids = new[] { "A3" }; global.procedure_error_criteria = new[] { rule };
            var g = new ActionRequirementSession(global); Pass(g); Pass(g); g.Advance(); Pass(g); g.Advance();
            ticket = g.BeginVerification(); fail = Reply(ticket, ActionRequirementSession.Yes); fail.results.Last().status = ActionRequirementSession.No; g.Accept(ticket, fail);
            g.RetryCurrentAction(); Pass(g); check(!g.CanAdvance, "global rule failure survives local retry");
            g.Restart(); check(!g.HasLatchedViolation, "new run explicitly resets global latch");

            var authored = example(); int calls = 0;
            var oneActionSchema = JObject.Parse(ActionProcedureJson.EnrichmentSchemaFor(new[] { "A1" }, false));
            check((int)oneActionSchema["properties"]["action_updates"]["maxItems"] == 1 &&
                (string)oneActionSchema["properties"]["action_updates"]["items"]["properties"]["action_id"]["enum"][0] == "A1", "generation schema pins the selected Action");
            var sharedSchema = JObject.Parse(ActionProcedureJson.EnrichmentSchemaFor(Array.Empty<string>(), true));
            check((int)sharedSchema["properties"]["action_updates"]["maxItems"] == 0, "shared-rule schema cannot rewrite Actions");
            Func<string, string, string, Task<string>> client = (prompt, inputJson, schema) =>
            {
                calls++; var input = JObject.Parse(inputJson);
                if (prompt == RequirementsPrompts.DecomposeActions)
                    return Task.FromResult(ActionProcedureJson.TaskGraph(authored).ToString());
                check(prompt == RequirementsPrompts.GenerateActionRequirements, "uses second-stage prompt");
                check(input["task_graph"]["steps"][0]["actions"][0]["preconditions"] == null, "generation context excludes the previous rubric");
                var ids = (JArray)input["requested_action_ids"];
                var updates = new JArray();
                if (ids.Count != 0)
                {
                    var a = authored.steps.SelectMany(s => s.actions).First(x => x.action_id == (string)ids[0]);
                    updates.Add(JObject.FromObject(new { action_id = a.action_id, manual_completion_required = a.manual_completion_required, preconditions = a.preconditions,
                        postconditions = a.postconditions, error_criteria = a.error_criteria, review_notes = new[] { "Image scope reviewed." } }));
                }
                return Task.FromResult(new JObject { ["action_updates"] = updates, ["procedure_error_criteria"] = new JArray(), ["review_notes"] = new JArray() }.ToString());
            };
            var graph = ActionRequirementAuthoring.Decompose(client, authored.instruction, authored.task_goal, CancellationToken.None).GetAwaiter().GetResult();
            check(graph.steps.SelectMany(s => s.actions).All(a => a.postconditions.Length == 0), "stage one does not generate requirements");
            var generated = ActionRequirementAuthoring.Enrich(client, graph, "", null, CancellationToken.None).GetAwaiter().GetResult();
            check(calls == 5 && generated.steps.Sum(s => s.actions.Length) == 3, "two logical stages with bounded Action batches");
            check(graph.steps[0].actions[0].postconditions.Length == 0, "merging does not edit the reviewed graph");
            var repeatedLocalIds = authored.steps.SelectMany(s => s.actions).Select(a =>
            {
                var update = JObject.FromObject(new { action_id = a.action_id, manual_completion_required = a.manual_completion_required,
                    preconditions = a.preconditions, postconditions = a.postconditions, error_criteria = a.error_criteria, review_notes = Array.Empty<string>() });
                if (a.action_id == "A2" || a.action_id == "A3") update["postconditions"][0]["requirement_id"] = "R1";
                if (a.action_id == "A3") update["error_criteria"][0]["related_requirement_ids"] = new JArray("R1");
                return new JObject { ["action_updates"] = new JArray(update), ["procedure_error_criteria"] = new JArray(), ["review_notes"] = new JArray() }.ToString();
            }).ToArray();
            var namespaced = ActionProcedureJson.Merge(authored, repeatedLocalIds);
            var secondAction = namespaced.steps.SelectMany(s => s.actions).First(a => a.action_id == "A2");
            var thirdAction = namespaced.steps.SelectMany(s => s.actions).First(a => a.action_id == "A3");
            check(secondAction.postconditions[0].requirement_id == "A2_R1" &&
                thirdAction.postconditions[0].requirement_id == "A3_R1", "per-Action generation cannot collide on model-local R1");
            check(thirdAction.error_criteria[0].related_requirement_ids.Single() == "A3_R1", "local rule references follow ID normalization");
            var ambiguousRule = JObject.FromObject(authored.steps.SelectMany(s => s.actions).First(a => a.action_id == "A3").error_criteria[0]);
            ambiguousRule["criterion_id"] = "P_AMBIGUOUS"; ambiguousRule["relevant_action_ids"] = new JArray("A2", "A3");
            ambiguousRule["related_requirement_ids"] = new JArray("R1");
            var ambiguousBatch = new JObject { ["action_updates"] = new JArray(), ["procedure_error_criteria"] = new JArray(ambiguousRule), ["review_notes"] = new JArray() };
            rejects(() => ActionProcedureJson.Merge(authored, repeatedLocalIds.Concat(new[] { ambiguousBatch.ToString() })), "ambiguous cross-Action reference is rejected rather than guessed");
            check(generated.review_notes.Contains("A1: Image scope reviewed."), "Action exclusion/review notes survive merging");
            int repairs = 0;
            Func<string, string, string, Task<string>> repairable = (prompt, inputJson, schema) =>
            {
                repairs++;
                var candidate = ActionProcedureJson.TaskGraph(authored);
                if (repairs == 1) candidate["steps"][0]["actions"][0]["original_text"] = "invented source quotation";
                else
                {
                    var repairInput = JObject.Parse(inputJson);
                    check(repairInput["previous_response"] != null && repairInput["review_feedback"].ToString().Contains("source quotation"), "invalid graph is retried with concrete validation feedback");
                }
                return Task.FromResult(candidate.ToString());
            };
            var repaired = ActionRequirementAuthoring.Decompose(repairable, authored.instruction, authored.task_goal, CancellationToken.None).GetAwaiter().GetResult();
            check(repairs == 2 && repaired.instruction == authored.instruction, "bounded graph repair preserves original source");
            rejects(() => ActionRequirementAuthoring.Decompose(client, "changed input", "goal", CancellationToken.None).GetAwaiter().GetResult(), "changed source rejected at authoring boundary");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool stopped = false; int beforeCancel = calls;
            try { ActionRequirementAuthoring.Enrich(client, graph, "", null, cancelled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { stopped = true; }
            check(stopped && calls == beforeCancel, "cancelled authoring never starts another request");
            Func<string, string, string, Task<string>> inventedExpert = (prompt, input, schema) => Task.FromResult(
                "{\"action_updates\":[{\"action_id\":\"A1\",\"preconditions\":[{\"source\":\"expert_prior\",\"source_reference\":\"general medical practice\"}],\"postconditions\":[],\"error_criteria\":[]}],\"procedure_error_criteria\":[],\"review_notes\":[]}");
            rejects(() => ActionRequirementAuthoring.Enrich(inventedExpert, graph, "", null, CancellationToken.None).GetAwaiter().GetResult(), "empty nurse prior cannot become medical authority");
            Func<string, string, string, Task<string>> falseAuthor = (prompt, input, schema) => Task.FromResult(
                "{\"action_updates\":[{\"action_id\":\"A1\",\"manual_completion_required\":false,\"preconditions\":[],\"postconditions\":[{\"source\":\"author_edit\",\"source_reference\":\"model pretended an author edited this\"}],\"error_criteria\":[]}],\"procedure_error_criteria\":[],\"review_notes\":[]}");
            bool authorRejected = false;
            try { ActionRequirementAuthoring.Enrich(falseAuthor, graph, "", null, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (ArgumentException error) { authorRejected = error.Message.Contains("reserved for actual edits"); }
            check(authorRejected, "model cannot use the manual-edit provenance exception");
            return count;
        }

        static ActionVerificationReply Reply(ActionVerificationTicket t, string status) => new()
        { request_id = t.RequestId, step_id = t.StepId, action_id = t.ActionId, phase = t.Phase, action_applicability = "applies",
            results = t.Checks.Select(q => new ActionCheckResult { requirement_id = q.requirement_id, status = status, reason = "Test observation for " + q.requirement_id }).ToArray() };
        static void Pass(ActionRequirementSession s) { var t = s.BeginVerification(); s.Accept(t, Reply(t, ActionRequirementSession.Yes)); }
    }
}
