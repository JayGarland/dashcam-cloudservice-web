# ProjectState

## Current Slice Status
- Completed slices: 1, 2, 3, 4A–4D, 5A, 5B ✅
- Pending slices: 5C, 5D

## Repo Structure (Focused Snapshot)
(Tree command unavailable; snapshot derived from `find` with exclusions.)

### apps/capture-client/src
```text
apps/capture-client/src
__tests__
    dhash64.spec.ts
    hashQueue.spec.ts
    sampler.spec.ts
    uploader.spec.ts
capture
    frameSource.ts
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

### services/validator-api
```text
services/validator-api
Auth
    SupabaseJwtValidator.cs
    ValidatorRoleRequirement.cs
Controllers
    ClaimsController.cs
Models
    CaptureModels.cs
    VerificationModels.cs
    VerifyClaimModels.cs
Program.cs
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
    DHash64Tests.cs
    FfmpegVideoFrameExtractorTests.cs
    HashMatcherTests.cs
    SupabaseHashStoreTests.cs
    VerificationServiceTests.cs
    validator-api.Tests.csproj
appsettings.Development.example.json
appsettings.Development.json
appsettings.json
validator-api.csproj
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
Auth service (`apps/validator-portal/src/app/auth/auth.service.ts`):
```ts
export class AuthService {
  async signIn(email: string, password: string): Promise<void> {
  }

  async signOut(): Promise<void> {
  }

  getAccessTokenSync(): string | null {
  }

  async getAccessToken(): Promise<string | null> {
  }

  async isAuthenticated(): Promise<boolean> {
  }
}
```

Auth guard (`apps/validator-portal/src/app/auth/auth.guard.ts`):
```ts
export class AuthGuard implements CanActivate {
  constructor(private auth: AuthService, private router: Router) {}

  async canActivate(): Promise<boolean | UrlTree> {
    const isAuthed = await this.auth.isAuthenticated();
    if (isAuthed) {
      return true;
    }

    return this.router.createUrlTree(['/login']);
  }
}
```

Access token forwarding (`apps/validator-portal/src/app/api/validator-api.service.ts`):
```ts
export type AuthTokenProvider = () => string | null;

export const AUTH_TOKEN_PROVIDER = new InjectionToken<AuthTokenProvider>(
  'AUTH_TOKEN_PROVIDER'
);
```
```ts
verifyClaim(
  videoFile: File,
  metadataFile: File,
  accessToken?: string | null
): Observable<VerificationResult> {
  const formData = buildVerifyClaimFormData(videoFile, metadataFile);
  const headers = this.buildAuthHeaders(accessToken);
  const url = this.buildUrl('/api/claims/verify');

  return this.http.post<VerificationResult>(url, formData, headers ? { headers } : {});
}
```
```ts
private buildAuthHeaders(overrideToken?: string | null): HttpHeaders | null {
  const token = overrideToken ?? this.tokenProvider?.();
  if (!token) {
    return null;
  }
  return new HttpHeaders({ Authorization: `Bearer ${token}` });
}
```

Routing protection (`apps/validator-portal/src/app/app-routing.module.ts`):
```ts
{
  path: 'claims/verify',
  component: VerifyClaimComponent,
  canActivate: [AuthGuard]
},
```

### Validator API (ASP.NET Core)
Authentication/authorization setup (`services/validator-api/Program.cs`):
```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = SupabaseJwtValidator.BuildTokenValidationParameters(builder.Configuration);
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ValidatorOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ValidatorRoleRequirement());
    });
});
builder.Services.AddSingleton<IAuthorizationHandler, ValidatorRoleHandler>();
```

JWT validation helper (`services/validator-api/Auth/SupabaseJwtValidator.cs`):
```csharp
public static class SupabaseJwtValidator
{
    public const string JwtSecretConfigKey = "Supabase:JwtSecret";
    public const string DefaultIssuer = "supabase";

    public static TokenValidationParameters BuildTokenValidationParameters(IConfiguration configuration)
    {
    }
}
```

Validator role requirement (`services/validator-api/Auth/ValidatorRoleRequirement.cs`):
```csharp
public sealed class ValidatorRoleRequirement : IAuthorizationRequirement
{
    public const string RoleName = "validator";
}

public sealed class ValidatorRoleHandler : AuthorizationHandler<ValidatorRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidatorRoleRequirement requirement)
    {
    }
}
```

Verify endpoint protection (`services/validator-api/Controllers/ClaimsController.cs`):
```csharp
[HttpPost("verify")]
[Consumes("multipart/form-data")]
[Authorize(Policy = "ValidatorOnly")]
public async Task<ActionResult<VerificationResult>> Verify([FromForm] VerifyClaimRequest request, CancellationToken ct)
```

Auth tests (`services/validator-api/Tests/AuthTests.cs`):
```csharp
public async Task Verify_Returns401_WhenMissingToken()
```
```csharp
public async Task Verify_Returns403_WhenRoleIsNotValidator()
```
```csharp
public async Task Verify_AllowsValidatorRole()
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
- Note: metadata upload is still required until Slice 5C.

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

## Manual Auth Verification Checklist (Slice 5B)
1) capture-client logged out → Start Session disabled; uploads do not occur.
2) capture-client logged in → can create session and hashes appear in Supabase.
3) validator-portal logged out → `/claims/verify` redirects to `/login`.
4) validator-portal logged in but not validator → API returns 403.
5) validator-portal logged in as validator → verify endpoint returns 200 (metadata still required until Slice 5C).

## Demo Checklist (Happy Path) — Slice 5A
1) Start camera → preview visible
2) Start session → session row inserted in Supabase
3) Hashes appear near real-time in Supabase
4) Offline → pending count grows
5) Online → backlog drains

## Evidence
- dotnet test (validator-api): NOT RUN (no test execution in this sync)
- npm test/build (validator-portal): NOT RUN (no test execution in this sync)
- npm test/build (capture-client): NOT RUN (no test execution in this sync)
- Dev server start logs: NOT RUN
