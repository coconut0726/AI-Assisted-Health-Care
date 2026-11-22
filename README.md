# Home Care Requirements Prototype

This project explores an AI-assisted augmented reality system for home healthcare procedure guidance and verification. The system converts urse-provided instructions into structured procedural requirements, provides step-by-step AR guidance, and evaluates user execution.

# Key Features
- Enable displaying step-by-step instructions through a Meta Quest headset.
- Enable generating and manually refining structured procedural requirements from nurse instructions.
- Enable capturing an image after step completion and using VLMs to evaluate procedural requirements.
- Enable managing verification results, retries, and progression through a structured session state machine.

# Progress
Version_1: Focuses on after-step verification, allowing users to review feedback and retry a step before proceeding.
Version_2: Add local lightweight model(Qwen3-VL-4B) for quick verification and periodical sampling module. 

# V3: Periodical Sampling with Lightweight VLM  

The main workflow is **VRGuidanceDemo + Quest Link + local Qwen**. Follow steps 1–4 below using the reviewed procedure already assigned to the scene. The launcher prepares missing local-model dependencies automatically; Unity and Quest Link must already be installed.

### 1. Connect the local VLM

1. Open **Tools → Home Care → VLM Backend** in Unity.
2. Select **Qwen** (`qwen3-vl:4b-instruct`) and click **Apply & Start**.
3. Wait for **Applied** and confirm **Running** shows `ollama / qwen3-vl:4b-instruct`. Unity is now connected to the local model.

First use requires internet access to download missing dependencies and model files automatically.

To use Gemini instead, select **Gemini**, enter your model ID and API key, and click **Apply & Start**.

### 2. Connect the headset through Quest Link

Start a working Quest Link session on the same PC. Use OpenXR for the real headset path and disable XR Simulation for that path. Prepare the headset camera access required by the Link setup.

Unity runs in the Editor and appears in the headset. There is no APK installation in this workflow.

### 3. Open the configured main scene

Open `Assets/_Project/Scenes/VRGuidanceDemo.unity` and enter Play mode. Choose **Caregiver / patient**.

The saved scene is already configured with:

| Setting | Saved value |
| --- | --- |
| `RealCareSession.procedureJson` | `wound_dressing_procedure.json`: 4 steps, 5 checks |
| `autoVerify` / `verificationInterval` | Enabled / 5 seconds |
| `AfterStepCamera.editorTestImage` | Empty |
| `captureViewCameraInEditor` | Disabled, requesting real passthrough over Link |
| Editor placeholder props | Disabled |
| Automatic supply detection, per-step object finding, touch-triggered relocalization | Disabled |

No setup menu is needed for the saved main scene. On the supply screen, **Look again** optionally checks supplies; **Start** begins the procedure without requiring a supply check.

### 4. Perform the procedure and review verification

Press **Start** on the supply screen. The current instruction and its requirements appear on the headset.

1. Perform the step while keeping the relevant work area in view.
2. After five seconds, the app captures a fresh image and sends the current step's requirements for verification. Further polling slots occur at five-second intervals.
3. The panel shows **Pass / Not met / Not sure** per requirement. Failed or uncertain checks keep the step active for a later attempt. Request errors also leave the step available for retry.
4. Once every requirement passes, polling stops and **Next** or **Finish** unlocks. Click it to advance or complete the procedure. A new step starts a fresh interval.

The interval controls when a request may start, not how quickly Qwen returns an answer. Slots are skipped while capture, verification, or an object search is busy; missed slots do not become queued requests.

**Check step / Check again** still performs a manual check with the photo countdown. Automatic checks skip that countdown. **Skip**, available after an unsuccessful check, is a presenter override and is recorded as skipped. Home, application pause, and disabling the component cancel step work; stale replies cannot update another step or a restarted session. Resuming waits a fresh interval.

| Input | Main-scene action |
| --- | --- |
| Hand ray + pinch, or fingertip touch | Press a panel button |
| Controller A / Editor Right Arrow, Space, or Enter | Start, check manually, or advance, depending on screen/state |
| Controller B / Editor Left Arrow or Backspace | Skip after an unsuccessful check; Home from the supply screen |
| Controller X / Editor R | Look for supplies or find the current step's items |
| Controller Y / Editor H or Escape | Home |

### 5. generate a custom procedure

