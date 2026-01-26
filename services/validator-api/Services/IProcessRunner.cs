using System.Threading;
using System.Threading.Tasks;

namespace ValidatorApi.Services;

public sealed record ProcessRunRequest(string FileName, string Arguments, string? WorkingDirectory = null);

public sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct);
}
