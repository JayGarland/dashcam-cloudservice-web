using System;
using System.Collections.Generic;
using System.Linq;
using ValidatorApi.Models;

namespace ValidatorApi.Services;

public class HashMatcher
{
    private const int MissingSpanFlagMs = 5000;
    private readonly int _threshold;
    private readonly int _toleranceMs;

    public HashMatcher(int threshold = 5, int toleranceMs = 200)
    {
        if (threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }
        if (toleranceMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceMs));
        }

        _threshold = threshold;
        _toleranceMs = toleranceMs;
    }

    public (int matched, double avgDist, int maxDist, List<MissingSpan> missingSpans) Match(
        IReadOnlyList<FrameHashRecord> reference,
        IReadOnlyList<FrameHashRecord> candidates,
        int intervalMs)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }
        if (intervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs));
        }

        var orderedReference = reference.OrderBy(r => r.ElapsedMs).ToList();
        var orderedCandidates = candidates.OrderBy(c => c.ElapsedMs).ToList();

        var matched = 0;
        var maxDist = 0;
        long totalDist = 0;
        var missingSpans = new List<MissingSpan>();

        var candidateStart = 0;
        var runStart = -1;
        var runEnd = -1;
        var runActive = false;

        foreach (var sample in orderedReference)
        {
            var lowerBound = sample.ElapsedMs - _toleranceMs;
            var upperBound = sample.ElapsedMs + _toleranceMs;

                 while (candidateStart < orderedCandidates.Count &&
                     orderedCandidates[candidateStart].ElapsedMs < lowerBound)
            {
                candidateStart += 1;
            }

            int? minDist = null;
            var scanIndex = candidateStart;
                 while (scanIndex < orderedCandidates.Count &&
                     orderedCandidates[scanIndex].ElapsedMs <= upperBound)
            {
                var dist = HammingDistance.BetweenHex64(sample.HashHex, orderedCandidates[scanIndex].HashHex);
                minDist = minDist.HasValue ? Math.Min(minDist.Value, dist) : dist;
                scanIndex += 1;
            }

            var isMatched = minDist.HasValue && minDist.Value <= _threshold;
            if (isMatched)
            {
                matched += 1;
                totalDist += minDist!.Value;
                if (minDist.Value > maxDist)
                {
                    maxDist = minDist.Value;
                }

                if (runActive)
                {
                    CloseRun(intervalMs, ref runStart, ref runEnd, ref runActive, missingSpans);
                }
            }
            else
            {
                if (!runActive)
                {
                    runStart = sample.ElapsedMs;
                    runActive = true;
                }
                runEnd = sample.ElapsedMs;
            }
        }

        if (runActive)
        {
            CloseRun(intervalMs, ref runStart, ref runEnd, ref runActive, missingSpans);
        }

        var avgDist = matched > 0 ? totalDist / (double)matched : 0d;
        return (matched, avgDist, maxDist, missingSpans);
    }

    private static void CloseRun(
        int intervalMs,
        ref int runStart,
        ref int runEnd,
        ref bool runActive,
        ICollection<MissingSpan> spans)
    {
        var durationMs = runEnd - runStart + intervalMs;
        if (durationMs >= MissingSpanFlagMs)
        {
            spans.Add(new MissingSpan
            {
                StartElapsedMs = runStart,
                EndElapsedMs = runEnd,
                Reason = "No match within tolerance/threshold"
            });
        }

        runStart = -1;
        runEnd = -1;
        runActive = false;
    }
}
