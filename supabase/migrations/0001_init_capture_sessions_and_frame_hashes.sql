-- Migration 0001: Initialize capture_sessions and frame_hashes tables
-- Purpose: Store capture session metadata and individual frame hash records
-- Author: Dashcam Cloud Service Team
-- Date: January 2026

-- ============================================================================
-- Table: capture_sessions
-- ============================================================================
-- Stores metadata for each capture session from dashcam devices

CREATE TABLE IF NOT EXISTS PUBLIC.CAPTURE_SESSIONS (
   SESSION_ID                  TEXT PRIMARY KEY,
   DEVICE_CLOCK_START_EPOCH_MS BIGINT NOT NULL,
   SAMPLING_INTERVAL_MS        INTEGER NOT NULL,
   ALGO_VERSION                TEXT NOT NULL DEFAULT 'dhash64_v1',
   CLIENT_VERSION              TEXT,
   CREATED_AT                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Constraints
   CONSTRAINT POSITIVE_START_TIME CHECK ( DEVICE_CLOCK_START_EPOCH_MS > 0 ),
   CONSTRAINT POSITIVE_INTERVAL CHECK ( SAMPLING_INTERVAL_MS > 0 )
);

-- Index for querying sessions by creation time (useful for retention)
CREATE INDEX IF NOT EXISTS IDX_CAPTURE_SESSIONS_CREATED_AT ON
   PUBLIC.CAPTURE_SESSIONS (
      CREATED_AT
   );

-- ============================================================================
-- Table: frame_hashes
-- ============================================================================
-- Stores individual frame hash records for each session

CREATE TABLE IF NOT EXISTS PUBLIC.FRAME_HASHES (
   ID                        BIGSERIAL PRIMARY KEY,
   SESSION_ID                TEXT NOT NULL
      REFERENCES PUBLIC.CAPTURE_SESSIONS ( SESSION_ID )
         ON DELETE CASCADE,
   SAMPLE_INDEX              INTEGER NOT NULL,
   ELAPSED_MS                BIGINT NOT NULL,
   SAMPLE_TIMESTAMP_EPOCH_MS BIGINT NOT NULL,
   HASH_HEX                  TEXT NOT NULL,
   INTERVAL_MS               INTEGER NOT NULL,
   ALGO_VERSION              TEXT NOT NULL DEFAULT 'dhash64_v1',
   CREATED_AT                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Constraints
   CONSTRAINT UNIQUE_SESSION_SAMPLE UNIQUE ( SESSION_ID,
                                             SAMPLE_INDEX ),
   CONSTRAINT POSITIVE_ELAPSED CHECK ( ELAPSED_MS >= 0 ),
   CONSTRAINT POSITIVE_SAMPLE_INDEX CHECK ( SAMPLE_INDEX >= 0 ),
   CONSTRAINT VALID_HASH_LENGTH CHECK ( LENGTH(HASH_HEX) = 16 ), -- 64 bits = 16 hex chars
   CONSTRAINT POSITIVE_SAMPLE_TIMESTAMP CHECK ( SAMPLE_TIMESTAMP_EPOCH_MS > 0 )
);

-- Index for efficient queries by session (used during verification)
CREATE INDEX IF NOT EXISTS IDX_FRAME_HASHES_SESSION_ID ON
   PUBLIC.FRAME_HASHES (
      SESSION_ID
   );

-- Index for timestamp range queries (used in claim verification)
CREATE INDEX IF NOT EXISTS IDX_FRAME_HASHES_TIMESTAMP ON
   PUBLIC.FRAME_HASHES (
      SESSION_ID,
      SAMPLE_TIMESTAMP_EPOCH_MS
   );

-- Index for sample_index ordering (used for sequential access)
CREATE INDEX IF NOT EXISTS IDX_FRAME_HASHES_SAMPLE_INDEX ON
   PUBLIC.FRAME_HASHES (
      SESSION_ID,
      SAMPLE_INDEX
   );

-- ============================================================================
-- Comments for documentation
-- ============================================================================

COMMENT ON TABLE PUBLIC.CAPTURE_SESSIONS IS
   'Capture session metadata from dashcam devices. Each session represents one continuous recording period.';

COMMENT ON COLUMN PUBLIC.CAPTURE_SESSIONS.SESSION_ID IS
   'Unique identifier for the capture session (UUID or similar)';

COMMENT ON COLUMN PUBLIC.CAPTURE_SESSIONS.DEVICE_CLOCK_START_EPOCH_MS IS
   'Device clock timestamp when capture started (milliseconds since Unix epoch)';

COMMENT ON COLUMN PUBLIC.CAPTURE_SESSIONS.SAMPLING_INTERVAL_MS IS
   'Target interval between frame samples in milliseconds (e.g., 500 for 2 FPS)';

COMMENT ON COLUMN PUBLIC.CAPTURE_SESSIONS.ALGO_VERSION IS
   'Hash algorithm version identifier (e.g., dhash64_v1)';

COMMENT ON TABLE PUBLIC.FRAME_HASHES IS
   'Individual frame hash records. Each record represents one sampled frame from a capture session.';

COMMENT ON COLUMN PUBLIC.FRAME_HASHES.SAMPLE_INDEX IS
   'Sequential index of this sample within the session (0, 1, 2, ...)';

COMMENT ON COLUMN PUBLIC.FRAME_HASHES.ELAPSED_MS IS
   'Elapsed time since session start in milliseconds';

COMMENT ON COLUMN PUBLIC.FRAME_HASHES.HASH_HEX IS
   '64-bit perceptual hash of the frame in hexadecimal (16 characters)';

-- ============================================================================
-- Verification Query (for testing)
-- ============================================================================

-- Run this after migration to verify tables were created correctly:
-- SELECT table_name, column_name, data_type 
-- FROM information_schema.columns 
-- WHERE table_name IN ('capture_sessions', 'frame_hashes') 
-- ORDER BY table_name, ordinal_position;