# Mobile app ↔ action_requirements_v1

Status: **decided, not implemented**. Read with `HANDOFF_mobile.md`.

The server stores `action_requirements_v1` JSON as is. The phone reads and writes the same JSON. There is no mapping layer. The format and its checks are defined in `ActionProcedure.cs` and `ActionProcedureJson.cs`, which are on the branch `feature/periodical-sampling-with-localVLM` and not merged yet.

## Decisions (2026-11-24)

1. **New "Watch out" items.** The nurse writes one sentence. The server asks the AI to fill in `error_type`, `violation_condition` and `related_requirement_ids`. The nurse's sentence goes into `compliance_condition` unchanged.
2. **Supplies and "Call the nurse if".** Both are added to the same JSON as optional fields. Newtonsoft ignores unknown fields, so the headset needs no change.
   - `supplies`: a top-level array, same shape as the older procedure JSON: `[{ itemId, displayName, keywords[] }]`. The nurse edits only `displayName`, on N10. The server derives `itemId` and `keywords`.
   - "Call the nurse if" comes from the action that tells the patient to call the nurse, for example `A11` in the burn case. That action gets an optional flag, `"notify_nurse": true`, set by the server.
     - The red card on N3 and P2 shows the flagged action's `applicability_condition`.
     - Editing the card edits that action.
     - The text is stored only once, so there is no `completion_note` field.
3. **Scope.** The phone only reviews and edits. Turning an instruction into a plan stays on VR or the PC (`ActionRequirementsWindow`). The phone does not add or delete steps or actions.

## UI ↔ JSON

| UI | JSON |
|---|---|
| Action card text / What to do | `actions[].instruction` |
| Recording quote (N4) | `original_text` (read-only) |
| Before | `preconditions[].condition` |
| Done when | `postconditions[].condition` |
| Watch out | `error_criteria[].compliance_condition` |
| Only if … | `applicability_condition` |
| Caregiver confirms | `manual_completion_required` |
| Please review / yellow note | any `requires_expert_review: true` |
| Safety Rules (N3) | `procedure_error_criteria[]` |
| Call the nurse if (N3, P2) | the action with `notify_nurse: true` |
| Supplies (N2, P1, N10) | `supplies[]` |
| Report "Not needed" | session result `not_applicable` |

## Rules the server checks on save

The headset rejects a file that fails `ActionProcedureJson.Validate`. The server runs the same checks before it stores a new version, and the phone's UI keeps the nurse from breaking them:

- **Nurse edits.** A requirement or criterion the nurse adds or rewrites gets `source: "author_edit"` and a non-empty `source_reference`. The headset already accepts `author_edit`, so it needs no change.
- **Review flag.** When the nurse saves, `requires_expert_review` is cleared on every item the nurse has seen. An item with `source: "inferred"` must keep the flag, so the server changes its source to `author_edit` when it clears the flag.
- **Caregiver confirms vs Done when.** `manual_completion_required: true` is allowed only on an action with no postconditions. An action with no postconditions must have it set to `true`. In N4:
  - Turning the switch on asks to remove the Done-when items.
  - Removing the last Done-when item turns the switch on.
  - Turning the switch off brings back "+ Add a done-when check", and Save needs at least one.
  - Figma (done 2026-11-24):
    - N4's switch hint now says that turning it on removes the Done-when checks.
    - The new frame **N4b** shows the switch on: the Done-when list is replaced by a "No photo check" note.
    - The rule is written in a note under N4b.
- **Shared rules.** A shared rule in `procedure_error_criteria` must not repeat the text of an action's own check, or the headset rejects the file.
- **Ids.** `requirement_id` and `criterion_id` must be unique. The server assigns them.
- **Original text.** `original_text` must quote `instruction` exactly. The phone never edits either one.

## Server API (proposal, used by `MobileApp/src/api/client.ts`)

The server stores every version. The phone only reads the latest and writes a new one.

- `GET /api/patients/:id/plan` returns the latest `PlanVersion`:
  `{ patient_id, version, updated_at, updated_by: "ai" | "nurse", plan, changed_step_ids }`.
- `PUT /api/patients/:id/plan` with `{ base_version, plan }` checks the plan (rules above), asks the AI to fill in new Watch-out items and supply `itemId` / `keywords`, stores it as `version + 1` and returns the new `PlanVersion`. If `base_version` is not the latest, it answers `409`.
- `changed_step_ids` lists the steps that differ from the previous version. The patient app marks them "Updated".
- The headset loads `plan` from the latest version at the start of a session.
