# Home Care Requirements Prototype

This project explores an AI-assisted augmented reality system for home healthcare procedure guidance and verification. The system converts urse-provided instructions into structured procedural requirements, provides step-by-step AR guidance, and evaluates user execution.

# Key Features
- Enable displaying step-by-step instructions through a Meta Quest headset.
- Enable generating and manually refining structured procedural requirements from nurse instructions.
- Enable capturing an image after step completion and using VLMs to evaluate procedural requirements.
- Enable managing verification results, retries, and progression through a structured session state machine.

# Progress
Version_1: Focuses on after-step verification, allowing users to review feedback and retry a step before proceeding.

# Testing
### 1. Start Verification Bridge

Open PowerShell:

```
$projectRoot = "D:\Projects\AI-Assisted-Health-Care"
$env:GEMINI_API_KEY = Read-Host "Enter Gemini API Key"
python (Join-Path $projectRoot "Tools\verification_bridge.py")
```

Check: http://127.0.0.1:8765/health

Expected response:

```
{"ok": true, "key_configured": true}
```

### 2. Unity Editor Testing

1. **Tools → Home Care → Open AR Prototype Scene**.
2. Keep the default `procedureJson` selected.
3. Assign a test image to `AfterStepCamera.editorTestImage`.
4. Enter Play Mode.

| Key | Action |
| --- | --- |
| `Enter` | Complete & Verify |
| `N` | Next Step (only if passed) |
| `R` | Restart Procedure |
| `C` | Recenter Panel |

### 3. Meta Quest Testing

Connect Quest via USB and run:

```
adb devices
adb reverse tcp:8765 tcp:8765
adb reverse --list
```

1. Keep the PC verification bridge running.
2. Build and launch the Unity application on Quest.
3. Follow the AR instructions and click **Complete & Verify** to capture an image using Meta's Passthrough Camera.
4. Review the results, then proceed or retry.

### 1.4 Generate Custom Procedure Requirements

1. **Tools → Home Care → Requirements Prototype**.
2. Enter the nurse's instructions → **Generate Step Requirements**.
3. **Open Edit JSON** → Review and modify → **Apply** → Save locally.
4. Replace `procedureJson` in the AR prototype scene with the saved JSON.
5. Enter Play Mode to test the custom procedure.


# Implementation
### 1. System Architecture

```
Procedure JSON
      ↓
HomeCareARController
      ↓
ProcedureSession (StepIndex, State)
      ↓
ProcedurePanel (AR Instructions)
      ↓
User Completes Step
      ↓
AfterStepCamera (JPEG)
      ↓
GeminiVerificationProtocol (JSON Request)
      ↓
VerificationBridgeClient
      ↓
PC Python Bridge → Gemini API
      ↓
VerificationResponse
      ↓
RequirementVerification
      ↓
Passed / NeedsRetry
      ↓
Next Step / Retry
```

### 2.Initialization

`HomeCareARController.Start()` creates a `ProcedureSession`:

- `Validate()` — validate the procedure JSON.
- `Serialize()` + `Parse()` — create an independent copy.
- `Restart()` — initialize `StepIndex = 0`, `State = Ready`.
- `Render()` — display the current instruction through `ProcedurePanel`.

The panel is positioned relative to `OVRCameraRig.centerEyeAnchor` and can be manually recentered.

### 3. Complete & Verify

When the user clicks **Complete & Verify**:

**1. Start Verification**

`BeginVerification()` → `State = Verifying`.

**2. Capture Image**

`AfterStepCamera.CaptureJpegAsync()`:

- Unity Editor: use `editorTestImage`.
- Meta Quest: use `PassthroughCameraAccess` (left camera, requested 1280 × 960, max 30 FPS).
- Wait for a fresh frame → `GetTexture()` → `EncodeToJPG(85)`.

**3. Send to Gemini**

- `CreateAfterImageRequest()` — package the current step, requirements, prompt, image, and response schema.
- `VerifyAsync()` — HTTP POST to `127.0.0.1:8765/verify`.
- ADB Reverse forwards the request to the PC.
- `verification_bridge.py` adds the API key and forwards it to Gemini.
- `ParseInteraction()` extracts the structured `VerificationResponse`.

**4. Evaluate Requirements**

`ProcedureSession.AcceptVerification(response, evidence)`:

- `Evaluate()` — evaluate each requirement using the response and evidence.
- `Aggregate()` — determine the overall verification status.
- Update the session state:

| State | Behavior |
| --- | --- |
| `Passed` | `CanAdvance = true`; enable Next Step |
| `NeedsRetry` | `CanAdvance = false`; display feedback and allow re-verification |

**5. Advance or Retry**

- **Passed:** User clicks Next → `Advance()` increments `StepIndex`, resets state to `Ready`, and calls `Render()` for the next instruction.
- **Last step passed:** `Advance()` sets `State = Completed` without incrementing `StepIndex`.
- **NeedsRetry:** User corrects the operation and clicks Complete & Verify again.