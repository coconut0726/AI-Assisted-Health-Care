# Care-plan server

The cloud between the headset and the mobile app. It stores every version of each patient's care plan (`action_requirements_v1`) and serves the API in `docs/SCHEMA_mobile.md`.

- **Cloud:** Google Cloud Run + Firestore (`CARE_PLAN_STORE=firestore`, set in the `Dockerfile`).
- **Local:** SQLite in `data/` (git-ignored), standard library only. Use it for development and tests.

## Run locally

```bash
python CarePlanServer/care_plan_server.py               # http://0.0.0.0:8770, SQLite, seeds the demo patient "james"
python CarePlanServer/care_plan_server.py --reset-demo  # wipe james's versions and seed the burn demo again
python -m unittest discover -s CarePlanServer -p "test_*.py"
```

## Environment

| Variable | Meaning |
|---|---|
| `CARE_PLAN_STORE` | `sqlite` (default) or `firestore` |
| `CARE_PLAN_TOKEN` | When set, `/api` needs `Authorization: Bearer <token>`. The app sends `EXPO_PUBLIC_SERVER_TOKEN`. |
| `GEMINI_API_KEY` | Fills `error_type` / `violation_condition` for new Watch-out items. Without it a plain fallback is used. |
| `CARE_PLAN_MODEL` | Gemini model, default `gemini-3.8-flash` |
| `PORT` | Set by Cloud Run; locally 8770 |

## Deploy to Google Cloud (one time)

Do these yourself in the Google Cloud console: create a project, link a billing account, then install the gcloud CLI and run `gcloud auth login`. Then from the repo root (replace `PROJECT_ID`):

```bash
gcloud config set project PROJECT_ID
gcloud services enable run.googleapis.com firestore.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com secretmanager.googleapis.com
gcloud firestore databases create --location=us-east1
```

Secrets (the values never go into git or into the deploy command):

```bash
printf '%s' "$GEMINI_API_KEY" | gcloud secrets create gemini-api-key --data-file=-
python -c "import secrets; print(secrets.token_urlsafe(24), end='')" | gcloud secrets create care-plan-token --data-file=-
```

Let Cloud Run's service account read the secrets and use Firestore (`PROJECT_NUMBER` is shown by `gcloud projects describe PROJECT_ID`):

```bash
SA=PROJECT_NUMBER-compute@developer.gserviceaccount.com
gcloud projects add-iam-policy-binding PROJECT_ID --member=serviceAccount:$SA --role=roles/datastore.user
gcloud projects add-iam-policy-binding PROJECT_ID --member=serviceAccount:$SA --role=roles/secretmanager.secretAccessor
```

Deploy (also how to update it later):

```bash
gcloud run deploy care-plan --source CarePlanServer --region us-east1 --allow-unauthenticated \
  --max-instances 1 --set-secrets GEMINI_API_KEY=gemini-api-key:latest,CARE_PLAN_TOKEN=care-plan-token:latest
```

The command prints the service URL, for example `https://care-plan-xxxxx-ue.a.run.app`. `--allow-unauthenticated` only opens the door to Cloud Run; every `/api` call still needs the token.

## Point the app at it

```bash
cd MobileApp
EXPO_PUBLIC_SERVER_URL=https://care-plan-xxxxx-ue.a.run.app EXPO_PUBLIC_SERVER_TOKEN=<token> npx expo start
```

Read the token with `gcloud secrets versions access latest --secret=care-plan-token`.

## Data and privacy

Firestore layout: `patients/<id>` holds `latest_version`; `patients/<id>/versions/<n>` holds each version, with the plan as a JSON string. This setup is for demo data only. The shared token ships inside the app, so real patient data needs per-user sign-in and a compliant setup first.
