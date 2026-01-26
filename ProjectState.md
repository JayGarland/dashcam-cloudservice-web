# ProjectState

## Slice Status
- Current slice: Slice 4A ✅
- Last commit: 4c9d99d Add claim verification API and core verification logic
- Next planned slice: Slice 4 (ffmpeg extraction + portal wiring + retention)
- Slice 4A (DONE ✅):
  - SupabaseHashStore real HTTP client via PostgREST
  - SupabaseOptions config keys + DI wiring helper
  - Unit tests with mocked HttpMessageHandler
- Slice 4 supabase setup (DONE ✅):
  - Supabase setup documentation + SQL migrations
  - Schema: capture_sessions + frame_hashes tables
  - RLS policies for secure client/server access
  - Retention cleanup function
  - Configuration scaffolding (no secrets committed)

**See [docs/supabase-setup.md](docs/supabase-setup.md) for step-by-step instructions.**

## Repo Tree (Relevant)
```text
F:.
|-- apps
|   `-- capture-client
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
|   `-- specV0.md
|-- services
|   `-- validator-api
|       |-- validator-api.csproj
|       |-- appsettings.json
|       |-- Controllers
|       |   `-- ClaimsController.cs
|       |-- Models
|       |   |-- CaptureModels.cs
|       |   |-- VerificationModels.cs
|       |   `-- VerifyClaimModels.cs
|       |-- Services
|       |   |-- DHash64.cs
|       |   |-- HammingDistance.cs
|       |   |-- HashMatcher.cs
|       |   |-- ISupabaseHashStore.cs
|       |   |-- IVideoFrameExtractor.cs
|       |   |-- ServiceExceptions.cs
|       |   |-- SupabaseHashStore.cs
|       |   |-- SupabaseOptions.cs
|       |   |-- SupabaseServiceCollectionExtensions.cs
|       |   `-- VerificationService.cs
|       `-- Tests
|           |-- DHash64Tests.cs
|           |-- HashMatcherTests.cs
|           |-- SupabaseHashStoreTests.cs
|           |-- VerificationServiceTests.cs
|           `-- validator-api.Tests.csproj
`-- ... (omitted)
```

## Core Interfaces (Slice 1–3)

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

### Supabase PostgREST reads (validator-api)
- capture_sessions: `GET {BaseUrl}/rest/v1/capture_sessions?session_id=eq.{sessionId}&select=*`
- frame_hashes: `GET {BaseUrl}/rest/v1/frame_hashes?session_id=eq.{sessionId}&select=*&order=elapsed_ms.asc`
- Required headers: `apikey: {ServiceRoleKey}`, `Authorization: Bearer {ServiceRoleKey}`, `Accept: application/json`
- Ordering constraint: `elapsed_ms asc` for frame hashes

## Repo Reality Snapshot (Slice 4A)
- `services/validator-api/Services/SupabaseHashStore.cs`: real PostgREST HTTP implementation of `ISupabaseHashStore`.
- `services/validator-api/Services/SupabaseOptions.cs`: config for Supabase BaseUrl/ServiceRoleKey/Schema/TimeoutSeconds.
- `services/validator-api/Services/SupabaseServiceCollectionExtensions.cs`: DI helper to register options + typed HttpClient.
- `services/validator-api/Tests/SupabaseHashStoreTests.cs`: unit tests with mocked HttpMessageHandler for URL/header/mapping/error cases.
- `services/validator-api/appsettings.json`: Supabase config placeholders.

## Evidence

```text
Test Files 4 passed (4)
Tests 10 passed (10)
Duration 870ms (transform 155ms, setup 0ms, collect 550ms, tests 36ms, environment 1ms, prepare 1.10s)
Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 27 ms - validator-api.Tests.dll (net8.0)
```

## Notes / Assumptions

* Multipart part names are `video` and `metadata`; metadata JSON parsing is case-insensitive.
* Supabase reads are REAL (PostgREST) as of Slice 4A; ffmpeg extraction remains mocked until later in Slice 4.
* `docs/slice3-log.md` is documentation-only (clarifies non-canonical service test fixtures).
