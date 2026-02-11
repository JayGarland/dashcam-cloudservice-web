using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ValidatorApi.Models;

namespace ValidatorApi.Services;

public class VerificationService
{
    public const int DefaultIntervalMs = 500;
    public const int DefaultToleranceMs = 200;
    public const int DefaultDistanceThreshold = 5;
    public const double VerifiedMatchRatio = 0.90;
    public const double SuspiciousMatchRatio = 0.80;
    private const int DebugSampleCount = 5;

    private readonly ISupabaseHashStore _store;
    private readonly IVideoFrameExtractor _extractor;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        ISupabaseHashStore store,
        IVideoFrameExtractor extractor,
        ILogger<VerificationService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VerificationResult> VerifyAsync(
        Stream videoStream,
        string sessionId,
        VerifyClaimMetadata? metadataOverride,
        CancellationToken ct,
        bool debugEnabled = false)
    {
        if (videoStream is null)
        {
            throw new ArgumentNullException(nameof(videoStream));
        }

        var normalizedSessionId = sessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            throw new ValidationException("sessionId is required.");
        }

        if (metadataOverride is not null
            && !string.IsNullOrWhiteSpace(metadataOverride.SessionId)
            && !string.Equals(metadataOverride.SessionId, normalizedSessionId, StringComparison.Ordinal))
        {
            throw new ValidationException("sessionId does not match metadata sessionId.");
        }

        CaptureSession? session;
        try
        {
            session = await _store.GetSessionAsync(normalizedSessionId, ct);
        }
        catch (SessionExpiredException)
        {
            throw;
        }

        if (session is null)
        {
            throw new NotFoundException($"Session '{normalizedSessionId}' not found.");
        }

        var deviceClockStartEpochMs = metadataOverride?.DeviceClockStartEpochMs ?? session.DeviceClockStartEpochMs;
        var samplingIntervalMs = session.SamplingIntervalMs;
        var algoVersion = metadataOverride?.AlgoVersion ?? session.AlgoVersion;
        var toleranceMs = metadataOverride?.ToleranceMs ?? DefaultToleranceMs;

        if (samplingIntervalMs <= 0)
        {
            throw new ValidationException("samplingIntervalMs must be positive.");
        }
        if (!string.Equals(algoVersion, "dhash64_v1", StringComparison.Ordinal))
        {
            throw new ValidationException("algoVersion must be dhash64_v1.");
        }
        if (toleranceMs <= 0)
        {
            throw new ValidationException("toleranceMs must be positive.");
        }

        var intervalMs = samplingIntervalMs > 0 ? samplingIntervalMs : DefaultIntervalMs;

        var reference = await _store.GetFrameHashesAsync(normalizedSessionId, ct) ?? Array.Empty<FrameHashRecord>();
        var orderedReference = reference.OrderBy(r => r.ElapsedMs).ToList();

        int? rotationDegrees = null;
        Stream extractionStream = videoStream;
        MemoryStream? extractionBufferStream = null;
        byte[]? bufferedVideo = null;

        if (debugEnabled && _extractor is IVideoMetadataReader metadataReader)
        {
            bufferedVideo = await BufferVideoAsync(videoStream, ct).ConfigureAwait(false);
            extractionBufferStream = new MemoryStream(bufferedVideo, writable: false);
            extractionStream = extractionBufferStream;
            await using var rotationStream = new MemoryStream(bufferedVideo, writable: false);
            rotationDegrees = await metadataReader.GetRotationDegreesAsync(rotationStream, ct).ConfigureAwait(false);
        }

        var frames = await _extractor.ExtractFramesAsync(extractionStream, intervalMs, ct) ?? Array.Empty<ExtractedFrame>();
        extractionBufferStream?.Dispose();
        var frameList = frames.ToList();
        var tooFewFrames = frameList.Count < 5;

        var candidates = new List<FrameHashRecord>(frameList.Count);
        for (var i = 0; i < frameList.Count; i += 1)
        {
            var frame = frameList[i];
            var hashValue = DHash64.FromRgba(frame.Rgba, frame.Width, frame.Height);
            var hashHex = DHash64.ToHex(hashValue);
            var elapsedMs = frame.ElapsedMs;
            var sampleTimestampEpochMs = deviceClockStartEpochMs + elapsedMs;

            candidates.Add(new FrameHashRecord
            {
                SessionId = normalizedSessionId,
                SampleIndex = i,
                ElapsedMs = elapsedMs,
                SampleTimestampEpochMs = sampleTimestampEpochMs,
                HashHex = hashHex,
                IntervalMs = intervalMs,
                AlgoVersion = algoVersion,
                CreatedAtEpochMs = 0,
                UploadState = "pending"
            });
        }

