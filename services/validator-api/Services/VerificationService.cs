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

        var frames = await _extractor.ExtractFramesAsync(videoStream, intervalMs, ct) ?? Array.Empty<ExtractedFrame>();
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
                orderedReference,
                candidates,
                frameList);
            _logger.LogInformation("Verification debug metrics for {SessionId}: {@Metrics}", normalizedSessionId, debugMetrics);
        }

        var matcher = new HashMatcher(DefaultDistanceThreshold, toleranceMs);
        var (matched, avgDist, maxDist, missingSpans) = matcher.Match(orderedReference, candidates, intervalMs);

        var expectedSamples = orderedReference.Count;
        var matchRatio = expectedSamples > 0 ? matched / (double)expectedSamples : 0d;

        var notes = new List<string>();
        if (expectedSamples == 0)
        {
            notes.Add("No reference hashes found");
        }
        if (tooFewFrames)
        {
            notes.Add("Too few frames extracted");
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

        return new VerificationDebugMetrics
        {
            SessionId = sessionId,
            SessionSamplingIntervalMs = sessionSamplingIntervalMs,
            ToleranceMs = toleranceMs,
            Threshold = DefaultDistanceThreshold,
            ReferenceHashCount = reference.Count,
            ExtractedFrameCount = frames.Count,
            ExtractedElapsedMsRange = elapsedRange,
            MatcherWindowStats = BuildMatcherWindowStats(reference, candidates, toleranceMs, DebugSampleCount)
        };
    }

    private static List<MatcherWindowStat> BuildMatcherWindowStats(
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<FrameHashRecord> candidates,
        int toleranceMs,
        int sampleCount)
    {
        var stats = new List<MatcherWindowStat>();
        if (reference.Count == 0 || sampleCount <= 0)
        {
            return stats;
        }

        var orderedReference = reference.OrderBy(sample => sample.ElapsedMs).ToList();
        var maxSamples = Math.Min(sampleCount, orderedReference.Count);

        for (var i = 0; i < maxSamples; i += 1)
        {
            var sample = orderedReference[i];
            var lowerBound = sample.SampleTimestampEpochMs - toleranceMs;
            var upperBound = sample.SampleTimestampEpochMs + toleranceMs;

            var candidateCount = 0;
            int? minDist = null;

            foreach (var candidate in candidates)
            {
                var timestamp = candidate.SampleTimestampEpochMs;
                if (timestamp < lowerBound || timestamp > upperBound)
                {
                    continue;
                }

                candidateCount += 1;
                var dist = HammingDistance.BetweenHex64(sample.HashHex, candidate.HashHex);
                minDist = minDist.HasValue ? Math.Min(minDist.Value, dist) : dist;
            }

            stats.Add(new MatcherWindowStat
            {
                RefElapsedMs = sample.ElapsedMs,
                CandidateCountInWindow = candidateCount,
                BestMinDistance = minDist
            });
        }

        return stats;
    }
}
