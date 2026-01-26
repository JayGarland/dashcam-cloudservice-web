## Revised Plan v2 — Split “Slice 4” into multiple slices

### Slice 4A — Real Supabase Read Store (C#) + Config + Tests (no network required)

**Goal:** Implement `ISupabaseHashStore` as a real HTTP client against Supabase (PostgREST), using **service role key**, fully testable via mocked `HttpMessageHandler`.
**Verify:** `dotnet test` passes; unit tests assert URL/path/query headers + JSON parsing.

Why first: it’s the cleanest integration surface, and it unlocks real end-to-end verification later without touching ffmpeg yet.

---

### Slice 4B — Supabase Schema + RLS + Retention SQL (infra code only)

**Goal:** Add `supabase/migrations/*` for `capture_sessions`, `frame_hashes`, indexes, RLS policies, and a retention function/script.
**Verify:** SQL files exist, are idempotent in intended order; add a `docs/supabase-setup.md` with steps. (Optional: add a basic SQL “smoke check” script.)

Why separate: schema/RLS is “infra contract”; keep it isolated from app code risk.

---

### Slice 4C — ffmpeg One-run Extractor (C#) + Tests (process mocked, parsing real)

**Goal:** Implement `IVideoFrameExtractor` as `FfmpegVideoFrameExtractor` that:

* runs **one ffmpeg process**,
* outputs PNG frames to temp folder,
* parses `pts_time` from `showinfo` log,
* returns `ExtractedFrame(elapsedMs, rgba, w, h)`.

**Verify:** `dotnet test` passes:

* process invocation tested (args formation),
* log parsing tested with real captured stderr samples,
* PNG→RGBA conversion tested with ImageSharp.

Why separate: ffmpeg integration is the highest “environment” risk.

---

### Slice 4D — Portal Wiring (Angular) → API multipart upload + Auth token forwarding

**Goal:** Build the minimal UI to upload `video` + `metadata`, call `/api/claims/verify`, display `VerificationResult`.
**Verify:** Angular build passes; manual upload works against local API.

Why separate: front-end wiring is mostly plumbing; don’t mix with ffmpeg/store.

---

### Slice 4E — End-to-End Demo Script + Regression Gate

**Goal:** A repeatable demo:

1. capture → hashes appear in Supabase
2. upload video+metadata in portal
3. Verified/Suspicious behaviors observable
4. retention case returns expired/not found gracefully

**Verify:** documented steps + recorded evidence (screenshots/logs).

---

## Core Loop Selection

For 4A/4B/4C/4D/4E: **PVI** (Plan → Verify → Implement).
No exploration needed; interfaces already exist and are mock-tested through Slice 3.