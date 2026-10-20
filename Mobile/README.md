# AI-Assisted Home Care — Mobile (web demo)

Website version of the nurse and caregiver/patient mobile apps, built from the Figma "Mobile" page.
It is a **demo build**: there is no backend. All data comes from `src/data/demo.ts` and is kept in the
browser's `localStorage`, so a nurse tab and a patient tab in the same browser stay in sync.

## Run locally

Requires Node.js 18+.

```bash
npm install
npm run dev
```

Open the printed URL. On a desktop browser the app is shown inside a phone frame with a demo panel
for switching roles; on a phone it fills the screen. The `Network` URL works from a phone on the same Wi-Fi.

## Demo script

1. **Nurse** → James Carter → Instruction. Step 2's first action is the AI's mistake ("Wet the bandage with water." — the condition is missing).
2. Edit Step 2, rewrite the action as a conditional, **Save & sync**.
3. **Patient** (second tab) → My Care Plan shows "Nurse Lee updated your instructions — Step 2 changed".
4. **Nurse** → Home Care Feedback → View report → **Send to patient**, then type a message.
5. **Patient** → Feedback shows the shared report and the message.

**Reset demo** (home page or desktop side panel) restores the starting state.

## Structure

| Path | What it is |
|---|---|
| `src/data/demo.ts` | Demo content: decomposed instruction, oversight checklist, weekly report, messages |
| `src/types.ts` | Data model; `Step`/`Action` mirror the Unity Decompose tool's JSON output |
| `src/store.tsx` | Demo state + actions, persisted to `localStorage` |
| `src/pages/nurse/*` | N1–N9: patient list, instruction, edit step / requirement, feedback |
| `src/pages/patient/*` | P1–P3: read-only instruction with change notice, feedback |
| `src/pages/Report.tsx` | Weekly report detail (N7 / P4), shared by both roles |
| `src/styles.css` | Design tokens and styles, matching the Figma sage restyle |

## Deploy

`.github/workflows/deploy-mobile.yml` (repo root) builds this folder and publishes it to GitHub Pages
on every push to `main` that touches `Mobile/`. Enable it once in the repository:
**Settings → Pages → Build and deployment → Source: GitHub Actions**.
