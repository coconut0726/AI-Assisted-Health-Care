using System;
using System.Collections.Generic;
using System.IO;
using HomeCare.Procedures;
using UnityEditor;
using UnityEngine;

namespace HomeCare.Authoring
{
    public class RequirementsPrototypeWindow : EditorWindow
    {
        enum Provider { Gemini, Claude, LocalOllama, ProjectBackend }
        const string ExamplePath = "Assets/_Project/Content/Procedures/requirements_example.json";
        Provider _provider = Provider.ProjectBackend;
        bool _followVerificationBackend = true;
        bool _localVerification = true;
        string _geminiModel = "gemini-3.8-flash";
        string _claudeModel = "claude-sonnet-5-5";
        string _instruction = "Place the blue pad on the tray. Put the red block on the blue pad. Keep the block on the pad while wrapping the yellow strap around it.";
        string _json = "";
        string _before = "";
        string _during = "";
        string _after = "";
        string _error = "";
        ProcedureRequirements _procedure;
        VerificationReport _report;
        bool _busy;
        bool _jsonDirty;
        int _tab;
        int _stepIndex;
        Vector2 _scroll;

        [MenuItem("Tools/Home Care/Requirements Prototype")]
        public static void Open() => GetWindow<RequirementsPrototypeWindow>("Requirements Prototype");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Instruction → Requirements → VQA", EditorStyles.boldLabel);
            if (GUILayout.Button("VLM Backend…")) VlmBackendWindow.Open();
            EditorGUILayout.HelpBox("Editor research prototype. Manually select a step and attach execution evidence. Generated requirements are editable candidates for author review.", MessageType.Info);
            using (new EditorGUI.DisabledScope(_busy))
            {
                int tab = GUILayout.Toolbar(_tab, new[] { "1. Author", "2. Verify", "3. Edit JSON" });
                if (tab != _tab) { _tab = tab; _scroll = Vector2.zero; }
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                if (_tab == 0) DrawAuthor();
                else if (_tab == 1) DrawVerification();
                else DrawJson();
                EditorGUILayout.EndScrollView();
            }
            if (_busy) EditorGUILayout.HelpBox("Waiting for model response…", MessageType.Info);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        }