        VerificationDebugMetrics? debugMetrics = null;
        if (debugEnabled)
        {
            debugMetrics = BuildDebugMetrics(
                normalizedSessionId,
                samplingIntervalMs,
                toleranceMs,
                rotationDegrees,
                orderedReference,
                candidates,
                frameList);
            _logger.LogInformation("Verification debug metrics for {SessionId}: {@Metrics}", normalizedSessionId, debugMetrics);
        }

        var matcher = new HashMatcher(DefaultDistanceThreshold, toleranceMs);
        var (matched, avgDist, maxDist, missingSpans) = matcher.Match(orderedReference, candidates, intervalMs);

        var expectedSamples = orderedReference.Count;
        var matchRatio = expectedSamples > 0 ? matched / (double)expectedSamples : 0d;

        if (debugEnabled && debugMetrics is not null && matchRatio < 0.5 && orderedReference.Count > 0 && candidates.Count > 0)
        {
            var sweep = RunDeltaSweep(orderedReference, candidates, toleranceMs, DefaultDistanceThreshold);
            debugMetrics.BestDeltaMs = sweep.BestDeltaMs;
            debugMetrics.BestMatchedSamples = sweep.BestMatched;
            _logger.LogInformation(
                "Verification delta sweep for {SessionId}: bestDeltaMs={BestDeltaMs} bestMatched={BestMatched}",
                normalizedSessionId,
                sweep.BestDeltaMs,
                sweep.BestMatched);
        }

        var notes = new List<string>();
        if (expectedSamples == 0)
        {
            notes.Add("No reference hashes found");
        }
        if (tooFewFrames)
        {
            notes.Add("Too few frames extracted");
        }
        if (debugEnabled && frameList.Count > 0)
        {
            var firstFrame = frameList[0];
            notes.Add($"Extracted frame size (first): {firstFrame.Width}x{firstFrame.Height}");
        }

        var verdict = Verdict.Inconclusive;
        if (!tooFewFrames)
        {
            if (matchRatio >= VerifiedMatchRatio && missingSpans.Count == 0)
            {
                verdict = Verdict.Verified;
            }
            else if (matchRatio < SuspiciousMatchRatio || missingSpans.Count > 0)
            {
                verdict = Verdict.Suspicious;
            }
        }

