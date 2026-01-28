# Slice 5D Demo Script (End-to-End)

## Preconditions
- Supabase configured for validator-api (service role key set, hash store accessible).
- Validator auth working (JWT with validator role).
- capture-client running and streaming hashes to Supabase.
- Validator portal or API client ready to upload a video + sessionId.

## Baseline (Original Recording)
1) Start a new capture session in capture-client.
2) Let it run for ~20–30 seconds.
3) Stop the session; note the sessionId.
4) Download or save the original recorded WebM locally (e.g., `input.webm`).
5) Verify the original:
   - Portal: upload `input.webm` + sessionId.
   - API: `POST /api/claims/verify?debug=1` with multipart `video` + `sessionId`.
6) Expect:
   - Match ratio ~1.00
   - Missing spans = none
   - Debug metrics show extractedFrameCount > 0 and candidateCountInWindow > 0 for first samples.

## Falsification Variants (Use Matrix)
1) Generate each falsified variant from `input.webm` using the commands in `docs/falsification-matrix.md`.
2) Verify each variant with debug enabled (`?debug=1` or `X-Debug: 1`).
3) Record:
   - matchRatio
   - missingSpans
   - debug metrics (candidate counts, min distance, elapsed range)
4) Classify each as Verified / Suspicious / Inconclusive based on the expected outcomes.

## Record Template
- SessionId:
- Variant:
- Command:
- Verdict:
- MatchRatio:
- MissingSpans:
- Debug summary:
  - extractedFrameCount / elapsed range
  - candidateCountInWindow (first 5)
  - bestMinDistance (first 5)
