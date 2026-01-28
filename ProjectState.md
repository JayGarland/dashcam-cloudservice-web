# ProjectState

## Current Slice Status
- Completed slices: 1, 2, 3, 4A–4D, 5A, 5B, 5C ✅
- Pending slices: 5D
- Pending patch: 5A-P1 (if not merged yet)

## Repo Structure (Focused Snapshot)
(Tree command unavailable; snapshot derived from `python3` with exclusions.)

### apps/capture-client/src
```text
apps/capture-client/src
  __tests__/
  capture/
  hash/
  storage/
  supabase/
  ui/
  constants.ts
  models.ts
  capture/frameSource.ts
  capture/sampler.ts
  hash/dhash64.ts
  storage/hashQueue.ts
  supabase/supabaseApi.ts
  supabase/uploader.ts
  ui/capturePage.ts
  ui/main.ts
  __tests__/dhash64.spec.ts
  __tests__/hashQueue.spec.ts
  __tests__/sampler.spec.ts
  __tests__/uploader.spec.ts
```

### apps/validator-portal/src/app
```text
apps/validator-portal/src/app
  api/
  auth/
  claims/
  models/
  app-routing.module.ts
  app.component.css
  app.component.html
  app.component.ts
  app.module.ts
  api/validator-api.service.spec.ts
  api/validator-api.service.ts
  auth/auth.component.css
  auth/auth.component.html
  auth/auth.component.ts
  auth/auth.guard.spec.ts
  auth/auth.guard.ts
  auth/auth.module.ts
  auth/auth.service.ts
  claims/verify-claim/
  claims/verify-claim/verify-claim.component.css
  claims/verify-claim/verify-claim.component.html
  claims/verify-claim/verify-claim.component.ts
  claims/verify-claim/verify-claim.module.ts
  models/verification.models.ts
```

### services/validator-api
```text
services/validator-api
  Auth/
  Controllers/
  Models/
  Services/
  Tests/
  Program.cs
  appsettings.Development.example.json
  appsettings.Development.json
  appsettings.json
  validator-api.csproj
  Auth/SupabaseJwtValidator.cs
  Auth/ValidatorRoleRequirement.cs
  Controllers/ClaimsController.cs
  Models/CaptureModels.cs
  Models/VerificationModels.cs
  Models/VerifyClaimModels.cs
  Services/DHash64.cs
  Services/FfmpegOptions.cs
  Services/FfmpegVideoFrameExtractor.cs
  Services/HammingDistance.cs
  Services/HashMatcher.cs
  Services/IProcessRunner.cs
  Services/ISupabaseHashStore.cs
  Services/IVideoFrameExtractor.cs
  Services/ProcessRunner.cs
  Services/ServiceExceptions.cs
  Services/SupabaseHashStore.cs
  Services/SupabaseOptions.cs
  Services/SupabaseServiceCollectionExtensions.cs
  Services/VerificationService.cs
  Tests/AuthTests.cs
  Tests/ClaimsControllerTests.cs
  Tests/DHash64Tests.cs
  Tests/FfmpegVideoFrameExtractorTests.cs
  Tests/HashMatcherTests.cs
  Tests/SupabaseHashStoreTests.cs
  Tests/VerificationServiceTests.cs
  Tests/validator-api.Tests.csproj
```

## Key Interfaces (Exact Signatures)

### Capture Client (TypeScript)
Auth exports (`apps/capture-client/src/supabase/supabaseApi.ts`):
```ts
export function configureSupabaseAuth(config: SupabaseConfig): void {
```
```ts
export async function signIn(
  email: string,
  password: string
): Promise<Session> {
```
```ts
export async function signOut(): Promise<void> {
```
```ts
export function getSession(): Session | null {
```
```ts
export async function getAccessToken(): Promise<string | undefined> {
```

Supabase client wrapper (token attachment):
```ts
private async post(path: string, payload: unknown): Promise<void> {
  if (!this.url || !this.anonKey) {
    return;
  }
  const token = (await this.getAccessToken?.()) ?? this.accessToken;
  if (!token) {
    throw new Error("Supabase user session is required before uploads.");
  }

  const response = await fetch(`${this.url}/rest/v1/${path}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      apikey: this.anonKey,
      Authorization: `Bearer ${token}`,
      Prefer: "return=minimal",
    },
    body: JSON.stringify(payload),
  });
```

Supabase API interfaces (unchanged):
```ts
export interface SupabaseApi {
  insertSession(session: CaptureSession): Promise<void>;
  insertFrameHashes(records: FrameHashRecord[]): Promise<void>;
}

