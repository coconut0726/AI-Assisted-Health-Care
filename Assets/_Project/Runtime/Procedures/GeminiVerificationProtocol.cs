using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HomeCare.Procedures
{
    // Same Interactions API shape as the existing Editor client. Contains no credentials.
    public static class GeminiVerificationProtocol
    {
        public static string CreateAfterImageRequest(ProcedureRequirements procedure, ProcedureStep step,
            byte[] jpeg, string filename, string model)
        {
            if (jpeg == null || jpeg.Length == 0) throw new ArgumentException("The camera image is empty.");
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A Gemini model is required.");
            var content = new { procedure_title = procedure.procedure_title,
                original_instruction = procedure.instruction, selected_step = step };
            return new JObject
            {
                ["model"] = model,
                ["store"] = false,
                ["system_instruction"] = RequirementsPrompts.Verify,
                ["input"] = new JArray(
                    new JObject { ["type"] = "text", ["text"] = RequirementsJson.Serialize(content) },
                    new JObject { ["type"] = "text", ["text"] = "Evidence scope: after_step; filename: " + filename },
                    new JObject { ["type"] = "image", ["mime_type"] = "image/jpeg", ["data"] = Convert.ToBase64String(jpeg) }),
                ["response_format"] = new JObject { ["type"] = "text", ["mime_type"] = "application/json",
                    ["schema"] = JObject.Parse(RequirementsJson.VerificationSchema) }
            }.ToString(Formatting.None);
        }

        public static VerificationResponse ParseInteraction(string json) =>
            RequirementsJson.ParseResponse(ExtractModelText(json));

        // Returns the final text output of a completed Interactions API response.
        public static string ExtractModelText(string json)
        {
            var interaction = JObject.Parse(json);
            if ((string)interaction["status"] != "completed")
                throw new ArgumentException("The model interaction did not complete.");
            var steps = interaction["steps"] as JArray;
            if (steps != null)
                for (int i = steps.Count - 1; i >= 0; i--)
                {
                    if ((string)steps[i]["type"] != "model_output") continue;
                    string output = "";
                    if (steps[i]["content"] is JArray parts)
                        foreach (var part in parts)
                            if ((string)part["type"] == "text") output += (string)part["text"];
                    if (!string.IsNullOrWhiteSpace(output)) return output;
                }
            throw new ArgumentException("The model returned no output.");
        }
    }
}
