# Plan v1

## 1. Physical Architecture (File Tree)

> Note: mark **NEW** files to create in this slice; anything not listed here is treated as **EXISTING/UNCHANGED** for now. If the repo already has equivalent folders, keep them and place files accordingly.

```
repo-root/
├─ apps/
│  ├─ capture-client/                          (EXISTING or CREATE DIR)
│  │  └─ src/
│  │     ├─ models.ts                          (NEW)
│  │     └─ hash/
│  │        └─ dhash64.ts                      (NEW)
│  │     └─ __tests__/                         (EXISTING or CREATE DIR)
│  │        └─ dhash64.spec.ts                 (NEW)
│  └─ validator-portal/                        (EXISTING - untouched in Slice 1)
│
├─ services/
│  └─ validator-api/                           (EXISTING or CREATE DIR)
│     ├─ Models/
│     │  ├─ CaptureModels.cs                   (NEW)
│     │  └─ VerificationModels.cs              (NEW)
│     ├─ Services/
│     │  ├─ DHash64.cs                         (NEW)
│     │  └─ HammingDistance.cs                 (NEW)
│     └─ Tests/                                (EXISTING or CREATE DIR)
│        └─ DHash64Tests.cs                    (NEW)
│
└─ (supabase/, other folders)                  (EXISTING - untouched in Slice 1)
```

**Why this structure for Slice 1:** it isolates the #1 invariant—**bit-identical dHash64** across TS and C#—and gives us immediate verifiable tests before any capture/uploader/ffmpeg work.

---

## 2. Step-by-Step Implementation Strategy (Slicing)

### Slice 1 — Hash Core + Shared Models + Cross-runtime Consistency Tests (Runnable)

**Goal:** Implement **canonical dHash64 + Hamming distance** in TS and C#, plus unit tests with deterministic fixtures.
**Verifiable:** `npm test` (or existing JS test command) passes; `dotnet test` passes.

### Slice 2 — Capture Client Happy Path: Sampling + IndexedDB Queue + Supabase Upload Skeleton

**Goal:** Create capture session, sample at 500ms, compute hashes, enqueue records, attempt upload; stub auth if needed.
**Verifiable:** local run shows hashes enqueued and (if configured) inserted to Supabase; basic integration tests for queue ordering.

### Slice 3 — Validator API Happy Path: Verify Endpoint + Matching Logic (ffmpeg mocked)

**Goal:** Implement `/api/claims/verify` contract, matching window + verdict logic; mock frame extraction + Supabase reads.
**Verifiable:** API unit/integration tests cover Verified/Suspicious/Inconclusive and error codes.

### Slice 4 — Full Wiring: ffmpeg one-run extraction + Supabase integration + Portal UI + Retention

**Goal:** Real ffmpeg extraction, real Supabase reads, Angular upload UI, retention cleanup job + “expired” behavior.
**Verifiable:** demo script end-to-end and retention scenario.

---

## 3. The Core Loop Selection

**Slice 1 Loop:** **PVI (Plan → Verify → Implement)**

* **Plan:** lock canonical algorithm + fixtures + bit order
* **Verify:** write tests first (golden vectors)
* **Implement:** TS + C# implementations until tests pass

---

# Copilot Instruction Packet — SLICE #1 (Paste to Copilot)

