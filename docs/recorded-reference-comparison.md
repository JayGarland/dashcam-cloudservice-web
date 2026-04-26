# Recorded Reference Comparison Protocol

## Goal
Compare verification results when using preview hashes versus recorded hashes.

## Prerequisites
- Supabase migration applied for `frame_hashes.source` and `capture_sessions.reference_source`.
- Validator API running with `/api/sessions/{sessionId}/recorded-hashes` enabled.
- Capture client signed in.

## Steps
1. Start the capture client and enable "Record local video".
2. Start a session and record ~15 seconds.
3. Stop the session and wait for the log line: "Recorded reference ready".
4. Verify using the recorded file twice:
   - Default:
     - `POST /api/claims/verify` with the file and sessionId.
   - Preview reference:
     - `POST /api/claims/verify?reference=preview` with the same file and sessionId.

## What to compare
From the response JSON:
- `MatchRatio`, `MatchedSamples`, `AvgDistance`, `MaxDistance`
- `Debug.BestDistanceHistogram`, `Debug.CountTooDissimilar`, `Debug.BestDeltaMs`
- `Debug.ReferenceSourceUsed`

## Expected outcome
- Recorded reference uses `ReferenceSourceUsed = "recorded"` and shows higher match ratios.
- Preview reference uses `ReferenceSourceUsed = "preview"` and may be lower or more variable.
