using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ValidatorApi.Services;
using Xunit;

namespace ValidatorApi.Tests;

public sealed class FfmpegVideoFrameExtractorTests
{
    [Fact]
    public async Task ParsesPtsTimeFromShowinfoAndMapsToElapsedMs()
    {
        var runner = new FakeProcessRunner(async (request, ct) =>
        {
            var outputDir = GetOutputDirectory(request.Arguments);
            await WritePngAsync(Path.Combine(outputDir, "frame_000001.png"), 1, 1, new Rgba32(1, 2, 3, 4));
            await WritePngAsync(Path.Combine(outputDir, "frame_000002.png"), 1, 1, new Rgba32(5, 6, 7, 8));

            var stderr =
                "[Parsed_showinfo_0 @ 0x000] n:0 pts:0 pts_time:0.0333333 pos:0 fmt:rgb24\n" +
                "[Parsed_showinfo_0 @ 0x000] n:1 pts:1 pts_time:1.5 pos:0 fmt:rgb24";

            return new ProcessRunResult(0, string.Empty, stderr);
        });

        var extractor = CreateExtractor(runner);
        using var video = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var frames = await extractor.ExtractFramesAsync(video, 1000, CancellationToken.None);

        Assert.Equal(2, frames.Count);
        Assert.Equal(33, frames[0].ElapsedMs);
        Assert.Equal(1500, frames[1].ElapsedMs);
    }

    [Fact]
    public async Task BuildsCorrectFfmpegArgsForIntervalMs500()
    {
        var runner = new FakeProcessRunner((request, _) =>
        {
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
        });

        var extractor = CreateExtractor(runner);
        using var video = new MemoryStream(new byte[] { 9, 8, 7 });

        var frames = await extractor.ExtractFramesAsync(video, 500, CancellationToken.None);

        Assert.Empty(frames);
        Assert.NotNull(runner.LastRequest);
        var args = runner.LastRequest!.Arguments;
        Assert.Contains("-hide_banner", args);
        Assert.Contains("-loglevel info", args);
        Assert.Contains("fps=2", args);
        Assert.Contains("frame_%06d.png", args);
    }

    [Fact]
    public async Task DecodesPngToRgbaCorrectlyUsingImageSharp()
    {
        var runner = new FakeProcessRunner(async (request, ct) =>
        {
            var outputDir = GetOutputDirectory(request.Arguments);
            await WritePngAsync(
                Path.Combine(outputDir, "frame_000001.png"),
                2,
                1,
                new Rgba32(255, 0, 0, 255),
                new Rgba32(0, 255, 0, 128));

            var stderr = "[Parsed_showinfo_0 @ 0x000] n:0 pts:0 pts_time:0.0 pos:0 fmt:rgba";
            return new ProcessRunResult(0, string.Empty, stderr);
        });

        var extractor = CreateExtractor(runner);
        using var video = new MemoryStream(new byte[] { 1, 2 });

        var frames = await extractor.ExtractFramesAsync(video, 1000, CancellationToken.None);

        Assert.Single(frames);
        var frame = frames[0];
        Assert.Equal(2, frame.Width);
        Assert.Equal(1, frame.Height);
        Assert.Equal(
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 128 },
            frame.Rgba);
    }

    [Fact]
    public async Task ExtractFramesAsyncReturnsFramesInFileOrderWithMatchingPtsTimeOrder()
    {
        var runner = new FakeProcessRunner(async (request, ct) =>
        {
            var outputDir = GetOutputDirectory(request.Arguments);
            await WritePngAsync(Path.Combine(outputDir, "frame_000002.png"), 1, 1, new Rgba32(2, 2, 2, 255));
            await WritePngAsync(Path.Combine(outputDir, "frame_000001.png"), 1, 1, new Rgba32(1, 1, 1, 255));
            await WritePngAsync(Path.Combine(outputDir, "frame_000003.png"), 1, 1, new Rgba32(3, 3, 3, 255));

            var stderr =
                "[Parsed_showinfo_0 @ 0x000] n:0 pts:0 pts_time:0.0 pos:0 fmt:rgba\n" +
                "[Parsed_showinfo_0 @ 0x000] n:1 pts:1 pts_time:1.234 pos:0 fmt:rgba\n" +
                "[Parsed_showinfo_0 @ 0x000] n:2 pts:2 pts_time:2.0 pos:0 fmt:rgba";

            return new ProcessRunResult(0, string.Empty, stderr);
        });

        var extractor = CreateExtractor(runner);
        using var video = new MemoryStream(new byte[] { 1, 2, 3 });

        var frames = await extractor.ExtractFramesAsync(video, 1000, CancellationToken.None);

        Assert.Equal(3, frames.Count);
        Assert.Equal(new[] { 0, 1234, 2000 }, frames.Select(frame => frame.ElapsedMs).ToArray());
    }

    [Fact]
    public async Task ThrowsOnNonZeroExitCodeWithStderrIncluded()
    {
        var runner = new FakeProcessRunner((request, _) =>
        {
            return Task.FromResult(new ProcessRunResult(1, string.Empty, "ffmpeg error details"));
        });

        var extractor = CreateExtractor(runner);
        using var video = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = await Assert.ThrowsAsync<FfmpegException>(() =>
            extractor.ExtractFramesAsync(video, 1000, CancellationToken.None));

        Assert.Equal(1, ex.ExitCode);
        Assert.Contains("ffmpeg error details", ex.Message);
    }

    private static FfmpegVideoFrameExtractor CreateExtractor(FakeProcessRunner runner)
    {
        return new FfmpegVideoFrameExtractor(runner, Options.Create(new FfmpegOptions { Path = "ffmpeg" }));
    }

    private static string GetOutputDirectory(string arguments)
    {
        const string marker = "frame_%06d.png";
        var markerIndex = arguments.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("Output pattern not found in ffmpeg arguments.");
        }

        var quoteStart = arguments.LastIndexOf('"', markerIndex);
        var quoteEnd = arguments.IndexOf('"', markerIndex);

        string outputPattern;
        if (quoteStart >= 0 && quoteEnd > quoteStart)
        {
            outputPattern = arguments.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }
        else
        {
            var start = arguments.LastIndexOf(' ', markerIndex);
            outputPattern = arguments.Substring(start + 1, markerIndex + marker.Length - (start + 1));
        }

        var directory = Path.GetDirectoryName(outputPattern);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Output directory could not be determined.");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WritePngAsync(string path, int width, int height, params Rgba32[] pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("Pixel count does not match dimensions.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var image = new Image<Rgba32>(width, height);
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = pixels[index++];
            }
        }

        await image.SaveAsPngAsync(path);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, CancellationToken, Task<ProcessRunResult>> _handler;

        public FakeProcessRunner(Func<ProcessRunRequest, CancellationToken, Task<ProcessRunResult>> handler)
        {
            _handler = handler;
        }

        public ProcessRunRequest? LastRequest { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return _handler(request, ct);
        }
    }
}
