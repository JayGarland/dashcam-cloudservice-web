using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

public class AuthTests : IClassFixture<AuthTests.ValidatorApiFactory>
{
    private readonly ValidatorApiFactory _factory;

    public AuthTests(ValidatorApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Verify_Returns401_WhenMissingToken()
    {
        using var client = _factory.CreateClient();
        using var request = BuildVerifyRequest();

        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Returns403_WhenRoleIsNotValidator()
    {
        using var client = _factory.CreateClient();
        using var request = BuildVerifyRequest();
        var token = CreateJwtToken("authenticated");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Verify_AllowsValidatorRole()
    {
        using var client = _factory.CreateClient();
        using var request = BuildVerifyRequest();
        var token = CreateJwtToken("validator");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static MultipartFormDataContent BuildVerifyRequest()
    {
        var metadata = new VerifyClaimMetadata
        {
            SessionId = "session-123",
            DeviceClockStartEpochMs = 1000,
            SamplingIntervalMs = 500,
            AlgoVersion = "dhash64_v1",
            ToleranceMs = 200
        };
        var metadataJson = JsonSerializer.Serialize(metadata);

        var content = new MultipartFormDataContent();
        var videoContent = new ByteArrayContent(new byte[] { 0x01, 0x02, 0x03 });
        videoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(videoContent, "Video", "clip.mp4");

        var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        content.Add(metadataContent, "Metadata", "metadata.json");

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

                services.AddSingleton<ISupabaseHashStore>(new FakeSupabaseHashStore());
                services.AddSingleton<IVideoFrameExtractor>(new FakeVideoFrameExtractor());
                services.AddScoped<VerificationService>();
            });
        }
    }

    private sealed class FakeSupabaseHashStore : ISupabaseHashStore
    {
        public Task<CaptureSession?> GetSessionAsync(string sessionId, CancellationToken ct)
        {
            return Task.FromResult<CaptureSession?>(new CaptureSession
            {
                SessionId = sessionId,
                DeviceClockStartEpochMs = 1000,
                SamplingIntervalMs = 500,
                AlgoVersion = "dhash64_v1",
                ClientVersion = "test"
            });
        }

        public Task<IReadOnlyList<FrameHashRecord>> GetFrameHashesAsync(string sessionId, CancellationToken ct)
        {
            var records = new List<FrameHashRecord>();
            for (var i = 0; i < 5; i += 1)
            {
                records.Add(new FrameHashRecord
                {
                    SessionId = sessionId,
                    SampleIndex = i,
                    ElapsedMs = i * 500,
                    SampleTimestampEpochMs = 1000 + (i * 500),
                    HashHex = "0000000000000000",
                    IntervalMs = 500,
                    AlgoVersion = "dhash64_v1",
                    CreatedAtEpochMs = 1000,
                    UploadState = "uploaded"
                });
            }
            return Task.FromResult<IReadOnlyList<FrameHashRecord>>(records);
        }
    }

    private sealed class FakeVideoFrameExtractor : IVideoFrameExtractor
    {
        public Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
            Stream videoStream,
            int intervalMs,
            CancellationToken ct)
        {
            var frames = new List<ExtractedFrame>();
            var rgba = new byte[9 * 8 * 4];
            for (var i = 0; i < 5; i += 1)
            {
                frames.Add(new ExtractedFrame(i * 500, rgba, 9, 8));
            }
            return Task.FromResult<IReadOnlyList<ExtractedFrame>>(frames);
        }
    }
}
