using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ValidatorApi.Auth;
using ValidatorApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSupabaseHashStore(builder.Configuration);
builder.Services.AddFfmpegFrameExtractor(builder.Configuration);
builder.Services.AddScoped<VerificationService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = SupabaseJwtValidator.BuildTokenValidationParameters(builder.Configuration);
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ValidatorOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ValidatorRoleRequirement());
    });
});
builder.Services.AddSingleton<IAuthorizationHandler, ValidatorRoleHandler>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
