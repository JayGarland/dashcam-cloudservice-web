using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ValidatorApi.Models;

public enum Verdict
{
    Verified,
    Suspicious,
    Inconclusive
}

public class MissingSpan
{
    public int StartElapsedMs { get; set; }
    public int EndElapsedMs { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class VerificationResult
{
    public Verdict Verdict { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public int Threshold { get; set; }
    public int ToleranceMs { get; set; }
    public int IntervalMs { get; set; }
    public int ExpectedSamples { get; set; }
    public int MatchedSamples { get; set; }
    public double MatchRatio { get; set; }
    public double AvgDistance { get; set; }
    public int MaxDistance { get; set; }
    public List<MissingSpan> MissingSpans { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VerificationDebugMetrics? Debug { get; set; }
}

public class VerificationDebugMetrics
{
    public string SessionId { get; set; } = string.Empty;
    public int SessionSamplingIntervalMs { get; set; }
    public int ToleranceMs { get; set; }
    public int Threshold { get; set; }
    public int ReferenceHashCount { get; set; }
    public int ExtractedFrameCount { get; set; }
    public DebugElapsedMsRange? ExtractedElapsedMsRange { get; set; }
    public List<MatcherWindowStat> MatcherWindowStats { get; set; } = new();
}

public class DebugElapsedMsRange
{
    public int MinElapsedMs { get; set; }
    public int MaxElapsedMs { get; set; }
}

public class MatcherWindowStat
{
    public int RefElapsedMs { get; set; }
    public int CandidateCountInWindow { get; set; }
    public int? BestMinDistance { get; set; }
}
