using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ValidatorApi.Models;
using ValidatorApi.Services;

namespace ValidatorApi.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ISupabaseHashStore _store;
    private readonly IVideoFrameExtractor _extractor;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISupabaseHashStore store,
        IVideoFrameExtractor extractor,
        ILogger<SessionsController> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("{sessionId}/recorded-hashes")]
    [Consumes("multipart/form-data")]
    [Authorize]
    public async Task<ActionResult<RecordedHashesResponse>> UploadRecordedHashes(
        string sessionId,
        [FromForm] RecordedHashesRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest("sessionId is required.");
        }
        if (request?.Video is null)
        {
            return BadRequest("Video file is required.");
        }

        var session = await _store.GetSessionAsync(sessionId, ct);
        if (session is null)
        {
            return NotFound();
        }

        var intervalMs = request.IntervalMs > 0 ? request.IntervalMs : session.SamplingIntervalMs;
        if (intervalMs <= 0)
        {
            return BadRequest("intervalMs must be positive.");
        }

        _logger.LogInformation("Recorded hash request for {SessionId}: intervalMs={IntervalMs}", sessionId, intervalMs);

        IReadOnlyList<ExtractedFrame> frames;
        await using (var videoStream = request.Video.OpenReadStream())
        {
            frames = await _extractor.ExtractFramesAsync(videoStream, intervalMs, ct);
        }

        var records = new List<FrameHashRecord>(frames.Count);
        for (var i = 0; i < frames.Count; i += 1)
        {
            var frame = frames[i];
            var hashValue = DHash64.FromRgba(frame.Rgba, frame.Width, frame.Height);
            var hashHex = DHash64.ToHex(hashValue);
            var elapsedMs = frame.ElapsedMs;

            records.Add(new FrameHashRecord
            {
                SessionId = session.SessionId,
                SampleIndex = i,
                ElapsedMs = elapsedMs,
                SampleTimestampEpochMs = session.DeviceClockStartEpochMs + elapsedMs,
                HashHex = hashHex,
                IntervalMs = intervalMs,
                AlgoVersion = session.AlgoVersion,
                CreatedAtEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UploadState = "uploaded",
                Source = "recorded"
            });
        }

        var first5Elapsed = records.Take(5).Select(record => record.ElapsedMs).ToList();
        _logger.LogInformation(
            "Recorded hashes elapsedMs first5 for {SessionId}: {Elapsed}",
            sessionId,
            first5Elapsed);

        if (records.Count > 0)
        {
            await _store.InsertFrameHashesAsync(records, ct);
            await _store.SetReferenceSourceAsync(sessionId, "recorded", ct);
        }

        _logger.LogInformation(
            "Recorded hashes uploaded for {SessionId}: {Count} samples",
            sessionId,
            records.Count);

        return Ok(new RecordedHashesResponse
        {
            SessionId = sessionId,
            StoredCount = records.Count
        });
    }

    public class RecordedHashesRequest
    {
        [Required]
        public IFormFile? Video { get; set; }

        public int IntervalMs { get; set; }
    }

    public class RecordedHashesResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public int StoredCount { get; set; }
    }
}
