# ProjectState

## Current Slice Status
- Completed slices: 1, 2, 3, 4A–4D, 5A, 5B ✅
- Patch: Hybrid JWT Validation ✅ (Auth patch)
- Slice 5C: pending, now unblocked by the auth patch
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

### apps/capture-client/src
```text
apps/capture-client/src
  src
    __tests__
      dhash64.spec.ts
      hashQueue.spec.ts
      sampler.spec.ts
      uploader.spec.ts
    capture
      frameSource.ts
      localRecorder.ts
      sampler.ts
    constants.ts
    hash
      dhash64.ts
    models.ts
    storage
      hashQueue.ts
    supabase
      supabaseApi.ts
      uploader.ts
    ui
      capturePage.ts
      main.ts
```

## Core Interface Signatures (Exact)

### Validator API (ASP.NET Core)
Supabase JWT validator (`services/validator-api/Auth/SupabaseJwtValidator.cs`):
```csharp
public sealed class SupabaseJwtValidator
```
```csharp
public SupabaseJwtValidator(IConfiguration configuration, HttpClient httpClient)
```
```csharp
public async Task<ClaimsPrincipal?> ValidateAsync(string jwt, CancellationToken ct)
```
```csharp
private async Task<SecurityKey?> GetSigningKeyAsync(string kid, string? alg, CancellationToken ct)
```
```csharp
private async Task<JsonWebKeySet?> FetchJwksAsync(CancellationToken ct)
```
```csharp
private async Task<ClaimsPrincipal?> ValidateViaUserEndpointAsync(string jwt, CancellationToken ct)
```
```csharp
private TokenValidationParameters BuildTokenValidationParameters(SecurityKey signingKey, string? algorithm)
```

Hybrid auth handler (`services/validator-api/Auth/SupabaseHybridAuthHandler.cs`):
```csharp
public sealed class SupabaseHybridAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
```
```csharp
public SupabaseHybridAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISystemClock clock,
    SupabaseJwtValidator validator)
```
```csharp
protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
```

Program auth wiring (`services/validator-api/Program.cs`):
```csharp
builder.Services
    .AddAuthentication("SupabaseHybrid")
    .AddScheme<AuthenticationSchemeOptions, SupabaseHybridAuthHandler>("SupabaseHybrid", _ => { });
```
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ValidatorOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ValidatorRoleRequirement());
    });
});
```

Validator role enforcement (`services/validator-api/Auth/ValidatorRoleRequirement.cs`):
```csharp
public sealed class ValidatorRoleRequirement : IAuthorizationRequirement
```
```csharp
public sealed class ValidatorRoleHandler : AuthorizationHandler<ValidatorRoleRequirement>
```
```csharp
protected override Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    ValidatorRoleRequirement requirement)
```

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

### Validator Portal (Angular)
Auth service public API (`apps/validator-portal/src/app/auth/auth.service.ts`):
```ts
async signIn(email: string, password: string): Promise<void>
```
```ts
async signOut(): Promise<void>
```
```ts
getAccessTokenSync(): string | null
```
```ts
async getAccessToken(): Promise<string | null>
```
```ts
async isAuthenticated(): Promise<boolean>
```

Access token retrieval + Authorization header (`apps/validator-portal/src/app/api/validator-api.service.ts`):
```ts
private buildAuthHeaders(overrideToken?: string | null): HttpHeaders | null {
  const token = overrideToken ?? this.tokenProvider?.();
  if (!token) {
    return null;
  }
  return new HttpHeaders({ Authorization: `Bearer ${token}` });
}
```

### Capture Client
No auth/config changes in this patch.

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
- capture-client (`apps/capture-client/src/ui/capturePage.ts` + localStorage):
  - Supabase URL + anon key are entered in the UI and cached in localStorage (keys like `capture-client.supabase.url` / `capture-client.supabase.key`).

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

## Evidence
### Tests
- validator-api: NOT RUN (dotnet not available)
  - Error: `/bin/bash: line 1: dotnet: command not found`
- validator-portal: NOT RUN

