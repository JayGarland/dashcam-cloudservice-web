-- Retention Cleanup Function
-- Purpose: Delete capture sessions older than specified hours (and cascade to frame_hashes)
-- Author: Dashcam Cloud Service Team
-- Date: January 2026

-- ============================================================================
-- Function: cleanup_expired_sessions
-- ============================================================================
-- Deletes capture_sessions (and cascades to frame_hashes) older than X hours
--
-- Parameters:
--   retention_hours INTEGER - Number of hours to retain data (e.g., 24 for 1 day)
--
-- Returns:
--   INTEGER - Number of sessions deleted
--
-- Usage:
--   SELECT public.cleanup_expired_sessions(24);  -- Delete sessions older than 24 hours
--   SELECT public.cleanup_expired_sessions(168); -- Delete sessions older than 1 week
--
-- ============================================================================

CREATE OR REPLACE FUNCTION public.cleanup_expired_sessions(retention_hours INTEGER)
RETURNS INTEGER
LANGUAGE plpgsql
SECURITY DEFINER -- Run with elevated privileges
AS $$
DECLARE
    deleted_count INTEGER;
    cutoff_timestamp TIMESTAMPTZ;
BEGIN
    -- Validate input
    IF retention_hours IS NULL OR retention_hours < 0 THEN
        RAISE EXCEPTION 'retention_hours must be a non-negative integer';
    END IF;

    -- Calculate cutoff timestamp
    cutoff_timestamp := NOW() - (retention_hours || ' hours')::INTERVAL;

    -- Delete old sessions (frame_hashes will cascade delete automatically)
    DELETE FROM public.capture_sessions
    WHERE created_at < cutoff_timestamp;

    -- Get number of deleted rows
    GET DIAGNOSTICS deleted_count = ROW_COUNT;

    -- Log the operation (optional - useful for auditing)
    RAISE NOTICE 'Deleted % sessions older than % (cutoff: %)', 
        deleted_count, 
        retention_hours || ' hours', 
        cutoff_timestamp;

    RETURN deleted_count;
END;
$$;

-- ============================================================================
-- Security and Permissions
-- ============================================================================

-- Allow service_role to execute this function
GRANT EXECUTE ON FUNCTION public.cleanup_expired_sessions(INTEGER) TO service_role;

-- Optionally allow authenticated users (if you want to expose via API)
-- GRANT EXECUTE ON FUNCTION public.cleanup_expired_sessions(INTEGER) TO authenticated;

-- ============================================================================
-- Comments for documentation
-- ============================================================================

COMMENT ON FUNCTION public.cleanup_expired_sessions(INTEGER) IS 
    'Deletes capture sessions older than the specified number of hours. Frame hashes are cascade deleted automatically. Returns the number of sessions deleted.';

-- ============================================================================
-- Testing and Manual Execution
-- ============================================================================

-- Test the function (dry run - see what would be deleted):
-- SELECT COUNT(*) FROM public.capture_sessions 
-- WHERE created_at < NOW() - INTERVAL '24 hours';

-- Actually run the cleanup (delete sessions older than 24 hours):
-- SELECT public.cleanup_expired_sessions(24);

-- Verify deletion worked:
-- SELECT COUNT(*) FROM public.capture_sessions;
-- SELECT COUNT(*) FROM public.frame_hashes;

-- ============================================================================
-- Automation Options
-- ============================================================================

-- OPTION A: Scheduled Edge Function (Supabase Pro plan)
-- Create an edge function and schedule it with Supabase Cron

-- OPTION B: pg_cron Extension (if available)
-- Schedule daily cleanup at 2 AM UTC:
-- SELECT cron.schedule(
--   'retention-cleanup-daily',
--   '0 2 * * *',
--   $$SELECT public.cleanup_expired_sessions(24)$$
-- );

-- OPTION C: External Cron
-- Set up a cron job that calls your API endpoint which executes this function

-- ============================================================================
-- Rollback (if needed)
-- ============================================================================

-- To remove this function:
-- DROP FUNCTION IF EXISTS public.cleanup_expired_sessions(INTEGER);
