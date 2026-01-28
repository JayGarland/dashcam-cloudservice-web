# Slice 5D QA Checklist

## Auth + API
- 401 when no JWT is provided.
- 403 when JWT lacks validator role.
- 404 when sessionId not found.
- 410 when session is expired (if retention enabled).

## Baseline Verification
- Original WebM verifies with matchRatio ~1.00.
- missingSpans empty on baseline.
- Debug metrics (debug=1):
  - extractedFrameCount > 0
  - elapsed range sane (min < max, non-negative)
  - candidateCountInWindow > 0 for initial samples

## Falsification Variants
- Pure re-encode: should be Verified (or explain failure via debug metrics).
- Trim variant: should be Suspicious with missing spans.
- FPS change: likely Suspicious or Inconclusive; debug indicates Case A/B/C.
- Speed change: Suspicious with increasing drift.
- Remux: Verified if container supports codec; else Inconclusive with documented error.

## Debug Interpretation
- Case A: candidateCountInWindow == 0 for most refs (timestamp/PTS drift).
- Case B: candidateCountInWindow > 0 but bestMinDistance > threshold (visual/hash mismatch).
- Case C: extractedFrameCount == 0 or elapsed range nonsense (extraction/PTS parse issue).

## Regression Checks
- No change to dHash64 or matching thresholds.
- Debug output only when debug=1 or X-Debug: 1.
- Logs contain no secrets.
