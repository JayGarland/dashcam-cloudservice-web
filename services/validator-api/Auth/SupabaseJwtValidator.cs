using System;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ValidatorApi.Auth;

public static class SupabaseJwtValidator
{
    public const string JwtSecretConfigKey = "Supabase:JwtSecret";
    public const string DefaultIssuer = "supabase";

    public static TokenValidationParameters BuildTokenValidationParameters(IConfiguration configuration)
    {
        var secret = configuration[JwtSecretConfigKey];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Supabase JwtSecret is required for validator authentication.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = DefaultIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role"
        };
    }
}
