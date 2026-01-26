using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ValidatorApi.Services;

public sealed class FfmpegVideoFrameExtractor : IVideoFrameExtractor
{
    private const string FramePattern = "frame_%06d.png";
    private const string PtsTimeToken = "pts_time:";
    private readonly IProcessRunner _processRunner;
    private readonly FfmpegOptions _options;

    public FfmpegVideoFrameExtractor(IProcessRunner processRunner, IOptions<FfmpegOptions> options)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
        Stream videoStream,
        int intervalMs,
        CancellationToken ct)
    {
        if (videoStream is null)
        {
            throw new ArgumentNullException(nameof(videoStream));
        }

        if (intervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Interval must be positive.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"dashcam-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var keepTemp = false;
#if DEBUG
        keepTemp = true;
#endif

        try
        {
            var inputPath = Path.Combine(tempDir, "input.bin");
            await using (var fileStream = new FileStream(inputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await videoStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            var fps = 1000.0 / intervalMs;
            var outputPattern = Path.Combine(tempDir, FramePattern);
            var args = BuildArguments(inputPath, outputPattern, fps);

            var result = await _processRunner
                .RunAsync(new ProcessRunRequest(_options.Path, args, tempDir), ct)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                var snippet = TrimStderr(result.StdErr);
                throw new FfmpegException(
                    $"ffmpeg exited with code {result.ExitCode}. Stderr: {snippet}",
                    result.ExitCode,
                    result.StdErr);
            }

            var ptsTimes = ParsePtsTimes(result.StdErr);
            var pngPaths = Directory
                .EnumerateFiles(tempDir, "frame_*.png")
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var frameCount = Math.Min(pngPaths.Count, ptsTimes.Count);
            if (frameCount == 0)
            {
                return Array.Empty<ExtractedFrame>();
            }

            var frames = new List<ExtractedFrame>(frameCount);
            for (var i = 0; i < frameCount; i++)
            {
                var (rgba, width, height) = await DecodePngToRgbaAsync(pngPaths[i], ct).ConfigureAwait(false);
                var elapsedMs = (int)Math.Round(ptsTimes[i] * 1000.0, MidpointRounding.AwayFromZero);
                frames.Add(new ExtractedFrame(elapsedMs, rgba, width, height));
            }

            return frames;
        }
        finally
        {
            if (!keepTemp)
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Best-effort cleanup; ignore failures.
                }
            }
        }
    }

    private static string BuildArguments(string inputPath, string outputPattern, double fps)
    {
        var fpsValue = fps.ToString(CultureInfo.InvariantCulture);
        return $"-hide_banner -loglevel info -i \"{inputPath}\" -vf \"fps={fpsValue},showinfo\" -vsync vfr \"{outputPattern}\"";
    }

    private static IReadOnlyList<double> ParsePtsTimes(string stderr)
    {
        var ptsTimes = new List<double>();
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return ptsTimes;
        }

        var lines = stderr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var index = line.IndexOf(PtsTimeToken, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            index += PtsTimeToken.Length;
            var end = index;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                end++;
            }

            var token = line.Substring(index, end - index);
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                ptsTimes.Add(value);
            }
        }

        return ptsTimes;
    }

    private static async Task<(byte[] Rgba, int Width, int Height)> DecodePngToRgbaAsync(
        string path,
        CancellationToken ct)
    {
        using var image = await Image.LoadAsync<Rgba32>(path, ct).ConfigureAwait(false);
        var rgba = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rgba);
        return (rgba, image.Width, image.Height);
    }

    private static string TrimStderr(string stderr, int maxChars = 4000)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return string.Empty;
        }

        var cleaned = stderr.Replace("\r", " ").Replace("\n", " ").Trim();
        if (cleaned.Length <= maxChars)
        {
            return cleaned;
        }

        return cleaned.Substring(0, maxChars);
    }
}
