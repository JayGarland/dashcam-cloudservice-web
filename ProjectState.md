# ProjectState

## Slice Status
- Current slice: Slice 4C ✅ DONE
- Last commit: 4c9d99d Add claim verification API and core verification logic
- Next planned slice: Slice 4D (portal wiring)
- Slice 4A (DONE ✅):
  - SupabaseHashStore real HTTP client via PostgREST
  - SupabaseOptions config keys + DI wiring helper
  - Unit tests with mocked HttpMessageHandler
- Slice 4B (DONE ✅):
  - Supabase schema: capture_sessions + frame_hashes tables with indexes
  - RLS policies for secure client (anon key INSERT) / server (service_role SELECT) access
  - Retention cleanup function: cleanup_expired_sessions(retention_hours)
  - Documentation hardening: secrets safety, SQL apply order, RLS model
  - PlantUML diagram updated with database layer
  - NO secrets committed (gitignore enforced, verified)
- Slice 4C (DONE ✅):
  - FfmpegVideoFrameExtractor one-run ffmpeg extraction
  - showinfo pts_time parsing -> elapsedMs mapping
  - PNG decode to RGBA with ImageSharp (no System.Drawing)
  - IProcessRunner abstraction + ProcessRunner implementation
  - Unit tests with mocked process runner and real parsing/PNG decode

**See [docs/supabase-setup.md](docs/supabase-setup.md) for step-by-step setup instructions.**
**See [docs/slice4b-log.md](docs/slice4b-log.md) for Slice 4B completion details.**
**See [docs/slice4c-log.md](docs/slice4c-log.md) for Slice 4C completion details.**

## Repo Tree (Relevant)
```text
F:.
|-- apps
|   `-- capture-client
|       |-- .env.example (committed)
|       |-- .env.local (gitignored - local secrets)
|       `-- src
|           |-- constants.ts
|           |-- models.ts
|           |-- capture
|           |   |-- frameSource.ts
|           |   `-- sampler.ts
|           |-- hash
|           |   `-- dhash64.ts
|           |-- storage
|           |   `-- hashQueue.ts
|           |-- supabase
|           |   |-- supabaseApi.ts
|           |   `-- uploader.ts
|           `-- __tests__
|               |-- dhash64.spec.ts
|               |-- hashQueue.spec.ts
|               |-- sampler.spec.ts
|               `-- uploader.spec.ts
|-- docs
|   |-- planV1.md
|   |-- planV2.md
|   |-- slice1-log.md
|   |-- slice2-log.md
|   |-- slice3-log.md
|   |-- slice4b-log.md
|   |-- slice4c-log.md
|   |-- specV0.md
|   |-- supabase-setup.md
|   `-- flow
|       `-- current-system-flow.puml
|-- services
|   `-- validator-api
|       |-- validator-api.csproj
|       |-- appsettings.json
|       |-- appsettings.Development.example.json (committed)
|       |-- appsettings.Development.json (gitignored - local secrets)
|       |-- Controllers
|       |   `-- ClaimsController.cs
|       |-- Models
|       |   |-- CaptureModels.cs
|       |   |-- VerificationModels.cs
|       |   `-- VerifyClaimModels.cs
|       |-- Services
|       |   |-- DHash64.cs
|       |   |-- FfmpegOptions.cs
|       |   |-- FfmpegVideoFrameExtractor.cs
|       |   |-- HammingDistance.cs
|       |   |-- HashMatcher.cs
|       |   |-- IProcessRunner.cs
|       |   |-- ISupabaseHashStore.cs
|       |   |-- IVideoFrameExtractor.cs
|       |   |-- ProcessRunner.cs
|       |   |-- ServiceExceptions.cs
|       |   |-- SupabaseHashStore.cs
|       |   |-- SupabaseOptions.cs
|       |   |-- SupabaseServiceCollectionExtensions.cs
|       |   `-- VerificationService.cs
|       `-- Tests
|           |-- DHash64Tests.cs
|           |-- FfmpegVideoFrameExtractorTests.cs
|           |-- HashMatcherTests.cs
|           |-- SupabaseHashStoreTests.cs
|           |-- VerificationServiceTests.cs
|           `-- validator-api.Tests.csproj
|-- supabase
|   |-- migrations
|   |   |-- 0001_init_capture_sessions_and_frame_hashes.sql
|   |   `-- 0002_rls_policies.sql
|   `-- functions
|       `-- retention_cleanup.sql
`-- .gitignore (enforces secrets safety)
```

## Core Interfaces (Slice 1–4C)

### TypeScript

