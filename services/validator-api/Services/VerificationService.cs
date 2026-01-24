using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValidatorApi.Models;

namespace ValidatorApi.Services;

public class VerificationService
{
    public const int DefaultIntervalMs = 500;
    public const int DefaultToleranceMs = 200;
    public const int DefaultDistanceThreshold = 5;
    public const double VerifiedMatchRatio = 0.90;
    public const double SuspiciousMatchRatio = 0.80;

    private readonly ISupabaseHashStore _store;
    private readonly IVideoFrameExtractor _extractor;

    public VerificationService(ISupabaseHashStore store, IVideoFrameExtractor extractor)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
    }

    public async Task<VerificationResult> VerifyAsync(
        Stream videoStream,
        VerifyClaimMetadata metadata,
        CancellationToken ct)
    {
        if (videoStream is null)
        {
            throw new ArgumentNullException(nameof(videoStream));
        }
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var sessionId = metadata.SessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ValidationException("sessionId is required.");
        }
        if (metadata.SamplingIntervalMs <= 0)
        {
            throw new ValidationException("samplingIntervalMs must be positive.");
        }
        if (!string.Equals(metadata.AlgoVersion, "dhash64_v1", StringComparison.Ordinal))
        {
            throw new ValidationException("algoVersion must be dhash64_v1.");
        }

        var intervalMs = metadata.SamplingIntervalMs > 0 ? metadata.SamplingIntervalMs : DefaultIntervalMs;
        var toleranceMs = metadata.ToleranceMs ?? DefaultToleranceMs;
        if (toleranceMs <= 0)
        {
            throw new ValidationException("toleranceMs must be positive.");
        }

        CaptureSession? session;
        try
        {
            session = await _store.GetSessionAsync(sessionId, ct);
        }
        catch (SessionExpiredException)
        {
            throw;
        }

        if (session is null)
        {
            throw new NotFoundException($"Session '{sessionId}' not found.");
        }

        var reference = await _store.GetFrameHashesAsync(sessionId, ct) ?? Array.Empty<FrameHashRecord>();
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
            var sampleTimestampEpochMs = metadata.DeviceClockStartEpochMs + elapsedMs;

            candidates.Add(new FrameHashRecord
            {
                SessionId = sessionId,
                SampleIndex = i,
                ElapsedMs = elapsedMs,
                SampleTimestampEpochMs = sampleTimestampEpochMs,
                HashHex = hashHex,
                IntervalMs = intervalMs,
                AlgoVersion = metadata.AlgoVersion,
                CreatedAtEpochMs = 0,
                UploadState = "pending"
            });
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
            SessionId = sessionId,
            Threshold = DefaultDistanceThreshold,
            ToleranceMs = toleranceMs,
            IntervalMs = intervalMs,
            ExpectedSamples = expectedSamples,
            MatchedSamples = matched,
            MatchRatio = matchRatio,
            AvgDistance = avgDist,
            MaxDistance = maxDist,
            MissingSpans = missingSpans,
            Notes = notes
        };
    }
}
