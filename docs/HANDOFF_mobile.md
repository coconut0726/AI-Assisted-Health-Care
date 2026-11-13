# Handoff: mobile app (nurse + patient)

Read this first when continuing the mobile work on the Mac.

## Goal

Make a real iPhone app for the nurse and the caregiver/patient. Data has to round-trip between the headset and the phone:

```
VR (nurse records an instruction)
  → AI breaks it into steps
  → server (a database on the Windows PC stands in for the cloud for now)
  → mobile app: nurse reviews and edits, then saves
  → server keeps the new version
  → VR loads the latest version at the start of the next care session
```

Not built yet: uploading from VR to the server. Build the server and the mobile side first. The PC database only simulates the cloud; later, only the server address should change.

## Who does what

| Machine | Work |
|---|---|
| Windows PC | Unity project, Quest 3 (USB + `adb reverse`), the local server/database |
| Mac | Mobile app development, testing on the iPhone (same Wi‑Fi as the PC) |

The iPhone reaches the PC server through the PC's LAN IP. The Windows firewall must allow the server's port.

## Design (do this before code)

Figma: https://www.figma.com/design/GU8zPMFbqZ5GcQEkh7LylF/Assisted-Home-Care (page **Mobile**)

- Nurse screens N1–N9, patient screens P1–P4, splash S0.
- **Done:** the tactic and guidance tags ("Command", "Movement: wet", "Contextual widget"…) were removed from the care steps on N2, N4, N5 and P1. The nurse doesn't need them; the program keeps them internally.
- The user is happy with the rest of the design, including the Oversight Checklist and the weekly report screens. Rule from the user: **change Figma first, then implement.**

## Existing code to reuse

- `Mobile/`: React + Vite web demo of all the Figma screens (no backend; demo data in `src/data/demo.ts`). Good reference for screens, styles (`src/styles.css` tokens), and the data model (`src/types.ts`). It still shows the tags that were removed from Figma.
- `DemoVideo/`: Remotion video that renders `Mobile/` screens. It does not have to stay in sync.
- A first attempt that changed the UX without updating Figma is on the local Windows branch `mobile-web-old-ux`. Do not use it.

## App technology: not decided yet

- Recommended: **Expo (React Native)**. It reuses React/TypeScript. Run it on the iPhone with **Expo Go**, or build it with Xcode on the Mac (a free Apple ID allows a 7-day install on your own phone).
- Alternative: keep the web app and install it as a PWA (Safari → Add to Home Screen).
- Ask the user before starting.

Mac setup: Xcode (App Store), Node.js LTS, Git, and Expo Go on the iPhone.

## Data formats on the headset side

**Procedure JSON**: what the headset runs. Example: `Assets/_Project/Content/Procedures/wound_dressing_procedure.json`.

```
procedure_title, instruction,
steps[]: { id, title, instruction, source_span,
           requirements[]: { id, category, description, source_span,
                             vqa_question, evidence_scope, observation_hint } },
supplies[]: { itemId, displayName, keywords[] },
completion_note
```

- Loaded by `Assets/_Project/Runtime/VRDemo/RealCareSession.cs`, which also reads `supplies` and `completion_note`.
- The case is switched in the Unity menu **AHC → Care Case**.

**Session report**: written by the headset after each session.

- File: `session_report.json`, under `persistentDataPath/HomeCareSessions/<session>/`.
- Shape: `Assets/_Project/Runtime/CareGuidance/SessionReport.cs`. Each step has attempts, and each attempt has per-check results (`satisfied` / `not_satisfied` / `unknown`) with reasons and photo files.
- Step `final_status` is one of `passed`, `not_met`, `not_verified`, `skipped`, `in_progress` or `not_started`.

**Open question: the two models don't match yet.** The Figma/mobile model has steps → actions (each with a hidden tactic and guidance type) plus an Oversight Checklist (Temporal / Continuous / State requirements). The headset's procedure JSON has steps with per-step after-step checks, plus supplies and a completion note. Agree with the user on one shared schema, or on a mapping, before building the server API.

## Related code on the PC side

- `Tools/verification_bridge.py`: the PC bridge to Gemini on port 8765 (`GET /health`, `POST /verify`). The headset reaches it through `adb reverse tcp:8765 tcp:8765`. The new server can sit next to it, for example a small HTTP server with SQLite in `Tools/`.
- `Assets/_Project/Editor/Authoring/RequirementsPrototypeWindow.cs`: Unity editor tool that turns a nurse's text instruction into step requirements with Gemini.
- `Assets/_Project/Runtime/VRDemo/VRDemoController.cs`: the nurse "record an instruction" flow in VR. It is still a scripted walkthrough, with no real recording or upload.

## Rules

- Never add Claude as a Git co-author in commit messages.
- Never commit `ProjectSettings/wit.config` (Wit.ai server token) or any API key. The Gemini key comes from the `GEMINI_API_KEY` environment variable.
- Commit or push only when the user asks.