```ts
export function dhash64FromRgba(
  rgba: Uint8ClampedArray,
  srcW: number,
  srcH: number
): bigint;

export function dhash64HexFromRgba(
  rgba: Uint8ClampedArray,
  srcW: number,
  srcH: number
): string;

export function hammingDistance64(aHex: string, bHex: string): number;

export type AlgoVersion = "dhash64_v1";

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

export const DEFAULT_INTERVAL_MS = 500;
export const DEFAULT_BATCH_SIZE = 100;
export const DEFAULT_ALGO_VERSION: "dhash64_v1" = "dhash64_v1";

export interface FrameData {
  rgba: Uint8ClampedArray;
  width: number;
  height: number;
}

export interface FrameSource {
  readFrame(): Promise<FrameData>;
}

export function makeSolidColorFrameSource(
  width: number,
  height: number,
  rgba: [number, number, number, number]
): FrameSource;

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

export class InMemoryHashQueue implements HashQueue {
  enqueue(record: FrameHashRecord): Promise<void>;
  getOldestPending(limit: number): Promise<FrameHashRecord[]>;
  markUploaded(sessionId: string, sampleIndex: number): Promise<void>;
  countPending(sessionId?: string): Promise<number>;
}

export interface SupabaseApi {
  insertSession(session: CaptureSession): Promise<void>;
  insertFrameHashes(records: FrameHashRecord[]): Promise<void>;
}

export interface SupabaseConfig {
  url?: string;
  anonKey?: string;
}

export class FetchSupabaseApi implements SupabaseApi {
  constructor(config: SupabaseConfig);
  insertSession(session: CaptureSession): Promise<void>;
  insertFrameHashes(records: FrameHashRecord[]): Promise<void>;
}

export interface UploaderConfig {
  batchSize: number;
}

export class Uploader {
  constructor(queue: HashQueue, api: SupabaseApi, config?: Partial<UploaderConfig>);
  uploadPending(): Promise<void>;
  attachOnlineListener(): void;
}
```

### C#

```csharp
public static class DHash64
{
    public static ulong FromRgba(byte[] rgba, int srcW, int srcH);
    public static string ToHex(ulong value);
}

public static class HammingDistance
{
    public static int BetweenHex64(string aHex, string bHex);
}

public record ExtractedFrame(int ElapsedMs, byte[] Rgba, int Width, int Height);

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

public sealed record ProcessRunRequest(string FileName, string Arguments, string? WorkingDirectory = null);

public sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct);
}

public class HashMatcher
{
    public HashMatcher(int threshold = 5, int toleranceMs = 200);
    public (int matched, double avgDist, int maxDist, List<MissingSpan> missingSpans) Match(
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<FrameHashRecord> candidates,
        int intervalMs);
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
    public Task<ActionResult<VerificationResult>> Verify([FromForm] VerifyClaimRequest request, CancellationToken ct);
}

public class VerifyClaimMetadata
{
    public string SessionId { get; set; }
    public long DeviceClockStartEpochMs { get; set; }
    public int SamplingIntervalMs { get; set; }
    public string AlgoVersion { get; set; }
    public int? ToleranceMs { get; set; }
}

public class VerifyClaimRequest
{
    public IFormFile? Video { get; set; }
    public IFormFile? Metadata { get; set; }
}

public class CaptureSession
{
    public string SessionId { get; set; }
    public long DeviceClockStartEpochMs { get; set; }
    public int SamplingIntervalMs { get; set; }
    public string AlgoVersion { get; set; }
    public string? ClientVersion { get; set; }
}

public class FrameHashRecord
{
    public string SessionId { get; set; }
    public int SampleIndex { get; set; }
    public int ElapsedMs { get; set; }
    public long SampleTimestampEpochMs { get; set; }
    public string HashHex { get; set; }
    public int IntervalMs { get; set; }
    public string AlgoVersion { get; set; }
    public long CreatedAtEpochMs { get; set; }
    public string UploadState { get; set; }
}

public enum Verdict
{
    Verified,
    Suspicious,
    Inconclusive
}

public class MissingSpan
{
    public int StartElapsedMs { get; set; }
    public int EndElapsedMs { get; set; }
    public string Reason { get; set; }
}

public class VerificationResult
{
    public Verdict Verdict { get; set; }
    public string SessionId { get; set; }
    public int Threshold { get; set; }
    public int ToleranceMs { get; set; }
    public int IntervalMs { get; set; }
    public int ExpectedSamples { get; set; }
    public int MatchedSamples { get; set; }
    public double MatchRatio { get; set; }
    public double AvgDistance { get; set; }
    public int MaxDistance { get; set; }
    public List<MissingSpan> MissingSpans { get; set; }
    public List<string> Notes { get; set; }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message);
}

public class SessionExpiredException : Exception
{
    public SessionExpiredException(string message);
}

// ValidationException is from System.ComponentModel.DataAnnotations.
public class ValidationException : Exception;
```

## Golden Vector (Canonical)

* Fixture: 18x16, deterministic pattern r=(x*17 + y*31)%256, g=(x*13 + y*7 + 50)%256, b=(x*3 + y*29 + 90)%256, a=255
* Expected hashHex: 1a1830f0624cf0c0
* Note: verification-service fixtures are NON-CANONICAL and must not redefine this value.

