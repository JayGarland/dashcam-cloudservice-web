namespace ValidatorApi.Models;

public class CaptureSession
{
    public string SessionId { get; set; } = string.Empty;
    public long DeviceClockStartEpochMs { get; set; }
    public int SamplingIntervalMs { get; set; }
    public string AlgoVersion { get; set; } = "dhash64_v1";
    public string? ClientVersion { get; set; }
}

public class FrameHashRecord
{
    public string SessionId { get; set; } = string.Empty;
    public int SampleIndex { get; set; }
    public int ElapsedMs { get; set; }
    public long SampleTimestampEpochMs { get; set; }
    public string HashHex { get; set; } = string.Empty;
    public int IntervalMs { get; set; }
    public string AlgoVersion { get; set; } = "dhash64_v1";
    public long CreatedAtEpochMs { get; set; }
    public string UploadState { get; set; } = "pending";
}