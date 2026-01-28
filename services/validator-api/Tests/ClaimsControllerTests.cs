using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ValidatorApi.Auth;
using ValidatorApi.Models;
using ValidatorApi.Services;
using Xunit;

namespace ValidatorApi.Tests;

public class ClaimsControllerTests
{
    [Fact]
    public async Task Verify_Returns400_WhenMissingSessionId()
    {
        using var factory = new ValidatorApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("validator"));

        using var request = new MultipartFormDataContent();
        var videoContent = new ByteArrayContent(new byte[] { 0x01, 0x02, 0x03 });
        videoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Add(videoContent, "video", "clip.mp4");

        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Returns404_WhenSessionNotFound()
    {
        using var factory = new ValidatorApiFactory
        {
            Store = { Session = null }
        };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("validator"));

        using var request = BuildVerifyRequest("session-missing", includeMetadata: false);
        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Succeeds_WhenMetadataMissing_AndSessionExists()
    {
        using var factory = new ValidatorApiFactory();
        var sessionId = "session-no-metadata";
        factory.Store.Session = new CaptureSession
        {
            SessionId = sessionId,
            DeviceClockStartEpochMs = 1000,
            SamplingIntervalMs = 750,
            AlgoVersion = "dhash64_v1"
        };

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("validator"));

        using var request = BuildVerifyRequest(sessionId, includeMetadata: false);
        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<VerificationResult>();
        Assert.NotNull(result);
        Assert.Equal(sessionId, result!.SessionId);
        Assert.Equal(750, result.IntervalMs);
        Assert.Equal(750, factory.Extractor.LastIntervalMs);
    }

    [Fact]
    public async Task Verify_Succeeds_WithMetadataProvided()
    {
        using var factory = new ValidatorApiFactory();
        var sessionId = "session-with-metadata";
        factory.Store.Session = new CaptureSession
        {
            SessionId = sessionId,
            DeviceClockStartEpochMs = 1000,
            SamplingIntervalMs = 750,
            AlgoVersion = "dhash64_v1"
        };

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("validator"));

        using var request = BuildVerifyRequest(sessionId, includeMetadata: true, metadataIntervalMs: 600);
        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<VerificationResult>();
        Assert.NotNull(result);
        Assert.Equal(sessionId, result!.SessionId);
        Assert.Equal(600, result.IntervalMs);
        Assert.Equal(600, factory.Extractor.LastIntervalMs);
    }

    private static MultipartFormDataContent BuildVerifyRequest(
        string sessionId,
        bool includeMetadata,
        int metadataIntervalMs = 500)
    {
        var content = new MultipartFormDataContent();
        var videoContent = new ByteArrayContent(new byte[] { 0x01, 0x02, 0x03 });
        videoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(videoContent, "video", "clip.mp4");

        var sessionIdContent = new StringContent(sessionId, Encoding.UTF8, "text/plain");
        content.Add(sessionIdContent, "sessionId");

        if (includeMetadata)
        {
            var metadata = new VerifyClaimMetadata
            {
                SessionId = sessionId,
                DeviceClockStartEpochMs = 1000,
                SamplingIntervalMs = metadataIntervalMs,
                AlgoVersion = "dhash64_v1",
                ToleranceMs = 200
            };
            var metadataJson = JsonSerializer.Serialize(metadata);
            var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
            content.Add(metadataContent, "metadata", "metadata.json");
        }

        return content;
    }

    private static string CreateJwtToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidatorApiFactory.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: SupabaseJwtValidator.DefaultIssuer,
            audience: null,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "user-123"),
                new Claim("role", role)
            },
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public class ValidatorApiFactory : WebApplicationFactory<Program>
    {
        public const string JwtSecret = "test-jwt-secret-please-change-1234567890";

        public TestSupabaseHashStore Store { get; } = new();
        public TestVideoFrameExtractor Extractor { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, configBuilder) =>
            {
                var config = new Dictionary<string, string?>
                {
                    ["Supabase:JwtSecret"] = JwtSecret,
                    ["Supabase:BaseUrl"] = "https://example.supabase.co",
                    ["Supabase:ServiceRoleKey"] = "service-role-key",
                    ["Ffmpeg:Path"] = "ffmpeg"
                };
                configBuilder.AddInMemoryCollection(config);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISupabaseHashStore>();
                services.RemoveAll<IVideoFrameExtractor>();

                services.AddSingleton<ISupabaseHashStore>(Store);
                services.AddSingleton<IVideoFrameExtractor>(Extractor);
                services.AddScoped<VerificationService>();
            });
        }
    }

    public sealed class TestSupabaseHashStore : ISupabaseHashStore
    {
        public CaptureSession? Session { get; set; } = new CaptureSession
        {
            SessionId = "session-default",
            DeviceClockStartEpochMs = 1000,
            SamplingIntervalMs = 500,
            AlgoVersion = "dhash64_v1"
        };

        public IReadOnlyList<FrameHashRecord> Hashes { get; set; } = new List<FrameHashRecord>();

        public Task<CaptureSession?> GetSessionAsync(string sessionId, CancellationToken ct)
        {
            return Task.FromResult(Session);
        }

        public Task<IReadOnlyList<FrameHashRecord>> GetFrameHashesAsync(string sessionId, CancellationToken ct)
        {
            return Task.FromResult(Hashes);
        }
    }

    public sealed class TestVideoFrameExtractor : IVideoFrameExtractor
    {
        public int? LastIntervalMs { get; private set; }
        public IReadOnlyList<ExtractedFrame>? Frames { get; set; }

        public Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
            Stream videoStream,
            int intervalMs,
            CancellationToken ct)
        {
            LastIntervalMs = intervalMs;
            if (Frames is not null)
            {
                return Task.FromResult(Frames);
            }

            var rgba = new byte[9 * 8 * 4];
            var frames = new List<ExtractedFrame>();
            for (var i = 0; i < 5; i += 1)
            {
                frames.Add(new ExtractedFrame(i * intervalMs, rgba, 9, 8));
            }
            return Task.FromResult<IReadOnlyList<ExtractedFrame>>(frames);
        }
    }
}
