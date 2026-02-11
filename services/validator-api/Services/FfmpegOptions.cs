namespace ValidatorApi.Services;

public sealed class FfmpegOptions
{
    public string Path { get; set; } = "ffmpeg";
    public string ProbePath { get; set; } = "ffprobe";
}
