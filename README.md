# Dashcam Cloud Service

## Project Overview
Dashcam Cloud Service detects video tampering by comparing a **hash timeline captured during recording** against **hashes re-extracted from a submitted video**. The core idea is: on-device perceptual hashing (dHash64) is streamed to Supabase, then the validator re-hashes frames from the submitted video and matches them with Hamming distance inside a timestamp tolerance window.

## Architecture (High Level)
- **Capture Client** (`apps/capture-client`) — Web camera capture + on-device hashing, offline buffer, uploads hash records to Supabase.
- **Validator Portal** (`apps/validator-portal`) — Angular UI for auth + video upload + sessionId entry; displays verdict and metrics.
- **Validator API** (`services/validator-api`) — ASP.NET Core service; validates JWT, reads Supabase timeline, extracts frames via ffmpeg, hashes and matches.

See `docs/index.md` for a navigation hub and `ProjectState.md` for the single source of truth.

## Demo “Happy Path” (10-minute flow)
1) **Start services** (see “How to Run” below).
2) **Capture a session** in the capture client:
   - Sign in (Supabase user), start camera, start session.
   - Hashes stream to Supabase in real time.
   - Optional: download the local recording (WebM) for later verification.
3) **Verify in portal**:
   - Upload the original WebM and enter the sessionId.
   - Expect **high match ratio**, **no missing spans**, and low distances.
4) **Enable debug** (optional): add `?debug=1` or header `X-Debug: 1` to return window stats.

## Falsification / Tamper Tests
- Full commands and expected outcomes: `docs/falsification-matrix.md`.
- Tested scenarios: **trim**, **re-encode**, **fps change**, **speed change**, **remux**.
- Debug mode helps classify 0-ratio cases:
  - **Case A** timestamp drift (no candidates in window)
  - **Case B** visual transform (candidates exist, min distance > threshold)
  - **Case C** extraction/PTS failure (0 frames or invalid elapsed range)

## Setup / Configuration (No Secrets)
### Config Matrix
| Component | Where | Required Values |
|---|---|---|
| capture-client | UI inputs | `Supabase URL`, `Supabase anon key`, login email/password |
| validator-portal | `apps/validator-portal/src/environments/environment*.ts` | `supabaseUrl`, `supabaseAnonKey`, `validatorApiBaseUrl` |
| validator-api | `services/validator-api/appsettings*.json` or env vars | `Supabase:BaseUrl`, `Supabase:PublishableKey`/`Supabase:AnonKey`, `Supabase:ServiceRoleKey` |

## How to Run (Exact Commands)
### Capture Client
```bash
cd apps/capture-client
npm install
npm run dev
```
- Dev server: `http://localhost:8000`

### Validator Portal
```bash
cd apps/validator-portal
npm install
npm run start
```
- Angular dev server: `http://localhost:4200`

### Validator API
```bash
cd services/validator-api
dotnet run
```
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`

## Troubleshooting
- **401/403 from validator API**: JWT missing/expired or user lacks `validator` role. Ensure portal auth is signed in and Supabase roles are configured.
- **Re-encode yields 0 ratio**: Often **timestamp drift** or **visual transform**. Use debug mode to inspect `candidateCountInWindow` and `bestMinDistance`.
- **ffmpeg not found / extraction errors**: Ensure ffmpeg is installed and available on PATH for `validator-api`.
- **Low match ratio on original**: Verify sessionId matches the original capture session and that the uploaded video is the original recording.

## Development Plans Evolution (v1 → v3)
- **Plan v1**: Hash core scaffolding + cross-runtime consistency tests (dHash64 + Hamming distance).
- **Plan v2**: Split Slice 4 into infra pieces (Supabase store/schema, ffmpeg extraction, portal wiring).
- **Plan v3**: Restored Spec alignment: mobile capture UX (5A), auth end-to-end (5B), sessionId verification (5C), demo/QA/falsification + debug (5D).

Plan docs: `docs/planV1.md`, `docs/planV2.md`, `docs/planv3.md`.

## Status + Next Steps
- **Plan v3 completed** (Slices 5A–5D ✅).
- Optional next work: retention polish, UX refinement, packaging/deployment, performance tuning.

## Documentation Links
- `docs/index.md` — navigation hub
- `docs/demo-script.md` — demo walkthrough
- `docs/qa-checklist.md` — QA checklist
- `docs/falsification-matrix.md` — tamper variants
- `ProjectState.md` — single source of truth
