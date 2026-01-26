using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ValidatorApi.Services;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public class SessionExpiredException : Exception
{
    public SessionExpiredException(string message) : base(message)
    {
    }
}

public class FfmpegException : Exception
{
    public FfmpegException(string message, int exitCode, string stderr)
        : base(message)
    {
        ExitCode = exitCode;
        Stderr = stderr ?? string.Empty;
    }

    public int ExitCode { get; }

    public string Stderr { get; }
}

public class SupabaseRequestException : Exception
{
    public SupabaseRequestException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static async Task<SupabaseRequestException> FromResponseAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var payload = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var message = $"Supabase request failed with {(int)response.StatusCode} {response.ReasonPhrase}.";
        if (!string.IsNullOrWhiteSpace(payload))
        {
            message = $"{message} Payload: {payload}";
        }
        return new SupabaseRequestException(message, (int)response.StatusCode);
    }
}
