# ProjectState

## Current Slice Status
- Completed slices: 1, 2, 3, 4A–4D, 5A, 5B, 5C ✅
- Patch: Hybrid JWT Validation ✅ (Auth patch)
- Pending slices: 5D

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
    CancellationToken ct)
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
- Changing samplingIntervalMs at verification time to a value different from the session’s stored samplingIntervalMs will reduce match ratio and produce missing spans. Verifier should use the session interval as the single source of truth (or reject mismatches).

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

