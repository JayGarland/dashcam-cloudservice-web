# Supabase Setup Guide

This guide walks you through setting up Supabase for the Dashcam Cloud Service project. Follow these steps to create your database schema, configure RLS policies, and set up retention automation.

## ⚠️ Security First

**CRITICAL:** Never commit real Supabase credentials to git!
- ✅ **Safe:** `.env.example`, `appsettings.Development.example.json` (placeholders only)
- ❌ **DANGEROUS:** `.env.local`, `appsettings.Development.json` (real keys — gitignored)
- ❌ **NEVER:** `service_role` key in committed files

## Prerequisites

- Supabase account (free tier is sufficient for development)
- Basic familiarity with SQL
- Project cloned and ready

---

## Step 1: Create Supabase Project

1. Go to [https://supabase.com/dashboard](https://supabase.com/dashboard)
2. Click **"New Project"**
3. Choose organization and fill in:
   - **Name:** `dashcam-cloudservice` (or your preference)
   - **Database Password:** Generate a strong password (save it securely!)
   - **Region:** Choose closest to your users
4. Click **"Create new project"**
5. Wait 2-3 minutes for provisioning

---

## Step 2: Get API Credentials

1. In your Supabase dashboard, go to **Project Settings** → **API**
2. Copy these values (you'll need them later):

   ```
   Project URL:      https://xxxxxxxxxxxxx.supabase.co
   anon (public):    eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ey...
   service_role:     eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ey...
   ```

3. **Important:** 
   - `anon` key: Used by capture client (browser-safe, limited permissions)
   - `service_role` key: Used by validator API (FULL access, keep secret!)

---

## Step 3: Apply Database Migrations

Run SQL files in order using the **SQL Editor** in Supabase Dashboard.

### 3.1 Create Tables

1. Go to **SQL Editor** in Supabase Dashboard
2. Click **"New Query"**
3. Copy and paste contents of:
   ```
   supabase/migrations/0001_init_capture_sessions_and_frame_hashes.sql
   ```
4. Click **"Run"**
5. Verify output shows success (no errors)

**Expected tables:**
- `capture_sessions` — Stores metadata for each capture session
- `frame_hashes` — Stores individual frame hash records

### 3.2 Set Up RLS Policies

1. Create another new query
2. Copy and paste contents of:
   ```
   supabase/migrations/0002_rls_policies.sql
   ```
3. Click **"Run"**
4. Verify RLS is enabled on both tables

**RLS Policies Overview:**
- **capture_sessions:** Anyone can insert (new sessions), service_role can read all
- **frame_hashes:** Anyone can insert (new hashes), service_role can read all
- This allows client apps to upload data but only server can query for verification

### 3.3 Create Retention Function

1. Create another new query
2. Copy and paste contents of:
   ```
   supabase/functions/retention_cleanup.sql
   ```
3. Click **"Run"**
4. Verify function `cleanup_expired_sessions` is created

**Purpose:** Deletes sessions older than X hours (configurable), including cascading deletes of frame_hashes.

---

## Step 4: Verify Schema

Run this query in SQL Editor to verify your setup:

```sql
-- Check tables exist
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'public' 
  AND table_name IN ('capture_sessions', 'frame_hashes');

-- Check capture_sessions columns
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'capture_sessions' 
ORDER BY ordinal_position;

-- Check frame_hashes columns
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'frame_hashes' 
ORDER BY ordinal_position;

-- Check RLS is enabled
SELECT tablename, rowsecurity 
FROM pg_tables 
WHERE schemaname = 'public' 
  AND tablename IN ('capture_sessions', 'frame_hashes');
```

**Expected output:**
- Both tables exist
- `capture_sessions` has: `session_id`, `device_clock_start_epoch_ms`, `sampling_interval_ms`, `algo_version`, `created_at`
- `frame_hashes` has: `session_id`, `sample_index`, `elapsed_ms`, `hash_hex`, `sample_timestamp_epoch_ms`, etc.
- RLS is `true` for both tables

---

## Step 5: Configure Local Applications

### 5.1 Capture Client (TypeScript)

1. Navigate to `apps/capture-client/`
2. Copy the example file:
   ```bash
   cp .env.example .env.local
   ```
3. Edit `.env.local` with your real credentials:
   ```env
   SUPABASE_URL=https://xxxxxxxxxxxxx.supabase.co
   SUPABASE_ANON_KEY=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ey...
   ```
4. **Verify:** `.env.local` is in `.gitignore` (it is by default)

### 5.2 Validator API (C#/.NET)

**Option A: Configuration File (Recommended for local dev)**

1. Navigate to `services/validator-api/`
2. Copy the example file:
   ```bash
   cp appsettings.Development.example.json appsettings.Development.json
   ```
3. Edit `appsettings.Development.json` with real credentials:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Information",
         "Microsoft.AspNetCore": "Warning"
       }
     },
     "Supabase": {
       "BaseUrl": "https://xxxxxxxxxxxxx.supabase.co",
       "ServiceRoleKey": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ey..."
     }
   }
   ```
4. **Verify:** `appsettings.Development.json` is in `.gitignore` (added in this setup)

**Option B: Environment Variables (For production/CI)**

Set these environment variables:
```bash
Supabase__BaseUrl=https://xxxxxxxxxxxxx.supabase.co
Supabase__ServiceRoleKey=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ey...
```

On Windows PowerShell:
```powershell
$env:Supabase__BaseUrl="https://xxxxxxxxxxxxx.supabase.co"
$env:Supabase__ServiceRoleKey="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ey..."
```

---

## Step 6: Test Connections

### Test Capture Client

```bash
cd apps/capture-client
npm install
npm run dev
# Open browser, check console for Supabase connection logs
```

### Test Validator API

```bash
cd services/validator-api
dotnet build
dotnet run
# API should start on https://localhost:5001
# Check logs for Supabase configuration loaded
```

Run integration tests (if available):
```bash
dotnet test
```

---

## Step 7: Set Up Retention Automation (Optional)

### Manual Run

To manually clean up sessions older than 24 hours:

```sql
SELECT public.cleanup_expired_sessions(24);
```

This returns the number of deleted sessions.

### Scheduled Automation

**Option A: Supabase Edge Functions (Recommended)**
1. Create an Edge Function that calls `cleanup_expired_sessions`
2. Schedule it using Supabase Cron (requires Pro plan)

**Option B: External Cron**
1. Create a simple API endpoint in validator-api: `POST /api/admin/retention`
2. Have it execute `cleanup_expired_sessions` via Supabase client
3. Schedule with external cron service (e.g., GitHub Actions, cron-job.org)

**Option C: Database Extension (pg_cron)**
*Requires pg_cron extension (check if available in your Supabase tier)*

```sql
-- Schedule daily cleanup at 2 AM UTC
SELECT cron.schedule(
  'retention-cleanup',
  '0 2 * * *',
  $$SELECT public.cleanup_expired_sessions(24)$$
);
```

---

## Step 8: Verify End-to-End Flow

1. **Capture:** Run capture client, simulate video capture, verify hashes uploaded
2. **Query:** Check Supabase dashboard → Table Editor → `frame_hashes` for data
3. **Verify:** Call validator API with a claim, should query stored hashes
4. **Retention:** Run cleanup function, verify old sessions are deleted

---

## Troubleshooting

### "relation does not exist" errors
- Ensure migrations were applied successfully
- Check you're running queries in the correct Supabase project

### "new row violates row-level security policy"
- Verify RLS policies were created correctly
- Check that service_role key is being used (not anon key) for validator API

### Capture client can't connect
- Verify `SUPABASE_URL` and `SUPABASE_ANON_KEY` in `.env.local`
- Check browser console for CORS errors (shouldn't happen with Supabase)

### Validator API can't connect
- Verify `Supabase:BaseUrl` and `Supabase:ServiceRoleKey` in config
- Check API logs for configuration errors
- Ensure no typos in JSON key names (case-sensitive)

### Retention function not working
- Verify function was created: `SELECT * FROM pg_proc WHERE proname = 'cleanup_expired_sessions';`
- Check cascade delete constraints exist on `frame_hashes`

---

## Security Checklist

Before committing any changes:

- [ ] No real `service_role` key in any tracked file
- [ ] No real project URLs in tracked files (only in examples)
- [ ] `.env.local` is in `.gitignore`
- [ ] `appsettings.Development.json` is in `.gitignore`
- [ ] Only `.env.example` and `appsettings.Development.example.json` are committed
- [ ] Team members know to create their own local config files

---

## Quick Reference

| Component | Config File | Key Type | Purpose |
|-----------|-------------|----------|---------|
| Capture Client | `.env.local` | `anon` | Upload hashes from browser |
| Validator API | `appsettings.Development.json` | `service_role` | Query all hashes for verification |

---

## Next Steps

After Supabase is set up:
- **Slice 4C:** Implement FFmpeg frame extraction in validator API
- **Slice 4D:** Create Angular portal UI for claim submission
- **Slice 4E:** Wire everything together for end-to-end demo

See [ProjectState.md](../ProjectState.md) for current progress.

---

**Last Updated:** January 26, 2026  
**Supabase Tier Tested:** Free tier  
**Minimum Required Version:** PostgREST 11.0+
