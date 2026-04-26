using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
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

public class AuthTests
{
    private const string TestBaseUrl = "https://example.supabase.co";
    private const string TestPublishableKey = "test-publishable-key";

    [Fact]
    public async Task ValidateAsync_NoKid_UsesUserEndpoint()
    {
        var handler = new TestSupabaseHandler
        {
            UserResponder = _ => BuildJsonResponse(HttpStatusCode.OK, BuildUserResponseJson("validator"))
        };
        using var httpClient = new HttpClient(handler);
        var validator = new SupabaseJwtValidator(BuildConfiguration(), httpClient);
        var token = CreateUnsignedJwt();

        var principal = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal("user-123", principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.True(principal.IsInRole(ValidatorRoleRequirement.RoleName));
        Assert.Equal(1, handler.UserCalls);
        Assert.Equal(0, handler.JwksCalls);
    }

    [Fact]
    public async Task ValidateAsync_NoKid_FailsWhenUserUnauthorized()
    {
        var handler = new TestSupabaseHandler
        {
            UserResponder = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        };
        using var httpClient = new HttpClient(handler);
        var validator = new SupabaseJwtValidator(BuildConfiguration(), httpClient);
        var token = CreateUnsignedJwt();

        var principal = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.Null(principal);
        Assert.Equal(1, handler.UserCalls);
    }

    [Fact]
    public async Task ValidateAsync_WithKid_UsesJwksAndSkipsUserEndpoint()
    {
        var jwt = CreateSignedEs256Jwt($"{TestBaseUrl}/auth/v1", "k1", out var jwk);
        var jwksJson = BuildJwksJson(jwk);
        var handler = new TestSupabaseHandler
        {
            JwksResponder = _ => BuildJsonResponse(HttpStatusCode.OK, jwksJson),
            UserResponder = _ => BuildJsonResponse(HttpStatusCode.OK, BuildUserResponseJson("validator"))
        };
        using var httpClient = new HttpClient(handler);
        var validator = new SupabaseJwtValidator(BuildConfiguration(), httpClient);

        var principal = await validator.ValidateAsync(jwt, CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal(1, handler.JwksCalls);
        Assert.Equal(0, handler.UserCalls);
    }

    [Fact]
    public async Task ValidateAsync_WithKid_FallsBackToUserWhenJwksEmpty()
    {
        var token = CreateUnsignedJwt("k1", "ES256");
        var handler = new TestSupabaseHandler
        {
            JwksResponder = _ => BuildJsonResponse(HttpStatusCode.OK, "{\"keys\":[]}"),
            UserResponder = _ => BuildJsonResponse(HttpStatusCode.OK, BuildUserResponseJson("validator"))
        };
        using var httpClient = new HttpClient(handler);
        var validator = new SupabaseJwtValidator(BuildConfiguration(), httpClient);

        var principal = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal(1, handler.JwksCalls);
        Assert.Equal(1, handler.UserCalls);
    }

    [Fact]
    public async Task Verify_Returns401_WhenMissingToken()
    {
        using var factory = new ValidatorApiFactory();
        using var client = factory.CreateClient();
        using var request = BuildVerifyRequest();

        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Returns403_WhenRoleIsNotValidator()
    {
        using var factory = new ValidatorApiFactory();
        factory.SupabaseHandler.UserResponder = _ =>
            BuildJsonResponse(HttpStatusCode.OK, BuildUserResponseJson("authenticated"));
        using var client = factory.CreateClient();
        using var request = BuildVerifyRequest();
        var token = CreateUnsignedJwt();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Verify_AllowsValidatorRole()
    {
        using var factory = new ValidatorApiFactory();
        factory.SupabaseHandler.UserResponder = _ =>
            BuildJsonResponse(HttpStatusCode.OK, BuildUserResponseJson("validator"));
        using var client = factory.CreateClient();
        using var request = BuildVerifyRequest();
        var token = CreateUnsignedJwt();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsync("/api/claims/verify", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static IConfiguration BuildConfiguration()
    {
        var config = new Dictionary<string, string?>
        {
            ["Supabase:BaseUrl"] = TestBaseUrl,
            ["Supabase:PublishableKey"] = TestPublishableKey
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();
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
        content.Add(videoContent, "video", "clip.mp4");

        var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        content.Add(metadataContent, "metadata", "metadata.json");

        var sessionIdContent = new StringContent(metadata.SessionId, Encoding.UTF8, "text/plain");
        content.Add(sessionIdContent, "sessionId");

        return content;
    }

    private static string CreateUnsignedJwt(string? kid = null, string? alg = null)
    {
        var header = new Dictionary<string, object?>
        {
            ["alg"] = string.IsNullOrWhiteSpace(alg) ? "HS256" : alg,
            ["typ"] = "JWT"
        };
        if (!string.IsNullOrWhiteSpace(kid))
        {
            header["kid"] = kid;
        }

        var payload = new Dictionary<string, object?>
        {
            ["sub"] = "user-123"
        };

        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);
        return $"{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(headerJson))}." +
               $"{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson))}.";
    }

    private static string CreateSignedEs256Jwt(string issuer, string kid, out JsonWebKey jwk)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ecdsa) { KeyId = kid };
        var creds = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: null,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "user-123"),
                new Claim("role", "validator")
            },
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(30),
            signingCredentials: creds);
        token.Header["kid"] = kid;

        var parameters = ecdsa.ExportParameters(false);
        jwk = new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(parameters.Q.X),
            Y = Base64UrlEncoder.Encode(parameters.Q.Y),
            Kid = kid,
            Alg = "ES256",
            Use = "sig"
        };

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string BuildUserResponseJson(string role)
    {
        var payload = new
        {
            id = "user-123",
            email = "a@b.com",
            app_metadata = new
            {
                role
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string BuildJwksJson(JsonWebKey jwk)
    {
        var payload = new
        {
            keys = new[]
            {
                new Dictionary<string, string?>
                {
                    ["kty"] = jwk.Kty,
                    ["crv"] = jwk.Crv,
                    ["x"] = jwk.X,
                    ["y"] = jwk.Y,
                    ["kid"] = jwk.Kid,
                    ["alg"] = jwk.Alg,
                    ["use"] = jwk.Use
                }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static HttpResponseMessage BuildJsonResponse(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public class ValidatorApiFactory : WebApplicationFactory<Program>
    {
        public TestSupabaseHandler SupabaseHandler { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, configBuilder) =>
            {
                var config = new Dictionary<string, string?>
                {
                    ["Supabase:BaseUrl"] = TestBaseUrl,
                    ["Supabase:PublishableKey"] = TestPublishableKey,
                    ["Supabase:ServiceRoleKey"] = "service-role-key",
                    ["Ffmpeg:Path"] = "ffmpeg"
                };
                configBuilder.AddInMemoryCollection(config);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SupabaseJwtValidator>();
                services.AddSingleton(sp => new SupabaseJwtValidator(
                    sp.GetRequiredService<IConfiguration>(),
                    new HttpClient(SupabaseHandler)));

                services.RemoveAll<ISupabaseHashStore>();
                services.RemoveAll<IVideoFrameExtractor>();

                services.AddSingleton<ISupabaseHashStore>(new FakeSupabaseHashStore());
                services.AddSingleton<IVideoFrameExtractor>(new FakeVideoFrameExtractor());
                services.AddScoped<VerificationService>();
            });
        }
    }

    private sealed class TestSupabaseHandler : HttpMessageHandler
    {
        public int JwksCalls { get; private set; }
        public int UserCalls { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? JwksResponder { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? UserResponder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("/auth/v1/.well-known/jwks.json", StringComparison.OrdinalIgnoreCase))
            {
                JwksCalls++;
                return Task.FromResult(JwksResponder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (path.EndsWith("/auth/v1/user", StringComparison.OrdinalIgnoreCase))
            {
                UserCalls++;
                return Task.FromResult(UserResponder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
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

        public Task<IReadOnlyList<FrameHashRecord>> GetFrameHashesAsync(string sessionId, string? source, CancellationToken ct)
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

        public Task InsertFrameHashesAsync(IReadOnlyList<FrameHashRecord> records, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SetReferenceSourceAsync(string sessionId, string referenceSource, CancellationToken ct)
        {
            return Task.CompletedTask;
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
