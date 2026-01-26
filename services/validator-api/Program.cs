using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ValidatorApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSupabaseHashStore(builder.Configuration);
builder.Services.AddFfmpegFrameExtractor(builder.Configuration);
builder.Services.AddScoped<VerificationService>();

var app = builder.Build();

app.MapControllers();

app.Run();
