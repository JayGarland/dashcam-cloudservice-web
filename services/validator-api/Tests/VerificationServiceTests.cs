using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValidatorApi.Models;
using ValidatorApi.Services;
using Xunit;

namespace ValidatorApi.Tests;

public class VerificationServiceTests
{
    private const long BaseEpochMs = 1700000000000;
    private const int IntervalMs = 500;

    [Fact]
    public async Task VerifyAsync_ReturnsVerified_WhenRatioHighAndNoMissingSpans()
    {
        var (rgba, width, height, hashHex) = BuildFixtureFrame();
        var sessionId = "session-verified";
        var reference = BuildReference(sessionId, 20, IntervalMs, BaseEpochMs, hashHex);
        var frames = BuildFramesForIndices(Enumerable.Range(0, 20).Where(i => i != 5 && i != 15), IntervalMs, rgba, width, height);

        var service = BuildService(sessionId, reference, frames);
        var result = await service.VerifyAsync(new MemoryStream(new byte[] { 1 }), sessionId, BuildMetadata(sessionId), CancellationToken.None);

        Assert.Equal(Verdict.Verified, result.Verdict);
        Assert.InRange(result.MatchRatio, 0.90, 1.0);
        Assert.Equal(20, result.ExpectedSamples);
        Assert.Equal(18, result.MatchedSamples);
        Assert.Empty(result.MissingSpans);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSuspicious_WhenRatioBelowThreshold()
    {
        var (rgba, width, height, hashHex) = BuildFixtureFrame();
        var sessionId = "session-low-ratio";
        var reference = BuildReference(sessionId, 20, IntervalMs, BaseEpochMs, hashHex);
        var frames = BuildFramesForIndices(Enumerable.Range(0, 10), IntervalMs, rgba, width, height);

        var service = BuildService(sessionId, reference, frames);
        var result = await service.VerifyAsync(new MemoryStream(new byte[] { 1 }), sessionId, BuildMetadata(sessionId), CancellationToken.None);

        Assert.Equal(Verdict.Suspicious, result.Verdict);
        Assert.True(result.MatchRatio < 0.80);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSuspicious_WhenMissingSpanExceedsThreshold()
    {
        var (rgba, width, height, hashHex) = BuildFixtureFrame();
        var sessionId = "session-missing-span";
        var reference = BuildReference(sessionId, 60, IntervalMs, BaseEpochMs, hashHex);
        var missingIndices = Enumerable.Range(20, 10).ToHashSet();
        var frames = BuildFramesForIndices(Enumerable.Range(0, 60).Where(i => !missingIndices.Contains(i)), IntervalMs, rgba, width, height);

        var service = BuildService(sessionId, reference, frames);
        var result = await service.VerifyAsync(new MemoryStream(new byte[] { 1 }), sessionId, BuildMetadata(sessionId), CancellationToken.None);

        Assert.Equal(Verdict.Suspicious, result.Verdict);
        Assert.NotEmpty(result.MissingSpans);
        Assert.InRange(result.MatchRatio, 0.80, 0.89);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsInconclusive_WhenTooFewFramesExtracted()
    {
        var (rgba, width, height, hashHex) = BuildFixtureFrame();
        var sessionId = "session-too-few";
        var reference = BuildReference(sessionId, 10, IntervalMs, BaseEpochMs, hashHex);
        var frames = BuildFramesForIndices(new[] { 0, 1, 2 }, IntervalMs, rgba, width, height);

        var service = BuildService(sessionId, reference, frames);
        var result = await service.VerifyAsync(new MemoryStream(new byte[] { 1 }), sessionId, BuildMetadata(sessionId), CancellationToken.None);

        Assert.Equal(Verdict.Inconclusive, result.Verdict);
        Assert.Contains("Too few frames extracted", result.Notes);
    }

    [Fact]
    public async Task VerifyAsync_ThrowsValidationException_ForUnsupportedAlgo()
    {
        var (rgba, width, height, hashHex) = BuildFixtureFrame();
        var sessionId = "session-invalid-algo";
        var reference = BuildReference(sessionId, 10, IntervalMs, BaseEpochMs, hashHex);
        var frames = BuildFramesForIndices(Enumerable.Range(0, 10), IntervalMs, rgba, width, height);
        var metadata = BuildMetadata(sessionId);
        metadata.AlgoVersion = "phash";

        var service = BuildService(sessionId, reference, frames);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.VerifyAsync(new MemoryStream(new byte[] { 1 }), sessionId, metadata, CancellationToken.None));
    }

    private static VerificationService BuildService(
        string sessionId,
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<ExtractedFrame> frames)
    {
        var store = new FakeSupabaseHashStore(new CaptureSession
        {
            SessionId = sessionId,
            DeviceClockStartEpochMs = BaseEpochMs,
            SamplingIntervalMs = IntervalMs,
            AlgoVersion = "dhash64_v1"
        }, reference);
        var extractor = new FakeVideoFrameExtractor(frames);
        return new VerificationService(store, extractor);
    }

    private static VerifyClaimMetadata BuildMetadata(string sessionId)
    {
        return new VerifyClaimMetadata
        {
            SessionId = sessionId,
            DeviceClockStartEpochMs = BaseEpochMs,
            SamplingIntervalMs = IntervalMs,
            AlgoVersion = "dhash64_v1",
            ToleranceMs = 200
        };
    }

    private static IReadOnlyList<FrameHashRecord> BuildReference(
        string sessionId,
        int count,
        int intervalMs,
        long baseEpochMs,
        string hashHex)
    {
        var records = new List<FrameHashRecord>(count);
        for (var i = 0; i < count; i += 1)
        {
            var elapsedMs = i * intervalMs;
            records.Add(new FrameHashRecord
            {
                SessionId = sessionId,
                SampleIndex = i,
                ElapsedMs = elapsedMs,
                SampleTimestampEpochMs = baseEpochMs + elapsedMs,
                HashHex = hashHex,
                IntervalMs = intervalMs,
                AlgoVersion = "dhash64_v1",
                CreatedAtEpochMs = 0,
                UploadState = "complete"
            });
        }
        return records;
    }

    private static IReadOnlyList<ExtractedFrame> BuildFramesForIndices(
        IEnumerable<int> indices,
        int intervalMs,
        byte[] rgba,
        int width,
        int height)
    {
        var frames = new List<ExtractedFrame>();
        foreach (var idx in indices.OrderBy(i => i))
        {
            frames.Add(new ExtractedFrame(idx * intervalMs, rgba, width, height));
        }
        return frames;
    }

    private static (byte[] rgba, int width, int height, string hashHex) BuildFixtureFrame()
    {
        const int width = 9;
        const int height = 8;
        var rgba = BuildSolidRgba(width, height, 0);
        var hashHex = DHash64.ToHex(DHash64.FromRgba(rgba, width, height));
        return (rgba, width, height, hashHex);
    }

    private static byte[] BuildSolidRgba(int width, int height, byte value)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = value;
            rgba[i + 1] = value;
            rgba[i + 2] = value;
            rgba[i + 3] = 255;
        }
        return rgba;
    }