export interface SupabaseConfig {
  url?: string;
  anonKey?: string;
  accessToken?: string;
  getAccessToken?: () => Promise<string | undefined>;
}
```

### Validator Portal (Angular)
Verify API client (`apps/validator-portal/src/app/api/validator-api.service.ts`):
```ts
verifyClaim(
  videoFile: File,
  sessionId: string,
  metadataFile?: File | null,
  accessToken?: string | null
): Observable<VerificationResult> {
  const formData = buildVerifyClaimFormData(videoFile, sessionId, metadataFile);
  const headers = this.buildAuthHeaders(accessToken);
  const url = this.buildUrl('/api/claims/verify');

  return this.http.post<VerificationResult>(url, formData, headers ? { headers } : {});
}
```

Verify-claim component inputs (`apps/validator-portal/src/app/claims/verify-claim/verify-claim.component.ts`):
```ts
export class VerifyClaimComponent {
  videoFile?: File;
  metadataFile?: File;
  sessionId = '';
```
```ts
if (!this.videoFile || !this.sessionId.trim()) {
  this.errorMessage = 'Please select a video file and enter a session ID.';
  return;
}
```

### Validator API (ASP.NET Core)
Verify endpoint protection + signature (`services/validator-api/Controllers/ClaimsController.cs`):
```csharp
[HttpPost("verify")]
[Consumes("multipart/form-data")]
[Authorize(Policy = "ValidatorOnly")]
public async Task<ActionResult<VerificationResult>> Verify([FromForm] VerifyClaimRequest request, CancellationToken ct)
```

VerificationService signature (`services/validator-api/Services/VerificationService.cs`):
```csharp
public async Task<VerificationResult> VerifyAsync(
    Stream videoStream,
    string sessionId,
    VerifyClaimMetadata? metadataOverride,
    CancellationToken ct)
```

Verify request DTOs (`services/validator-api/Models/VerifyClaimModels.cs`):
```csharp
public class VerifyClaimMetadata
{
    public string SessionId { get; set; } = string.Empty;
    public long DeviceClockStartEpochMs { get; set; }
    public int SamplingIntervalMs { get; set; }
    public string AlgoVersion { get; set; } = "dhash64_v1";
    public int? ToleranceMs { get; set; }
}

public class VerifyClaimRequest
{
    public IFormFile? Video { get; set; }
    public IFormFile? Metadata { get; set; }
    public string? SessionId { get; set; }
}
```

## Auth Model (Slice 5B)
- Capture client: create a Supabase Auth user (email/password) and sign in from the capture-client UI.
- Capture client session stored in localStorage (`capture-client.supabase.session`).
- Validator portal: Supabase Auth login; session stored in localStorage (`validator-portal.supabase.session`).
- Validator role assignment: `auth.users.raw_app_meta_data.role = "validator"`.
- Portal → API: `Authorization: Bearer <access_token>`.
- API enforcement:
  - Missing/invalid token → 401
  - Valid token without validator role → 403
  - Valid validator token → 200
- Note: metadata upload is optional (debug/override); sessionId is required (Slice 5C).

## Verification Flow (Slice 5C)
- Required multipart fields: `video` (file), `sessionId` (string)
- Optional multipart field: `metadata` (file, advanced/debug override)
- If metadata omitted, validator-api loads session anchors from Supabase `capture_sessions`

## Runtime Notes / How to Run

### capture-client
```bash
cd apps/capture-client
npm install
npm run dev
```
- URL: `http://localhost:8000`
- Configure Supabase URL + anon key in the UI before signing in.
- Mobile testing: HTTPS required for camera access.

### validator-api
```bash
cd services/validator-api
dotnet run
```
- URLs: `https://localhost:5001` / `http://localhost:5000`
- Requires `Supabase:JwtSecret` (from Supabase project settings) to validate JWTs.

### validator-portal
```bash
cd apps/validator-portal
npm install
npm start
```
- URL: `http://localhost:4200`
- Configure Supabase URL + anon key in `apps/validator-portal/src/environments/environment.development.ts`.

## Auth Setup (Slice 5B)
1) Create a capture user in Supabase Auth (email/password) for the capture-client login.
2) Create a validator user in Supabase Auth (email/password).
3) Assign the validator role via app metadata (example SQL):
   ```sql
   update auth.users
   set raw_app_meta_data = jsonb_set(coalesce(raw_app_meta_data, '{}'::jsonb), '{role}', '"validator"', true)
   where email = 'validator@example.com';
   ```
4) Sign out/in so the JWT includes the updated role claim.
5) Set `Supabase:JwtSecret` in `services/validator-api/appsettings.Development.json` (or env var).

## Manual Verification Checklist (Slice 5C)
1) Log in as a validator in the portal.
2) Upload video + sessionId only (no metadata) → returns verdict.

## Demo Checklist (Happy Path) — Slice 5A
1) Start camera → preview visible
2) Start session → session row inserted in Supabase
3) Hashes appear near real-time in Supabase
4) Offline → pending count grows
5) Online → backlog drains

## Evidence
- dotnet test (validator-api): NOT RUN (dotnet not available in environment)
- npm test/build (validator-portal): NOT RUN (WSL2/Node not available in environment)
- npm test/build (capture-client): NOT RUN
- Dev server start logs: NOT RUN
