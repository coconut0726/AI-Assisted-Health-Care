# Home Care Requirements Prototype

This project explores an AI-assisted augmented reality system for home healthcare procedure guidance and verification. The system converts urse-provided instructions into structured procedural requirements, provides step-by-step AR guidance, and evaluates user execution.

# Start Preperation

The main workflow is **VRGuidanceDemo + Quest Link + local Qwen/API**. Follow steps 1–3 below using the reviewed procedure already assigned to the scene. The launcher prepares missing local-model dependencies automatically; Unity and Quest Link must already be installed.

### 1. Connect the local VLM or API

1. Open **AHC → VLM Backend** in Unity.
2. Select **Qwen** (`qwen3-vl:4b-instruct`) / select **Gemini** (enter your model ID and API key) and click **Apply & Start**.
3. Wait for **Applied** and confirm **Running** shows `ollama / qwen3-vl:4b-instruct`. Unity is now connected to the local model.

### 2. Connect the headset through Quest Link

Start a working Quest Link session on the same PC. Use OpenXR for the real headset path and disable XR Simulation for that path. Prepare the headset camera access required by the Link setup. Unity runs in the Editor and appears in the headset. There is no APK installation in this workflow.

### 3. generate a custom procedure

1. Exit Play mode, open **AHC → Requirement Generation**, and click **Legacy requirements editor…** for this original demo.
2. In **Author**, enter the nurse's instruction, keep **ProjectBackend** selected to follow the VLM Backend window, and click **Generate step requirements**. Explicit Gemini/Claude/LocalOllama overrides remain available.
3. Review the steps, requirements, VQA questions, evidence scopes, and source quotations. In **3. Edit JSON**, apply any corrections with **Validate and apply JSON**.
4. Return to **1. Author** and click **Save requirements**. Save under `Assets/_Project/Content/Procedures/` so Unity imports the JSON as a TextAsset.

# V3: Periodical Sampling with LocalVLM (`VRGuidanceDemo_V3.unity`)

V3 adds **periodical sampling** and **Action-level verification**. 

1. In **AHC → VLM Backend**, select **Qwen** and click **Apply & Start**.
2. Open **AHC → Open V3 Demo**, or `Assets/_Project/Scenes/VRGuidanceDemoActions.unity`, then Play and **Begin**.
3. The app first checks the current Action's applicability and preconditions, then checks its completion requirements and applicable execution rules together. New polling slots occur every **1 second**; a busy request skips slots without a queue. Model inference can take much longer than one second.
4. **Next Action** requires all active checks confirmed and graph prerequisites resolved. Unknown never passes. Checks continue until Next; a failed fresh check removes an earlier model pass. Recorded execution-rule violations remain blocked until an explicit retry, and global violations require a new procedure run.

Conditional applicability uses its own VLM verification request: the Action's trigger and one current image are sent with `phase = Applicability`, before any pre/postcondition or error checks. If the result is `not_applicable`, the UI shows **NOT APPLICABLE**, displays the trigger and offers **Skip Action** or **Recheck condition**. On `ActionGuidanceSession`, enable **Auto Skip Not Applicable** to advance automatically once the normal progression gate permits it; it is off by default. Manual and automatic skipping use the same progression path, record `not_applicable` rather than a successful condition check, and leave a notice naming the skipped Action. 

**Current verification logic:** Current Action → preconditions satisfied → postconditions and error criteria checked together → Next Action. Conditional Actions first require applicability confirmation; Actions without preconditions enter postcondition/error checks directly. The Session maintains the current Action rather than recognizing it from the image. Passed preconditions are recorded and are not continuously rechecked in the next phase. Actions with no image postcondition use **Confirm Action completed** before **Next Action**; they never pass automatically because their check list is empty. Each verification request contains the task goal, current Step and Action, current phase and its active requirements, the established Action applicability, one current RGB frame with its capture time, and run/request/Step/Action identifiers. 

# V2: Hands-only care guidance (`VRGuidanceDemo_V2.unity`)

Code: `Assets/_Project/Runtime/CareGuidance/`. Procedure: `Content/Procedures/burn_care_procedure.json` (minor burn dressing change). Rebuild the scene objects with **Tools → Home Care → Set Up Care Guidance V2 Scene**.

