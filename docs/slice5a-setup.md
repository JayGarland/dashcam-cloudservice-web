# Slice 5A Setup + Manual Test

This guide covers local setup and a manual test walkthrough for the Capture Client “Real Dashcam” UX (Slice 5A).

## 1) Prereqs

- Node.js 18+ (or the version you already use for the repo)
- A Supabase project with migrations applied:
  - `supabase/migrations/0001_init_capture_sessions_and_frame_hashes.sql`
  - `supabase/migrations/0002_rls_policies.sql`
- Supabase Project URL + anon key (safe for client use)
- HTTPS on mobile (required for camera access)

## 2) Configure Supabase

From `docs/supabase-setup.md`, get:
- Supabase URL (looks like `https://xxxx.supabase.co`)
- Supabase anon key

No secrets should be hardcoded into the repo.

## 3) Install and Run Capture Client

```bash
cd apps/capture-client
npm install
npm run dev
```

Default dev URL:
- `http://localhost:8000`

## 4) Open the UI

1. Open `http://localhost:8000` in a desktop browser.
2. In the UI, fill in:
   - Supabase URL
   - Supabase anon key
   - (Optional) Auth token
3. Sampling interval defaults to 500ms (you can change it).

## 5) Mobile Testing (HTTPS Required)

Camera access on iOS Safari / Android Chrome requires HTTPS.

Options:
1) **LAN + HTTPS reverse proxy**
   - Run a local HTTPS proxy (Caddy, mkcert + simple HTTPS server, etc.)
   - Open the HTTPS URL from your phone on the same Wi‑Fi
2) **Secure tunnel**
   - Use a tunnel tool that provides HTTPS (e.g., localtunnel, cloudflared)
   - Open the HTTPS tunnel URL on your phone

## 6) Manual Test Checklist

### A. Start Camera
- [ ] Click **Start Camera**
- [ ] Verify video preview is visible

### B. Start Session + Sampling
- [ ] Click **Start Session**
- [ ] Confirm Activity Log shows `insertSession ok` with a sessionId
- [ ] Observe `Uploaded sample #...` messages over time

### C. Offline Buffering
- [ ] Turn off network (DevTools > Network > Offline, or airplane mode)
- [ ] Confirm Activity Log shows offline
- [ ] Confirm **Pending hashes** count increases

### D. Resume Upload
- [ ] Restore network
- [ ] Confirm Activity Log shows online and upload resumes
- [ ] Confirm **Pending hashes** count decreases back to 0

### E. Stop Session
- [ ] Click **Stop Session**
- [ ] Confirm Activity Log shows sampler stopped and upload flush

## 7) Evidence to Capture

- Screenshot of UI with:
  - camera preview visible
  - sessionId shown
  - pending/uploaded counts
- Console log or Activity Log showing:
  - `insertSession ok: <sessionId>`
  - upload activity while online
  - offline/online toggles

## 8) Expected Outcomes

- Camera opens with a live preview
- Session insert succeeds in Supabase
- Hash records upload continuously while online
- Pending queue grows while offline and drains when back online

