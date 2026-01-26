# Slice 4C Log — ffmpeg-based Frame Extraction (Validator API)

Date: 2026-01-26

## What changed
- Added `FfmpegVideoFrameExtractor` (one ffmpeg run, showinfo pts_time parsing, PNG -> RGBA via ImageSharp).
- Introduced `IProcessRunner` abstraction + `ProcessRunner` for real process execution.
- Added `FfmpegOptions` config (Ffmpeg:Path).
- Added unit tests for ffmpeg args, pts_time parsing, PNG decode, ordering, and error handling.
- Updated flow diagram to mark `IVideoFrameExtractor` as REAL for Slice 4C.

## How to verify
```
cd services/validator-api
dotnet test
```

Latest local run:
```
dotnet test .\Tests\validator-api.Tests.csproj -v minimal
Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 331 ms - validator-api.Tests.dll (net8.0)
```

NuGet warnings observed:
- NU1902/NU1903 for SixLabors.ImageSharp 3.1.4 (known vulnerabilities).

## Notes / assumptions
- The order of `pts_time` values emitted by `showinfo` matches the order of output PNG files (`frame_%06d.png`).
- Cleanup of temp directories is best-effort; debug builds keep the temp directory.

## What remains mocked after 4C
- Validator Portal UI (Slice 4D) is still pending.
