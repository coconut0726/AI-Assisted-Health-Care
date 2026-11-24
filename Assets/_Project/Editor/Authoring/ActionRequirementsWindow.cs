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
using UnityEngine.SceneManagement;
using AHC.VR;

namespace HomeCare.Authoring
{
    public sealed class ActionRequirementsWindow : EditorWindow
    {
        const string Content = "Assets/_Project/Content/Procedures/";
        public const string ScenePath = "Assets/_Project/Scenes/VRGuidanceDemoActions.unity";
        [SerializeField] string _instruction = "Start with an empty tray. Place the blue pad on the tray. Put the red block on the blue pad. Keep the red block on the blue pad while wrapping the yellow strap around it.";
        [SerializeField] string _goal = "Tabletop requirement flow", _prior = "", _json = "", _savePath = "";
        string _status = "", _error = "";
        bool _busy;
        Vector2 _scroll;
        [SerializeField] ActionProcedure _procedure;
        [SerializeField] int _stepIndex, _actionIndex;
        [SerializeField] bool _showGeneration = true, _showAdvanced, _showShared, _jsonPending, _dirty;
        readonly Dictionary<string, bool> _details = new();
        GUIStyle _wrappedTextArea;
        GUIStyle WrappedTextArea => _wrappedTextArea ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        CancellationTokenSource _lifetime;
        public static void Open() { var window = GetWindow<ActionRequirementsWindow>("Requirement Generation"); window.minSize = new Vector2(650, 650); }
        void OnEnable() { titleContent = new GUIContent("Requirement Generation"); _lifetime = new CancellationTokenSource(); Undo.undoRedoPerformed += OnUndo; }
        void OnDisable() { Undo.undoRedoPerformed -= OnUndo; _lifetime?.Cancel(); _lifetime?.Dispose(); }
        void OnUndo() { if (_procedure != null && !_jsonPending) _json = ActionProcedureJson.Serialize(_procedure); _dirty = true; Repaint(); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Instruction → Steps / Actions → Requirements", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("VLM Backend…")) VlmBackendWindow.Open();
            if (GUILayout.Button("Legacy requirements editor…")) RequirementsPrototypeWindow.Open();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Open V3 Demo")) OpenScene();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Generation creates a draft in this window; it does not save files automatically. Save writes the whole current procedure JSON. Save & use in V3 Demo also assigns it to V3 for the next Play session.", MessageType.Info);
            if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("Exit Play mode to edit. The running session keeps its own copy of the procedure.", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(_busy || EditorApplication.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                bool loaded = false;
                if (GUILayout.Button("Burn case")) { LoadJson(Content + "burn_action_requirements.json"); loaded = true; }
                if (GUILayout.Button("Simple wound case")) { LoadJson(Content + "wound_action_requirements.json"); loaded = true; }
                if (GUILayout.Button("Tabletop example")) { LoadJson(Content + "tabletop_action_requirements.json"); loaded = true; }
                if (GUILayout.Button("Load JSON…"))
                { string path = EditorUtility.OpenFilePanel("Load Action procedure", Content, "json"); if (!string.IsNullOrEmpty(path)) { LoadJson(path); loaded = true; } }
                EditorGUILayout.EndHorizontal();
                if (loaded) GUIUtility.ExitGUI();
                var asset = string.IsNullOrEmpty(_savePath) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(_savePath);
                var selected = (TextAsset)EditorGUILayout.ObjectField("Procedure file", asset, typeof(TextAsset), false);
                if (selected != null && selected != asset) { LoadJson(AssetDatabase.GetAssetPath(selected)); GUIUtility.ExitGUI(); }
                _showGeneration = EditorGUILayout.Foldout(_showGeneration || _procedure == null, "Generate from instructions", true);
                if (_showGeneration)
                {
                _goal = EditorGUILayout.TextField("Task goal / title", _goal);
                EditorGUILayout.LabelField("Original instruction");
                _instruction = EditorGUILayout.TextArea(_instruction, WrappedTextArea, GUILayout.MinHeight(95));
                EditorGUILayout.LabelField("Optional supplied nurse knowledge (empty = none)");
                _prior = EditorGUILayout.TextArea(_prior, WrappedTextArea, GUILayout.MinHeight(40));
#if false // Hidden shortcuts: the case loaders above already restore the original instruction.
                EditorGUILayout.BeginHorizontal();
                bool instructionLoaded = false;
                if (GUILayout.Button("Burn instruction")) { LoadInstruction("burn_care_procedure.json"); instructionLoaded = true; }
                if (GUILayout.Button("Simple wound instruction")) { LoadInstruction("wound_dressing_procedure.json"); instructionLoaded = true; }
                if (GUILayout.Button("Tabletop instruction")) { LoadInstruction("tabletop_action_requirements.json"); instructionLoaded = true; }
                EditorGUILayout.EndHorizontal();
                if (instructionLoaded) GUIUtility.ExitGUI();
#endif
                if (GUILayout.Button("1. Generate Steps and atomic Actions", GUILayout.Height(28))) GenerateGraph();
                using (new EditorGUI.DisabledScope(_procedure == null))
                    if (GUILayout.Button("2. Generate Action requirements and criteria", GUILayout.Height(28))) GenerateRequirements();
                EditorGUILayout.LabelField("Generating requirements replaces the current rubric. Use the form below for manual changes.", EditorStyles.wordWrappedMiniLabel);
                }
                if (_procedure != null)
                {
                    EditorGUILayout.LabelField($"{_procedure.steps.Length} Steps / {_procedure.steps.Sum(s => s.actions.Length)} Actions", EditorStyles.boldLabel);
                    using (new EditorGUI.DisabledScope(_jsonPending)) DrawProcedureForm();
                    _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced JSON (optional)", true);
                    if (_showAdvanced)
                    {
                        var json = EditorGUILayout.TextArea(_json, WrappedTextArea, GUILayout.MinHeight(180));
                        if (json != _json) { _json = json; _jsonPending = true; _dirty = true; }
                        EditorGUILayout.BeginHorizontal();
                        bool apply = GUILayout.Button("Apply JSON");
                        if (apply) ApplyJson();
                        if (GUILayout.Button("Discard JSON changes")) { _jsonPending = false; _json = ActionProcedureJson.Serialize(_procedure); _error = ""; }
                        EditorGUILayout.EndHorizontal();
                        if (apply) GUIUtility.ExitGUI();
                    }
                    if (_jsonPending) EditorGUILayout.HelpBox("Apply or discard the Advanced JSON changes before returning to the form. Saving will validate them first.", MessageType.Warning);
                    else foreach (var issue in ActionRequirementEditing.Issues(_procedure).Take(3)) EditorGUILayout.HelpBox(issue, MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(_dirty ? "Save *" : "Save")) SaveFile(false);
                if (GUILayout.Button("Save as…")) SaveFile(true);
                if (GUILayout.Button("Save & use in V3 Demo", GUILayout.Height(28))) SaveAndUse();
                EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
        }
        static void OpenScene()
        { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath); }

        void Edited()
        {
            ActionRequirementEditing.NoteEdited(_procedure);
            ActionRequirementEditing.SyncCompletionModes(_procedure);
            _json = ActionProcedureJson.Serialize(_procedure); _jsonPending = false; _dirty = true; _error = "";
            EditorUtility.SetDirty(this);
        }
        void RecordEdit() => Undo.RecordObject(this, "Edit Action requirements");
        void EditBox(string label, string value, Action<string> change)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            string next = EditorGUILayout.TextArea(value ?? "", WrappedTextArea, GUILayout.MinHeight(38));
            if (next != value) { RecordEdit(); change(next); Edited(); }
        }
        void DrawProcedureForm()
        {
            ActionRequirementEditing.SyncCompletionModes(_procedure);
            _stepIndex = Mathf.Clamp(_stepIndex, 0, _procedure.steps.Length - 1);
            int step = EditorGUILayout.Popup("Step", _stepIndex, _procedure.steps.Select(s => s.step_id + " · " + s.step_name).ToArray());
            if (step != _stepIndex) { _stepIndex = step; _actionIndex = 0; GUIUtility.ExitGUI(); }
            var current = _procedure.steps[_stepIndex];
            _actionIndex = Mathf.Clamp(_actionIndex, 0, current.actions.Length - 1);
            int selectedAction = EditorGUILayout.Popup("Action", _actionIndex, current.actions.Select(a => a.action_id + " · " + Short(a.instruction, 70)).ToArray());
            if (selectedAction != _actionIndex) { _actionIndex = selectedAction; GUIUtility.ExitGUI(); }
            var action = current.actions[_actionIndex];
            EditorGUILayout.LabelField(action.instruction, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(5);
            DrawConditions(action, true);
            DrawConditions(action, false);
            EditorGUILayout.LabelField(action.manual_completion_required ? "Completion: user confirms this Action (no image postconditions)." : "Completion: image postconditions must pass.", EditorStyles.wordWrappedMiniLabel);
            DrawErrors(action, false);
            _showShared = EditorGUILayout.Foldout(_showShared, "Shared rules (" + _procedure.procedure_error_criteria.Length + ")", true);
            if (_showShared) DrawErrors(action, true);
        }
        void DrawConditions(ProcedureAction action, bool before)
        {
            var rows = before ? action.preconditions : action.postconditions;
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField(before ? "Preconditions — before this Action" : "Postconditions — visible result", EditorStyles.boldLabel);
            if (rows.Length == 0) EditorGUILayout.LabelField("No conditions. Add one only if needed.", EditorStyles.miniLabel);
            for (int i = 0; i < rows.Length; i++)
            {
                var q = rows[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Condition " + (i + 1), EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    RecordEdit(); ActionRequirementEditing.RemoveCondition(_procedure, action, q, before); Edited();
                    EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); GUIUtility.ExitGUI(); return;
                }
                EditorGUILayout.EndHorizontal();
                EditBox("Visible state to check", q.condition, value => { q.condition = value; ActionRequirementEditing.MarkEdited(q, action.action_id); });
                if (q.requires_expert_review) EditorGUILayout.HelpBox("Automatic approval is paused for this condition. Review it, then clear Needs review under Details when ready.", MessageType.Warning);
                if (Details(q.requirement_id, q.source))
                {
                    EditorGUILayout.LabelField("ID (managed): " + q.requirement_id, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(q.source_reference ?? "", EditorStyles.wordWrappedMiniLabel);
                    bool review = EditorGUILayout.ToggleLeft("Needs review — automatic approval stays blocked", q.requires_expert_review);
                    if (review != q.requires_expert_review)
                    { RecordEdit(); q.requires_expert_review = review; ActionRequirementEditing.MarkEdited(q, action.action_id); Edited(); }
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button(before ? "+ Add precondition" : "+ Add postcondition"))
            { RecordEdit(); ActionRequirementEditing.AddCondition(_procedure, action, before); Edited(); GUIUtility.ExitGUI(); }
        }
        void DrawErrors(ProcedureAction action, bool global)
        {
            var rows = global ? _procedure.procedure_error_criteria : action.error_criteria;
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField(global ? "Shared error criteria" : "Error criteria — optional", EditorStyles.boldLabel);
            if (rows.Length == 0) EditorGUILayout.LabelField("The five error types are choices, not five required checks per Action.", EditorStyles.wordWrappedMiniLabel);
            var types = ActionProcedureJson.SupportedErrorTypes;
            var names = new[] { "Wrong Object", "Spatial Error", "Technique Error", "Measurement Error", "Timing / Parameter Error" };
            for (int i = 0; i < rows.Length; i++)
            {
                var e = rows[i]; string owner = global ? "procedure" : action.action_id;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Rule " + (i + 1), EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    RecordEdit(); ActionRequirementEditing.RemoveError(_procedure, action, e, global); Edited();
                    EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); GUIUtility.ExitGUI(); return;
                }
                EditorGUILayout.EndHorizontal();
                int oldType = Mathf.Max(0, Array.IndexOf(types, e.error_type));
                int nextType = EditorGUILayout.Popup("Error type", oldType, names);
                if (nextType != oldType) { RecordEdit(); e.error_type = types[nextType]; ActionRequirementEditing.MarkEdited(e, owner); Edited(); }
                EditBox("Error state — what would be wrong?", e.violation_condition, value => { e.violation_condition = value; ActionRequirementEditing.MarkEdited(e, owner); });
                EditBox("Correct state — what should the image show?", e.compliance_condition, value => { e.compliance_condition = value; ActionRequirementEditing.MarkEdited(e, owner); });
                if (e.requires_expert_review) EditorGUILayout.HelpBox("Automatic approval is paused for this rule. Review it, then clear Needs review under Details when ready.", MessageType.Warning);
                if (global)
                {
                    EditorGUILayout.LabelField("Check during these Actions", EditorStyles.miniBoldLabel);
                    foreach (var a in ActionRequirementEditing.Actions(_procedure))
                    {
                        bool on = e.relevant_action_ids.Contains(a.action_id);
                        bool next = EditorGUILayout.ToggleLeft(a.action_id + " · " + Short(a.instruction, 65), on);
                        if (next != on) { RecordEdit(); e.relevant_action_ids = Toggle(e.relevant_action_ids, a.action_id, next); ActionRequirementEditing.MarkEdited(e, owner); Edited(); }
                    }
                }
                if (Details(e.criterion_id, e.source))
                {
                    EditorGUILayout.LabelField("ID (managed): " + e.criterion_id, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(e.source_reference ?? "", EditorStyles.wordWrappedMiniLabel);
                    EditBox("Applies only when (optional)", e.applicability_condition, value => { e.applicability_condition = value; ActionRequirementEditing.MarkEdited(e, owner); });
                    bool review = EditorGUILayout.ToggleLeft("Needs review — automatic approval stays blocked", e.requires_expert_review);
                    if (review != e.requires_expert_review) { RecordEdit(); e.requires_expert_review = review; ActionRequirementEditing.MarkEdited(e, owner); Edited(); }
                    EditorGUILayout.LabelField("Related conditions (optional)", EditorStyles.miniBoldLabel);
                    foreach (var a in ActionRequirementEditing.Actions(_procedure))
                        foreach (var q in a.preconditions.Concat(a.postconditions))
                        {
                            bool on = e.related_requirement_ids.Contains(q.requirement_id);
                            bool next = EditorGUILayout.ToggleLeft(a.action_id + " · " + Short(q.condition, 65), on);
                            if (next != on) { RecordEdit(); e.related_requirement_ids = Toggle(e.related_requirement_ids, q.requirement_id, next); ActionRequirementEditing.MarkEdited(e, owner); Edited(); }
                        }
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button(global ? "+ Add shared error rule" : "+ Add error criterion"))
            { RecordEdit(); ActionRequirementEditing.AddError(_procedure, action, "spatial_error", global); Edited(); GUIUtility.ExitGUI(); }
        }
        bool Details(string id, string source)
        {
            _details.TryGetValue(id, out bool open);
            return _details[id] = EditorGUILayout.Foldout(open, "Details · " + (source == ActionRequirementEditing.AuthorSource ? "Author edited" : source), true);
        }
        static string Short(string value, int length) => string.IsNullOrEmpty(value) ? "(empty)" : value.Length <= length ? value : value.Substring(0, length) + "…";
        static string[] Toggle(string[] values, string id, bool add) => add ? values.Append(id).Distinct().ToArray() : values.Where(v => v != id).ToArray();
        bool CanReplaceDraft() => !_dirty || EditorUtility.DisplayDialog("Unsaved requirement edits", "Discard the unsaved edits and load another procedure?", "Discard edits", "Cancel");
        async void GenerateGraph()
        {
            _busy = true; _error = ""; _status = "Generating task graph…";
            try
            {
                var graph = await ActionRequirementAuthoring.Decompose(new LocalVlmClient(true).DecomposeAsync, _instruction, _goal, _lifetime.Token);
                if (_procedure == null || _procedure.instruction != graph.instruction) _savePath = "";
                _procedure = graph; RefreshProcedureFields(); _dirty = true;
                _status = "Step/Action draft generated. Review it, then click 2. Generate Action requirements and criteria. The draft has not been saved to a file.";
            }
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
                RefreshProcedureFields(); _dirty = true; _showGeneration = false;
                _status = "Requirements generated in this window. Review the form, then click Save or Save & use in V3 Demo. Nothing is saved automatically.";
            }
            catch (Exception e) { _error = e.Message; }
            finally { _busy = false; if (this != null) Repaint(); }
        }
        void RefreshProcedureFields()
        {
            ActionRequirementEditing.SyncCompletionModes(_procedure);
            _json = ActionProcedureJson.Serialize(_procedure); _jsonPending = false;
            _instruction = _procedure.instruction; _goal = _procedure.task_goal ?? _procedure.procedure_title;
        }
        void ApplyJson()
        {
            try
            {
                var root = ActionProcedureJson.Object(_json);
                if (root["steps"] is JArray steps)
                    foreach (var step in steps)
                        if (step["actions"] is JArray actions)
                            foreach (var action in actions)
                                if (action["postconditions"] is JArray post) action["manual_completion_required"] = post.Count == 0;
                var parsed = ActionProcedureJson.Parse(root.ToString(), false);
                RecordEdit(); _procedure = parsed; _error = ""; RefreshProcedureFields(); _dirty = true;
            }
            catch (Exception e) { _error = ActionRequirementEditing.FriendlyError(e.Message); }
        }
        void LoadInstruction(string name)
        {
            if (!CanReplaceDraft()) return;
            var root = JObject.Parse(File.ReadAllText(Content + name)); _instruction = (string)root["instruction"]; _goal = (string)root["procedure_title"];
            _procedure = null; _json = ""; _savePath = ""; _jsonPending = _dirty = false; _showGeneration = true;
            _error = ""; _status = "Source instruction loaded. Existing legacy requirements were not imported.";
        }
        void LoadJson(string path)
        {
            if (!CanReplaceDraft()) return;
            try
            {
                var parsed = ActionProcedureJson.Parse(File.ReadAllText(path), false);
                string full = Path.GetFullPath(path).Replace('\\', '/');
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
                string relative = full.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? full.Substring(project.Length) : "";
                _savePath = relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? relative : "";
                _procedure = parsed; _stepIndex = _actionIndex = 0; _details.Clear();
                RefreshProcedureFields(); _dirty = false; _showGeneration = false; _showAdvanced = false;
                _error = ""; _status = "Loaded. Select an Action and edit its conditions below.";
            }
            catch (Exception e) { _error = ActionRequirementEditing.FriendlyError(e.Message); }
        }
        bool SaveFile(bool saveAs)
        {
            try
            {
                if (_procedure == null) return false;
                if (_jsonPending) { ApplyJson(); if (_jsonPending) return false; }
                var issues = ActionRequirementEditing.Issues(_procedure);
                if (issues.Length != 0) { _error = string.Join("\n", issues.Take(5)); return false; }
                var parsed = ActionRequirementEditing.ReadyToSave(_procedure);
                string path = _savePath;
                if (saveAs || string.IsNullOrEmpty(path))
                    path = EditorUtility.SaveFilePanelInProject("Save Action procedure", string.IsNullOrEmpty(_savePath) ? "new_action_requirements" : Path.GetFileNameWithoutExtension(_savePath) + "_edited", "json", "Save the edited Action procedure.", Content);
                if (string.IsNullOrEmpty(path)) return false;
                if (new[] { Content + "burn_care_procedure.json", Content + "wound_dressing_procedure.json", Content + "requirements_example.json", Content + "ar_example.json" }.Contains(path, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("Save under a new Action-flow filename; keep the legacy demo data.");
                if (File.Exists(path) && (string)JObject.Parse(File.ReadAllText(path))["schema_version"] != ActionProcedureJson.Version)
                    throw new ArgumentException("This is existing legacy data. Choose a new filename.");
                File.WriteAllText(path, ActionProcedureJson.Serialize(parsed));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                _procedure = parsed; _savePath = path; RefreshProcedureFields(); _dirty = false; _error = "";
                _status = "Saved " + path + ". Save & use in V3 Demo also assigns it to the scene.";
                return true;
            }
            catch (Exception e) { _error = ActionRequirementEditing.FriendlyError(e.Message); return false; }
        }
        void SaveAndUse()
        {
            if (!SaveFile(false)) return;
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.path != ScenePath)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { _status = "Procedure saved. Scene assignment canceled."; return; }
                    scene = EditorSceneManager.OpenScene(ScenePath);
                }
                var session = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ActionGuidanceSession>(true)).SingleOrDefault();
                if (session == null) throw new ArgumentException("The V3 demo session was not found in " + ScenePath);
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(_savePath);
                if (asset == null) throw new ArgumentException("The saved JSON has not imported as a TextAsset.");
                Undo.RecordObject(session, "Use edited Action procedure");
                session.procedureJson = asset;
                session.examples = (session.examples ?? Array.Empty<TextAsset>()).Append(asset).Where(a => a != null).Distinct().ToArray();
                EditorUtility.SetDirty(session); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Procedure saved, but the demo scene could not be saved.");
                Selection.activeGameObject = session.gameObject;
                _status = "Saved and assigned to V3. Enter Play and press Begin to use " + Path.GetFileName(_savePath) + ".";
            }
            catch (Exception e) { _error = ActionRequirementEditing.FriendlyError(e.Message); }
        }
    }
}