```text
[CONTEXT]
We are implementing Slice 1 of the Dashcam Hash Timeline + Video Validator.
Current Goal: Implement canonical dHash64 (9x8 grayscale, 64-bit) + Hamming distance in BOTH:
- Browser TypeScript (capture client)
- ASP.NET Core C# (validator API)
And add deterministic unit tests so we can prove cross-runtime bit-identical output.

This slice MUST be runnable/verifiable by tests; do NOT implement capture, uploader, Supabase, ffmpeg, or Angular UI yet.

[FILES TO CREATE/EDIT]
TypeScript (apps/capture-client):
- apps/capture-client/src/models.ts
- apps/capture-client/src/hash/dhash64.ts
- apps/capture-client/src/__tests__/dhash64.spec.ts

C# (services/validator-api):
- services/validator-api/Models/CaptureModels.cs
- services/validator-api/Models/VerificationModels.cs
- services/validator-api/Services/DHash64.cs
- services/validator-api/Services/HammingDistance.cs
- services/validator-api/Tests/DHash64Tests.cs

If the repo uses different test folder conventions, follow the repo’s existing convention, but keep file names and class/function names the same.

[SPECIFICATION]
Hard invariants (must not change):
- Hash algorithm is dHash, 64-bit, grayscale, resized to 9×8, adjacent horizontal comparisons.
- Bit packing order is row-major; bitIndex = dy*8 + dx; LSB corresponds to bitIndex 0.
- Output hex must be lowercase, exactly 16 hex chars.

Canonical dHash64 algorithm (MUST match in TS and C#):
1) Input: RGBA pixels + src width/height.
2) Resize to 9×8 using CUSTOM bilinear (do NOT rely on Canvas scaling or library resize):
   - For dst pixel (dx, dy):
     sx = (dx + 0.5) * (srcW / 9) - 0.5
     sy = (dy + 0.5) * (srcH / 8) - 0.5
     Bilinear sample 4 neighbors with edge clamping.
3) Convert RGB to grayscale luma using Rec.709:
   Y = 0.2126*R + 0.7152*G + 0.0722*B
   Use float/double internally; comparisons use these values.
4) For each dy=0..7 and dx=0..7 compare Y[dy][dx] > Y[dy][dx+1]:
   bit = 1 if true else 0
5) Pack bits:
   bitIndex = dy*8 + dx
   value |= (bit ? 1 : 0) << bitIndex
6) Return:
   - TS: bigint (unsigned 64-bit) and helper to hex
   - C#: ulong and helper to hex

Required TS exports in apps/capture-client/src/hash/dhash64.ts:
- export function dhash64FromRgba(rgba: Uint8ClampedArray, srcW: number, srcH: number): bigint
- export function dhash64HexFromRgba(rgba: Uint8ClampedArray, srcW: number, srcH: number): string
- export function hammingDistance64(aHex: string, bHex: string): number

Required C# in services/validator-api/Services:
- public static class DHash64
  - public static ulong FromRgba(byte[] rgba, int srcW, int srcH) // rgba length = srcW*srcH*4
  - public static string ToHex(ulong value) // lowercase, 16 chars
- public static class HammingDistance
  - public static int BetweenHex64(string aHex, string bHex)

Models scaffolding (no logic yet, just types):
TS apps/capture-client/src/models.ts:
- AlgoVersion type = "dhash64_v1"
- CaptureSession interface with sessionId, deviceClockStartEpochMs, samplingIntervalMs, algoVersion, optional clientVersion
- FrameHashRecord interface with sessionId, sampleIndex, elapsedMs, sampleTimestampEpochMs, hashHex, intervalMs, algoVersion, createdAtEpochMs, uploadState

C# Models:
- CaptureModels.cs: CaptureSession + FrameHashRecord equivalents (properties; no behavior)
- VerificationModels.cs: placeholder response model for future (Verdict enum + metrics fields) – just types

Implementation constraints:
- C# must NOT use System.Drawing. (Slice 1 doesn’t need ImageSharp yet because we accept RGBA input directly.)
- No global mutable state.
- Keep functions pure and deterministic.

[TESTING STRATEGY]
Write tests FIRST (golden vectors), then implement until passing.

TS tests (apps/capture-client/src/__tests__/dhash64.spec.ts):
1) "should produce expected hex for a deterministic synthetic RGBA fixture"
   - Build a small source image in code (e.g., 18x16 RGBA) with a deterministic gradient/pattern.
   - Compute hashHex and assert it equals a fixed expected string.
2) "bit packing should be LSB-first row-major"
   - Craft a source image such that after resize+grayscale, only the first comparison (dy=0, dx=0) is true.
   - Expect hex ends with ...0001 (LSB set).
3) hammingDistance tests:
   - dist("000...0","000...0") == 0
   - dist("000...0","000...1") == 1
   - dist("ffffffffffffffff","000...0") == 64

C# tests (services/validator-api/Tests/DHash64Tests.cs) using the repo’s existing test framework (prefer xUnit):
1) Use THE SAME synthetic RGBA fixture logic as TS (same width/height/pattern).
2) Assert DHash64.ToHex(DHash64.FromRgba(...)) equals the SAME expected hex constant used in TS.
3) Same Hamming distance assertions.

Important: pick ONE fixture pattern and one expected hex constant; keep it identical across both test suites.
If needed, you may temporarily write a small helper inside the test to generate the expected constant ONCE (but final tests must assert against a fixed literal so we detect regressions).

Verification commands:
- Run the existing JS test command in repo (npm/pnpm/yarn). If none exists, add minimal config consistent with repo, but avoid large tooling changes.
- Run dotnet test for validator-api.

[COMMAND]
1) Create the files listed above (respect existing repo structure).
2) Implement tests first (TS + C#) with the shared golden vectors.
3) Implement dhash64 + hex + hamming in TS and C# until tests pass.
4) Ensure formatting/lint/build passes.
5) DO NOT implement capture sampling, IndexedDB, Supabase, ffmpeg, or Angular UI in this slice.
6) When done, output:
   - the final expected golden hex constant
   - how to run the tests (exact commands)
   - any assumptions made about existing tooling in the repo
```
