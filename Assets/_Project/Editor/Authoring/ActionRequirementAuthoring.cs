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
            cancellation.ThrowIfCancellationRequested();
            string json = await complete(RequirementsPrompts.DecomposeActions,
                ActionProcedureJson.Serialize(new { procedure_title = goal, original_instruction = instruction }), ActionProcedureJson.GraphSchema);
            cancellation.ThrowIfCancellationRequested();
            var graph = ActionProcedureJson.Parse(json, false);
            if (graph.instruction != instruction) throw new ArgumentException("The model changed the original instruction.");
            graph.task_goal = goal; return graph;
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
                    requested_action_ids = global ? Array.Empty<string>() : new[] { ids[i] }, include_procedure_error_criteria = global };
                string json = await complete(RequirementsPrompts.GenerateActionRequirements, ActionProcedureJson.Serialize(input), ActionProcedureJson.EnrichmentSchema);
                cancellation.ThrowIfCancellationRequested();
                var response = ActionProcedureJson.Object(json);
                if (!(response["action_updates"] is JArray updates) || !(response["procedure_error_criteria"] is JArray criteria)) throw new ArgumentException("Missing authoring update arrays.");
                if (global ? updates.Count != 0 : updates.Count != 1 || (string)updates[0]["action_id"] != ids[i] || criteria.Count != 0)
                    throw new ArgumentException("The model changed the requested Action scope.");
                foreach (var obj in response.Descendants().OfType<JObject>().Where(o => (string)o["source"] == "expert_prior"))
                    if (string.IsNullOrWhiteSpace(nursePrior) || string.IsNullOrWhiteSpace((string)obj["source_reference"]) || !nursePrior.Contains((string)obj["source_reference"]))
                        throw new ArgumentException("The model cited expert knowledge not present in the supplied nurse prior.");
                if (response.Descendants().OfType<JProperty>().Any(p => p.Name.StartsWith("evidence", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("The model added an observation specification to the no-specification format.");
                batches.Add(json);
            }
            return ActionProcedureJson.Merge(snapshot, batches);
        }
    }

}
