# Slice 4B Implementation Log

**Date:** January 26, 2026  
**Status:** ✅ DONE  
**Focus:** Supabase Schema + RLS + Retention (Doc & Infra Hardening)

---

## Scope

Slice 4B is a **documentation and infrastructure finalization** slice. NO new application logic (TS/C#) was implemented. The focus was on:

1. **Secrets Safety**: Ensure no credentials are committed to git
2. **SQL Apply Order**: Document exact migration order for repeatability
3. **RLS Intent**: Document Row Level Security model and assumptions
4. **Retention Behavior**: Document cleanup function and scheduling options
5. **PlantUML Update**: Reflect Slice 4B reality (schema/RLS/retention present)
6. **ProjectState Update**: Mark Slice 4B as DONE with accurate repo tree

---

## Files Modified/Created

### Modified
- `.gitignore` - Added `appsettings.Development.json` to secrets patterns
- `docs/flow/current-system-flow.puml` - Updated to show Supabase database with schema/RLS/retention
- `ProjectState.md` - Marked Slice 4B as DONE, updated repo tree, added Supabase contracts

### Created
- `docs/slice4b-log.md` (this file) - Documentation of Slice 4B completion

### Already Existed (from earlier work)
- `supabase/migrations/0001_init_capture_sessions_and_frame_hashes.sql`
- `supabase/migrations/0002_rls_policies.sql`
- `supabase/functions/retention_cleanup.sql`
- `docs/supabase-setup.md` - Comprehensive setup guide with safety checks

---

## SQL Apply Order (Crystal Clear)

Execute these SQL files **in exact order** using Supabase SQL Editor:

### Step 1: Create Tables and Indexes
**File:** `supabase/migrations/0001_init_capture_sessions_and_frame_hashes.sql`

Creates:
- `public.capture_sessions` table with columns: `session_id`, `device_clock_start_epoch_ms`, `sampling_interval_ms`, `algo_version`, `client_version`, `created_at`
- `public.frame_hashes` table with columns: `id`, `session_id`, `sample_index`, `elapsed_ms`, `sample_timestamp_epoch_ms`, `hash_hex`, `interval_ms`, `algo_version`, `created_at`
- Indexes: `idx_capture_sessions_created_at`, `idx_frame_hashes_session_id`, `idx_frame_hashes_timestamp`, `idx_frame_hashes_sample_index`
- Constraints: CHECK constraints, UNIQUE constraint on `(session_id, sample_index)`, CASCADE DELETE from sessions to hashes

### Step 2: Enable RLS and Create Policies
**File:** `supabase/migrations/0002_rls_policies.sql`

Enables:
- Row Level Security on both tables
- Policies allowing INSERT for `anon` and `authenticated` roles
- Policies allowing SELECT for `service_role` (validator API)
- Prevents clients from reading other users' data

### Step 3: Create Retention Cleanup Function
**File:** `supabase/functions/retention_cleanup.sql`

Creates:
- `public.cleanup_expired_sessions(retention_hours INTEGER)` function
- Returns number of sessions deleted
- Cascade deletes frame_hashes automatically
- Grants EXECUTE to `service_role`

### Step 4: (Optional) Schedule Retention Job
See `docs/supabase-setup.md` Step 7 for scheduling options:
- Option A: Supabase Edge Function + Cron (requires Pro plan)
- Option B: External cron calling validator API endpoint
- Option C: pg_cron extension (if available)

---

## RLS Security Model

### Client (Capture Client with `anon` key)
- **Can INSERT** into `capture_sessions` and `frame_hashes`
- **Cannot SELECT/UPDATE/DELETE** (prevents data snooping)
- Uses `SUPABASE_ANON_KEY` from `.env.local` (gitignored)

### Server (Validator API with `service_role` key)
- **Full access** to both tables (read all sessions/hashes for verification)
- Bypasses RLS by default (superuser-like permissions)
- Uses `ServiceRoleKey` from `appsettings.Development.json` (gitignored)

### Security Assumptions
- `session_id` is client-generated UUID (no server-side assignment yet)
- No user authentication required for uploads (anonymous write allowed)
- Future enhancement: Add `owner_user_id` column for multi-tenant isolation
- Rate limiting should be implemented at edge function or application level

### Critical Columns
- `session_id` (TEXT): Primary key, references from `frame_hashes`
- `created_at` (TIMESTAMPTZ): Used by retention cleanup
- `elapsed_ms` (BIGINT): Used for ordering in verification matching
- `hash_hex` (TEXT): 16-character hex string (64 bits), CHECK constraint enforces length

### Cascade Behavior
- Deleting a `capture_session` automatically deletes all associated `frame_hashes`
- Retention cleanup leverages this for efficient bulk deletion

---

## Retention Behavior

### What "Expired" Means
- **Definition**: Sessions where `created_at < NOW() - retention_hours`
- **Default retention**: Not enforced by database (manual or scheduled cleanup required)
- **Typical retention**: 24-168 hours (1-7 days) depending on use case

### Cleanup Function
```sql
SELECT public.cleanup_expired_sessions(24); -- Delete sessions older than 24 hours
```

**Returns:** Number of sessions deleted (frame_hashes cascade deleted automatically)

### Integration with Validator API
When `SupabaseHashStore.GetSessionAsync(sessionId)` returns `null`:
- Validator throws `NotFoundException`
- **Future**: Should throw `SessionExpiredException` if session existed but was cleaned up
- **Current workaround**: All missing sessions treated as `NotFoundException`

### Scheduling Notes
- **Manual trigger**: Run SQL in Supabase SQL Editor
- **Automated**: See `docs/supabase-setup.md` Step 7 for options
- **Recommended**: Daily cleanup at low-traffic hours (e.g., 2 AM UTC)
- **Monitoring**: Track `deleted_count` return value for auditing

---

## Secrets Safety Evidence

### Gitignore Patterns (Confirmed)
```gitignore
# Environment variables
.env
.env.local
.env.*.local

# Development configuration with secrets
appsettings.Development.json
```

### Git Status Check (January 26, 2026)
```bash
$ git status
On branch main
Your branch is up to date with 'origin/main'.

nothing to commit, working tree clean
```
✅ No secrets files staged or tracked

### Git Grep Results
```bash
# Check for service_role key
$ git grep -n "service_role"
```
✅ Only documentation references found (no actual keys)

```bash
# Check for Supabase URLs
$ git grep -n "supabase.co"
```
✅ Only placeholder/example URLs found (e.g., `https://xxxxxxxxxxxxx.supabase.co`)

```bash
# Verify secret files not tracked
$ git ls-files | grep -E "(\.env\.local|appsettings\.Development\.json)"
```
✅ No results (files are properly gitignored)

---

## Manual Apply Checklist

Use this checklist when applying Slice 4B to a new Supabase project:

### Pre-Flight Checks
- [ ] Supabase project created
- [ ] Project URL and keys copied (anon + service_role)
- [ ] `.env.local` created (capture client) - **NOT COMMITTED**
- [ ] `appsettings.Development.json` created (validator API) - **NOT COMMITTED**
- [ ] Git status confirms no secrets tracked

### Apply SQL Migrations
- [ ] Run `supabase/migrations/0001_init_capture_sessions_and_frame_hashes.sql`
- [ ] Verify tables created: `capture_sessions`, `frame_hashes`
- [ ] Verify indexes created: Check `\d capture_sessions`, `\d frame_hashes`
- [ ] Run `supabase/migrations/0002_rls_policies.sql`
- [ ] Verify RLS enabled: `SELECT tablename, rowsecurity FROM pg_tables WHERE tablename IN ('capture_sessions', 'frame_hashes');`
- [ ] Run `supabase/functions/retention_cleanup.sql`
- [ ] Verify function created: `SELECT * FROM pg_proc WHERE proname = 'cleanup_expired_sessions';`

### Test Connections
- [ ] Capture client can connect (check browser console)
- [ ] Validator API can connect (check startup logs)
- [ ] Insert test session via capture client
- [ ] Query test session via validator API
- [ ] Run cleanup function: `SELECT public.cleanup_expired_sessions(0);` (should delete test session)

### Post-Apply Verification
- [ ] Run `git status` - no secrets files shown
- [ ] Run `git grep -n "service_role"` - only docs references
- [ ] Run `git grep -n "supabase.co"` - only placeholders
- [ ] Document actual project URL (in team wiki, not in git)

---

## PlantUML Diagram Update

Updated `docs/flow/current-system-flow.puml` to reflect Slice 4B reality:

**Added:**
- Supabase database component with schema details
- RLS policy notation (anon vs service_role)
- Retention cleanup job
- Capture client write path (anon key)
- Validator API read path (service_role key)

**Labels:**
- `<<REAL>>` SupabaseHashStore (Slice 4A)
- `<<REAL>>` Supabase Schema/RLS/Retention (Slice 4B)
- `<<MOCK>>` IVideoFrameExtractor (until Slice 4C)

---

## Testing Strategy (Doc/Infra Focus)

Slice 4B is **not** about unit tests - it's about **repo integrity** and **apply readiness**.

### Verification Outputs
1. ✅ Git status shows clean working tree
2. ✅ Git grep shows no real keys/URLs
3. ✅ .gitignore includes all secret patterns
4. ✅ SQL files listed with correct paths
5. ✅ Manual apply checklist provided
6. ✅ ProjectState.md updated with Slice 4B status
7. ✅ PlantUML diagram updated with database layer

### No Code Tests Required
- No new TS/C# logic to test
- Existing tests from Slice 1-4A remain valid
- SQL migrations verified via manual apply (Step 4 in supabase-setup.md)

---

## Assumptions and Constraints

### Database Assumptions
- Supabase uses PostgreSQL 15+
- PostgREST 11.0+ available for API queries
- CASCADE DELETE supported (for retention cleanup)
- RLS policies work as documented

### Client Assumptions
- Capture client generates UUIDs for `session_id` (no server assignment)
- No authentication required for uploads (anonymous INSERT allowed)
- Client clock (`device_clock_start_epoch_ms`) is trusted (no server validation yet)

### API Assumptions
- Validator API uses `service_role` key (full database access)
- No rate limiting enforced at database level (should be added at edge/app level)
- Ordering constraint: frame_hashes must be queried with `order=elapsed_ms.asc`

### Future Enhancements
- Add `owner_user_id` column for multi-tenant RLS
- Implement authentication for capture client
- Add server-side validation of session metadata
- Implement rate limiting (edge functions or app-level)
- Track retention cleanup metrics (deleted_count over time)

---

## Contracts Stability

### Supabase PostgREST API (Validator API)
**Endpoint:** `GET {BaseUrl}/rest/v1/capture_sessions?session_id=eq.{sessionId}&select=*`  
**Headers:**
- `apikey: {ServiceRoleKey}`
- `Authorization: Bearer {ServiceRoleKey}`
- `Accept: application/json`

**Response:** Single object or empty array

**Endpoint:** `GET {BaseUrl}/rest/v1/frame_hashes?session_id=eq.{sessionId}&select=*&order=elapsed_ms.asc`  
**Headers:** Same as above  
**Response:** Array of frame hash objects, **MUST be ordered by `elapsed_ms` ascending**

### Retention Function
**Signature:** `public.cleanup_expired_sessions(retention_hours INTEGER) RETURNS INTEGER`  
**Behavior:** Deletes sessions where `created_at < NOW() - retention_hours`, cascades to frame_hashes  
**Returns:** Number of sessions deleted

---

## Next Steps (Slice 4C and Beyond)

Slice 4B is **complete**. The database layer is production-ready for local development.

**Slice 4C: FFmpeg Frame Extraction**
- Implement real `IVideoFrameExtractor` using FFmpeg
- Replace mock in `VerificationService`
- Update PlantUML to mark `<<REAL>>` for extractor

**Slice 4D: Angular Portal UI**
- Create claim submission form (video upload + metadata)
- Integrate with validator API `/api/claims/verify`

**Slice 4E: End-to-End Demo**
- Wire capture client → Supabase → validator API → portal UI
- Demo video: Record → Hash → Upload → Submit Claim → Verify

---

## Evidence Summary

| Check | Result | Evidence |
|-------|--------|----------|
| Secrets gitignored | ✅ | `.gitignore` includes `.env.local`, `appsettings.Development.json` |
| No secrets tracked | ✅ | `git ls-files` returns empty for secret files |
| No real keys in repo | ✅ | `git grep` shows only docs/examples |
| SQL apply order documented | ✅ | See "SQL Apply Order" section above |
| RLS model documented | ✅ | See "RLS Security Model" section above |
| Retention behavior documented | ✅ | See "Retention Behavior" section above |
| PlantUML updated | ✅ | `docs/flow/current-system-flow.puml` includes database |
| ProjectState.md updated | ✅ | Slice 4B marked DONE, repo tree accurate |

---

**Slice 4B is production-ready for local development. All documentation and infrastructure hardening complete.**