| Gesture (headset) | Action |
| --- | --- |
| Point the hand ray at a panel button and pinch | Press it (the ray turns blue on a button) |
| Touch a panel button with your index fingertip | Press it (up close, the ray hides) |
| Pinch or make a fist next to an item | Pick it up (release by opening the hand) |
| Left-hand pinch in empty air for ~1 s | Bring the panel in front of you |

The dot on each index fingertip shows the hand is tracked. The top-right of the panel shows the hand-tracking state (`not seen` / `tracked` / `pinch` / `fist`), and the log prints finger curl and pinch distances every 3 s for tuning the thresholds in `HandTracking`.

On the headset, start the local bridge with USB forwarding before using **Complete & Verify** or item guidance: `powershell -ExecutionPolicy Bypass -File Tools/Start-LocalVlm.ps1 -Usb -AdbPath <path to adb.exe>`. Editor + Quest Link uses **Start-LocalVlm.cmd** without USB forwarding.

- Items that the current step mentions (keyword match on the step text against each `CareItem`) get a blue box; mentioned items missing from the scene are listed as "not found".
- Picking up an item asks Gemini (through the same PC bridge) how to use it for this step: text on the panel, a green box on the held item, and an amber box + arrow to its target. Without the bridge, it falls back to the step text.
- **Complete & Verify** runs the verification-test flow. In the Editor with no test image, it captures the virtual camera view.
- Every session writes `session_report.json` and `session_report.md` (plus per-attempt images and JSON) to `Application.persistentDataPath/HomeCareSessions/<session>/`. **Save report** on the panel (or `S` in the Editor) saves it on demand.
- Editor keys: `Enter` verify · `N` next · `R` restart · `C` recenter · `S` save report. The mouse clicks buttons and drags items.

## Full demo: guided care with detection and verification (`VRGuidanceDemo.unity`)

The caregiver flow uses the reviewed procedure assigned to `RealCareSession` (the saved scene currently uses the 4-step simple wound dressing case), camera-based requirement verification, and a session report. The nurse flow is still the scripted walkthrough. The saved scene is already configured; no setup menu is needed.

1. In **AHC → VLM Backend**, choose **Qwen** for LocalVLM or **Gemini**, then click **Apply & Start**. The demo follows this shared selection; it has no separate scene-level model choice. Connect Quest Link and open `Assets/_Project/Scenes/VRGuidanceDemo.unity`, then enter Play mode. Choose **Caregiver / patient**.
2. **Supplies on the table**: automatic supply detection is off in the saved scene. **Look again** runs it on demand; **Start** begins the procedure without waiting for it.
3. Each step: read the instruction and perform it with the work area in view. After 1 second, and at subsequent 1-second polling slots by default, the app captures a fresh image and sends only the current step's postconditions for verification. Pass / Not met / Not sure are shown per condition. Preconditions and execution constraints remain in the source procedure but are outside this demo's runtime checks. A step without any postconditions is rejected on load. A failed, uncertain or failed-network check is retried at later slots. A running capture, verification or object search skips the slot; requests are never stacked to catch up. Model latency therefore determines the actual result frequency.
4. Once every current postcondition passes in one check, the attempt is saved and the app automatically advances the UI and speaks the next instruction once. The new step starts a fresh polling interval; the final pass opens the completion screen and stops polling. Automatic retry feedback appears on screen without interrupting the step's spoken instruction. **Check step** / **Check again** still performs a manual check with the existing photo countdown and spoken feedback; a manual pass also advances automatically. **Skip** remains a presenter override and is recorded as skipped. Home, application pause and disabling the component cancel step work; old replies cannot update a new step or restarted session. Resuming waits a fresh interval.
5. The completion screen shows the care note and report path (`persistentDataPath/HomeCareSessions/<session>/session_report.md`). Each completed verification attempt records its image, requirement results, `trigger` (`automatic_poll` / `manual`), and `duration_ms` (capture/countdown through result processing).