This is a preparation workflow. Skip it when using the procedure already assigned to the scene. Start the selected backend using step 1 before generating.

1. Exit Play mode and open **Tools → Home Care → Requirements Prototype**.
2. In **1. Author**, enter the nurse's instruction, keep **ProjectBackend** selected to follow the VLM Backend window, and click **Generate step requirements**. Explicit Gemini/Claude/LocalOllama overrides remain available.
3. Review the steps, requirements, VQA questions, evidence scopes, and source quotations. In **3. Edit JSON**, apply any corrections with **Validate and apply JSON**.
4. Return to **1. Author** and click **Save requirements**. Save under `Assets/_Project/Content/Procedures/` so Unity imports the JSON as a TextAsset.
5. Select **AHC VR Demo** in `VRGuidanceDemo` and assign that asset to **RealCareSession → Procedure Json**. Save the scene outside Play mode.
6. Continue with Testing steps 2–4 to run the custom procedure.

The same window's **2. Verify** tab supports manually selected before/after images for a selected step. Local VQA explicitly rejects whole-step MP4 video; it does not discard video or relabel it as after-step evidence.

The older **Tools → Home Care → Decompose Instruction** window generates the earlier `steps → subgoals → actions` schema. Use **Requirements Prototype** for the requirement JSON consumed by `RealCareSession`.

To switch between included care cases, use **AHC → Care Case → Simple Wound Dressing (prop test)** or **Burn Dressing**. These menus change the main scene's assigned procedure.



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

1. Double-click **Start-LocalVlm.cmd** for local Ollama + Editor/Quest Link (for an Android build over USB, run `Tools/Start-LocalVlm.ps1 -Usb`). Connect Quest Link and open `Assets/_Project/Scenes/VRGuidanceDemo.unity`, then enter Play mode. Choose **Caregiver / patient**.
2. **Supplies on the table**: automatic supply detection is off in the saved scene. **Look again** runs it on demand; **Start** begins the procedure without waiting for it.
3. Each step: read the instruction and perform it with the work area in view. After 5 seconds, and at subsequent 5-second polling slots, the app captures a fresh image and sends the current step's requirements for verification. Pass / Not met / Not sure are shown per requirement. A failed, uncertain or failed-network check is retried at later slots. A running capture, verification or object search skips the slot; requests are never stacked to catch up. Model latency therefore determines the actual result frequency.
4. Once every requirement passes, automatic checking stops and **Next** / **Finish** unlocks. Advancing starts a fresh interval for the new step. **Check step** / **Check again** still performs a manual check with the existing photo countdown. **Skip** remains a presenter override and is recorded as skipped. Home, application pause and disabling the component cancel step work; old replies cannot update a new step or restarted session. Resuming waits a fresh interval.
5. The completion screen shows the care note and report path (`persistentDataPath/HomeCareSessions/<session>/session_report.md`). Each completed verification attempt records its image, requirement results, `trigger` (`automatic_poll` / `manual`), and `duration_ms` (capture/countdown through result processing).

On **AHC VR Demo → RealCareSession**, **Auto Verify** enables polling and **Verification Interval** defaults to 5 seconds. The saved scene uses the real passthrough camera over Link (`AfterStepCamera.captureViewCameraInEditor = false`) and hides placeholder props. To test without a headset, explicitly assign **Editor Test Image** or enable **Capture View Camera In Editor**. Automatic supply detection, per-step object detection and touch-triggered relocalization are off for this baseline; **Look again** / **Find items** remain available manually.

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

### Stand-alone test scene (`3DGuidanceTestScence.unity`)

Tests detection without the procedure flow. Rebuild it with **Tools → Home Care → Set Up 3D Guidance Test Scene**. It contains an OVRCameraRig, stand-in supplies (Editor only, hidden on the headset), and `ObjectDetectionTestRig`.

- In the Inspector, set `stepInstruction` (the step the guidance follows) and `prompts` (one panel button each).
- Editor: keys `1`–`4` run the prompts, `F` runs `testPrompt`, `X` clears, `C` moves the panel. Editor captures leave out the UI layer, as the headset camera does.
- Every request is saved as an image and JSON (boxes, 3D size, depth source, latency) in `persistentDataPath/HomeCareDetectionTests/`.
- The bridge handles one request at a time, so wait for a result (about 10–25 s) before pressing the next prompt.
