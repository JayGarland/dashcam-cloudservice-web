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
            var result = await _verificationService.VerifyAsync(videoStream, sessionId, metadata, ct);
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
}
