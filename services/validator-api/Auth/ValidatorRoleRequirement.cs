using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace ValidatorApi.Auth;

public sealed class ValidatorRoleRequirement : IAuthorizationRequirement
{
    public const string RoleName = "validator";
}

public sealed class ValidatorRoleHandler : AuthorizationHandler<ValidatorRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidatorRoleRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (HasValidatorRole(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool HasValidatorRole(ClaimsPrincipal user)
    {
        if (user.IsInRole(ValidatorRoleRequirement.RoleName))
        {
            return true;
        }

        var roleClaim = user.Claims.FirstOrDefault(claim =>
            claim.Type == "role" || claim.Type == ClaimTypes.Role);
        if (roleClaim != null && string.Equals(roleClaim.Value, ValidatorRoleRequirement.RoleName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rolesClaim = user.Claims.FirstOrDefault(claim => claim.Type == "roles");
        if (rolesClaim != null && ClaimContainsRole(rolesClaim.Value, ValidatorRoleRequirement.RoleName))
        {
            return true;
        }

        var appMetadataClaim = user.Claims.FirstOrDefault(claim => claim.Type == "app_metadata");
        if (appMetadataClaim != null && AppMetadataContainsRole(appMetadataClaim.Value, ValidatorRoleRequirement.RoleName))
        {
            return true;
        }

        return false;
    }

    private static bool ClaimContainsRole(string value, string role)
    {
        if (string.Equals(value, role, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!value.Contains('[', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String &&
                    string.Equals(entry.GetString(), role, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool AppMetadataContainsRole(string json, string role)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (doc.RootElement.TryGetProperty("role", out var roleProperty))
            {
                if (roleProperty.ValueKind == JsonValueKind.String &&
                    string.Equals(roleProperty.GetString(), role, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (doc.RootElement.TryGetProperty("roles", out var rolesProperty))
            {
                if (rolesProperty.ValueKind == JsonValueKind.String &&
                    string.Equals(rolesProperty.GetString(), role, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (rolesProperty.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in rolesProperty.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.String &&
                            string.Equals(entry.GetString(), role, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
