using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AHC.VR
{
    /// <summary>
    /// Demo build of the headset UI from the Figma "XR" page: the nurse recording flow (VN1–VN3)
    /// and the caregiver/patient guided-care flow (VP1–VP5). Everything is built at runtime from
    /// the demo JSON; add it to a scene with  AHC ▸ Add VR Demo to Scene.
    ///
    /// Controls — Quest controllers:  A next · B back · X repeat (or "nurse" on the first screen) ·
    /// Y home · hold right trigger to move the guidance onto the real burn.
    /// Editor keyboard:  → / Space next · ← back · R repeat · 1 patient · 2 nurse · H home ·
    /// I J K L U O nudge the workspace · P put it back in front of you. Buttons are also clickable.
    /// </summary>
    public class VRDemoController : MonoBehaviour
    {
        enum View { RoleSelect, NurseStart, NurseRecording, NurseSaved, PatientStart, PatientStep, PatientComplete }

        [Tooltip("Assets/_Project/Data/BurnDressingDemo.json")]
        public TextAsset demoJson;

        [Tooltip("The AI's character (Assets/_Project/Art/Ghost/Ghost.fbx). When set, it appears in front of the " +
                 "user's first view, stays at that spot, and speaks the instructions instead of the voice bar.")]
        public GameObject ghostModel;

        [Header("Placement")]
        [Tooltip("Distance of the floating panels from the eyes, in metres.")]
        public float hudDistance = 1.0f;
        [Tooltip("Height of the panels' centre relative to the eyes, in metres. Raise it if the voice bar covers the table.")]
        public float hudHeight = 0.08f;
        [Tooltip("Where the workspace (the burn) starts, relative to the eyes: forward and down, in metres.")]
        public Vector2 workspaceStart = new(0.5f, -0.5f);

        [Header("Testing")]
        [Tooltip("Show simple stand-in objects (arm, supplies). On by default in the Editor only.")]
        public bool placeholderPropsInEditor = true;
        public bool placeholderPropsOnDevice = false;

        const float HudScale = 0.001f; // metres per Figma pixel: the 1440×900 HUD is 1.44 m × 0.9 m

        GuidanceData _data;
        List<(int step, int action)> _flat;
        View _view;
        int _cursor;
        float _recordStart, _recordLength, _careStart;
        int _transcriptIndex;

        Transform _head;
        OVRCameraRig _rig;
        Camera _eventCamera;
        RectTransform _hud;
        Transform _workspace;
        WorkspaceOverlays _overlays;
        GhostCompanion _ghost;
        TextMeshProUGUI _timer, _transcript;
        RectTransform _voiceBar;

        // ------------------------------------------------------------------ setup

        void Start()
        {
            if (demoJson == null)
            {
                Debug.LogError("[AHC] VRDemoController needs the demo JSON (Assets/_Project/Data/BurnDressingDemo.json).", this);
                enabled = false;
                return;
            }
            _data = GuidanceData.FromJson(demoJson.text);
            _flat = _data.Flatten();

            _rig = FindAnyObjectByType<OVRCameraRig>();
            _head = _rig != null && _rig.centerEyeAnchor != null ? _rig.centerEyeAnchor
                  : Camera.main != null ? Camera.main.transform : transform;
            _eventCamera = _head.TryGetComponent<Camera>(out var cam) ? cam : Camera.main;

            EnsureEventSystem();
            BuildHud();
            BuildWorkspace();
            if (ghostModel != null) _ghost = GhostCompanion.Spawn(ghostModel, transform, _head); // appears in the first view, then stays
            SetView(View.RoleSelect);
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        void BuildHud()
        {
            var follow = new GameObject("HUD").AddComponent<LazyFollow>();
            follow.transform.SetParent(transform, false);
            follow.head = _head;
            follow.distance = hudDistance;
            follow.heightOffset = hudHeight;

            var canvas = Ui.WorldCanvas("HUD Canvas", follow.transform, new Vector2(1440, 900), HudScale, _eventCamera);
            canvas.sortingOrder = 10;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            _hud = (RectTransform)canvas.transform;
        }

        void BuildWorkspace()
        {
            _workspace = new GameObject("Workspace (burn)").transform;
            _workspace.SetParent(transform, false);
            PlaceWorkspaceInFront();
            _overlays = _workspace.gameObject.AddComponent<WorkspaceOverlays>();
            _overlays.Init(_data, _head);

            bool props = Application.isEditor ? placeholderPropsInEditor : placeholderPropsOnDevice;
            if (props) PlaceholderProps.Build(_workspace, _data);
        }

        void PlaceWorkspaceInFront()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            _workspace.SetPositionAndRotation(
                _head.position + fwd * workspaceStart.x + Vector3.up * workspaceStart.y,
                Quaternion.LookRotation(fwd, Vector3.up));
        }

        // ------------------------------------------------------------------ input

        void Update()
        {
            if (_data == null) return;

            if (Pressed(OVRInput.Button.One, Key.RightArrow, Key.Space, Key.Enter)) Next();
            else if (Pressed(OVRInput.Button.Two, Key.LeftArrow, Key.Backspace)) Back();
            else if (Pressed(OVRInput.Button.Three, Key.R)) Alt();
            else if (Pressed(OVRInput.Button.Four, Key.H, Key.Escape))
            {
                SetView(View.RoleSelect);
                if (_ghost != null) _ghost.Recenter(); // also brings the guide back in front of you
            }
            else if (_view == View.RoleSelect && KeyDown(Key.Digit1)) SetView(View.PatientStart);
            else if (_view == View.RoleSelect && KeyDown(Key.Digit2)) SetView(View.NurseStart);

            MoveWorkspace();

            if (_view == View.NurseRecording) TickRecording();
        }

        static bool KeyDown(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;

        static bool Pressed(OVRInput.Button button, params Key[] keys)
        {
            if (OVRInput.GetDown(button)) return true;
            foreach (var k in keys) if (KeyDown(k)) return true;
            return false;
        }

        void MoveWorkspace()
        {
            // Hold the right trigger: the workspace follows the right controller, so you can put it on the burn.
            if (_rig != null && OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger) && _rig.rightControllerAnchor != null)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
                _workspace.SetPositionAndRotation(_rig.rightControllerAnchor.position, Quaternion.LookRotation(fwd, Vector3.up));
            }

            var kb = Keyboard.current;
            if (kb == null) return;
            float step = 0.3f * Time.deltaTime;
            Vector3 d = Vector3.zero;
            if (kb[Key.J].isPressed) d.x -= step;
            if (kb[Key.L].isPressed) d.x += step;
            if (kb[Key.I].isPressed) d.z += step;
            if (kb[Key.K].isPressed) d.z -= step;
            if (kb[Key.U].isPressed) d.y -= step;
            if (kb[Key.O].isPressed) d.y += step;
            if (d != Vector3.zero) _workspace.Translate(d, Space.Self);
            if (kb[Key.P].wasPressedThisFrame) PlaceWorkspaceInFront();
        }

        void Next()
        {
            switch (_view)
            {
                case View.RoleSelect: SetView(View.PatientStart); break;
                case View.PatientStart: _careStart = Time.time; _cursor = 0; SetView(View.PatientStep); break;
                case View.PatientStep:
                    if (_cursor < _flat.Count - 1) { _cursor++; SetView(View.PatientStep); }
                    else SetView(View.PatientComplete);
                    break;
                case View.PatientComplete: SetView(View.RoleSelect); break;
                case View.NurseStart: SetView(View.NurseRecording); break;
                case View.NurseRecording: _recordLength = Time.time - _recordStart; SetView(View.NurseSaved); break;
                case View.NurseSaved: SetView(View.RoleSelect); break;
            }
        }

        void Back()
        {
            switch (_view)
            {
                case View.PatientStart: case View.NurseStart: SetView(View.RoleSelect); break;
                case View.PatientStep:
                    if (_cursor > 0) { _cursor--; SetView(View.PatientStep); }
                    else SetView(View.PatientStart);
                    break;
                case View.PatientComplete: _cursor = _flat.Count - 1; SetView(View.PatientStep); break;
                case View.NurseRecording: SetView(View.NurseStart); break;
                case View.NurseSaved: SetView(View.NurseRecording); break; // "Re-record"
            }
        }

        void Alt()
        {
            if (_view == View.RoleSelect) SetView(View.NurseStart);
            else if (_view == View.PatientStep) Repeat();
        }

        void Repeat()
        {
            // No text-to-speech yet: replaying is shown by the ghost "talking" again, or a pulse on the voice bar.
            if (_ghost != null) _ghost.Repeat();
            if (_voiceBar != null) StartCoroutine(PulseRoutine(_voiceBar));
        }

        string GhostLine(View v) => v switch
        {
            View.RoleSelect => "Hi! I'm your care guide. Who's wearing the headset today?",
            View.PatientStart => "Let's change your dressing together. First, check that your supplies are in view.",
            View.PatientStep => _data.steps[_flat[_cursor].step].actions[_flat[_cursor].action].voice,
            View.PatientComplete => $"All done, great job! I'll share this session with {_data.nurseName}.",
            View.NurseStart => "Show me how it's done and explain each step out loud. I'll turn it into a guide.",
            View.NurseRecording => "I'm watching and listening…",
            View.NurseSaved => "Got it! I'm breaking your recording into steps now.",
            _ => "",
        };

        System.Collections.IEnumerator PulseRoutine(Transform t)
        {
            for (float e = 0; e < 0.35f; e += Time.deltaTime)
            {
                if (t == null) yield break;
                t.localScale = Vector3.one * (1f + Mathf.Sin(e / 0.35f * Mathf.PI) * 0.05f);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        // ------------------------------------------------------------------ views

        void SetView(View v)
        {
            _view = v;
            Ui.Clear(_hud);
            _timer = _transcript = null;
            _voiceBar = null;
            _overlays.Clear();

            switch (v)
            {
                case View.RoleSelect: BuildRoleSelect(); break;
                case View.PatientStart: BuildPatientStart(); break;
                case View.PatientStep: BuildPatientStep(); break;
                case View.PatientComplete: BuildPatientComplete(); break;
                case View.NurseStart: BuildNurseStart(); break;
                case View.NurseRecording: BuildNurseRecording(); break;
                case View.NurseSaved: BuildNurseSaved(); break;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_hud);
            if (_ghost != null) _ghost.Say(GhostLine(v));
        }

        Image CenterPanel(float y = 0)
        {
            var p = Ui.Card(_hud, "Panel", 560, Theme.Panel, new RectOffset(36, 36, 34, 34), 18, 32);
            Ui.Place(p.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, y), 560);
            return p;
        }

        void Title(Transform parent, string text) => Ui.Text(parent, text, 34, Theme.Text, bold: true);
        void Sub(Transform parent, string text) => Ui.Text(parent, text, 18, Theme.Muted);

        void DoneIcon(Transform parent)
        {
            var row = Ui.Row(parent, "Icon row", 0);
            var c = Ui.Image(row.transform, "Done icon", Theme.PrimaryBg, Sprites.Circle());
            Ui.Fixed(c.gameObject, 60, 60);
            var check = Ui.Image(c.transform, "Check", Theme.Primary, Sprites.Check());
            Ui.Stretch(check.rectTransform);
            check.rectTransform.offsetMin = new Vector2(12, 12);
            check.rectTransform.offsetMax = new Vector2(-12, -12);
        }

        void BuildRoleSelect()
        {
            var p = CenterPanel();
            Ui.PillRow(p.transform, "Demo", Theme.PrimaryDark, Theme.PrimaryBg);
            Title(p.transform, "AI-Assisted Home Care");
            Sub(p.transform, "Who is wearing the headset?");
            Ui.Button(p.transform, "Caregiver / patient — guided care", true, () => SetView(View.PatientStart), "A");
            Ui.Button(p.transform, "Nurse — record an instruction", false, () => SetView(View.NurseStart), "X");
            Ui.Note(p.transform, "Hold the right trigger and move your hand to put the guidance on the burn.", Theme.PrimaryDark, Theme.PrimaryBg);
        }

        // ---------- caregiver / patient (VP1–VP5)

        void BuildPatientStart()
        {
            var p = CenterPanel();
            Ui.PillRow(p.transform, "Today's care", Theme.PrimaryDark, Theme.PrimaryBg);
            Title(p.transform, _data.procedureTitle);
            Sub(p.transform, _data.estimate);
            if (!string.IsNullOrEmpty(_data.updateNotice)) Ui.Note(p.transform, _data.updateNotice, Theme.Lav, Theme.LavBg);

            var box = Ui.Image(p.transform, "Supplies", Theme.Surface, Sprites.Rounded(22));
            Ui.VStack(box.gameObject, new RectOffset(20, 20, 16, 16), 10);
            Ui.Text(box.transform, "SUPPLIES IN VIEW", 13, Theme.Primary, bold: true).characterSpacing = 8;
            foreach (var s in _data.supplies)
            {
                var r = Ui.Row(box.transform, s.label, 10);
                Ui.Text(r.transform, s.label, 17, Theme.Text, bold: true);
                Ui.Pill(r.transform, s.found ? "Found" : "Not found yet", s.found ? Theme.PrimaryDark : Theme.Amber, s.found ? Theme.PrimaryBg : Theme.AmberBg, 13);
            }
            Ui.Button(p.transform, "Start", true, Next, "A");
            _overlays.Show(SupplyBoxes());
        }

        OverlayData[] SupplyBoxes()
        {
            var list = new List<OverlayData>();
            foreach (var o in _data.objects)
                if (o.id != "wound") list.Add(new OverlayData { type = "box", target = o.id, text = o.label });
            return list.ToArray();
        }

        void BuildPatientStep()
        {
            var (si, ai) = _flat[_cursor];
            var step = _data.steps[si];
            var action = step.actions[ai];

            // Step panel, top-left (Figma: 390 wide at 40,40).
            var panel = Ui.Card(_hud, "Step panel", 400, Theme.Panel, new RectOffset(26, 26, 24, 24), 14, 28);
            Ui.Place(panel.rectTransform, new Vector2(0, 1), new Vector2(40, -40), 400);
            var head = Ui.Row(panel.transform, "Head", 10);
            var label = Ui.Text(head.transform, $"STEP {si + 1} OF {_data.steps.Length}", 13, Theme.Primary, bold: true, wrap: false);
            label.characterSpacing = 8;
            if (step.updated) Ui.Pill(head.transform, "Updated", Theme.Lav, Theme.LavBg, 13);
            Ui.Text(panel.transform, step.title, 28, Theme.Text, bold: true);

            var progress = Ui.Row(panel.transform, "Progress", 6);
            for (int i = 0; i < _data.steps.Length; i++)
            {
                var seg = Ui.Image(progress.transform, "Seg", i <= si ? Theme.Primary : Theme.Border, Sprites.Rounded(4));
                Ui.Fixed(seg.gameObject, -1, 7);
                Ui.Flexible(seg.gameObject);
            }

            for (int i = 0; i < step.actions.Length; i++)
            {
                var row = Ui.Row(panel.transform, "Action", 12);
                StateIcon(row.transform, i < ai ? 0 : i == ai ? 1 : 2);
                Ui.Text(row.transform, step.actions[i].label, 19, i == ai ? Theme.Text : Theme.Muted, bold: i == ai);
            }
            Ui.Divider(panel.transform);
            Ui.Text(panel.transform, "Done when: " + step.expectedState, 16, Theme.Muted);

            if (_ghost == null) BuildVoiceBar(action.voice); // otherwise the ghost speaks it
            bool last = _cursor == _flat.Count - 1;
            BuildControls(("Back", false, (UnityEngine.Events.UnityAction)Back, "B"),
                          ("Repeat", false, Repeat, "X"),
                          (last ? "Finish" : "Next", true, Next, "A"));

            _overlays.Show(action.overlays);
        }

        void StateIcon(Transform parent, int state) // 0 done, 1 current, 2 to do
        {
            var icon = Ui.Image(parent, "State", state == 0 ? Theme.Primary : state == 1 ? Theme.Primary : Theme.Placeholder,
                                state == 0 ? Sprites.Circle() : Sprites.Ring(0.14f));
            Ui.Fixed(icon.gameObject, 26, 26);
            if (state == 0)
            {
                var c = Ui.Image(icon.transform, "Check", Theme.White, Sprites.Check());
                Ui.Stretch(c.rectTransform);
            }
            else if (state == 1)
            {
                var dot = Ui.Image(icon.transform, "Dot", Theme.Primary, Sprites.Circle());
                Ui.Stretch(dot.rectTransform);
                dot.rectTransform.offsetMin = new Vector2(8, 8);
                dot.rectTransform.offsetMax = new Vector2(-8, -8);
            }
        }

        void BuildVoiceBar(string text)
        {
            var bar = Ui.Image(_hud, "Voice instruction", Theme.White, Sprites.Rounded(36));
            bar.gameObject.AddComponent<VerticalGradient>();
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(28, 28, 20, 20);
            h.spacing = 18;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Leaves room on the right for the Back / Repeat / Next buttons.
            Ui.Place(bar.rectTransform, new Vector2(0.5f, 0), new Vector2(-200, 48), 640);

            var wave = Ui.Row(bar.transform, "Speaker", 4, TextAnchor.MiddleCenter);
            foreach (var hgt in new[] { 12, 24, 16, 28, 14 })
                Ui.Fixed(Ui.Image(wave.transform, "Bar", Theme.White, Sprites.Rounded(4)).gameObject, 5, hgt);
            Ui.Text(bar.transform, $"“{text}”", 22, Theme.White, bold: true);
            _voiceBar = bar.rectTransform;
        }

        void BuildControls(params (string label, bool primary, UnityEngine.Events.UnityAction action, string hint)[] buttons)
        {
            var row = Ui.Row(_hud, "Controls", 12, TextAnchor.MiddleRight);
            var fit = row.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Ui.Place((RectTransform)row.transform, new Vector2(1, 0), new Vector2(-40, 56));
            foreach (var b in buttons) Ui.Button(row.transform, b.label, b.primary, b.action, b.hint);
        }

        void BuildPatientComplete()
        {
            var p = CenterPanel();
            DoneIcon(p.transform);
            Title(p.transform, $"All {_data.steps.Length} steps done");
            Sub(p.transform, "This session was recorded for your nurse's weekly review.");
            int minutes = Mathf.Max(1, Mathf.RoundToInt((Time.time - _careStart) / 60f));
            Ui.Details(p.transform, new (string, string, Color?)[]
            {
                ("Time today", $"{minutes} min", null),
                ("Next dressing change", "Tomorrow", null),
            });
            Ui.Note(p.transform, _data.redFlags, Theme.Red, Theme.RedBg);
            Ui.Button(p.transform, "Done", true, Next, "A");
        }

        // ---------- nurse (VN1–VN3)

        void BuildNurseStart()
        {
            var p = CenterPanel();
            Ui.PillRow(p.transform, "New instruction", Theme.PrimaryDark, Theme.PrimaryBg);
            Title(p.transform, "Record a care instruction");
            Sub(p.transform, "Demonstrate the procedure as you normally would and explain each step out loud. The AI turns your recording into steps for the patient.");
            Ui.Details(p.transform, new (string, string, Color?)[]
            {
                ("Patient", _data.patientName, null),
                ("Procedure", _data.procedureTitle, null),
            });
            Ui.Note(p.transform, "Keep your hands and the supplies in view. Say conditions and warnings out loud.", Theme.PrimaryDark, Theme.PrimaryBg);
            Ui.Button(p.transform, "Start recording", true, Next, "A");
        }

        void BuildNurseRecording()
        {
            _recordStart = Time.time;
            _transcriptIndex = 0;

            // REC status, top-left.
            var rec = Ui.Image(_hud, "Recording status", Theme.Panel, Sprites.Rounded(30));
            var h = rec.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(24, 24, 14, 14);
            h.spacing = 14;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var fit = rec.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Ui.Place(rec.rectTransform, new Vector2(0, 1), new Vector2(40, -40));
            var dot = Ui.Image(rec.transform, "Rec dot", Theme.Red, Sprites.Circle());
            Ui.Fixed(dot.gameObject, 14, 14);
            dot.gameObject.AddComponent<Pulse>().amount = 0.25f;
            _timer = Ui.Text(rec.transform, "REC  00:00", 20, Theme.Text, bold: true, wrap: false);
            Ui.Fixed(Ui.Image(rec.transform, "Divider", Theme.Border).gameObject, 2, 22);
            Ui.Text(rec.transform, $"{_data.patientName} · {_data.procedureTitle}", 17, Theme.Muted, wrap: false);

            // Capture check, top-right.
            var ck = Ui.Card(_hud, "Capture check", 270, Theme.Panel, new RectOffset(22, 22, 18, 18), 10, 24);
            Ui.Place(ck.rectTransform, new Vector2(1, 1), new Vector2(-40, -40), 270);
            Ui.Text(ck.transform, "CAPTURE CHECK", 13, Theme.Primary, bold: true).characterSpacing = 8;
            foreach (var l in new[] { "Hands in view", "Voice is clear" })
            {
                var r = Ui.Row(ck.transform, l, 10);
                Ui.Fixed(Ui.Image(r.transform, "Dot", Theme.Primary, Sprites.Circle()).gameObject, 12, 12);
                Ui.Text(r.transform, l, 17, Theme.Text, bold: true);
            }

            // Live transcript, bottom.
            var tr = Ui.Card(_hud, "Live transcript", 640, Theme.Panel, new RectOffset(28, 28, 18, 20), 6, 30);
            Ui.Place(tr.rectTransform, new Vector2(0.5f, 0), new Vector2(-200, 48), 640);
            Ui.Text(tr.transform, "LIVE TRANSCRIPT", 13, Theme.Primary, bold: true).characterSpacing = 8;
            _transcript = Ui.Text(tr.transform, "", 20, Theme.Text);

            BuildControls(("Pause", false, null, null), ("Finish", true, (UnityEngine.Events.UnityAction)Next, "A"));
            _overlays.Show(SupplyBoxes());
            TickRecording();
        }

        void TickRecording()
        {
            if (_timer == null) return;
            float t = Time.time - _recordStart;
            _timer.text = $"REC  {(int)t / 60:00}:{(int)t % 60:00}";
            // Simulated speech-to-text: one sentence of the nurse's narration every 4 s.
            int idx = Mathf.Min((int)(t / 4f), _data.nurseTranscript.Length - 1);
            if (idx != _transcriptIndex || string.IsNullOrEmpty(_transcript.text))
            {
                _transcriptIndex = idx;
                _transcript.text = $"“…{_data.nurseTranscript[idx]}”";
            }
        }

        void BuildNurseSaved()
        {
            var p = CenterPanel();
            DoneIcon(p.transform);
            Title(p.transform, "Recording saved");
            Sub(p.transform, "The AI is decomposing your recording into care steps and an oversight checklist.");
            int secs = Mathf.RoundToInt(_recordLength);
            Ui.Details(p.transform, new (string, string, Color?)[]
            {
                ("Patient", _data.patientName, null),
                ("Length", $"{secs / 60}:{secs % 60:00}", null),
                ("Status", "Decomposing… about 1 min", Theme.Primary),
            });
            Ui.Note(p.transform, "Next: review and correct the steps in the mobile app. Changes sync to the patient as soon as you save.", Theme.Lav, Theme.LavBg);
            var row = Ui.Row(p.transform, "Actions", 12);
            Ui.Button(row.transform, "Re-record", false, Back, "B", stretch: true);
            Ui.Button(row.transform, "Done", true, Next, "A", stretch: true);
        }
    }
}
