using System.Collections.Generic;
using System.Linq;
using ValidatorApi.Models;
using ValidatorApi.Services;
using Xunit;

namespace ValidatorApi.Tests;

public class HashMatcherTests
{
    private const string BaseHash = "0000000000000000";
    private const string AltHash = "ffffffffffffffff";
    private const long BaseEpochMs = 1700000000000;

    [Fact]
    public void Match_ExactAlignment_MatchesAll()
    {
        const int intervalMs = 500;
        const int toleranceMs = 200;
        var reference = BuildReference(6, intervalMs, BaseHash);
        var candidates = reference.Select(sample => BuildCandidate(sample, 0, BaseHash)).ToList();

        var matcher = new HashMatcher(threshold: 5, toleranceMs: toleranceMs);
        var (matched, _, _, missingSpans) = matcher.Match(reference, candidates, intervalMs);

        Assert.Equal(reference.Count, matched);
        Assert.Empty(missingSpans);
    }

    [Fact]
    public void Match_WithinTolerance_Matches()
    {
        const int intervalMs = 500;
        const int toleranceMs = 200;
        var reference = BuildReference(5, intervalMs, BaseHash);
        var candidates = reference.Select(sample => BuildCandidate(sample, 150, BaseHash)).ToList();

        var matcher = new HashMatcher(threshold: 5, toleranceMs: toleranceMs);
        var (matched, _, _, missingSpans) = matcher.Match(reference, candidates, intervalMs);

        Assert.Equal(reference.Count, matched);
        Assert.Empty(missingSpans);
    }

    [Fact]
    public void Match_OutsideTolerance_Misses()
    {
        const int intervalMs = 500;
        const int toleranceMs = 200;
        var reference = BuildReference(5, intervalMs, BaseHash);
        var candidates = reference.Select(sample => BuildCandidate(sample, 250, BaseHash)).ToList();

        var matcher = new HashMatcher(threshold: 5, toleranceMs: toleranceMs);
        var (matched, _, _, missingSpans) = matcher.Match(reference, candidates, intervalMs);

        Assert.Equal(0, matched);
        Assert.Empty(missingSpans);
    }

    [Fact]
    public void Match_MultipleCandidatesInWindow_ChoosesMinDistance()
    {
        const int intervalMs = 500;
        const int toleranceMs = 200;
        var reference = BuildReference(4, intervalMs, BaseHash);
        var candidates = new List<FrameHashRecord>();

        foreach (var sample in reference)
        {
            candidates.Add(BuildCandidate(sample, 50, AltHash));
            candidates.Add(BuildCandidate(sample, 150, BaseHash));
        }

        var matcher = new HashMatcher(threshold: 5, toleranceMs: toleranceMs);
        var (matched, avgDist, maxDist, missingSpans) = matcher.Match(reference, candidates, intervalMs);

        Assert.Equal(reference.Count, matched);
        Assert.Equal(0d, avgDist);
        Assert.Equal(0, maxDist);
        Assert.Empty(missingSpans);
    }

    [Fact]
    public void Match_ThresholdEnforced_FailsWhenDistanceTooHigh()
    {
        const int intervalMs = 500;
        const int toleranceMs = 200;
        var reference = BuildReference(3, intervalMs, BaseHash);
        var candidates = reference.Select(sample => BuildCandidate(sample, 100, AltHash)).ToList();

        var matcher = new HashMatcher(threshold: 5, toleranceMs: toleranceMs);
        var (matched, _, _, missingSpans) = matcher.Match(reference, candidates, intervalMs);

        Assert.Equal(0, matched);
        Assert.Empty(missingSpans);
    }

    [Fact]
    public void Match_ShouldProduceMissingSpanWhenRunExceedsThreshold()
    {
        const int intervalMs = 500;
        const int toleranceMs = 200;
        var reference = BuildReference(30, intervalMs, BaseHash);
        var candidates = new List<FrameHashRecord>();

        for (var i = 0; i < 5; i += 1)
        {
            candidates.Add(BuildCandidate(reference[i], 0, BaseHash));
        }

        for (var i = 25; i < 30; i += 1)
        {
            candidates.Add(BuildCandidate(reference[i], 0, BaseHash));
        }

        var matcher = new HashMatcher(threshold: 5, toleranceMs: toleranceMs);
        var (matched, _, _, missingSpans) = matcher.Match(reference, candidates, intervalMs);

        Assert.Equal(10, matched);
        var span = Assert.Single(missingSpans);
        Assert.Equal(5 * intervalMs, span.StartElapsedMs);
        Assert.Equal(24 * intervalMs, span.EndElapsedMs);
        Assert.Equal("No match within tolerance/threshold", span.Reason);
    }

    private static List<FrameHashRecord> BuildReference(int count, int intervalMs, string hashHex)
    {
        var records = new List<FrameHashRecord>(count);
        for (var i = 0; i < count; i += 1)
        {
            var elapsedMs = i * intervalMs;
            records.Add(new FrameHashRecord
            {
                SessionId = "session-1",
                SampleIndex = i,
                ElapsedMs = elapsedMs,
                SampleTimestampEpochMs = BaseEpochMs + elapsedMs,
                HashHex = hashHex,
                IntervalMs = intervalMs,
                AlgoVersion = "dhash64_v1",
                CreatedAtEpochMs = 0,
                UploadState = "complete"
            });
        }
        return records;
    }

    private static FrameHashRecord BuildCandidate(FrameHashRecord reference, int offsetMs, string hashHex)
    {
        return new FrameHashRecord
        {
            SessionId = reference.SessionId,
            SampleIndex = reference.SampleIndex,
            ElapsedMs = reference.ElapsedMs + offsetMs,
            SampleTimestampEpochMs = reference.SampleTimestampEpochMs + offsetMs,
            HashHex = hashHex,
            IntervalMs = reference.IntervalMs,
            AlgoVersion = reference.AlgoVersion,
            CreatedAtEpochMs = 0,
            UploadState = "pending"
        };
    }
}