    private sealed class FakeSupabaseHashStore : ISupabaseHashStore
    {
        private readonly CaptureSession? _session;
        private readonly IReadOnlyList<FrameHashRecord> _hashes;
        private readonly Exception? _sessionException;

        public FakeSupabaseHashStore(
            CaptureSession? session,
            IReadOnlyList<FrameHashRecord> hashes,
            Exception? sessionException = null)
        {
            _session = session;
            _hashes = hashes;
            _sessionException = sessionException;
        }

        public Task<CaptureSession?> GetSessionAsync(string sessionId, CancellationToken ct)
        {
            if (_sessionException is not null)
            {
                throw _sessionException;
            }
            return Task.FromResult(_session);
        }

        public Task<IReadOnlyList<FrameHashRecord>> GetFrameHashesAsync(string sessionId, CancellationToken ct)
        {
            return Task.FromResult(_hashes);
        }
    }

    private sealed class FakeVideoFrameExtractor : IVideoFrameExtractor
    {
        private readonly IReadOnlyList<ExtractedFrame> _frames;

        public FakeVideoFrameExtractor(IReadOnlyList<ExtractedFrame> frames)
        {
            _frames = frames;
        }

        public Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
            Stream videoStream,
            int intervalMs,
            CancellationToken ct)
        {
            return Task.FromResult(_frames);
        }
    }
}
