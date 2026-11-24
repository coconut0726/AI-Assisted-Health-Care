using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HomeCare.Procedures;
using HomeCare.Procedures.Actions;
using Newtonsoft.Json.Linq;

namespace HomeCare.Authoring
{
    public static class ActionRequirementAuthoring
    {
        public static async Task<ActionProcedure> Decompose(Func<string, string, string, Task<string>> complete, string instruction, string goal, CancellationToken cancellation)
        {
            var input = new JObject { ["procedure_title"] = goal, ["original_instruction"] = instruction };
            for (int attempt = 0; ; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                string json = await complete(RequirementsPrompts.DecomposeActions, input.ToString(), ActionProcedureJson.GraphSchema);
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var graph = ActionProcedureJson.Parse(json, false);
                    if (graph.instruction != instruction) throw new ArgumentException("The model changed the original instruction.");
                    if (graph.steps.Any(s => s.dependencies.Length != 0))
                        throw new ArgumentException("All Step dependencies must be []; keep only source-supported Action edges.");
                    graph.task_goal = goal; return graph;
                }
                catch (Exception error) when (attempt < 2 && (error is ArgumentException || error is Newtonsoft.Json.JsonException))
                {
                    input["previous_response"] = json;
                    input["review_feedback"] = new JArray("Correct the entire graph, preserving source content: " + error.Message);
                }
            }
        }

        public static async Task<ActionProcedure> Enrich(Func<string, string, string, Task<string>> complete, ActionProcedure graph, string nursePrior,
            Action<string> progress, CancellationToken cancellation)
        {
            var snapshot = ActionProcedureJson.Parse(ActionProcedureJson.Serialize(graph), false);
            var batches = new List<string>();
            var ids = snapshot.steps.SelectMany(s => s.actions).Select(a => a.action_id).ToArray();
            for (int i = 0; i <= ids.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                bool global = i == ids.Length;
                progress?.Invoke(global ? "Generating shared execution rules…" : $"Requirements for {ids[i]} ({i + 1}/{ids.Length})…");
                var input = new { original_instruction = snapshot.instruction, task_graph = ActionProcedureJson.TaskGraph(snapshot),
                    nurse_prior = string.IsNullOrWhiteSpace(nursePrior) ? null : nursePrior,
                    requested_action_ids = global ? Array.Empty<string>() : new[] { ids[i] }, include_procedure_error_criteria = global,
                    existing_action_checks = global ? batches.SelectMany(b => (JArray)ActionProcedureJson.Object(b)["action_updates"]).Select(a => new
                    {
                        action_id = (string)a["action_id"],
                        conditions = a["preconditions"].Concat(a["postconditions"]).Select(q => (string)q["condition"])
                            .Concat(a["error_criteria"].Select(e => (string)e["compliance_condition"])).ToArray()
                    }).ToArray() : null };
                var request = JObject.FromObject(input);
                for (int attempt = 0; ; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string json = await complete(RequirementsPrompts.GenerateActionRequirements, request.ToString(),
                        ActionProcedureJson.EnrichmentSchemaFor(input.requested_action_ids, global));
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var response = ActionProcedureJson.Object(json);
                        if (!(response["action_updates"] is JArray updates) || !(response["procedure_error_criteria"] is JArray criteria)) throw new ArgumentException("Missing authoring update arrays.");
                        if (global ? updates.Count != 0 : updates.Count != 1 || (string)updates[0]["action_id"] != ids[i] || criteria.Count != 0)
                            throw new ArgumentException("The model changed the requested Action scope.");
                        if (response.Descendants().OfType<JObject>().Any(o => (string)o["source"] == "author_edit"))
                            throw new ArgumentException("author_edit is reserved for actual edits in the authoring window, not model-generated claims.");
                        foreach (var obj in response.Descendants().OfType<JObject>().Where(o => (string)o["source"] == "expert_prior"))
                            if (string.IsNullOrWhiteSpace(nursePrior) || string.IsNullOrWhiteSpace((string)obj["source_reference"]) || !nursePrior.Contains((string)obj["source_reference"]))
                                throw new ArgumentException("The model cited expert knowledge not present in the supplied nurse prior.");
                        if (response.Descendants().OfType<JProperty>().Any(p => p.Name.StartsWith("evidence", StringComparison.OrdinalIgnoreCase)))
                            throw new ArgumentException("The model added an observation specification to the no-specification format.");
                        if (global) ActionProcedureJson.Merge(snapshot, batches.Concat(new[] { json }));
                        batches.Add(json); break;
                    }
                    catch (Exception error) when (attempt < 2 && (error is ArgumentException || error is Newtonsoft.Json.JsonException))
                    {
                        request["previous_response"] = json;
                        request["review_feedback"] = new JArray("Correct the actual JSON fields: " + error.Message);
                        progress?.Invoke("Correcting an invalid authoring response (attempt " + (attempt + 2) + "/3)…");
                    }
                }
            }
            return ActionProcedureJson.Merge(snapshot, batches);
        }
    }

}