## Verification Commands

```bash
cd apps/capture-client
npm test

cd services/validator-api
dotnet test
```

## Contracts that MUST remain stable

### Supabase PostgREST API (validator-api reads)
- **capture_sessions GET**: `{BaseUrl}/rest/v1/capture_sessions?session_id=eq.{sessionId}&select=*`
- **frame_hashes GET**: `{BaseUrl}/rest/v1/frame_hashes?session_id=eq.{sessionId}&select=*&order=elapsed_ms.asc`
- **Required headers**: `apikey: {ServiceRoleKey}`, `Authorization: Bearer {ServiceRoleKey}`, `Accept: application/json`
- **Ordering constraint**: `elapsed_ms asc` for frame hashes (critical for matching algorithm)

### Supabase PostgREST API (capture-client writes)
- **capture_sessions INSERT**: `{BaseUrl}/rest/v1/capture_sessions` (POST with anon key)
- **frame_hashes INSERT**: `{BaseUrl}/rest/v1/frame_hashes` (POST with anon key)
- **Required headers**: `apikey: {AnonKey}`, `Authorization: Bearer {AnonKey}`, `Content-Type: application/json`
- **RLS**: Allows INSERT for anon/authenticated, SELECT only for service_role

### Supabase Retention (manual or scheduled)
- **Function signature**: `public.cleanup_expired_sessions(retention_hours INTEGER) RETURNS INTEGER`
- **Behavior**: Deletes sessions WHERE `created_at < NOW() - retention_hours`, cascades to frame_hashes
- **Execution**: Requires service_role or explicit GRANT EXECUTE permission

### ffmpeg Extraction (validator-api reads)
- **One-run rule**: One ffmpeg process per verification; no per-frame spawning.
- **Args formation**: fps = 1000.0 / intervalMs; output pattern `frame_%06d.png`; filter includes `showinfo`.
- **Mapping rule**: pts_time order maps to output frame order.

## Repo Reality Snapshot

### Slice 4A (Validator API - Supabase Integration)
- `services/validator-api/Services/SupabaseHashStore.cs`: real PostgREST HTTP implementation of `ISupabaseHashStore`.
- `services/validator-api/Services/SupabaseOptions.cs`: config for Supabase BaseUrl/ServiceRoleKey/Schema/TimeoutSeconds.
- `services/validator-api/Services/SupabaseServiceCollectionExtensions.cs`: DI helper to register options + typed HttpClient.
- `services/validator-api/Tests/SupabaseHashStoreTests.cs`: unit tests with mocked HttpMessageHandler for URL/header/mapping/error cases.
- `services/validator-api/appsettings.json`: Supabase config placeholders.

### Slice 4B (Supabase Schema + RLS + Retention)
- `supabase/migrations/0001_init_capture_sessions_and_frame_hashes.sql`: Creates tables, indexes, constraints, comments.
- `supabase/migrations/0002_rls_policies.sql`: Enables RLS, creates INSERT policies (anon) and SELECT policies (service_role).
- `supabase/functions/retention_cleanup.sql`: Creates `cleanup_expired_sessions(retention_hours)` function with cascade delete.
- `docs/supabase-setup.md`: Comprehensive setup guide with SQL apply order, secrets safety, testing steps.
- `docs/slice4b-log.md`: Documentation of Slice 4B completion, apply checklist, security evidence.
- `docs/flow/current-system-flow.puml`: PlantUML diagram showing capture flow, verification flow, retention cleanup.
- `.gitignore`: Enforces secrets safety (`.env.local`, `appsettings.Development.json`).

### Slice 4C (ffmpeg Extraction)
- `services/validator-api/Services/FfmpegVideoFrameExtractor.cs`: one-run ffmpeg extraction, showinfo parsing, PNG->RGBA via ImageSharp.
- `services/validator-api/Services/FfmpegOptions.cs`: config for ffmpeg path (`Ffmpeg:Path`).
- `services/validator-api/Services/IProcessRunner.cs`: process execution abstraction (mockable).
- `services/validator-api/Services/ProcessRunner.cs`: real process runner using System.Diagnostics.Process.
- `services/validator-api/Tests/FfmpegVideoFrameExtractorTests.cs`: deterministic tests for args, parsing, decode, ordering, and failures.
- `services/validator-api/appsettings.json`: ffmpeg path placeholder.

## Evidence

```text
Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 331 ms - validator-api.Tests.dll (net8.0)
```

## Notes / Assumptions

* Multipart part names are `video` and `metadata`; metadata JSON parsing is case-insensitive.
* Supabase reads are REAL (PostgREST) as of Slice 4A; ffmpeg extraction is REAL as of Slice 4C.
* `docs/slice3-log.md` is documentation-only (clarifies non-canonical service test fixtures).
