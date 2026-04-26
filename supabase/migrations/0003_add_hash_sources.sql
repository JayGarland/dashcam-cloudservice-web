-- Migration 0003: Add source columns for recorded/preview timelines
-- Purpose: Track hash source (preview vs recorded) and reference source on sessions
-- Author: Dashcam Cloud Service Team
-- Date: February 2026

ALTER TABLE public.capture_sessions
    ADD COLUMN IF NOT EXISTS reference_source TEXT NOT NULL DEFAULT 'preview';

ALTER TABLE public.frame_hashes
    ADD COLUMN IF NOT EXISTS source TEXT NOT NULL DEFAULT 'preview';

-- Allow parallel timelines per session by including source in the uniqueness constraint
ALTER TABLE public.frame_hashes
    DROP CONSTRAINT IF EXISTS unique_session_sample;

ALTER TABLE public.frame_hashes
    ADD CONSTRAINT unique_session_source_sample UNIQUE (session_id, source, sample_index);

-- Index for recorded vs preview queries by elapsed time
CREATE INDEX IF NOT EXISTS idx_frame_hashes_source_elapsed ON
    public.frame_hashes (session_id, source, elapsed_ms);
