using Microsoft.AspNetCore.Http;

namespace ValidatorApi.Models;

public class VerifyClaimMetadata
{
    public string SessionId { get; set; } = string.Empty;
    public long DeviceClockStartEpochMs { get; set; }
    public int SamplingIntervalMs { get; set; }
    public string AlgoVersion { get; set; } = "dhash64_v1";
    public int? ToleranceMs { get; set; }
}

public class VerifyClaimRequest
{
    public IFormFile? Video { get; set; }
    public IFormFile? Metadata { get; set; }
}
