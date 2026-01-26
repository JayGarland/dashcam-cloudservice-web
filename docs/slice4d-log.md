# Slice 4D Log - Validator Portal (Angular) Wiring

Date: 2026-01-26

## What changed
- Added a new Angular app at `apps/validator-portal` with routing and a Verify Claim screen.
- Implemented `ValidatorApiService` to POST multipart form data with `Video` and `Metadata` parts.
- Added response models for `VerificationResult`, `MissingSpan`, and `Verdict`.
- Included optional bearer token forwarding (if a token provider is registered later).
- Added a dev proxy configuration to avoid CORS when using the Angular dev server.
- Updated PlantUML flow diagram to mark the portal wiring as REAL (Slice 4D).

## Files added/updated (high level)
- `apps/validator-portal/` (new Angular app scaffold)
- `apps/validator-portal/src/app/api/validator-api.service.ts`
- `apps/validator-portal/src/app/models/verification.models.ts`
- `apps/validator-portal/src/app/claims/verify-claim/*`
- `apps/validator-portal/src/environments/environment*.ts`
- `apps/validator-portal/proxy.conf.json`
- `docs/flow/current-system-flow.puml`
- `docs/slice4d-log.md` (this file)

## How to run locally (manual demo)

### 1) Start validator-api
```bash
cd services/validator-api
# Ensure appsettings.Development.json is configured (gitignored)
# API should start on https://localhost:5001

dotnet run
```

### 2) Start validator-portal
```bash
cd apps/validator-portal
npm install
npm start
```

If you hit CORS errors in the browser console, set the base URL to empty and rely on the proxy:
- Edit `apps/validator-portal/src/environments/environment.development.ts`
- Set `validatorApiBaseUrl` to ''
- The dev server already uses `proxy.conf.json`

### 3) Verify a claim
- Open http://localhost:4200/claims/verify
- Select a video file (e.g., `.avi`) and the corresponding `metadata.json`
- Click **Verify Claim**
- Confirm verdict, metrics, missing spans, and notes render

## Notes
- Multipart field names must be `Video` and `Metadata` (matching `VerifyClaimRequest` property names).
- No secrets or auth tokens are committed; token forwarding is optional via DI.
- Hashing, matching, ffmpeg extraction, and Supabase store logic remain unchanged.
