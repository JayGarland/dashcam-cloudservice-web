using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ValidatorApi.Models;

namespace ValidatorApi.Services;

public class SupabaseHashStore : ISupabaseHashStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;

    public SupabaseHashStore(HttpClient httpClient, IOptions<SupabaseOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new ArgumentException("Supabase BaseUrl is required.", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(_options.ServiceRoleKey))
        {
            throw new ArgumentException("Supabase ServiceRoleKey is required.", nameof(options));
        }
    }

    public async Task<CaptureSession?> GetSessionAsync(string sessionId, CancellationToken ct)
    {
        var escapedSessionId = Uri.EscapeDataString(sessionId);
        var requestUri = BuildUri("capture_sessions", $"session_id=eq.{escapedSessionId}&select=*");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        AddHeaders(request);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await SupabaseRequestException.FromResponseAsync(response, ct).ConfigureAwait(false);
        }

        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var rows = JsonSerializer.Deserialize<List<CaptureSessionDto>>(payload, JsonOptions) ?? new List<CaptureSessionDto>();
        if (rows.Count == 0)
        {
            return null;
        }

        return MapSession(rows[0]);
    }

    public async Task<IReadOnlyList<FrameHashRecord>> GetFrameHashesAsync(string sessionId, CancellationToken ct)
    {
        var escapedSessionId = Uri.EscapeDataString(sessionId);
        var requestUri = BuildUri("frame_hashes", $"session_id=eq.{escapedSessionId}&select=*&order=elapsed_ms.asc");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        AddHeaders(request);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await SupabaseRequestException.FromResponseAsync(response, ct).ConfigureAwait(false);
        }

        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var rows = JsonSerializer.Deserialize<List<FrameHashRecordDto>>(payload, JsonOptions) ?? new List<FrameHashRecordDto>();
        var results = new List<FrameHashRecord>(rows.Count);
        foreach (var row in rows)
        {
            results.Add(MapHash(row));
        }
        return results;
    }

    private string BuildUri(string table, string query)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/rest/v1/{table}?{query}";
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("apikey", _options.ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_options.Schema) && _options.Schema != "public")
        {
            request.Headers.Add("Accept-Profile", _options.Schema);
        }
    }

    private static CaptureSession MapSession(CaptureSessionDto dto)
    {
        return new CaptureSession
        {
            SessionId = dto.SessionId ?? string.Empty,
            DeviceClockStartEpochMs = dto.DeviceClockStartEpochMs,
            SamplingIntervalMs = dto.SamplingIntervalMs,
            AlgoVersion = dto.AlgoVersion ?? "dhash64_v1",
            ClientVersion = dto.ClientVersion
        };
    }

    private static FrameHashRecord MapHash(FrameHashRecordDto dto)
    {
        return new FrameHashRecord
        {
            SessionId = dto.SessionId ?? string.Empty,
            SampleIndex = dto.SampleIndex,
            ElapsedMs = dto.ElapsedMs,
            SampleTimestampEpochMs = dto.SampleTimestampEpochMs,
            HashHex = dto.HashHex ?? string.Empty,
            IntervalMs = dto.IntervalMs,
            AlgoVersion = dto.AlgoVersion ?? "dhash64_v1",
            CreatedAtEpochMs = dto.CreatedAtEpochMs,
            UploadState = dto.UploadState ?? "pending"
        };
    }

    private sealed class CaptureSessionDto
    {
        [JsonPropertyName("session_id")]
        public string? SessionId { get; set; }

        [JsonPropertyName("device_clock_start_epoch_ms")]
        public long DeviceClockStartEpochMs { get; set; }

        [JsonPropertyName("sampling_interval_ms")]
        public int SamplingIntervalMs { get; set; }

        [JsonPropertyName("algo_version")]
        public string? AlgoVersion { get; set; }

        [JsonPropertyName("client_version")]
        public string? ClientVersion { get; set; }
    }

    private sealed class FrameHashRecordDto
    {
        [JsonPropertyName("session_id")]
        public string? SessionId { get; set; }

        [JsonPropertyName("sample_index")]
        public int SampleIndex { get; set; }

        [JsonPropertyName("elapsed_ms")]
        public int ElapsedMs { get; set; }

        [JsonPropertyName("sample_timestamp_epoch_ms")]
        public long SampleTimestampEpochMs { get; set; }

        [JsonPropertyName("hash_hex")]
        public string? HashHex { get; set; }

        [JsonPropertyName("interval_ms")]
        public int IntervalMs { get; set; }

        [JsonPropertyName("algo_version")]
        public string? AlgoVersion { get; set; }

        [JsonPropertyName("created_at_epoch_ms")]
        public long CreatedAtEpochMs { get; set; }

        [JsonPropertyName("upload_state")]
        public string? UploadState { get; set; }
    }
}
