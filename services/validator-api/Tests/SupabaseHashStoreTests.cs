using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ValidatorApi.Models;
using ValidatorApi.Services;
using Xunit;

namespace ValidatorApi.Tests;

public class SupabaseHashStoreTests
{
    private const string BaseUrl = "https://example.supabase.co";
    private const string ServiceRoleKey = "service-role-key";

    [Fact]
    public async Task GetSessionAsync_ReturnsNull_OnEmptyArray()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        var store = BuildStore(handler);

        var result = await store.GetSessionAsync("session-123", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsMappedCaptureSession()
    {
        var json = "[" +
                   "{\"session_id\":\"session-123\"," +
                   "\"device_clock_start_epoch_ms\":1700000000000," +
                   "\"sampling_interval_ms\":500," +
                   "\"algo_version\":\"dhash64_v1\"," +
                   "\"client_version\":\"1.2.3\"}" +
                   "]";
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        var store = BuildStore(handler);

        var result = await store.GetSessionAsync("session-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("session-123", result!.SessionId);
        Assert.Equal(1700000000000, result.DeviceClockStartEpochMs);
        Assert.Equal(500, result.SamplingIntervalMs);
        Assert.Equal("dhash64_v1", result.AlgoVersion);
        Assert.Equal("1.2.3", result.ClientVersion);
    }

    [Fact]
    public async Task GetSessionAsync_UsesExpectedRequestUrl_AndHeaders()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        var store = BuildStore(handler);

        await store.GetSessionAsync("session-123", CancellationToken.None);

        var request = handler.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request!.Method);
        Assert.Equal($"{BaseUrl}/rest/v1/capture_sessions?session_id=eq.session-123&select=*", request.RequestUri!.ToString());
        AssertHeader(request.Headers, "apikey", ServiceRoleKey);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(ServiceRoleKey, request.Headers.Authorization!.Parameter);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/json");
    }

    [Fact]
    public async Task GetFrameHashesAsync_UsesExpectedRequestUrl_AndMapsRecords()
    {
        var json = "[" +
                   "{\"session_id\":\"session-123\"," +
                   "\"sample_index\":0," +
                   "\"elapsed_ms\":0," +
                   "\"sample_timestamp_epoch_ms\":1700000000000," +
                   "\"hash_hex\":\"abc\"," +
                   "\"interval_ms\":500," +
                   "\"algo_version\":\"dhash64_v1\"," +
                   "\"created_at_epoch_ms\":1700000000100," +
                   "\"upload_state\":\"uploaded\"}," +
                   "{\"session_id\":\"session-123\"," +
                   "\"sample_index\":1," +
                   "\"elapsed_ms\":500," +
                   "\"sample_timestamp_epoch_ms\":1700000000500," +
                   "\"hash_hex\":\"def\"," +
                   "\"interval_ms\":500," +
                   "\"algo_version\":\"dhash64_v1\"," +
                   "\"created_at_epoch_ms\":1700000000600," +
                   "\"upload_state\":\"uploaded\"}" +
                   "]";
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        var store = BuildStore(handler);

        var result = await store.GetFrameHashesAsync("session-123", CancellationToken.None);

        var request = handler.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request!.Method);
        Assert.Equal($"{BaseUrl}/rest/v1/frame_hashes?session_id=eq.session-123&select=*&order=elapsed_ms.asc", request.RequestUri!.ToString());
        Assert.Equal(2, result.Count);
        Assert.Equal("abc", result[0].HashHex);
        Assert.Equal(0, result[0].ElapsedMs);
        Assert.Equal(1, result[1].SampleIndex);
        Assert.Equal(500, result[1].ElapsedMs);
    }

    [Fact]
    public async Task GetFrameHashesAsync_ThrowsOnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"message\":\"boom\"}", Encoding.UTF8, "application/json")
            });
        var store = BuildStore(handler);

        await Assert.ThrowsAsync<SupabaseRequestException>(() =>
            store.GetFrameHashesAsync("session-123", CancellationToken.None));
    }

    private static SupabaseHashStore BuildStore(FakeHttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var options = Options.Create(new SupabaseOptions
        {
            BaseUrl = BaseUrl,
            ServiceRoleKey = ServiceRoleKey,
            Schema = "public"
        });
        return new SupabaseHashStore(client, options);
    }

    private static void AssertHeader(HttpRequestHeaders headers, string name, string expected)
    {
        Assert.True(headers.TryGetValues(name, out var values));
        Assert.Contains(expected, values!);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }
}
