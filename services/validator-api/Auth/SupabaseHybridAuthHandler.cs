using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ValidatorApi.Auth;

public sealed class SupabaseHybridAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly SupabaseJwtValidator _validator;

    public SupabaseHybridAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        SupabaseJwtValidator validator)
        : base(options, logger, encoder, clock)
    {
        _validator = validator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return AuthenticateResult.NoResult();
        }

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.Fail("Missing bearer token.");
        }

        var principal = await _validator.ValidateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (principal == null)
        {
            return AuthenticateResult.Fail("Invalid token.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
