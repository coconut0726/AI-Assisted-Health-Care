using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HomeCare.Procedures;
using Newtonsoft.Json;
using UnityEngine;

namespace HomeCare.Guidance
{
    [Serializable]
    public class SessionReport
    {
        public string report_version = "1";
        public string session_id;
        public string procedure_title;
        public string device;
        public string verification_model;
        public string started_at_utc;
        public string finished_at_utc;
        public bool completed;
        public string nurse_instruction;
        public List<StepReport> steps = new();
    }

    [Serializable]
    public class StepReport
    {
        public string step_id;
        public string title;
        public string instruction;
        public string started_at_utc;
        public string passed_at_utc;
        public string final_status = "not_started";
        public List<ItemPresence> mentioned_items = new();
        public List<AttemptReport> attempts = new();
        public List<GuidanceEvent> guidance = new();
        public List<DetectionEvent> detections = new();
    }

    [Serializable]
    public class ItemPresence
    {
        public string item_id;
        public string name;
        public bool found_in_scene;
    }

    [Serializable]
    public class AttemptReport
    {
        public string at_utc;
        public string image_file;
        public string overall_status;
        public string error;
        public List<RequirementReport> results = new();
    }

    [Serializable]
    public class RequirementReport
    {
        public string requirement_id;
        public string description;
        public string evidence_scope;
        public string status;
        public string evidence;
        public string reason;
    }

    [Serializable]
    public class GuidanceEvent
    {
        public string at_utc;
        public string held_item_id;
        public string held_item_name;
        public string source;
        public bool item_used_in_step;
        public string guidance_text;
        public string caution;
        public string target_item_id;
        public string visual_cue;
    }

    /// <summary>One "find this object" request: what was asked, what the model found and where it was placed.</summary>
    [Serializable]
    public class DetectionEvent
    {
        public string at_utc;
        public string request;
        public string source;
        public string image_file;
        public string frame_source;
        public int depth_samples;
        public string visual_type;
        public string guidance_text;
        public string caution;
        public string error;
        public List<DetectionObjectReport> objects = new();
    }

    [Serializable]
    public class DetectionObjectReport
    {
        public string query;
        public string label;
        public string role;
        public bool found;
        /// <summary>[y_min, x_min, y_max, x_max], 0-1000.</summary>
        public int[] box_2d;
        /// <summary>depth | live_depth | default_distance, empty when not found.</summary>
        public string depth_source;
        /// <summary>World position and size (metres) of the placed 3D box.</summary>
        public float[] world_center;
        public float[] world_size;
    }

    /// <summary>
    /// Keeps one report per care session and rewrites it after every change, so a report exists
    /// even if the app is closed mid-procedure. Files: session_report.json and session_report.md,
    /// next to the captured images in persistentDataPath/HomeCareSessions/&lt;session&gt;/.
    /// </summary>
    public sealed class SessionReportWriter
    {
        public SessionReport Report { get; }
        public string Directory { get; }
        public string JsonPath => Path.Combine(Directory, "session_report.json");
        public string MarkdownPath => Path.Combine(Directory, "session_report.md");

        public SessionReportWriter(string directory, ProcedureRequirements procedure, string model)
        {
            Directory = directory;
            Report = new SessionReport
            {
                session_id = Path.GetFileName(directory),
                procedure_title = procedure.procedure_title,
                nurse_instruction = procedure.instruction,
                device = Application.isEditor ? "Unity Editor" : SystemInfo.deviceModel,
                verification_model = model,
                started_at_utc = Now(),
            };
            foreach (var step in procedure.steps)
                Report.steps.Add(new StepReport { step_id = step.id, title = step.title, instruction = step.instruction });
        }

        public static string Now() => DateTime.UtcNow.ToString("o");

        public StepReport Step(string id) => Report.steps.Find(s => s.step_id == id);

        public void Save()
        {
            try
            {
                File.WriteAllText(JsonPath, JsonConvert.SerializeObject(Report, Formatting.Indented));
                File.WriteAllText(MarkdownPath, ToMarkdown(Report));
            }
            catch (IOException e) { Debug.LogWarning("[HomeCare] Could not save the session report: " + e.Message); }
        }

        static string Label(string status) => status switch
        {
            RequirementVerification.Satisfied => "PASS",
            RequirementVerification.NotSatisfied => "NOT MET",
            RequirementVerification.Unknown => "NOT VERIFIED",
            _ => status,
        };

