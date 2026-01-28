# ProjectState

## Current Slice Status
- Completed slices: 1, 2, 3, 4A–4D, 5A, 5B, 5C ✅
- Patch: Hybrid JWT Validation ✅ (Auth patch)
- Pending slices: 5D (QA + falsification validation)

## Repo Structure (Focused Snapshot)
(Tree command unavailable; snapshot derived from `find` with exclusions.)

### services/validator-api
```text
services/validator-api
  validator-api
    appsettings.Development.example.json
    appsettings.Development.json
    appsettings.json
    Auth
      SupabaseHybridAuthHandler.cs
      SupabaseJwtValidator.cs
      ValidatorRoleRequirement.cs
    Controllers
      ClaimsController.cs
    Models
      CaptureModels.cs
      VerificationModels.cs
      VerifyClaimModels.cs
    Program.cs
    Properties
      launchSettings.json
    Services
      DHash64.cs
      FfmpegOptions.cs
      FfmpegVideoFrameExtractor.cs
      HammingDistance.cs
      HashMatcher.cs
      IProcessRunner.cs
      ISupabaseHashStore.cs
      IVideoFrameExtractor.cs
      ProcessRunner.cs
      ServiceExceptions.cs
      SupabaseHashStore.cs
      SupabaseOptions.cs
      SupabaseServiceCollectionExtensions.cs
      VerificationService.cs
    Tests
      AuthTests.cs
      ClaimsControllerTests.cs
      DHash64Tests.cs
      FfmpegVideoFrameExtractorTests.cs
      HashMatcherTests.cs
      SupabaseHashStoreTests.cs
      validator-api.Tests.csproj
      VerificationServiceTests.cs
    validator-api.csproj
```

### apps/validator-portal/src/app
```text
apps/validator-portal/src/app
  api
    validator-api.service.spec.ts
    validator-api.service.ts
  app-routing.module.ts
  app.component.css
  app.component.html
  app.component.ts
  app.module.ts
  auth
    auth.component.css
    auth.component.html
    auth.component.ts
    auth.guard.spec.ts
    auth.guard.ts
    auth.module.ts
    auth.service.ts
  claims
    verify-claim
      verify-claim.component.css
      verify-claim.component.html
      verify-claim.component.ts
      verify-claim.module.ts
  models
    verification.models.ts
```

## Core Interface Signatures (Exact)

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

Verify-claim component FormData fields (`apps/validator-portal/src/app/api/validator-api.service.ts`):
```ts
export function buildVerifyClaimFormData(
  videoFile: File,
  sessionId: string,
  metadataFile?: File | null
): FormData {
  const formData = new FormData();
  formData.append('video', videoFile);
  formData.append('sessionId', sessionId);
  if (metadataFile) {
    formData.append('metadata', metadataFile);
  }
  return formData;
}
```

### Validator API (ASP.NET Core)
Claims endpoint (`services/validator-api/Controllers/ClaimsController.cs`):
```csharp
[ApiController]
[Route("api/claims")]
public class ClaimsController : ControllerBase
```
```csharp
[HttpPost("verify")]
[Consumes("multipart/form-data")]
[Authorize(Policy = "ValidatorOnly")]
public async Task<ActionResult<VerificationResult>> Verify([FromForm] VerifyClaimRequest request, CancellationToken ct)
```

VerificationService (`services/validator-api/Services/VerificationService.cs`):
```csharp
public async Task<VerificationResult> VerifyAsync(
    Stream videoStream,
    string sessionId,
    VerifyClaimMetadata? metadataOverride,
    CancellationToken ct,
    bool debugEnabled = false)
```

Verify request/DTOs (`services/validator-api/Models/VerifyClaimModels.cs`):
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

## Verify Claim Contract (5C)
- Required multipart/form-data fields:
  - `video`: file
  - `sessionId`: string
- Optional multipart/form-data fields:
  - `metadata`: file (debug/backward-compat only)
- Metadata upload is no longer required for normal flow.

