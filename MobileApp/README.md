# Mobile app (Expo)

The iPhone app for the nurse and the caregiver/patient. It replaces the `Mobile/` web demo.
Screens follow the Figma page "Mobile". Data is `action_requirements_v1`, see `docs/SCHEMA_mobile.md`.

## Run on the iPhone

Needs Node 22.13 or later (`node -v`) and the Expo Go app on the iPhone. The Mac and the iPhone must be on the same Wi‑Fi.

```bash
cd MobileApp
npm install
npx expo start
```

Scan the QR code with the iPhone camera. It opens in Expo Go.

Browser preview on the Mac: `npx expo start --web`.

## Server

Without a server, the care plan is stored on the phone and starts from the burn demo (`src/data/`). "Reset demo" on the first screen restores it.

To use the care-plan server, start it on the Windows PC (`python Tools/care_plan_server.py`, port 8770, see `docs/HANDOFF_mobile.md`), then start Expo with the PC's LAN address:

```bash
EXPO_PUBLIC_SERVER_URL=http://192.168.1.20:8770 npx expo start
```

The endpoints the app calls are listed in `src/api/client.ts` and `docs/SCHEMA_mobile.md`.

## What's built

| Figma | Route |
|---|---|
| S0 Splash | first launch, over any screen |
| N1 Patients | `/nurse` |
| N2 Care Steps, N3 Safety Rules, N5 Saved | `/nurse/[patientId]` (N5 = `?saved=1`) |
| N4 / N4b Edit Action | `/nurse/[patientId]/action/[actionId]` |
| N10 Edit Supplies | `/nurse/[patientId]/supplies` |
| P1 Care Steps, P2 Safety Rules | `/patient` |

Not built yet: feedback and weekly report (N6–N9, P3–P4), editing a safety rule or the "Call the nurse if" card (no Figma screen yet).

## Checks

```bash
npx tsc --noEmit
npx expo lint
```
