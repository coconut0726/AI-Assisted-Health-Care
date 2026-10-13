// Assets/_Project/Editor/Authoring/DecomposeTestWindow.cs
// Menu: Tools > Home Care > Decompose Instruction
// Paste a nurse instruction, pick a provider, click Decompose, then save the JSON.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HomeCare.Authoring
{
    public class DecomposeTestWindow : EditorWindow
    {
        enum Provider { Gemini, Claude }

        Provider _provider = Provider.Gemini;
        string _instruction =
            "So once a day you're gonna change this, wash your hands first, and if the old bandage is stuck, " +
            "just wet it with some water so it comes off easy, don't rip it. Then wash the burn real gently with " +
            "mild soap and water and pat it dry, no rubbing. Put a thin layer of the cream on with a clean Q-tip, " +
            "then the non-stick pad goes right on the burn, and wrap the gauze roll around it and tape it. " +
            "Don't pop any blisters. If it gets redder, starts oozing pus, smells bad, or you get a fever, call us.";
        string _videoPath = "";
        string _output = "";
        bool _busy;
        Vector2 _scroll;

        const string SystemPrompt =
            "You decompose a nurse's home care instruction into a structured procedure for an AR guidance app.\n" +
            "Hierarchy: steps -> subgoals -> actions.\n" +
            "- A step is one coherent part of the procedure.\n" +
            "- A subgoal is an observable end state within the step.\n" +
            "- An action is one short voice instruction (12 words or fewer, one verb where possible).\n" +
            "Label each action with exactly one narration-centric tactic: Command, StatusCheck, " +
            "QuantitativeSpecification, ConditionalStatement, Rationale.\n" +
            "Choose one guidance type: ContextualWidget (param = the text to show), Movement (param = movement type, " +
            "e.g. peel, wrap, spread), HandGesture (param = gesture type, e.g. pat, pinch, press).\n" +
            "List objects to detect, each with visual attributes (color, shape) to help detection.\n" +
            "Rules: copy the nurse's exact words into source_span. Never add steps, warnings, or details the nurse did not say. " +
            "Keep every warning and red-flag sign. Keep manner words such as gently, slowly, thin, no rubbing.";

        // Kept small on purpose; extend as the schema settles.
        const string Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""procedure_title"": { ""type"": ""string"" },
    ""steps"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""properties"": {
          ""title"": { ""type"": ""string"" },
          ""source_span"": { ""type"": ""string"" },
          ""subgoals"": {
            ""type"": ""array"",
            ""items"": {
              ""type"": ""object"",
              ""properties"": {
                ""expected_state"": { ""type"": ""string"" },
                ""actions"": {
                  ""type"": ""array"",
                  ""items"": {
                    ""type"": ""object"",
                    ""properties"": {
                      ""voice"": { ""type"": ""string"" },
                      ""tactic"": { ""type"": ""string"", ""enum"": [""Command"", ""StatusCheck"", ""QuantitativeSpecification"", ""ConditionalStatement"", ""Rationale""] },
                      ""guidance_type"": { ""type"": ""string"", ""enum"": [""ContextualWidget"", ""Movement"", ""HandGesture""] },
                      ""guidance_param"": { ""type"": ""string"" },
                      ""objects"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
                    },
                    ""required"": [""voice"", ""tactic"", ""guidance_type"", ""guidance_param"", ""objects""]
                  }
                }
              },
              ""required"": [""expected_state"", ""actions""]
            }
          }
        },
        ""required"": [""title"", ""source_span"", ""subgoals""]
      }
    },
    ""safety_notes"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
    ""red_flags"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
  },
  ""required"": [""procedure_title"", ""steps"", ""safety_notes"", ""red_flags""]
}";

        [MenuItem("Tools/Home Care/Decompose Instruction")]
        static void Open() => GetWindow<DecomposeTestWindow>("Decompose");

        void OnGUI()
        {
            _provider = (Provider)EditorGUILayout.EnumPopup("Provider", _provider);

            EditorGUILayout.LabelField("Nurse instruction");
            _instruction = EditorGUILayout.TextArea(_instruction, GUILayout.MinHeight(100));

            using (new EditorGUI.DisabledScope(_provider != Provider.Gemini))
            {
                EditorGUILayout.BeginHorizontal();
                _videoPath = EditorGUILayout.TextField("Video (optional, Gemini)", _videoPath);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                    _videoPath = EditorUtility.OpenFilePanel("Select video", "", "mp4");
                EditorGUILayout.EndHorizontal();
            }

            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button(_busy ? "Working..." : "Decompose", GUILayout.Height(28)))
                    Run();
            }

            EditorGUILayout.LabelField("Output JSON");
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            _output = EditorGUILayout.TextArea(_output, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_output) || _busy))
            {
                if (GUILayout.Button("Save JSON"))
                {
                    string dir = "Assets/_Project/Content/Procedures";
                    Directory.CreateDirectory(dir);
                    string path = EditorUtility.SaveFilePanelInProject("Save procedure", "procedure", "json", "", dir);
                    if (!string.IsNullOrEmpty(path))
                    {
                        File.WriteAllText(path, _output);
                        AssetDatabase.Refresh();
                    }
                }
            }
        }

        async void Run()
        {
            _busy = true;
            _output = "";
            Repaint();
            try
            {
                if (_provider == Provider.Gemini)
                {
                    var gemini = new GeminiClient();
                    _output = string.IsNullOrEmpty(_videoPath)
                        ? await gemini.DecomposeAsync(SystemPrompt, _instruction, Schema)
                        : await gemini.DecomposeWithVideoAsync(SystemPrompt, _instruction, _videoPath, Schema);
                }
                else
                {
                    _output = await new ClaudeClient().DecomposeAsync(SystemPrompt, _instruction, Schema);
                }
            }
            catch (Exception e)
            {
                _output = "ERROR: " + e.Message;
                Debug.LogException(e);
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }
    }
}
