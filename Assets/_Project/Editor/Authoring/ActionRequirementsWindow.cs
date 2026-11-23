using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HomeCare.Procedures;
using HomeCare.Procedures.Actions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HomeCare.Authoring
{
    public sealed class ActionRequirementsWindow : EditorWindow
    {
        const string Content = "Assets/_Project/Content/Procedures/";
        public const string ScenePath = "Assets/_Project/Scenes/VRGuidanceDemoActions.unity";
        string _instruction = "Start with an empty tray. Place the blue pad on the tray. Put the red block on the blue pad. Keep the red block on the blue pad while wrapping the yellow strap around it.";
        string _goal = "Tabletop requirement flow", _prior = "", _json = "", _status = "", _error = "";
        bool _busy;
        Vector2 _scroll;
        ActionProcedure _procedure;
        CancellationTokenSource _lifetime;
        public static void Open() => GetWindow<ActionRequirementsWindow>("Action Requirements");
        void OnEnable() => _lifetime = new CancellationTokenSource();
        void OnDisable() { _lifetime?.Cancel(); _lifetime?.Dispose(); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Instruction → Steps / Actions → Requirements", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("VLM Backend…")) VlmBackendWindow.Open();
            if (GUILayout.Button("Legacy requirements editor…")) RequirementsPrototypeWindow.Open();
            if (GUILayout.Button("Open new demo")) OpenScene();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("New Action-flow format. No observation/evidence fields. The original demo and its JSON remain separate. Generation follows the project backend; the new runtime defaults to local Ollama.", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(_busy || EditorApplication.isPlaying))
            {
                _goal = EditorGUILayout.TextField("Task goal / title", _goal);
                EditorGUILayout.LabelField("Original instruction");
                _instruction = EditorGUILayout.TextArea(_instruction, GUILayout.MinHeight(95));
                EditorGUILayout.LabelField("Optional supplied nurse knowledge (empty = none)");
                _prior = EditorGUILayout.TextArea(_prior, GUILayout.MinHeight(40));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Burn instruction")) LoadInstruction("burn_care_procedure.json");
                if (GUILayout.Button("Simple wound instruction")) LoadInstruction("wound_dressing_procedure.json");
                if (GUILayout.Button("Load new example")) LoadJson(Content + "tabletop_action_requirements.json");
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("1. Generate Steps and atomic Actions", GUILayout.Height(28))) GenerateGraph();
                using (new EditorGUI.DisabledScope(_procedure == null))
                    if (GUILayout.Button("2. Generate Action requirements and criteria", GUILayout.Height(28))) GenerateRequirements();
                if (_procedure != null)
                    EditorGUILayout.LabelField($"{_procedure.steps.Length} Steps / {_procedure.steps.Sum(s => s.actions.Length)} Actions", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Review / edit JSON");
                _json = EditorGUILayout.TextArea(_json, GUILayout.MinHeight(230));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply JSON")) ApplyJson();
                if (GUILayout.Button("Load JSON…"))
                { string path = EditorUtility.OpenFilePanel("Load Action procedure", Content, "json"); if (!string.IsNullOrEmpty(path)) LoadJson(path); }
                if (GUILayout.Button("Save Action procedure…")) Save();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        }
        static void OpenScene()
        { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath); }
        async void GenerateGraph()
        {
            _busy = true; _error = ""; _status = "Generating task graph…";
            try { _procedure = await ActionRequirementAuthoring.Decompose(new LocalVlmClient(true).DecomposeAsync, _instruction, _goal, _lifetime.Token); RefreshProcedureFields(); }
            catch (Exception e) { _error = e.Message; }
            finally { _busy = false; if (this != null) Repaint(); }
        }
        async void GenerateRequirements()
        {
            _busy = true; _error = "";
            try
            {
                string instructionBeforeApply = _instruction;
                ApplyJson(); if (_procedure == null || !string.IsNullOrEmpty(_error)) return;
                if (_procedure.instruction != instructionBeforeApply) throw new ArgumentException("Instruction changed. Generate a new graph first.");
                _procedure = await ActionRequirementAuthoring.Enrich(new LocalVlmClient(true).DecomposeAsync, _procedure, _prior,
                    text => { _status = text; if (this != null) Repaint(); }, _lifetime.Token);
                RefreshProcedureFields(); _status = "Generated. Review source grounding and conditions before saving; Next is never unlocked by unknown.";
            }
            catch (Exception e) { _error = e.Message; }
            finally { _busy = false; if (this != null) Repaint(); }
        }
        void RefreshProcedureFields() { _json = ActionProcedureJson.Serialize(_procedure); _instruction = _procedure.instruction; _goal = _procedure.task_goal ?? _procedure.procedure_title; }
        void ApplyJson()
        { try { var parsed = ActionProcedureJson.Parse(_json, false); _procedure = parsed; _error = ""; RefreshProcedureFields(); } catch (Exception e) { _error = e.Message; } }
        void LoadInstruction(string name)
        {
            var root = JObject.Parse(File.ReadAllText(Content + name)); _instruction = (string)root["instruction"]; _goal = (string)root["procedure_title"];
            _procedure = null; _json = ""; _error = ""; _status = "Source instruction loaded. Existing legacy requirements were not imported.";
        }
        void LoadJson(string path)
        { try { _procedure = ActionProcedureJson.Parse(File.ReadAllText(path), false); RefreshProcedureFields(); _error = ""; } catch (Exception e) { _error = e.Message; } }
        void Save()
        {
            try
            {
                var parsed = ActionProcedureJson.Parse(_json); // Draft graphs without postconditions cannot become runtime procedures.
                string path = EditorUtility.SaveFilePanelInProject("Save Action procedure", "new_action_requirements", "json", "Save separately from the legacy procedures.", Content);
                if (string.IsNullOrEmpty(path)) return;
                if (new[] { Content + "burn_care_procedure.json", Content + "wound_dressing_procedure.json", Content + "requirements_example.json", Content + "ar_example.json" }.Contains(path))
                    throw new ArgumentException("Save under a new Action-flow filename; keep the legacy demo data.");
                if (File.Exists(path) && (string)JObject.Parse(File.ReadAllText(path))["schema_version"] != ActionProcedureJson.Version)
                    throw new ArgumentException("This is existing legacy data. Choose a new filename.");
                File.WriteAllText(path, ActionProcedureJson.Serialize(parsed)); AssetDatabase.Refresh(); _status = "Saved. Assign this TextAsset to ActionGuidanceSession in the new scene.";
            }
            catch (Exception e) { _error = e.Message; }
        }
    }
}
