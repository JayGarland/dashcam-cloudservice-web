# Falsification Matrix (Slice 5D)

Use these commands to generate variants from the original recording (`input.webm`).
Run verification with debug enabled (`?debug=1` or `X-Debug: 1`) and capture metrics.

## A) Pure re-encode (no fps/scale/speed change)
- Command:
  ```bash
  ffmpeg -i input.webm -c:v libx264 -crf 23 -preset veryfast -pix_fmt yuv420p -an reencode.mp4
  ```
- Changes: codec/bitrate/container only (visual content preserved).
- Expected effect: **Verified** (ideally). If it fails, debug should reveal *why*.
- Likely debug pattern if it fails:
  - Case A (timestamp drift): candidateCountInWindow == 0 for most refs.
  - Case B (visual mismatch): candidateCountInWindow > 0 but bestMinDistance > threshold.
  - Case C (extract/parsing): extractedFrameCount == 0 or elapsed range nonsense.

## B) Container remux only (no re-encode)
- Command:
  ```bash
  ffmpeg -i input.webm -c copy remux.mkv
  ```
- Changes: container only (stream copy).
- Expected effect: **Verified** if container supports the codec.
- Note: if remux fails due to codec/container constraints, record it as **Inconclusive** and document error.

## C) Trim out a middle segment (tamper)
- Command:
  ```bash
  ffmpeg -i input.webm -filter_complex "[0:v]trim=0:8,setpts=PTS-STARTPTS[v0];[0:v]trim=11,setpts=PTS-STARTPTS[v1];[v0][v1]concat=n=2:v=1:a=0" -an trimmed.webm
  ```
- Changes: removes ~3s segment (gap in timeline).
- Expected effect: **Suspicious** (missing spans should appear).
- Likely debug pattern:
  - candidateCountInWindow drops for ref samples in the removed window; missingSpans reported.

## D) FPS change (tamper-ish)
- Command:
  ```bash
  ffmpeg -i input.webm -vf fps=25 -an fps25.webm
  ```
- Changes: frame rate conversion (timestamps may drift).
- Expected effect: **Suspicious** or **Inconclusive** depending on PTS handling.
- Likely debug pattern:
  - Case A if candidate windows go empty.
  - Case B if candidates exist but distances exceed threshold.

## E) Speed change (tamper)
- Command:
  ```bash
  ffmpeg -i input.webm -filter:v "setpts=0.9*PTS" -an speedup.webm
  ```
- Changes: 10% speedup (timeline compression).
- Expected effect: **Suspicious**.
- Likely debug pattern:
  - Candidate windows gradually drift (candidateCountInWindow drops over time).