        return new VerificationResult
        {
            Verdict = verdict,
            SessionId = normalizedSessionId,
            Threshold = DefaultDistanceThreshold,
            ToleranceMs = toleranceMs,
            IntervalMs = intervalMs,
            ExpectedSamples = expectedSamples,
            MatchedSamples = matched,
            MatchRatio = matchRatio,
            AvgDistance = avgDist,
            MaxDistance = maxDist,
            MissingSpans = missingSpans,
            Notes = notes,
            Debug = debugMetrics
        };
    }

    private static VerificationDebugMetrics BuildDebugMetrics(
        string sessionId,
        int sessionSamplingIntervalMs,
        int toleranceMs,
        int? rotationDegrees,
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<FrameHashRecord> candidates,
        IReadOnlyList<ExtractedFrame> frames)
    {
        DebugElapsedMsRange? elapsedRange = null;
        if (frames.Count > 0)
        {
            var minElapsed = frames.Min(frame => frame.ElapsedMs);
            var maxElapsed = frames.Max(frame => frame.ElapsedMs);
            elapsedRange = new DebugElapsedMsRange
            {
                MinElapsedMs = minElapsed,
                MaxElapsedMs = maxElapsed
            };
        }

        var diagnostics = BuildDistanceDiagnostics(reference, candidates, toleranceMs, DefaultDistanceThreshold);
        var sampleStats = diagnostics.Stats.Take(DebugSampleCount).ToList();

        return new VerificationDebugMetrics
        {
            SessionId = sessionId,
            SessionSamplingIntervalMs = sessionSamplingIntervalMs,
            ToleranceMs = toleranceMs,
            Threshold = DefaultDistanceThreshold,
            ReferenceHashCount = reference.Count,
            ExtractedFrameCount = frames.Count,
            ExtractedElapsedMsRange = elapsedRange,
            RotationDegrees = rotationDegrees,
            CountNoCandidates = diagnostics.CountNoCandidates,
            CountTooDissimilar = diagnostics.CountTooDissimilar,
            BestDistanceHistogram = diagnostics.Histogram,
            MatcherWindowStats = sampleStats
        };
    }

    private readonly record struct DistanceDiagnostics(
        List<MatcherWindowStat> Stats,
        int CountNoCandidates,
        int CountTooDissimilar,
        BestDistanceHistogram Histogram);

    private static DistanceDiagnostics BuildDistanceDiagnostics(
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<FrameHashRecord> candidates,
        int toleranceMs,
        int threshold)
    {
        var stats = new List<MatcherWindowStat>(reference.Count);
        var histogram = new BestDistanceHistogram();
        var countNoCandidates = 0;
        var countTooDissimilar = 0;

        if (reference.Count == 0)
        {
            return new DistanceDiagnostics(stats, countNoCandidates, countTooDissimilar, histogram);
        }

        var orderedReference = reference.OrderBy(sample => sample.ElapsedMs).ToList();

        foreach (var sample in orderedReference)
        {
            var lowerBound = sample.ElapsedMs - toleranceMs;
            var upperBound = sample.ElapsedMs + toleranceMs;

            var candidateCount = 0;
            int? bestDist = null;
            int? secondBestDist = null;
            int? bestCandidateElapsedMs = null;

            foreach (var candidate in candidates)
            {
                var elapsed = candidate.ElapsedMs;
                if (elapsed < lowerBound || elapsed > upperBound)
                {
                    continue;
                }

                candidateCount += 1;
                var dist = HammingDistance.BetweenHex64(sample.HashHex, candidate.HashHex);
                if (!bestDist.HasValue || dist < bestDist.Value)
                {
                    secondBestDist = bestDist;
                    bestDist = dist;
                    bestCandidateElapsedMs = elapsed;
                }
                else if (!secondBestDist.HasValue || dist < secondBestDist.Value)
                {
                    secondBestDist = dist;
                }
            }

            if (candidateCount == 0)
            {
                countNoCandidates += 1;
            }
            else if (bestDist.HasValue && bestDist.Value > threshold)
            {
                countTooDissimilar += 1;
            }

            if (bestDist.HasValue)
            {
                AddHistogramBucket(histogram, bestDist.Value);
            }

            stats.Add(new MatcherWindowStat
            {
                RefElapsedMs = sample.ElapsedMs,
                CandidateCountInWindow = candidateCount,
                BestMinDistance = bestDist,
                SecondBestDistance = secondBestDist,
                BestCandidateElapsedMs = bestCandidateElapsedMs
            });
        }

        return new DistanceDiagnostics(stats, countNoCandidates, countTooDissimilar, histogram);
    }

    private static void AddHistogramBucket(BestDistanceHistogram histogram, int distance)
    {
        if (distance <= 5)
        {
            histogram.Bucket0To5 += 1;
        }
        else if (distance <= 10)
        {
            histogram.Bucket6To10 += 1;
        }
        else if (distance <= 20)
        {
            histogram.Bucket11To20 += 1;
        }
        else
        {
            histogram.Bucket21To64 += 1;
        }
    }

    private readonly record struct DeltaSweepResult(int BestDeltaMs, int BestMatched);

    private static DeltaSweepResult RunDeltaSweep(
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<FrameHashRecord> candidates,
        int toleranceMs,
        int threshold)
    {
        const int minDelta = -2000;
        const int maxDelta = 2000;
        const int step = 50;

        var bestDelta = 0;
        var bestMatched = -1;

        for (var delta = minDelta; delta <= maxDelta; delta += step)
        {
            var matched = CountMatchesWithDelta(reference, candidates, toleranceMs, threshold, delta);
            if (matched > bestMatched)
            {
                bestMatched = matched;
                bestDelta = delta;
            }
        }

        return new DeltaSweepResult(bestDelta, bestMatched);
    }

    private static int CountMatchesWithDelta(
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<FrameHashRecord> candidates,
        int toleranceMs,
        int threshold,
        int deltaMs)
    {
        var orderedReference = reference.OrderBy(r => r.ElapsedMs).ToList();
        var orderedCandidates = candidates.OrderBy(c => c.ElapsedMs).ToList();

        var matched = 0;
        var candidateStart = 0;

        foreach (var sample in orderedReference)
        {
            var lowerBound = sample.ElapsedMs - toleranceMs;
            var upperBound = sample.ElapsedMs + toleranceMs;

            while (candidateStart < orderedCandidates.Count &&
                   orderedCandidates[candidateStart].ElapsedMs + deltaMs < lowerBound)
            {
                candidateStart += 1;
            }

            int? minDist = null;
            var scanIndex = candidateStart;
            while (scanIndex < orderedCandidates.Count &&
                   orderedCandidates[scanIndex].ElapsedMs + deltaMs <= upperBound)
            {
                var dist = HammingDistance.BetweenHex64(sample.HashHex, orderedCandidates[scanIndex].HashHex);
                minDist = minDist.HasValue ? Math.Min(minDist.Value, dist) : dist;
                scanIndex += 1;
            }

            if (minDist.HasValue && minDist.Value <= threshold)
            {
                matched += 1;
            }
        }

        return matched;
    }

    private static async Task<byte[]> BufferVideoAsync(Stream videoStream, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await videoStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