        static string Cell(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        public static string ToMarkdown(SessionReport r)
        {
            var md = new StringBuilder();
            md.AppendLine("# Home care session report");
            md.AppendLine();
            md.AppendLine($"- **Procedure:** {r.procedure_title}");
            md.AppendLine($"- **Session:** {r.session_id}");
            md.AppendLine($"- **Device:** {r.device}");
            md.AppendLine($"- **Verification model:** {r.verification_model}");
            md.AppendLine($"- **Started (UTC):** {r.started_at_utc}");
            md.AppendLine($"- **Finished (UTC):** {(string.IsNullOrEmpty(r.finished_at_utc) ? "—" : r.finished_at_utc)}");
            int skipped = r.steps.Count(s => s.final_status == "skipped");
            md.AppendLine($"- **Completed:** {(!r.completed ? "no" : skipped == 0 ? "yes, all steps passed" : $"yes, {skipped} step(s) skipped without a passed check")}");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine();
            md.AppendLine("| # | Step | Status | Attempts | Guidance shown |");
            md.AppendLine("|---|------|--------|----------|----------------|");
            for (int i = 0; i < r.steps.Count; i++)
            {
                var s = r.steps[i];
                md.AppendLine($"| {i + 1} | {Cell(s.title)} | {Cell(s.final_status)} | {s.attempts.Count} | {s.guidance.Count} |");
            }
            md.AppendLine();
            for (int i = 0; i < r.steps.Count; i++)
            {
                var s = r.steps[i];
                md.AppendLine($"## Step {i + 1}: {s.title}");
                md.AppendLine();
                md.AppendLine($"> {s.instruction}");
                md.AppendLine();
                md.AppendLine($"Status: **{s.final_status}**" + (string.IsNullOrEmpty(s.passed_at_utc) ? "" : $" (passed {s.passed_at_utc})"));
                md.AppendLine();
                if (s.mentioned_items.Count > 0)
                {
                    md.Append("Items mentioned: ");
                    var parts = new List<string>();
                    foreach (var item in s.mentioned_items) parts.Add(item.name + (item.found_in_scene ? " (found)" : " (not found in scene)"));
                    md.AppendLine(string.Join(", ", parts));
                    md.AppendLine();
                }
                for (int a = 0; a < s.attempts.Count; a++)
                {
                    var at = s.attempts[a];
                    md.AppendLine($"**Attempt {a + 1}** — {at.at_utc} — {Label(at.overall_status)}" +
                                  (string.IsNullOrEmpty(at.image_file) ? "" : $" — image `{at.image_file}`"));
                    if (!string.IsNullOrEmpty(at.error)) md.AppendLine($"  - Error: {at.error}");
                    foreach (var res in at.results)
                        md.AppendLine($"  - {Label(res.status)}: {res.description} — {Cell(res.reason)}");
                    md.AppendLine();
                }
                foreach (var g in s.guidance)
                {
                    md.AppendLine($"- Guidance ({g.source}, holding {g.held_item_name}, {g.at_utc}): {g.guidance_text}" +
                                  (string.IsNullOrEmpty(g.caution) ? "" : $" Caution: {g.caution}"));
                }
                if (s.guidance.Count > 0) md.AppendLine();
                foreach (var d in s.detections)
                {
                    var found = new List<string>();
                    foreach (var o in d.objects) found.Add(o.label + (o.found ? $" ({o.role}, {o.depth_source})" : " (not seen)"));
                    md.AppendLine($"- Find \"{Cell(d.request)}\" ({d.source}, {d.at_utc}" +
                                  (string.IsNullOrEmpty(d.image_file) ? "" : $", image `{d.image_file}`") + "): " +
                                  (string.IsNullOrEmpty(d.error) ? string.Join(", ", found) + " — " + Cell(d.guidance_text) : "Error: " + Cell(d.error)));
                }
                if (s.detections.Count > 0) md.AppendLine();
            }
            md.AppendLine("---");
            md.AppendLine("Verification results come from a vision-language model and are a prototype aid, not a clinical assessment. " +
                          "NOT VERIFIED means the evidence was insufficient, not that the step was done wrong.");
            return md.ToString();
        }
    }
}
