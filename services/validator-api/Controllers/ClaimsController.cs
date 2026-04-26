using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ValidatorApi.Models;
using ValidatorApi.Services;

namespace ValidatorApi.Controllers;

[ApiController]
[Route("api/claims")]
public class ClaimsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly VerificationService _verificationService;

    public ClaimsController(VerificationService verificationService)
    {
        _verificationService = verificationService;
    }

    [HttpPost("verify")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = "ValidatorOnly")]
    public async Task<ActionResult<VerificationResult>> Verify([FromForm] VerifyClaimRequest request, CancellationToken ct)
    {
        if (request is null || request.Video is null)
        {
            return BadRequest("Video file is required.");
        }

        var sessionId = request.SessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest("sessionId is required.");
        }

        VerifyClaimMetadata? metadata = null;
        if (request.Metadata is not null)
        {
            try
            {
                await using var metadataStream = request.Metadata.OpenReadStream();
                metadata = await JsonSerializer.DeserializeAsync<VerifyClaimMetadata>(metadataStream, JsonOptions, ct);
            }
            catch (JsonException)
            {
                return BadRequest("Invalid metadata JSON.");
            }

            if (metadata is null)
            {
                return BadRequest("Invalid metadata JSON.");
            }
        }

        try
        {
            await using var videoStream = request.Video.OpenReadStream();
            var debugEnabled = IsDebugEnabled(Request);
            var referenceSource = GetReferenceSourceOverride(Request);
            var result = await _verificationService.VerifyAsync(
                videoStream,
                sessionId,
                metadata,
                ct,
                debugEnabled,
                referenceSource);
            return Ok(result);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (SessionExpiredException)
        {
            return StatusCode(StatusCodes.Status410Gone);
        }
    }

    private static bool IsDebugEnabled(HttpRequest request)
    {
        if (request.Query.TryGetValue("debug", out var debugValue))
        {
            if (IsTruthy(debugValue.ToString()))
            {
                return true;
            }
        }

        if (request.Headers.TryGetValue("X-Debug", out var headerValue))
        {
            if (IsTruthy(headerValue.ToString()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetReferenceSourceOverride(HttpRequest request)
    {
        if (request.Query.TryGetValue("reference", out var referenceValue))
        {
            var value = referenceValue.ToString();
            if (string.Equals(value, "preview", StringComparison.OrdinalIgnoreCase))
            {
                return "preview";
            }
            if (string.Equals(value, "recorded", StringComparison.OrdinalIgnoreCase))
            {
                return "recorded";
            }
        }

        return null;
    }
}