        void DrawAuthor()
        {
            EditorGUILayout.LabelField("Original instruction");
            _instruction = EditorGUILayout.TextArea(_instruction, GUILayout.MinHeight(100));
            _provider = (Provider)EditorGUILayout.EnumPopup("Generation provider", _provider);
            if (_provider == Provider.Gemini) _geminiModel = EditorGUILayout.TextField("Model", _geminiModel);
            else if (_provider == Provider.Claude) _claudeModel = EditorGUILayout.TextField("Model", _claudeModel);
            else EditorGUILayout.HelpBox(_provider == Provider.ProjectBackend
                ? "Follows Tools → Home Care → VLM Backend."
                : "Requires the bridge to be set to Qwen in VLM Backend.", MessageType.Info);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_instruction)))
                if (GUILayout.Button("Generate step requirements", GUILayout.Height(28))) Generate();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load example (no API)")) TryLoad(ExamplePath);
            if (GUILayout.Button("Load requirements JSON"))
            {
                string path = EditorUtility.OpenFilePanel("Load requirements", "", "json");
                if (!string.IsNullOrEmpty(path)) TryLoad(path);
            }
            using (new EditorGUI.DisabledScope(_procedure == null || _jsonDirty))
                if (GUILayout.Button("Save requirements")) SaveProcedure();
            EditorGUILayout.EndHorizontal();
            if (_procedure != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(_procedure.procedure_title, EditorStyles.boldLabel);
                foreach (var step in _procedure.steps)
                    EditorGUILayout.LabelField(step.id + ". " + step.title + " (" + step.requirements.Length + " requirements)");
            }
        }

        void DrawJson()
        {
            EditorGUILayout.HelpBox("Edit requirement descriptions, VQA questions and evidence scopes here, then apply. source_span must remain an exact quote from the original instruction. Applying changes clears the previous verification.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            _json = EditorGUILayout.TextArea(_json, GUILayout.MinHeight(350));
            if (EditorGUI.EndChangeCheck()) { _jsonDirty = true; _report = null; }
            if (GUILayout.Button("Validate and apply JSON"))
            {
                try { Apply(_json); }
                catch (Exception e) { _error = e.Message; }
            }
        }

        void DrawVerification()
        {
            if (_procedure == null || _jsonDirty)
            {
                EditorGUILayout.HelpBox("Generate or load requirements, and apply any JSON edits first.", MessageType.Info);
                return;
            }
            string[] names = Array.ConvertAll(_procedure.steps, s => s.id + ": " + s.title);
            int next = EditorGUILayout.Popup("Selected step", _stepIndex, names);
            if (next != _stepIndex)
            {
                _stepIndex = next;
                _before = _during = _after = "";
                _report = null;
            }
            var step = _procedure.steps[_stepIndex];
            EditorGUILayout.HelpBox(step.instruction, MessageType.None);
            foreach (var requirement in step.requirements)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(requirement.id + " · " + requirement.category + " · " + requirement.evidence_scope, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(requirement.description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("VQA: " + requirement.vqa_question, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Observe: " + requirement.observation_hint, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Source: “" + requirement.source_span + "”", EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Execution evidence for this step", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Attach only evidence from this execution. Whole-step video must cover the relevant action. Missing scopes stay unknown. Total upload budget: 18 MiB.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            EvidenceField("Before image", ref _before, false);
            EvidenceField("Whole-step video", ref _during, true);
            EvidenceField("After image", ref _after, false);
            _followVerificationBackend = EditorGUILayout.Toggle("Follow project VLM backend", _followVerificationBackend);
            if (!_followVerificationBackend)
            {
                _localVerification = EditorGUILayout.Toggle("Use local Ollama for VQA", _localVerification);
                if (!_localVerification) _geminiModel = EditorGUILayout.TextField("VQA Gemini model", _geminiModel);
            }
            EditorGUILayout.HelpBox("Bridge uploads are limited to 8 MiB after encoding. Qwen accepts images; whole-step video requires Gemini.", MessageType.Info);
            if (EditorGUI.EndChangeCheck()) _report = null;
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_before) && string.IsNullOrWhiteSpace(_during) && string.IsNullOrWhiteSpace(_after)))
                if (GUILayout.Button(_followVerificationBackend ? "Verify selected step (project backend)"
                    : _localVerification ? "Verify selected step (local)" : "Verify selected step (Gemini)", GUILayout.Height(28))) Verify();
            DrawReport();
        }

        void EvidenceField(string label, ref string path, bool video)
        {
            EditorGUILayout.BeginHorizontal();
            path = EditorGUILayout.TextField(label, path);
            if (GUILayout.Button("Browse", GUILayout.Width(65)))
            {
                string selected = EditorUtility.OpenFilePanelWithFilters(label, "", video
                    ? new[] { "MP4 video", "mp4" } : new[] { "Images", "png,jpg,jpeg" });
                if (!string.IsNullOrEmpty(selected)) { path = selected; _report = null; }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawReport()
        {
            if (_report == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Step result: " + _report.overall_status, EditorStyles.boldLabel);
            foreach (var result in _report.results)
                EditorGUILayout.HelpBox(result.requirement_id + ": " + result.status + "\n" + result.evidence + "\n" + result.reason,
                    result.status == RequirementVerification.Satisfied ? MessageType.Info : MessageType.Warning);
            if (GUILayout.Button("Save verification report"))
            {
                string path = EditorUtility.SaveFilePanel("Save verification report", "", _report.step_id + "_verification", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    try { File.WriteAllText(path, RequirementsJson.Serialize(_report)); }
                    catch (Exception e) { _error = e.Message; }
                }
            }
        }

        async void Generate()
        {
            _busy = true;
            _error = "";
            _report = null;
            string instruction = _instruction;
            Repaint();
            try
            {
                ILLMClient client = _provider == Provider.ProjectBackend ? new LocalVlmClient(followProjectBackend: true)
                    : _provider == Provider.LocalOllama ? new LocalVlmClient()
                    : _provider == Provider.Gemini ? new GeminiClient(_geminiModel.Trim()) : new ClaudeClient(_claudeModel.Trim());
                string json = await client.DecomposeAsync(RequirementsPrompts.Generate, instruction, RequirementsJson.ProcedureSchema);
                var generated = RequirementsJson.Parse(json);
                if (generated.instruction != instruction)
                    throw new InvalidOperationException("The model changed the original instruction. Generate again.");
                Apply(json);
            }
            catch (Exception e) { _error = e.Message; Debug.LogException(e); }
            finally { _busy = false; if (this != null) Repaint(); }
        }

        async void Verify()
        {
            _busy = true;
            _error = "";
            _report = null;
            Repaint();
            try
            {
                // Freeze the exact rubric and evidence selection used for this request.
                var procedure = RequirementsJson.Parse(RequirementsJson.Serialize(_procedure));
                var step = procedure.steps[_stepIndex];
                var attachments = new List<EvidenceAttachment>();
                if (!string.IsNullOrWhiteSpace(_before)) attachments.Add(new EvidenceAttachment { scope = "before_step", path = _before });
                if (!string.IsNullOrWhiteSpace(_during)) attachments.Add(new EvidenceAttachment { scope = "whole_step", path = _during });
                if (!string.IsNullOrWhiteSpace(_after)) attachments.Add(new EvidenceAttachment { scope = "after_step", path = _after });
                var evidence = attachments.ToArray();
                string model = _geminiModel.Trim();
                string provider = "Gemini";
                string content = RequirementsJson.Serialize(new { procedure_title = procedure.procedure_title,
                    original_instruction = procedure.instruction, selected_step = step });
                string json;
                if (_followVerificationBackend || _localVerification)
                {
                    var client = new LocalVlmClient(followProjectBackend: _followVerificationBackend);
                    model = client.Model;
                    provider = client.Provider == "ollama" ? "Ollama" : "Gemini";
                    json = await client.GenerateWithEvidenceAsync(RequirementsPrompts.Verify, content, evidence, RequirementsJson.VerificationSchema);
                }
                else json = await new GeminiClient(model).GenerateWithEvidenceAsync(RequirementsPrompts.Verify,
                    content, evidence, RequirementsJson.VerificationSchema);
                var results = RequirementVerification.Evaluate(step, RequirementsJson.ParseResponse(json), evidence);
                _report = new VerificationReport { verified_at_utc = DateTime.UtcNow.ToString("o"), provider = provider,
                    model = model, procedure = procedure, step_id = step.id, attachments = evidence,
                    overall_status = RequirementVerification.Aggregate(results), results = results };
            }
            catch (Exception e) { _error = e.Message; Debug.LogException(e); }
            finally { _busy = false; if (this != null) Repaint(); }
        }

        void Apply(string json)
        {
            var procedure = RequirementsJson.Parse(json);
            _procedure = procedure;
            _json = RequirementsJson.Serialize(procedure);
            _instruction = procedure.instruction;
            _jsonDirty = false;
            _stepIndex = 0;
            _report = null;
            _before = _during = _after = "";
            _error = "";
        }

        void TryLoad(string path)
        {
            try { Apply(File.ReadAllText(path)); }
            catch (Exception e) { _error = e.Message; }
        }

        void SaveProcedure()
        {
            string directory = "Assets/_Project/Content/Procedures";
            Directory.CreateDirectory(directory);
            string path = EditorUtility.SaveFilePanelInProject("Save requirements", "requirements", "json", "", directory);
            if (string.IsNullOrEmpty(path)) return;
            try { File.WriteAllText(path, RequirementsJson.Serialize(_procedure)); AssetDatabase.Refresh(); }
            catch (Exception e) { _error = e.Message; }
        }
    }
}
