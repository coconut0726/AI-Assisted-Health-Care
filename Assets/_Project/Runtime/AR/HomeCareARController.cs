using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using HomeCare.Procedures;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Templates.MR;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace HomeCare.AR
{
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(AfterStepCamera))]
    public sealed class HomeCareARController : MonoBehaviour
    {
        [Header("Procedure generated and reviewed in the Editor")]
        public TextAsset procedureJson;
        [Header("PC verification bridge (USB: adb reverse tcp:8765 tcp:8765)")]
        public string verificationEndpoint = "http://127.0.0.1:8765/verify";
        public string geminiModel = "gemini-3.8-flash";
        [Tooltip("Template UI/demo objects hidden only in this prototype scene.")]
        public GameObject[] hideTemplateObjects;

        ProcedureSession _session;
        ProcedurePanel _panel;
        AfterStepCamera _camera;
        CancellationTokenSource _lifetime;
        string _sessionDirectory;
        string _feedback = "Perform the instruction, then select Complete & Verify.";

        void Awake()
        {
            _lifetime = new CancellationTokenSource();
            foreach (var goal in FindObjectsByType<GoalManager>(FindObjectsInactive.Include))
                goal.enabled = false;
            // The copied rig also contains a template wrist menu. Hide its canvases without
            // disabling the controller/hand tracking objects or the shared EventSystem.
            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                canvas.enabled = false;
                foreach (var raycaster in canvas.GetComponents<BaseRaycaster>()) raycaster.enabled = false;
            }
            if (hideTemplateObjects != null)
                foreach (var item in hideTemplateObjects) if (item != null) item.SetActive(false);
            var manager = FindAnyObjectByType<OVRManager>();
            if (manager != null) manager.isInsightPassthroughEnabled = true;
        }

        IEnumerator Start()
        {
            _camera = GetComponent<AfterStepCamera>();
            _panel = new ProcedurePanel(transform, CompleteAndVerify, NextStep, RestartProcedure, RecenterPanel);
            try
            {
                if (procedureJson == null) throw new InvalidOperationException("Assign a reviewed procedure JSON in the Inspector.");
                _session = new ProcedureSession(RequirementsJson.Parse(procedureJson.text));
                CreateSessionDirectory();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _feedback = "The procedure could not be loaded. Check the procedure setup before starting.";
            }
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && FindAnyObjectByType<OVRPassthroughLayer>() == null)
                rig.gameObject.AddComponent<OVRPassthroughLayer>();
            Render();
            // Let head tracking establish the initial pose before positioning the world-space panel.
            yield return null;
            yield return null;
            RecenterPanel();
        }

        Camera ViewCamera()
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            return rig != null && rig.centerEyeAnchor != null
                ? rig.centerEyeAnchor.GetComponent<Camera>() : Camera.main;
        }

        public void RecenterPanel()
        {
            var view = ViewCamera();
            if (view != null)
            {
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = Color.clear;
            }
            _panel?.Recenter(view);
        }

        public async void CompleteAndVerify()
        {
            if (_session == null || (_session.State != ProcedureSessionState.Ready && _session.State != ProcedureSessionState.NeedsRetry)) return;
            _session.BeginVerification();
            _feedback = "Capturing the result. Keep the relevant objects in view.";
            Render();
            try
            {
                var cancellation = _lifetime.Token;
                byte[] jpeg = await _camera.CaptureJpegAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                string attempt = Guid.NewGuid().ToString("N").Substring(0, 10);
                string prefix = _camera.IsEditorTest ? "editor_test" : "headset";
                string basename = prefix + "_step" + (_session.StepIndex + 1) + "_" + attempt;
                string imagePath = Path.Combine(_sessionDirectory, basename + ".jpg");
                File.WriteAllBytes(imagePath, jpeg);
                var attachments = new[] { new EvidenceAttachment { scope = "after_step", path = imagePath } };
                string request = GeminiVerificationProtocol.CreateAfterImageRequest(_session.Procedure,
                    _session.CurrentStep, jpeg, Path.GetFileName(imagePath), geminiModel);
                _feedback = "Checking the result...";
                Render();
                var response = await VerificationBridgeClient.VerifyAsync(verificationEndpoint, request, cancellation);
                cancellation.ThrowIfCancellationRequested();
                _session.AcceptVerification(response, attachments);
                _feedback = ResultText();
                var report = new VerificationReport { verified_at_utc = DateTime.UtcNow.ToString("o"),
                    provider = "Gemini", model = geminiModel, procedure = _session.Procedure,
                    step_id = _session.CurrentStep.id, attachments = attachments,
                    overall_status = _session.OverallStatus, results = _session.Results };
                try { File.WriteAllText(Path.Combine(_sessionDirectory, basename + ".json"), RequirementsJson.Serialize(report)); }
                catch (IOException exception) { Debug.LogWarning("Verification succeeded but its report could not be saved: " + exception.Message, this); }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                _session.FailVerification();
                _feedback = "Could not complete verification. Check the camera and connection, then try again.";
                Debug.LogException(exception, this);
            }
            if (this != null) Render();
        }

        string ResultText()
        {
            var text = new StringBuilder();
            text.AppendLine(_session.CanAdvance ? "Passed. You can continue." :
                _session.OverallStatus == RequirementVerification.NotSatisfied ? "Some checks were not satisfied. Review them and try again." :
                "More evidence is needed. This step has not passed.");
            for (int i = 0; i < _session.Results.Length; i++)
            {
                var result = _session.Results[i];
                var requirement = _session.CurrentStep.requirements[i];
                string state = result.status == RequirementVerification.Satisfied ? "PASS" :
                    result.status == RequirementVerification.NotSatisfied ? "NOT MET" : "NOT VERIFIED";
                text.Append("\n").Append(state).Append(": ").AppendLine(requirement.description);
                if (requirement.evidence_scope != "after_step")
                    text.AppendLine("This check needs evidence from before or during the action, or another observation method.");
                text.AppendLine(result.reason);
            }
            return text.ToString();
        }

        public void NextStep()
        {
            if (_session == null || !_session.CanAdvance) return;
            _session.Advance();
            _feedback = _session.State == ProcedureSessionState.Completed
                ? "All steps have passed. Procedure complete." : "Perform the instruction, then select Complete & Verify.";
            Render();
        }

        public void RestartProcedure()
        {
            if (_session == null || _session.State == ProcedureSessionState.Verifying) return;
            _session.Restart();
            CreateSessionDirectory();
            _feedback = "New run. Perform the instruction, then select Complete & Verify.";
            Render();
        }

        void CreateSessionDirectory()
        {
            _sessionDirectory = Path.Combine(Application.persistentDataPath, "HomeCareSessions",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_sessionDirectory);
        }

        void Render()
        {
            if (_panel == null) return;
            if (_session == null)
            {
                _panel.Set("Home Care", "Procedure unavailable", _feedback, false, false, false, false, false);
                return;
            }
            bool completed = _session.State == ProcedureSessionState.Completed;
            bool busy = _session.State == ProcedureSessionState.Verifying;
            string progress = _session.Procedure.procedure_title + "  |  " + (_session.StepIndex + 1) + " / " + _session.Procedure.steps.Length;
            string instruction = completed ? "Procedure complete" : _session.CurrentStep.title + "\n" + _session.CurrentStep.instruction;
            _panel.Set(progress, instruction, _feedback, busy, !completed && !_session.CanAdvance,
                _session.CanAdvance, completed, _session.StepIndex == _session.Procedure.steps.Length - 1);
        }

#if UNITY_EDITOR
        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.enterKey.wasPressedThisFrame) CompleteAndVerify();
            if (keyboard.nKey.wasPressedThisFrame) NextStep();
            if (keyboard.rKey.wasPressedThisFrame) RestartProcedure();
            if (keyboard.cKey.wasPressedThisFrame) RecenterPanel();
        }
#endif

        void OnDestroy()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
        }
    }
}
