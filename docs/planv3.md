You’re right to call this out. Plan v2 was **implementation-feasible**, but it was **not requirements-complete** relative to Spec v0 + your input table: it over-focused on the validator pipeline (Supabase-read + ffmpeg + portal) and didn’t explicitly finish the **capture-side “web dashcam” product** (mobile-friendly camera access, auth UX, live/on-the-fly streaming, “video upload optional”, etc.). That’s a planning failure on my side.

I’m going to re-plan from the **actual repo reality** you posted (Slice 4D done, capture-client modules exist but not yet a “real camera dashcam UX”).

---

## First: your key question (metadata.json)

### Do you still need to manually upload `metadata.json`?

**No — not as a user-facing requirement.** It’s reasonable to remove the “upload metadata.json alongside video” step.

You already store the anchor data in Supabase (`capture_sessions` includes `deviceClockStartEpochMs`, `samplingIntervalMs`, `algoVersion`). So the validator can get that **by sessionId** and doesn’t need the human to carry a JSON file around.

### Revised contract proposal (Plan v3)

* **Portal**: user uploads **video** + enters/selects **sessionId** (or “latest session”), **metadata optional/advanced**.
* **API**: accept `sessionId` as a form field and make `Metadata` part optional:

  * If `Metadata` is present: validate it matches Supabase session (or allow override only if you explicitly want that).
  * If absent: derive everything from Supabase `capture_sessions`.

This keeps your current work (4D UI + API models) mostly reusable, while aligning to “video upload optional on capture side” and removing the weird “video+metadata file” burden.

---

# Plan v3 — Requirements-complete re-slice (from Slice 4D state)

## 1) Physical Architecture (File Tree)

(Only listing what matters for the remaining work; ✅ means already exists per ProjectState.)

```
apps/
  capture-client/ ✅
    src/
      capture/
        frameSource.ts ✅        (needs real camera implementation)
        sampler.ts ✅
      storage/hashQueue.ts ✅
      supabase/
        supabaseApi.ts ✅
        uploader.ts ✅
      hash/dhash64.ts ✅
      models.ts ✅
      constants.ts ✅
      ui/                        (NEW - minimal pages/components)
        index.html               (NEW or adapt existing tooling)
        main.ts                  (NEW)
        capturePage.ts           (NEW)
      __tests__/ ✅              (add tests for camera frame source as feasible)

  validator-portal/ ✅
    src/app/
      claims/verify-claim/* ✅   (modify: sessionId input, metadata optional)
      auth/*                     (NEW - validator auth UI)
      api/validator-api.service.ts ✅ (modify: multipart fields)

services/
  validator-api/ ✅
    Controllers/ClaimsController.cs ✅ (modify: accept SessionId + optional Metadata)
    Models/VerifyClaimModels.cs ✅     (modify)
    Services/VerificationService.cs ✅ (modify: load session from store when metadata absent)
    Services/SupabaseHashStore.cs ✅
    Services/FfmpegVideoFrameExtractor.cs ✅

supabase/ ✅
  migrations/*.sql ✅
  functions/retention_cleanup.sql ✅
  (OPTIONAL) seed/validator_role.sql (NEW)

docs/
  demo-script.md                (NEW)
  qa-checklist.md               (NEW)
```

This structure now explicitly includes the missing “product layer”: **capture-client UI + auth UX** and **portal auth**.

---

## 2) Step-by-Step Implementation Strategy (Slicing)

### Slice 5A — Capture Client “Real Dashcam” (Mobile-friendly camera + live sampling + on-the-fly upload)

**Goal:** Turn capture-client from “core modules” into a **real web dashcam page**:

* open camera on laptop/phone (mobile support)
* start/stop session
* live sampling running (500ms default)
* on-the-fly upload to Supabase + offline buffering (already exists; wire it)
* optional: record video locally (MediaRecorder) + download (NOT Supabase)

**Verify:** manual demo on phone + desktop:

* camera opens
* session row appears in Supabase
* `frame_hashes` stream in near real-time
* toggle airplane mode → queue grows → reconnect → backlog uploads

> This slice directly restores the “web-based dashcam capture client” and “on-the-fly” requirement from your input table.

---

### Slice 5B — Auth End-to-End (Capture user + Validator user)

**Goal:** implement the auth story that was underspecified in Plan v2:

* capture-client: Supabase Auth login (even a simple email/password for demo)
* validator-portal: Supabase Auth login (validator role)
* validator-api: validate JWT (or at minimum gate with validator role claim) before allowing `/api/claims/verify`

**Verify:**

* unauthenticated capture cannot write
* unauthenticated portal cannot verify
* API rejects missing/invalid token with 401/403

> Your Supabase RLS already exists; this slice makes the *apps* actually use it.

---

### Slice 5C — Remove “metadata.json burden” + Session-based verification UX

**Goal:** align verification flow to your “metadata upload makes no sense” point:

* Portal: upload video + **sessionId select/input**; metadata optional advanced
* API: `sessionId` required; `metadata` optional
* Service: if metadata absent, load session anchor from Supabase and proceed

