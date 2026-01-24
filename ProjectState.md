# ProjectState

## Slice Status
- Current slice: Slice 2 ✅
- Last commit: 8c54a0e Add frame sampling, hashing, and upload modules with tests
- Next planned slice: Slice 3 (Validator API verify endpoint + matcher + ffmpeg mocked)

## Repo Tree (Relevant)
```text
F:.
|-- apps
|   `-- capture-client
|       |-- ... (omitted)
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
|-- services
|   `-- validator-api
|       |-- validator-api.csproj
|       |-- Models
|       |   |-- CaptureModels.cs
|       |   `-- VerificationModels.cs
|       |-- Services
|       |   |-- DHash64.cs
|       |   `-- HammingDistance.cs
|       |-- Tests
|       |   |-- DHash64Tests.cs
|       |   `-- validator-api.Tests.csproj
|       `-- ... (omitted)
`-- ... (omitted)
```

## Core Interfaces (Slice 1–2)

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

Unchanged in Slice 2.

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
```

## Golden Vector (Cross-runtime)

* Fixture: 18x16, deterministic pattern r=(x*17 + y*31)%256, g=(x*13 + y*7 + 50)%256, b=(x*3 + y*29 + 90)%256, a=255
* Expected hashHex: 1a1830f0624cf0c0

## Verification Commands

```bash
cd apps/capture-client
npm test
```

## Evidence

```text
Test Files 4 passed (4)
Tests 10 passed (10)
Duration 870ms (transform 155ms, setup 0ms, collect 550ms, tests 36ms, environment 1ms, prepare 1.10s)
```

## Notes / Assumptions

* Sampler enforces config interval equality with session.samplingIntervalMs and throws on mismatch.
* FetchSupabaseApi is a stub that throws unless configured; IndexedDbHashQueue is not implemented.
* Validator API tests were not rerun for this snapshot.
