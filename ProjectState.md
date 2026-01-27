# ProjectState

## Current Slice Status
- Completed slices: 1, 2, 3, 4A–4D, 5A, 5B ✅
- Pending slices: 5C, 5D

## Repo Structure (Focused Snapshot)
(Tree command unavailable; snapshot derived from `rg --files`.)

### apps/capture-client/src
```text
apps/capture-client/src
|-- constants.ts
|-- models.ts
|-- capture
|   |-- frameSource.ts
|   `-- sampler.ts
|-- hash
|   `-- dhash64.ts
|-- storage
|   `-- hashQueue.ts
|-- supabase
|   |-- supabaseApi.ts
|   `-- uploader.ts
|-- ui
|   |-- capturePage.ts
|   `-- main.ts
`-- __tests__
    |-- dhash64.spec.ts
    |-- hashQueue.spec.ts
    |-- sampler.spec.ts
    `-- uploader.spec.ts
```

### services/validator-api (top-level + Services + Models + Tests)
```text
services/validator-api
|-- Program.cs
|-- validator-api.csproj
|-- appsettings.json
|-- appsettings.Development.example.json
|-- Auth
|   |-- SupabaseJwtValidator.cs
|   `-- ValidatorRoleRequirement.cs
|-- Controllers
|   `-- ClaimsController.cs
|-- Models
|   |-- CaptureModels.cs
|   |-- VerificationModels.cs
|   `-- VerifyClaimModels.cs
|-- Services
|   |-- DHash64.cs
|   |-- FfmpegOptions.cs
|   |-- FfmpegVideoFrameExtractor.cs
|   |-- HammingDistance.cs
|   |-- HashMatcher.cs
|   |-- IProcessRunner.cs
|   |-- ISupabaseHashStore.cs
|   |-- IVideoFrameExtractor.cs
|   |-- ProcessRunner.cs
|   |-- ServiceExceptions.cs
|   |-- SupabaseHashStore.cs
|   |-- SupabaseOptions.cs
|   |-- SupabaseServiceCollectionExtensions.cs
|   `-- VerificationService.cs
`-- Tests
    |-- AuthTests.cs
    |-- DHash64Tests.cs
    |-- FfmpegVideoFrameExtractorTests.cs
    |-- HashMatcherTests.cs
    |-- SupabaseHashStoreTests.cs
    |-- VerificationServiceTests.cs
    `-- validator-api.Tests.csproj
```

### apps/validator-portal/src/app (top-level + claims + api)
```text
apps/validator-portal/src/app
|-- app-routing.module.ts
|-- app.component.css
|-- app.component.html
|-- app.component.ts
|-- app.module.ts
|-- auth
|   |-- auth.component.css
|   |-- auth.component.html
|   |-- auth.component.ts
|   |-- auth.guard.ts
|   |-- auth.guard.spec.ts
|   |-- auth.module.ts
|   `-- auth.service.ts
|-- api
|   |-- validator-api.service.ts
|   `-- validator-api.service.spec.ts
|-- claims
|   `-- verify-claim
|       |-- verify-claim.component.css
|       |-- verify-claim.component.html
|       |-- verify-claim.component.ts
|       `-- verify-claim.module.ts
`-- models
    `-- verification.models.ts
```

## Key Interfaces (Exact Signatures)

### Capture Client (TypeScript)
```ts
export interface FrameData {
  rgba: Uint8ClampedArray;
  width: number;
  height: number;
}

export interface FrameSource {
  readFrame(): Promise<FrameData>;
}

export interface BrowserCameraOptions {
  videoElement?: HTMLVideoElement;
  width?: number;
  height?: number;
  facingMode?: "user" | "environment";
  deviceId?: string;
}

export class BrowserCameraFrameSource implements FrameSource {
  constructor(options: BrowserCameraOptions = {});
  async start(): Promise<void>;
  stop(): void;
  getVideoElement(): HTMLVideoElement;
  async readFrame(): Promise<FrameData>;
}

export interface SamplerConfig {
  samplingIntervalMs: number;
}

export interface SamplerDeps {
  frameSource: FrameSource;
  queue: HashQueue;
  now: () => number;
}

export class Sampler {
  constructor(session: CaptureSession, config: SamplerConfig, deps: SamplerDeps);
  start(): void;
  stop(): void;
}