**Verify:**

* verification works with **video + sessionId only**
* still supports metadata (optional) for debugging/backward compatibility

---

### Slice 5D — End-to-End Demo + QA checklist + Retention behavior

**Goal:** produce the actual deliverable:

* repeatable demo script
* explicit tamper simulations (trim/insert/re-encode/fps change)
* retention: when expired, portal shows “data expired/not found” cleanly

**Verify:** the DoD checklist from Spec v0 is demonstrably satisfied.

---

## 3) Core Loop Selection

All slices 5A–5D: **PVI** (Plan → Verify → Implement).
Not exploratory: you already have the core algorithms, queue, store, extractor, portal screen.

---

# Copilot Instruction Packet — Slice 5A (Capture Client Real Dashcam)

```text
[CONTEXT]
We are implementing Slice 5A: Capture Client “Real Dashcam” UX (mobile-friendly camera access + live sampling + on-the-fly upload).
Core modules already exist (Sampler, HashQueue, Uploader, FetchSupabaseApi). We need to wire them into a working browser UI that:
1) opens camera on phone/laptop
2) starts a capture session and begins sampling frames (default 500ms)
3) uploads hashes+session to Supabase on-the-fly
4) supports offline buffering (already implemented) and resumes when online
Video recording/upload to Supabase is NOT required (optional local download is ok).

Use existing repo tooling (inspect apps/capture-client/package.json to see bundler/dev server). Do not introduce a new framework unless the project already has one.

[FILES TO CREATE/EDIT]
In apps/capture-client:

EDIT:
- src/capture/frameSource.ts
  Add a real BrowserCameraFrameSource that reads RGBA frames from getUserMedia video + canvas.

EDIT/ADD as needed (depending on existing build tooling):
- src/ui/main.ts (NEW) OR existing entry file
- src/ui/capturePage.ts (NEW)
- index.html or equivalent (NEW if not present)

EDIT:
- src/supabase/supabaseApi.ts (only if needed for auth token handling; keep minimal)

OPTIONAL:
- src/capture/mediaRecorder.ts (NEW) for local video download (optional feature)

[SPECIFICATION]
Must satisfy Spec v0 capture requirements:
- Web camera access in browser (mobile-supported via HTTPS + user gesture).
- Timestamp-based sampling interval configurable; default 500ms.
- For each sampled frame:
  - compute dhash64_v1 hashHex
  - create FrameHashRecord with:
    sessionId, sampleIndex, elapsedMs, sampleTimestampEpochMs = deviceClockStartEpochMs + elapsedMs
  - enqueue locally first, then attempt upload immediately
- Offline buffering:
  - if upload fails, records remain pending
  - when online, resume from earliest pending until caught up

Do NOT upload raw video/frames to Supabase.

UI minimal requirements (prototype-friendly):
- Buttons: “Start Camera”, “Start Session”, “Stop Session”
- Display: sessionId, pendingCount, uploadedCount (or lastUploadedIndex), online/offline indicator
- If auth already exists in capture-client, use it; if not, use anon key for now BUT keep it ready for Slice 5B auth integration (no hardcoding secrets).

Camera FrameSource contract:
- FrameSource.readFrame(): Promise<FrameData { rgba, width, height }>
Implement BrowserCameraFrameSource:
- Internally creates <video> element bound to MediaStream from getUserMedia
- Uses canvas drawImage + getImageData to produce RGBA
- Ensure dimensions are stable (set video constraints and wait for loadedmetadata)
- Works on mobile Safari/Chrome (keep it simple)

Sampler wiring:
- On “Start Session”:
  - create CaptureSession { sessionId(uuid), deviceClockStartEpochMs(Date.now), samplingIntervalMs(500), algoVersion("dhash64_v1") }
  - call SupabaseApi.insertSession(session)
  - start Sampler(session,...)
  - start Uploader.attachOnlineListener() once at app init
- On “Stop Session”:
  - stop sampler
  - trigger uploader.uploadPending()

[TESTING STRATEGY]
This slice is UI/browser integration heavy. Do:
1) Keep existing unit tests unchanged.
2) Add one small unit test if feasible:
   - BrowserCameraFrameSource cannot be easily tested in Node; skip if environment blocks it.
3) Add a manual demo checklist in docs or console logs:
   - Start camera -> confirm video preview
   - Start session -> confirm insertSession called (log)
   - Observe uploader sending batches (log)
   - Toggle offline -> pending increases
   - Back online -> pending decreases to 0

[COMMAND]
1) Implement BrowserCameraFrameSource in frameSource.ts.
2) Add minimal UI entry that wires camera + session + sampler + uploader.
3) Ensure dev server runs (npm start/dev) and no existing tests break:
   cd apps/capture-client
   npm test
4) Provide:
   - how to run the capture-client locally
   - what URL to open
   - how to test on mobile (same LAN + HTTPS note if needed)
   - evidence logs/screenshots guidance
```