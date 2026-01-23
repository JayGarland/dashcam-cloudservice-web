# ProjectState

## Slice Status
- Current slice: Slice 1
- Last commit: d93ec8b feat: implement slice 1 (scaffolding and core types) - ref Plan v1 Initial project structure and DHash64 implementation
- Next planned slice: Slice 2 (Capture sampling + queue + uploader skeleton)

## Repo Tree (Relevant)
```text
F:.
|-- apps
|   `-- capture-client
|       |-- package.json
|       |-- tsconfig.json
|       |-- vitest.config.ts
|       `-- src
|           |-- models.ts
|           |-- hash
|           |   `-- dhash64.ts
|           `-- __tests__
|               `-- dhash64.spec.ts
|-- services
|   `-- validator-api
|       |-- validator-api.csproj
|       |-- Models
|       |   |-- CaptureModels.cs
|       |   `-- VerificationModels.cs
|       |-- Services
|       |   |-- DHash64.cs
|       |   `-- HammingDistance.cs
|       `-- Tests
|           |-- DHash64Tests.cs
|           `-- validator-api.Tests.csproj
`-- ... (omitted)
```

## Core Interfaces (Slice 1)

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

cd services/validator-api
dotnet test .\Tests\validator-api.Tests.csproj
```

## Evidence

```text
✓ src/__tests__/dhash64.spec.ts (4 tests)
Test Files 1 passed (1)
Tests 4 passed (4)

Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 2 ms - validator-api.Tests.dll (net8.0)
```

## Notes / Assumptions

* Repo tree is filtered to omit node_modules, bin, and obj directories.
* Tests run via Vitest in apps/capture-client and xUnit in services/validator-api (requires Node/npm and .NET SDK).