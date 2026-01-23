# Spec v0 — Dashcam Hash Timeline + Video Validator (dHash64)

## 1. Context & Goal

Build a **web-based dashcam capture client** that samples camera frames, computes **on-device dHash64**, streams **hashes+metadata** to Supabase with offline buffering, and an **authenticated Angular validator portal + ASP.NET Core API** that verifies an uploaded accident video by re-hashing sampled frames and matching via **Hamming distance** with timestamp tolerance.

---

## 2. Core Constraints & Invariants

### Tech Stack

* **Capture client:** TypeScript (browser). Use Web APIs: `getUserMedia`, `MediaRecorder`, `Canvas`/`OffscreenCanvas`, `IndexedDB`.
* **Validator portal:** Angular (latest in repo), calls ASP.NET Core API.
* **Validator API:** ASP.NET Core (C#). Video decoding via **ffmpeg CLI** (not video libraries).
* **Image processing in C#:** **SixLabors.ImageSharp** (do **not** use `System.Drawing`).
* **Storage/Auth/Cron:** Supabase (Postgres + Auth + scheduled job).
* **Hash algorithm:** **dHash 64-bit** (9×8 grayscale), **must be bit-identical** across Browser TS and C#.
* **Matching:** **Hamming distance**, not exact equality.

### File Structure (create/modify)

> Use these folders; if repo already has equivalents, match existing conventions but keep the module boundaries.

* `apps/capture-client/` (browser TS web app)

  * `src/hash/dhash64.ts`
  * `src/capture/sampler.ts`
  * `src/storage/hashQueue.ts` (IndexedDB)
  * `src/supabase/uploader.ts`
  * `src/models.ts`
* `apps/validator-portal/` (Angular)

  * `src/app/claims/claim-upload/*`
  * `src/app/auth/*`
  * `src/app/api/validator-api.service.ts`
* `services/validator-api/` (ASP.NET Core)

  * `Controllers/ClaimsController.cs`
  * `Services/VideoFrameExtractorFfmpeg.cs`
  * `Services/DHash64.cs`
  * `Services/HashMatcher.cs`
  * `Services/SupabaseHashStore.cs`
  * `Models/*`
  * `appsettings.json` (ffmpeg path, supabase url/keys)
* `supabase/`

  * `migrations/*` (tables, indexes, RLS)
  * `functions/retention_cleanup.sql`
  * `cron/*` (scheduled job definition)

### Naming Conventions

* TypeScript: `camelCase` vars/functions, `PascalCase` types/classes, file names `kebab-case.ts` or existing repo style.
* C#: `PascalCase` for public members, `_camelCase` private fields, interfaces `I*`.

### Prohibited Patterns

* **No `System.Drawing`** in C#.
* **No global mutable singletons** for state; use injected services (C#) and module instances (TS).
* **No ffmpeg per-frame process spawn** (too slow). Extract frames in **one ffmpeg run per verification**.
* **No storing video/frames** in Supabase. Supabase stores **hashes + metadata only**.

### Hard Invariants (must NOT change)

* Hashing occurs **on-device during recording** (client is source-of-truth timeline).
* Supabase stores **only hashes+metadata**.
* Time reference anchor includes **device clock at recording start** (`deviceClockStartEpochMs`).
* Matching uses **Hamming distance** with **timestamp tolerance window**.

---

## 3. API / Interface Contracts

### Shared Constants

```ts
// Capture defaults
DEFAULT_INTERVAL_MS = 500
DEFAULT_TOLERANCE_MS = 200

// Matching thresholds (from spike)
DEFAULT_DISTANCE_THRESHOLD = 5
VERIFIED_MATCH_RATIO = 0.90
SUSPICIOUS_MATCH_RATIO = 0.80

// Missing span heuristic
MISSING_SPAN_FLAG_MS = 5000  // 5 seconds
```

### Supabase Data Model (Postgres)

#### Table: `capture_sessions`

* `id uuid primary key` (sessionId)
* `owner_user_id uuid not null` (Supabase Auth user id)
* `created_at timestamptz not null default now()`
* `device_clock_start_epoch_ms bigint not null`
* `sampling_interval_ms int not null`
* `algo_version text not null` (e.g., `"dhash64_v1"`)
* `client_version text null`
* Index: `(owner_user_id, created_at)`

#### Table: `frame_hashes`

* `id bigserial primary key`
* `session_id uuid not null references capture_sessions(id) on delete cascade`
* `owner_user_id uuid not null`
* `sample_index int not null` (0..N)
* `elapsed_ms int not null` (monotonic from session start; multiples of interval ideally)
* `sample_timestamp_epoch_ms bigint not null` (`device_clock_start_epoch_ms + elapsed_ms`)
* `hash_hex text not null` (lowercase 16 hex chars)
* `interval_ms int not null`
* `algo_version text not null`
* `created_at timestamptz not null default now()`
* Unique: `(session_id, sample_index)`
* Index: `(session_id, sample_timestamp_epoch_ms)`

#### RLS Policies

* `capture_sessions`: owner can `select/insert` where `owner_user_id = auth.uid()`.
* `frame_hashes`: owner can `select/insert` where `owner_user_id = auth.uid()`.
* Validator portal **must not** read Supabase directly; it calls validator API. Validator API uses **service role key** to bypass RLS safely.

#### Retention Job

* Scheduled deletion: delete sessions older than a configurable window (e.g., `RETENTION_HOURS=1..24`), cascades to `frame_hashes`.

---

### Capture Client Contracts (TypeScript)

#### Models

```ts
export type AlgoVersion = "dhash64_v1";

export interface CaptureSession {
  sessionId: string; // uuid
  deviceClockStartEpochMs: number; // Date.now() at start
  samplingIntervalMs: number; // default 500
  algoVersion: AlgoVersion; // dhash64_v1
  clientVersion?: string;
}

export interface FrameHashRecord {
  sessionId: string;
  sampleIndex: number;
  elapsedMs: number;                // monotonic (preferred)
  sampleTimestampEpochMs: number;   // deviceClockStartEpochMs + elapsedMs
  hashHex: string;                  // 16 hex chars, lowercase
  intervalMs: number;
  algoVersion: AlgoVersion;
  createdAtEpochMs: number;         // local creation timestamp
  uploadState: "pending" | "uploaded";
}
```

#### Hashing

```ts
export function dhash64FromRgba(
  rgba: Uint8ClampedArray, // length = srcW*srcH*4
  srcW: number,
  srcH: number
): bigint; // unsigned 64-bit represented as BigInt

export function dhash64HexFromRgba(...): string; // 16-char lowercase hex
export function hammingDistance64(aHex: string, bHex: string): number;
```

**dHash64 canonical algorithm (MUST match C# exactly):**

1. Input: source RGBA pixels.
2. Resize to **9×8** using **custom bilinear** resize (not browser canvas scaling):

   * For each dst pixel `(dx, dy)`, map to src float coords:

     * `sx = (dx + 0.5) * (srcW / 9) - 0.5`
     * `sy = (dy + 0.5) * (srcH / 8) - 0.5`
   * Bilinear sample 4 neighbors with clamping at edges.
3. Convert resized RGB to grayscale **luma** using Rec.709:

   * `Y = 0.2126*R + 0.7152*G + 0.0722*B`
   * Keep as float; comparisons use float values.
4. For each row `dy in [0..7]` and col `dx in [0..7]`:

   * `bit = (Y[dy][dx] > Y[dy][dx+1]) ? 1 : 0`
5. Bit packing order:

   * `bitIndex = dy*8 + dx` (row-major)
   * Set **LSB = bitIndex 0**, i.e.:

     * `value |= (bit ? 1n : 0n) << BigInt(bitIndex)`
6. Output hex:

   * lowercase, exactly 16 hex chars (`padStart(16, "0")`).

#### Sampling & Capture

```ts
export interface SamplerConfig {
  samplingIntervalMs: number; // default 500
}

export interface CaptureController {
  start(): Promise<CaptureSession>;
  stop(): Promise<{ session: CaptureSession; videoBlob?: Blob; metadataJson: Blob }>;
}
```

Sampling rules:

* Create `sessionId` at start.
* Set `deviceClockStartEpochMs = Date.now()` at start.
* Maintain `elapsedMs` as `sampleIndex * samplingIntervalMs` (monotonic, stable).
* For each tick:

  * Grab current video frame pixels from `<video>` via `CanvasRenderingContext2D.getImageData`.
  * Compute `hashHex`.
  * Create `FrameHashRecord` with `sampleTimestampEpochMs = deviceClockStartEpochMs + elapsedMs`.
  * Persist to IndexedDB queue **before** attempting upload.

Prefer frame capture:

* If available, use `HTMLVideoElement.requestVideoFrameCallback` to keep frame reads aligned to decode (still schedule sampling by interval).
* Fallback: `setInterval` + canvas draw.

#### Offline Buffering & Upload

```ts
export interface HashQueue {
  enqueue(record: FrameHashRecord): Promise<void>;
  getOldestPending(limit: number): Promise<FrameHashRecord[]>;
  markUploaded(sessionId: string, sampleIndex: number): Promise<void>;
}

export interface Uploader {
  uploadPending(): Promise<void>; // uploads from earliest pending forward
}
```

Upload behavior:

* “On-the-fly”: after enqueue, trigger `uploadPending()`.
* If upload fails, keep records pending.
* When network returns (listen `window.online`), resume upload starting from **earliest pending timestamp**.
* Use batch inserts (e.g., 100 records per request) to Supabase for efficiency.
* Never reorder records inside a session: upload by `sampleIndex ASC`.

Auth:

* Capture client must authenticate with Supabase Auth (email/password or magic link — choose simplest existing).
* Every insert must include `owner_user_id` (or Supabase default via RPC). Implementation can rely on Supabase RLS + `auth.uid()` by using Postgres default via trigger/RPC if desired, but simplest is explicit `owner_user_id = user.id`.

Metadata export:

* On stop, produce `metadata.json` containing:

```json
{
  "sessionId": "...",
  "deviceClockStartEpochMs": 1700000000000,
  "samplingIntervalMs": 500,
  "algoVersion": "dhash64_v1",
  "toleranceMs": 200
}
```

---

### Validator Portal + API Contracts

#### Portal UI flow

* Login required.
* Claim submission requires:

  * `videoFile` (AVI accepted; any ffmpeg-readable container is acceptable)
  * `metadata.json` (from capture client)
* Show result: `Verdict` + metrics + missing span list.

#### Validator API Endpoint

`POST /api/claims/verify`

* Content-Type: `multipart/form-data`
* Parts:

  * `video` (file)
  * `metadata` (JSON file)

Response `200`:

```json
{
  "verdict": "Verified" | "Suspicious" | "Inconclusive",
  "sessionId": "uuid",
  "threshold": 5,
  "toleranceMs": 200,
  "intervalMs": 500,
  "expectedSamples": 157,
  "matchedSamples": 155,
  "matchRatio": 0.987,
  "avgDistance": 1.86,
  "maxDistance": 6,
  "missingSpans": [
    { "startElapsedMs": 51500, "endElapsedMs": 53000, "reason": "No match within tolerance/threshold" }
  ],
  "notes": ["Data expired or partially missing" /* optional */]
}
```

Error responses:

* `400`: malformed metadata, unsupported algoVersion, missing form parts.
* `401/403`: not authenticated / not validator role.
* `404`: session not found in Supabase.
* `410`: session found but expired/deleted (retention).
* `500`: ffmpeg failure, unexpected.

#### Validator role enforcement

* Portal authenticates via Supabase Auth.
* API validates JWT and requires `role=validator` (stored in Supabase user metadata or a `user_roles` table).
* API uses Supabase **service role key** for DB reads.

---

## 4. Behavior & Edge Cases

### Happy Path (end-to-end)

1. Capture client logs in.
2. User starts session:

   * session row inserted into `capture_sessions`.
3. Every 500ms:

   * capture frame → compute `hashHex` → enqueue record → attempt upload.
4. Supabase shows `frame_hashes` appearing near real-time.
5. User stops session and downloads `metadata.json` (and optionally recorded video blob).
6. Validator logs into portal.
7. Validator uploads `video` + `metadata.json`.
8. API:

   * parses metadata, fetches all hashes for session
   * extracts frames from video at approx `fps = 1000/intervalMs`
   * computes dHash64 per extracted frame
   * matches expected timeline with tolerance window
   * returns verdict + metrics

### Matching Logic (canonical)

Definitions:

* Reference samples = Supabase hashes for `sessionId` (ordered by `elapsed_ms`).
* Candidate samples = hashes computed from uploaded video frames, each with `elapsedMsFromVideo ≈ pts_time*1000`.

For each **reference** sample `r` at `r.sampleTimestampEpochMs`:

1. Find candidates `c` where:

   * `abs(c.sampleTimestampEpochMs - r.sampleTimestampEpochMs) <= toleranceMs`
2. Compute Hamming distance for each candidate; take `minDist`.
3. If `minDist <= threshold` → matched else unmatched.

Metrics:

* `matchRatio = matchedSamples / expectedSamples`
* `avgDistance` computed over matched samples only
* `maxDistance` over matched samples only
* Missing spans:

  * consecutive unmatched reference samples; convert to time spans using their `elapsedMs`.
  * Flag span if duration `>= MISSING_SPAN_FLAG_MS`.

Verdict:

* **Verified**: `matchRatio >= 0.90` AND no missing span `>= 5s`
* **Suspicious**: `matchRatio < 0.80` OR any missing span `>= 5s`
* **Inconclusive**: otherwise (including partial data, borderline ratios)

### ffmpeg Extraction (one run)

* Use a temp folder per request.
* Run one ffmpeg command to extract frames at fixed fps and log timestamps:

Example (conceptual):

* `fps = 1000.0 / intervalMs` (e.g., 2.0)
* Command should:

  * decode input
  * apply `fps={fps}`
  * output PNGs: `frame_%06d.png`
  * emit `showinfo` to stderr so we can parse `pts_time`

**Requirement:** Implementation must parse `pts_time` per extracted frame and compute:

* `elapsedMs = round(pts_time * 1000)`
* `sampleTimestampEpochMs = deviceClockStartEpochMs + elapsedMs`

### Error Handling Rules

* Capture client:

  * If camera permission denied → show user-facing error; do not start session.
  * If Supabase insert fails → keep queued; do not lose data.
  * If IndexedDB unavailable → fail fast (prototype) with clear message.
* Validator API:

  * If ffmpeg missing/unusable → `500` with “ffmpeg not configured”.
  * If session not found → `404`.
  * If session expired (deleted) → `410` (detected by “session exists?” check failing AND metadata indicates old start time beyond retention window OR a direct “not found” after lookup).
  * If algoVersion mismatch → `400`.

### Edge Cases

* **Empty / tiny video**: if extracted frames < 5 samples → `Inconclusive`.
* **Timestamp drift** (metadata wrong): if matchRatio < 0.20 but frames exist, include note: “Possible start-time mismatch”.
* **FPS differences (25/30)**: should still match due to timestamp sampling + tolerance.
* **Re-encode artifacts**: tolerated via threshold=5.
* **Duplicate uploads**: Supabase unique `(session_id, sample_index)` prevents duplicates; uploader should upsert or ignore conflict.
* **Interval changes mid-session**: out of scope for v0; capture client locks interval per session.

---

## 5. Testing Strategy (Pseudo-tests Copilot must implement first)

### Unit — dHash64 (TS)

```ts
test("dhash64FromRgba should produce expected hex for known RGBA fixture", () => {
  // given: a small synthetic image (e.g., 18x16) with deterministic pixel pattern
  // when: compute hash
  // then: expect(hashHex).toBe("e3b1... (16 hex chars)");
});

test("dhash64FromRgba bit packing is LSB-first row-major", () => {
  // given: a crafted 9x8 grayscale outcome where only first comparison is true
  // then: hashHex ends with ...0001
});

test("hammingDistance64 should return 0 for identical hashes", () => {
  expect(hammingDistance64("0000000000000000","0000000000000000")).toBe(0);
});

test("hammingDistance64 should count differing bits", () => {
  expect(hammingDistance64("0000000000000000","0000000000000001")).toBe(1);
});
```

### Unit — dHash64 (C#)

```csharp
test("DHash64.FromRgba should match TS expected hex for same RGBA fixture");
test("DHash64 bit packing is LSB-first row-major");
test("HammingDistance returns correct counts");
```

### Unit — Matching window

```ts
test("matcher should pick min distance within tolerance window", () => {
  // given: one reference at t=1000, two candidates at 900 and 1100 within tolerance
  // distances 6 and 3
  // then: matched with minDist=3
});

test("matcher should mark unmatched when no candidate in tolerance window", () => {
  // then: unmatched count increments and missing span tracked
});
```

### Integration — Capture queue

```ts
test("uploader should resume from oldest pending after offline period", () => {
  // given: enqueue 10 pending, simulate failures, then success
  // then: mark all uploaded in order, none lost
});

test("inserts should not duplicate due to (session_id, sample_index) unique", () => {
  // given: retry same batch
  // then: no crash; duplicates ignored or upserted
});
```

### Integration — Validator API (ffmpeg mocked)

```csharp
test("verify returns Verified when matchRatio >= 0.90 and no long missing spans", () => {});
test("verify returns Suspicious when long missing span >= 5s exists", () => {});
test("verify returns Inconclusive when ratio between 0.80 and 0.90", () => {});
test("verify returns 410 when session expired/deleted", () => {});
```

### E2E smoke (manual scripted)

* Start capture → confirm rows appear in Supabase.
* Disconnect network mid-session → confirm queue grows locally.
* Reconnect → confirm backlog uploads and ordering preserved.
* Upload original/re-encoded/fps-changed video → confirm Verified.
* Upload trimmed video (remove 10s middle) → confirm Suspicious with missing span.

---

## 6. Definition of Done (DoD) checklist

* [ ] Capture client records a session and uploads hash timeline to Supabase in near real-time (happy path).
* [ ] Offline buffering works: disconnect → keep sampling → reconnect → uploads resume from earliest pending.
* [ ] Supabase RLS blocks unauthenticated access; authenticated capture user can only access own sessions.
* [ ] Validator portal requires login and can submit video + metadata.
* [ ] Validator API uses ffmpeg one-run extraction and computes hashes with identical dhash64 implementation.
* [ ] Verdict logic implemented: Verified / Suspicious / Inconclusive with metrics and missing spans.
* [ ] Retention job exists (or runnable) and validator returns a clear “expired/not found” outcome.
* [ ] All unit + integration tests passing (hashing, hamming, matcher, queue).
* [ ] Demo script is repeatable end-to-end and covers re-encode + fps tolerance + trim simulation.

---

If you want, I can now generate the **Copilot Instruction Packet** version of this spec (same content, but rewritten as direct “DO THIS / CREATE THESE FILES / IMPLEMENT THESE FUNCTIONS” steps).
