using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ValidatorApi.Auth;

public sealed class SupabaseJwtValidator
{
    public const string BaseUrlConfigKey = "Supabase:BaseUrl";
    public const string PublishableKeyConfigKey = "Supabase:PublishableKey";
    public const string AnonKeyConfigKey = "Supabase:AnonKey";
    public const string DisableIssuerValidationKey = "Supabase:DisableIssuerValidation";
    public const string DefaultIssuer = "supabase";

    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public SupabaseJwtValidator(IConfiguration configuration, HttpClient httpClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string jwt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        JwtSecurityToken token;
        try
        {
            token = _tokenHandler.ReadJwtToken(jwt);
        }
        catch
        {
            return null;
        }

        var kid = token.Header.Kid;
        var alg = token.Header.Alg;

        if (!string.IsNullOrWhiteSpace(kid) && IsAsymmetricAlg(alg))
        {
            var signingKey = await GetSigningKeyAsync(kid, alg, ct).ConfigureAwait(false);
            if (signingKey != null)
            {
                var parameters = BuildTokenValidationParameters(signingKey, alg);
                try
                {
                    var principal = _tokenHandler.ValidateToken(jwt, parameters, out _);
                    return principal;
                }
                catch (SecurityTokenException)
                {
                    return null;
                }
            }
        }

        return await ValidateViaUserEndpointAsync(jwt, ct).ConfigureAwait(false);
    }

    private static bool IsAsymmetricAlg(string? alg)
    {
        if (string.IsNullOrWhiteSpace(alg))
        {
            return false;
        }

        return alg.StartsWith("ES", StringComparison.OrdinalIgnoreCase)
            || alg.StartsWith("RS", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SecurityKey?> GetSigningKeyAsync(string kid, string? alg, CancellationToken ct)
    {
        var jwks = await FetchJwksAsync(ct).ConfigureAwait(false);
        if (jwks == null || jwks.Keys == null || jwks.Keys.Count == 0)
        {
            return null;
        }

        foreach (var key in jwks.Keys)
        {
            if (!string.Equals(key.Kid, kid, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(alg) &&
                !string.IsNullOrWhiteSpace(key.Alg) &&
                !string.Equals(key.Alg, alg, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return key;
        }

        return null;
    }

    private async Task<JsonWebKeySet?> FetchJwksAsync(CancellationToken ct)
    {
        var baseUrl = GetNormalizedBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var jwksUrl = $"{baseUrl}/auth/v1/.well-known/jwks.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, jwksUrl);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return new JsonWebKeySet(json);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task<ClaimsPrincipal?> ValidateViaUserEndpointAsync(string jwt, CancellationToken ct)
    {
        var baseUrl = GetNormalizedBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var apiKey = GetPublishableKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/auth/v1/user");
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return BuildPrincipalFromUser(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ClaimsPrincipal? BuildPrincipalFromUser(JsonElement root)
    {
        if (!TryGetString(root, "id", out var userId))
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(JwtRegisteredClaimNames.Sub, userId)
        };

        if (TryGetString(root, "email", out var email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        if (root.TryGetProperty("app_metadata", out var appMetadata) &&
            appMetadata.ValueKind == JsonValueKind.Object)
        {
            claims.Add(new Claim("app_metadata", appMetadata.GetRawText()));

            if (TryGetRoleFromMetadata(appMetadata, out var role))
            {
                claims.Add(new Claim("role", role));
            }
        }

        if (!claims.Any(c => c.Type == "role") &&
            root.TryGetProperty("user_metadata", out var userMetadata) &&
            userMetadata.ValueKind == JsonValueKind.Object &&
            TryGetRoleFromMetadata(userMetadata, out var userRole))
        {
            claims.Add(new Claim("role", userRole));
        }

        var identity = new ClaimsIdentity(claims, "SupabaseHybrid", ClaimTypes.NameIdentifier, "role");
        return new ClaimsPrincipal(identity);
    }

    private static bool TryGetRoleFromMetadata(JsonElement metadata, out string role)
    {
        role = string.Empty;
        return TryGetString(metadata, "role", out role);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            var result = property.GetString();
            if (!string.IsNullOrWhiteSpace(result))
            {
                value = result;
                return true;
            }
        }

        return false;
    }

    private TokenValidationParameters BuildTokenValidationParameters(SecurityKey signingKey, string? algorithm)
    {
        var validIssuers = BuildValidIssuers();
        var validateIssuer = ShouldValidateIssuer();

        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = validateIssuer,
            ValidIssuers = validIssuers,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role",
            RequireSignedTokens = true,
            ValidAlgorithms = string.IsNullOrWhiteSpace(algorithm) ? null : new[] { algorithm }
        };
    }

    private string[] BuildValidIssuers()
    {
        var baseUrl = GetNormalizedBaseUrl();
        var issuers = new List<string>();

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            issuers.Add($"{baseUrl}/auth/v1");
            issuers.Add(baseUrl);
        }

        issuers.Add(DefaultIssuer);

        return issuers.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToArray();
    }

    private bool ShouldValidateIssuer()
    {
        var disableIssuerValidation = false;
        if (bool.TryParse(_configuration[DisableIssuerValidationKey], out var disableFlag))
        {
            disableIssuerValidation = disableFlag;
        }

        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        return !(disableIssuerValidation && isDevelopment);
    }

    private string? GetNormalizedBaseUrl()
    {
        var baseUrl = _configuration[BaseUrlConfigKey];
        return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
    }

    private string? GetPublishableKey()
    {
        var publishable = _configuration[PublishableKeyConfigKey];
        if (!string.IsNullOrWhiteSpace(publishable))
        {
            return publishable;
        }

        var anon = _configuration[AnonKeyConfigKey];
        return string.IsNullOrWhiteSpace(anon) ? null : anon;
    }
}