On **AHC VR Demo → RealCareSession**, **Auto Verify** enables polling and **Verification Interval** is exposed in the Inspector, defaults to **1 second**, and accepts values of **0.1 seconds or more**. Researchers can change it before or during Play; changing it during Play restarts the polling timer using the new interval. Select Gemini or LocalVLM in **AHC → VLM Backend**; the shared bridge chooses the actual provider/model and the verification reports record the returned provenance. The saved scene uses the real passthrough camera over Link (`AfterStepCamera.captureViewCameraInEditor = false`) and hides placeholder props. To test without a headset, explicitly assign **Editor Test Image** or enable **Capture View Camera In Editor**. Automatic supply detection, per-step object detection and touch-triggered relocalization are off for this baseline; **Look again** / **Find items** remain available manually.

This baseline borrows the periodic decision cadence from [RegulAR](https://arxiv.org/abs/2608.26715). RegulAR uses a 1 fps frame window and graph state in its 5-second decision cycle. Here, each check uses one fresh image with `after_step` evidence; it does not accumulate a video window or implement graph-based action recognition. `before_step`, `whole_step`, or nonvisual requirements remain unknown when the evidence is insufficient; repeated snapshots cannot establish that a technique or constraint held throughout the step.

Run `pwsh -NoProfile -File Tools/Test-RequirementsPrototype.ps1` for the requirement, step progression and polling checks without making API calls. The polling checks also appear under **Tools → Home Care → Run Verification Polling Checks**.

| Hands | Controllers | Editor |
| --- | --- | --- |
| Hand ray + pinch, or touch a button | A check step / next · B skip (after a failed check) · X find items · Y home | → / Space next · ← back/skip · R find items · H home |

**Care cases.** **AHC → Care Case → Simple Wound Dressing (prop test)** switches the flow to `wound_dressing_procedure.json`: 4 steps (5 checks) a patient can do at home with 4 props: gloves, the practice skin pad (the wound), non-stick pads and a zip bag. A procedure file may list its own `supplies` and a `completion_note`. **AHC → Care Case → Burn Dressing** switches back.

**UI stays where it appears.** The panels are placed once, in front of the first tracked view, and do not follow the head. Labels, cards and the ghost's speech bubble turn toward the user once, when they appear, and then stay fixed.

**Voice (Meta Voice SDK + Wit.ai, free).** The guide reads its lines aloud, and you can talk to it: press **V** (Editor), click the left thumbstick, or press **Talk**, then say one sentence: "next" / "check", "find the scissors", "repeat", "skip", or "home". Setup: create a Wit.ai app (English), paste its Server Access Token in **Meta → Voice SDK → Get Started**, then run **AHC → Set Up Voice (Meta Voice SDK)**. `ProjectSettings/wit.config` holds that token and is git-ignored. Do not commit it.

The PC bridge handles one request at a time. The app waits for it (up to 60 s) instead of failing. In the Editor, turn off the console's **Error Pause**: a Pipeline/CLI timeout logs an error, and Error Pause then freezes Play mode. Screenshots of an Editor run are in `Reports/full_demo/`.

## Real-object detection (Find items)

Code: `Assets/_Project/Runtime/Detection/`. Added automatically to the V2 controller.

1. **Find items** on the panel (or `F` in the Editor) captures the passthrough frame, its camera pose, and a grid of depth samples (Quest depth API via `EnvironmentRaycastManager`).
2. Through the same PC bridge, Gemini rewrites the request into a short key-object phrase (prompt enhancement from the open-set detection study), returns `box_2d = [y_min, x_min, y_max, x_max]` (0–1000) for each object, and picks a visual type plus guidance text for the current step.
3. Each box becomes a 3D box in the room. Objects smaller than 5 cm get a pulsing dot instead. `move_to_target` / `apply_to_target` adds an arrow and a ghost box that moves to the target, and `wait` adds a countdown.

The request defaults to the items the current step names. `FindItems("any object name")` takes free text, so voice input can call it later. Inspector (`ObjectDetectionGuidance`): `testPrompt` (used by `F`), `findOnNewStep`, `defaultDistance` (used when there is no depth), `minBoxEdge`. Each request is saved in the session report with its image (`detect_step*.jpg`), boxes, 3D position, and depth source. The headset asks for the spatial data permission (`USE_SCENE`) for depth. Without it, boxes are placed at `defaultDistance`.