export interface HashQueue {
  enqueue(record: FrameHashRecord): Promise<void>;
  getOldestPending(limit: number): Promise<FrameHashRecord[]>;
  markUploaded(sessionId: string, sampleIndex: number): Promise<void>;
  countPending?(sessionId?: string): Promise<number>;
}

export interface UploaderConfig {
  batchSize: number;
}

export class Uploader {
  constructor(queue: HashQueue, api: SupabaseApi, config?: Partial<UploaderConfig>);
  uploadPending(): Promise<void>;
  attachOnlineListener(): void;
}

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

export interface CaptureSession {
  sessionId: string;
  deviceClockStartEpochMs: number;
  samplingIntervalMs: number;
  algoVersion: AlgoVersion;
  clientVersion?: string;
}

export interface FrameHashRecord {
  sessionId: string;
  sampleIndex: number;
  elapsedMs: number;
  sampleTimestampEpochMs: number;
  hashHex: string;
  intervalMs: number;
  algoVersion: AlgoVersion;
  createdAtEpochMs: number;
  uploadState: "pending" | "uploaded";
}
```

### Validator API (C#)
```csharp
public interface IVideoFrameExtractor
{
    Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
        Stream videoStream,
        int intervalMs,
        CancellationToken ct);
}

public interface ISupabaseHashStore
{
    Task<CaptureSession?> GetSessionAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<FrameHashRecord>> GetFrameHashesAsync(string sessionId, CancellationToken ct);
}

public class VerificationService
{
    public VerificationService(ISupabaseHashStore store, IVideoFrameExtractor extractor);
    public Task<VerificationResult> VerifyAsync(
        Stream videoStream,
        VerifyClaimMetadata metadata,
        CancellationToken ct);
}

[ApiController]
[Route("api/claims")]
public class ClaimsController : ControllerBase
{
    [HttpPost("verify")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = "ValidatorOnly")]
    public async Task<ActionResult<VerificationResult>> Verify(
        [FromForm] VerifyClaimRequest request,
        CancellationToken ct);
}

public enum Verdict
{
    Verified,
    Suspicious,
    Inconclusive
}

public class VerificationResult
{
    public Verdict Verdict { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public int Threshold { get; set; }
    public int ToleranceMs { get; set; }
    public int IntervalMs { get; set; }
    public int ExpectedSamples { get; set; }
    public int MatchedSamples { get; set; }
    public double MatchRatio { get; set; }
    public double AvgDistance { get; set; }
    public int MaxDistance { get; set; }
    public List<MissingSpan> MissingSpans { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}
```

## Runtime Notes / How to Run

### capture-client
```bash
cd apps/capture-client
npm install
npm run dev
```
- URL: `http://localhost:8000`
- Mobile testing: HTTPS required for camera access.
 - Configure Supabase URL + anon key in the UI before signing in.

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

## Auth Setup (Slice 5B)
1) Create a validator user in Supabase Auth (email/password).
2) Assign the validator role via app metadata (example SQL):
   ```sql
   update auth.users
   set app_metadata = jsonb_set(coalesce(app_metadata, '{}'::jsonb), '{role}', '"validator"', true)
   where email = 'validator@example.com';
   ```
3) Sign out/in so the JWT includes the updated role claim.
4) Set `Supabase:JwtSecret` in `services/validator-api/appsettings.Development.json` (or env var).

## Auth Manual Checklist — Slice 5B
1) capture-client: with no login, Start Session stays disabled and uploads never fire.
2) capture-client: sign in, then Start Session enables and inserts succeed.
3) validator-portal: opening `/claims/verify` when logged out redirects to `/login`.
4) validator-api: POST `/api/claims/verify` with no Authorization header returns 401.
5) validator-api: POST with a valid token missing role=validator returns 403.
6) validator-api: POST with validator token returns 200 and normal verification payload.

## Demo Checklist (Happy Path) — Slice 5A
1) Start camera → preview visible
2) Start session → session row inserted in Supabase
3) Hashes appear near real-time in Supabase
4) Offline → pending count grows
5) Online → backlog drains

## Evidence
- Tests: not run in this sync (new: validator-api AuthTests, validator-portal AuthGuard spec)
- Dev server start logs: not captured in this sync