## Evidence (5C)
- Session ID: 9c3412bc-112d-42fb-8943-71a86d01e960
- Match ratio: 0.30
- Matched samples: 3 / 10
- Avg distance: 4.33
- Max distance: 5
- Missing spans: 8000ms - 14000ms (No match within tolerance/threshold)
- Notes: No notes from the verifier.

## Important Notes / Expected Behavior
- Sampling interval is taken from the capture session as the single source of truth; the validator portal no longer exposes an interval override by default. Using a different interval reduces match ratio and produces missing spans, so metadata overrides are ignored.

## Slice 5D: Demo + Falsification + Debug
- Debug toggle: `POST /api/claims/verify?debug=1` or header `X-Debug: 1` returns debug metrics in the response and logs them.
- Debug interpretation:
  - Case A: candidateCountInWindow == 0 for most refs → timestamp/PTS drift.
  - Case B: candidateCountInWindow > 0 but bestMinDistance > threshold → visual/hash mismatch.
  - Case C: extractedFrameCount == 0 or elapsed range nonsense → extraction/PTS parse issue.
- Falsification commands (from `docs/falsification-matrix.md`):
```bash
ffmpeg -i input.webm -c:v libx264 -crf 23 -preset veryfast -pix_fmt yuv420p -an reencode.mp4
ffmpeg -i input.webm -c copy remux.mkv
ffmpeg -i input.webm -filter_complex \"[0:v]trim=0:8,setpts=PTS-STARTPTS[v0];[0:v]trim=14,setpts=PTS-STARTPTS[v1];[v0][v1]concat=n=2:v=1:a=0\" -an trimmed.webm
ffmpeg -i input.webm -vf fps=25 -an fps25.webm
ffmpeg -i input.webm -filter:v \"setpts=0.9*PTS\" -an speedup.webm
```
- Current observation: only the original WebM yields match ratio ~1.00; re-encodes/variants yield 0.00. Use debug metrics to determine whether failure is Case A, B, or C.

## Auth Implementation Details (Hybrid JWT Validation)
- Issuers accepted: `{BaseUrl}/auth/v1`, `{BaseUrl}`, and legacy `supabase`.
- JWKS URL: `{BaseUrl}/auth/v1/.well-known/jwks.json`.
- Hybrid behavior:
  - `kid` present and `alg` starts with `ES` or `RS` => fetch JWKS, select matching key, validate locally.
  - `kid` missing (or alg not ES/RS, or JWKS empty) => call `GET {BaseUrl}/auth/v1/user` with `apikey` header (PublishableKey/AnonKey) and `Authorization: Bearer <jwt>`.
- Scheme name: `SupabaseHybrid`.
- Authorization policy: `ValidatorOnly` (enforces `validator` role).

## Config Matrix (Authoritative)
- validator-api (`services/validator-api/appsettings*.json` or env vars):
  - `Supabase:BaseUrl`
  - `Supabase:PublishableKey` or `Supabase:AnonKey` (used for `/auth/v1/user`)
  - `Supabase:ServiceRoleKey` (Supabase data reads)
  - `Supabase:JwtSecret` (present in config but not used by hybrid validator)
  - `Supabase:DisableIssuerValidation` (dev-only toggle)
- validator-portal (`apps/validator-portal/src/environments/environment*.ts`):
  - `supabaseUrl`
  - `supabaseAnonKey`
  - `validatorApiBaseUrl`

## Runtime Sanity Checks
- Startup logs (validator-api) print:
  - Environment
  - `Supabase:BaseUrl`
  - `Supabase:PublishableKey/AnonKey` (set/length only)
  - `Supabase:ServiceRoleKey` (set/length only)
- Expected auth behavior:
  - Missing/invalid token => 401
  - Authenticated but not `validator` role => 403
  - `validator` role => 200 (controller reached)

## How to Run / Manual Demo Steps (5C)
1) Sign in as a validator user in the validator portal.
2) Enter the sessionId.
3) Upload the video file.
4) Click Verify.
5) Review the result metrics.
6) Optional: upload metadata only for debug/backward-compat.

## Evidence
### Tests
- validator-api: NOT RUN (dotnet not available)
  - Error: `/bin/bash: line 1: dotnet: command not found`
- validator-portal: NOT RUN
