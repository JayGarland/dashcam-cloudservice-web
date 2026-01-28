using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ValidatorApi.Auth;
using ValidatorApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Startup config logging (to debug issuer validation)
var environment = builder.Environment.EnvironmentName;
var supabaseBaseUrl = builder.Configuration["Supabase:BaseUrl"];
var publishableKey = builder.Configuration["Supabase:PublishableKey"] ?? builder.Configuration["Supabase:AnonKey"];
var publishableKeyExists = !string.IsNullOrWhiteSpace(publishableKey);
var publishableKeyLength = publishableKeyExists ? publishableKey?.Length ?? 0 : 0;
var serviceRoleKeyExists = !string.IsNullOrWhiteSpace(builder.Configuration["Supabase:ServiceRoleKey"]);
var serviceRoleKeyLength = serviceRoleKeyExists ? builder.Configuration["Supabase:ServiceRoleKey"]?.Length ?? 0 : 0;

Console.WriteLine("=== Validator API Startup Config ===");
Console.WriteLine($"Environment: {environment}");
Console.WriteLine($"Supabase:BaseUrl: {supabaseBaseUrl}");
Console.WriteLine($"Supabase:PublishableKey/AnonKey: {(publishableKeyExists ? $"Set (length={publishableKeyLength})" : "NOT SET")}");
Console.WriteLine($"Supabase:ServiceRoleKey: {(serviceRoleKeyExists ? $"Set (length={serviceRoleKeyLength})" : "NOT SET")}");
Console.WriteLine("=====================================");

builder.Services.AddControllers();
builder.Services.AddSupabaseHashStore(builder.Configuration);
builder.Services.AddFfmpegFrameExtractor(builder.Configuration);
builder.Services.AddScoped<VerificationService>();
builder.Services.AddHttpClient<SupabaseJwtValidator>();
builder.Services
    .AddAuthentication("SupabaseHybrid")
    .AddScheme<AuthenticationSchemeOptions, SupabaseHybridAuthHandler>("SupabaseHybrid", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ValidatorOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ValidatorRoleRequirement());
    });
});
builder.Services.AddSingleton<IAuthorizationHandler, ValidatorRoleHandler>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});


var app = builder.Build();

app.UseCors("DevCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
