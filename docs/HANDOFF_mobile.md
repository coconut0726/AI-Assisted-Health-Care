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

- Nurse screens N1–N10, patient screens P1–P4, splash S0. Rule from the user: **change Figma first, then implement.**
- The screens follow the headset's V3 action-level model (`action_requirements_v1`, see below). Content is the burn case from `burn_action_requirements.json`.
- **Plain language for the nurse.** The nurse never sees error types, ids, `source`, dependencies, or the old tactic/guidance tags ("Command", "Movement"…). The program keeps them.

| Screen | What it shows |
|---|---|
| N2 Care Steps (nurse) | Supplies, then steps as collapsible cards ("2 actions"). An open step lists its actions. Each action card shows **Before** (preconditions), **Done when** (postconditions), **Watch out** (error criteria, phrased by `compliance_condition`), plus tags: **Only if …** (`applicability_condition`), **Caregiver confirms** (`manual_completion_required`). A step with an item the AI is unsure about (`requires_expert_review`) shows **Please review**. Tapping an action opens N4. |
| N3 Safety Rules (was Oversight Checklist) | Rules that hold the whole session (`procedure_error_criteria`), each editable, plus the red "Call the nurse if" card. The old Temporal / Continuous / State tags are gone: ordering is now action dependencies, and end states are now each action's Done when. |
| N4 Edit Action (was Edit Step) | The recording quote, **What to do**, then three editable lists (Before starting / Done when / Watch out) with "+ Add", a yellow note on items flagged for review, a **Caregiver confirms this action** switch, and **+ Only do this if…**. Save & sync. |
| N5 Saved | Same cards as N2, banner: saved and synced to the patient's app and the headset. |
| P1 / P2 (patient) | Read-only versions of N2 / N3. P1 marks changed steps **Updated**. |
| N6–N9, P3–P4 (feedback & report) | Unchanged, except the report stats gained a fourth count, **Not needed** (conditional actions skipped as `not_applicable`). |

- Reusable components are in the **Components · Care plan** panel left of the screens: `Condition` (Kind = Before / Done when / Watch out) and `Tag` (Kind = Only if / Caregiver confirms / Please review).

## Existing code to reuse

- `Mobile/`: React + Vite web demo of the **old** Figma screens (no backend; demo data in `src/data/demo.ts`). Good reference for styles (`src/styles.css` tokens) and the general screen structure. Its data model (`src/types.ts`: steps → actions with tactic/guidance, Temporal/Continuous/State requirements) is out of date; replace it with the action model below.
- `DemoVideo/`: Remotion video that renders `Mobile/` screens. It does not have to stay in sync.
- A first attempt that changed the UX without updating Figma is on the local Windows branch `mobile-web-old-ux`. Do not use it.

## App technology: not decided yet

- Recommended: **Expo (React Native)**. It reuses React/TypeScript. Run it on the iPhone with **Expo Go**, or build it with Xcode on the Mac (a free Apple ID allows a 7-day install on your own phone).
- Alternative: keep the web app and install it as a PWA (Safari → Add to Home Screen).
- Ask the user before starting.

Mac setup: Xcode (App Store), Node.js LTS, Git, and Expo Go on the iPhone.

## Data formats on the headset side

**Action requirements JSON (`action_requirements_v1`)**: the new V3 format, added by Kezhou on branch `feature/periodical-sampling-with-localVLM` (not merged into main yet). Examples: `Assets/_Project/Content/Procedures/{burn,wound,tabletop}_action_requirements.json`. C# model: `Assets/_Project/Runtime/Procedures/ActionProcedure.cs`.

```
schema_version, procedure_title, task_goal, instruction,
steps[]: { step_id, step_name, step_description, dependencies[],
           actions[]: { action_id, instruction, original_text,
                        dependencies[], conditional_dependencies[] {action_id, condition},
                        applicability_condition, actor[], tools[], materials[],
                        contact_media[], targets[], associated_constraints[],
                        manual_completion_required,
                        preconditions[] / postconditions[]:
                          { requirement_id, condition, source, source_reference, requires_expert_review },
                        error_criteria[]:
                          { criterion_id, error_type, violation_condition, compliance_condition,
                            applicability_condition, related_requirement_ids[], source,
                            source_reference, requires_expert_review } } },
procedure_error_criteria[] (same shape, plus relevant_action_ids[]),
context_notes[], review_notes[]
```

- `error_type` is one of `wrong_object`, `spatial_error`, `technique_error`, `measurement_error`, `timing_parameter_error`.
- Edited in Unity with **AHC → Requirement Generation** (`ActionRequirementsWindow.cs`): pick a step and action, then edit its preconditions, postconditions and error criteria, plus the shared rules. **Save & use in V3 Demo** assigns the file to the V3 scene. The mobile N4 screen is the phone version of this window.
- Run by `VRGuidanceDemo_V3.unity` (`ActionGuidanceController`, `ActionGuidanceSession`). The flow: applicability check (if conditional), then preconditions, then postconditions and error criteria together, sampled every 1 s through the bridge. The VLM is Gemini or local Qwen3‑VL‑4B, chosen in **AHC → VLM Backend**.

**Procedure JSON (older format)**: what the V1/V2 headset demos run. Example: `Assets/_Project/Content/Procedures/wound_dressing_procedure.json`.

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

**Shared schema: use `action_requirements_v1`.** The Figma screens were redesigned around it, so the server should store this JSON as is, and the phone should read and write it. Mapping for the UI:

| UI | JSON |
|---|---|
| Action card text / What to do | `actions[].instruction` |
| Before | `preconditions[].condition` |
| Done when | `postconditions[].condition` |
| Watch out | `error_criteria[].compliance_condition` |
| Only if … | `applicability_condition` |
| Caregiver confirms | `manual_completion_required` |
| Please review / yellow note | any `requires_expert_review: true` (clear it when the nurse saves) |
| Safety Rules (N3) | `procedure_error_criteria[]` |
| Recording quote (N4) | `original_text` |
| Report "Not needed" | session result `not_applicable` |

Still open, ask the user:
- When the nurse adds a Watch out item, who fills `error_type` and `violation_condition`? Suggested: the AI fills them from the nurse's sentence.
- Supplies and the "Call the nurse if" text are not in `action_requirements_v1` yet. They're in the older procedure JSON (`supplies`, `completion_note`). Add them to the new format, or keep them separate.
- Suggested scope: the phone only reviews and edits. Generating a plan from an instruction stays on VR or the PC.

## Related code on the PC side

- `Tools/verification_bridge.py`: the PC bridge to Gemini on port 8765 (`GET /health`, `POST /verify`). The headset reaches it through `adb reverse tcp:8765 tcp:8765`. The new server can sit next to it, for example a small HTTP server with SQLite in `Tools/`.
- On Kezhou's branch the bridge also serves a local Qwen3‑VL‑4B (via ollama). Setup: `Tools/Setup-LocalVlm.ps1`, `Tools/Start-LocalVlm.ps1`.
- `Assets/_Project/Editor/Authoring/ActionRequirementsWindow.cs` (Kezhou's branch): generates and edits `action_requirements_v1` from an instruction. `RequirementsPrototypeWindow.cs` is now the "Legacy requirements editor".
- `Assets/_Project/Runtime/VRDemo/VRDemoController.cs`: the nurse "record an instruction" flow in VR. It is still a scripted walkthrough, with no real recording or upload.

## Rules

- Never add Claude as a Git co-author in commit messages.
- Never commit `ProjectSettings/wit.config` (Wit.ai server token) or any API key. The Gemini key comes from the `GEMINI_API_KEY` environment variable.
- Commit or push only when the user asks.
