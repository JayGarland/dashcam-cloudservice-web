-- Migration 0002: Row Level Security (RLS) Policies
-- Purpose: Secure access to capture_sessions and frame_hashes tables
-- Author: Dashcam Cloud Service Team
-- Date: January 2026

-- ============================================================================
-- Security Model Overview
-- ============================================================================
-- 
-- CLIENT (anon key):
--   - Can INSERT into capture_sessions (create new sessions)
--   - Can INSERT into frame_hashes (upload hashes)
--   - CANNOT SELECT/UPDATE/DELETE (prevents data snooping)
--
-- SERVER (service_role key):
--   - Full access to both tables (used by validator API)
--   - Bypasses RLS by default (service_role is superuser-like)
--
-- This model allows:
--   1. Dashcam devices to upload data freely
--   2. Only the validator API can query data for verification
--   3. Prevents malicious clients from reading other users' hashes
--
-- ============================================================================

-- ============================================================================
-- Enable RLS on tables
-- ============================================================================

ALTER TABLE public.capture_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.frame_hashes ENABLE ROW LEVEL SECURITY;

-- ============================================================================
-- Policies for capture_sessions
-- ============================================================================

-- Policy: Allow anyone to create new capture sessions
CREATE POLICY "Allow insert capture_sessions for all users"
    ON public.capture_sessions
    FOR INSERT
    TO anon, authenticated
    WITH CHECK (true);

-- Policy: service_role can read all sessions (for verification queries)
-- Note: service_role bypasses RLS by default, but we define this for clarity
CREATE POLICY "Allow select capture_sessions for service_role"
    ON public.capture_sessions
    FOR SELECT
    TO service_role
    USING (true);

-- ============================================================================
-- Policies for frame_hashes
-- ============================================================================

-- Policy: Allow anyone to insert new frame hashes
CREATE POLICY "Allow insert frame_hashes for all users"
    ON public.frame_hashes
    FOR INSERT
    TO anon, authenticated
    WITH CHECK (true);

-- Policy: service_role can read all hashes (for verification queries)
-- Note: service_role bypasses RLS by default, but we define this for clarity
CREATE POLICY "Allow select frame_hashes for service_role"
    ON public.frame_hashes
    FOR SELECT
    TO service_role
    USING (true);

-- ============================================================================
-- Additional Security Notes
-- ============================================================================
--
-- IMPORTANT: The service_role key must be kept SECRET:
--   - Never expose in client-side code
--   - Only use in backend services (validator API)
--   - Store in environment variables or secure configuration
--
-- ANON KEY USAGE:
--   - Safe to use in browser/client apps
--   - Has limited permissions via RLS
--   - Can only INSERT, not SELECT/UPDATE/DELETE
--
-- FUTURE ENHANCEMENTS:
--   - Add UPDATE/DELETE policies if clients need to modify their own data
--   - Add user_id column if implementing multi-tenant isolation
--   - Consider rate limiting at the application or Supabase edge function level
--
-- ============================================================================

-- ============================================================================
-- Verification Query (for testing)
-- ============================================================================

-- Run this to verify RLS is enabled:
-- SELECT tablename, rowsecurity 
-- FROM pg_tables 
-- WHERE schemaname = 'public' 
--   AND tablename IN ('capture_sessions', 'frame_hashes');

-- Run this to view all policies:
-- SELECT schemaname, tablename, policyname, permissive, roles, cmd, qual, with_check
-- FROM pg_policies
-- WHERE tablename IN ('capture_sessions', 'frame_hashes');
