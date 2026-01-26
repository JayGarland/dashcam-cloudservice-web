using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ValidatorApi.Services;

public static class SupabaseServiceCollectionExtensions
{
    public static IServiceCollection AddSupabaseHashStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SupabaseOptions>(configuration.GetSection("Supabase"));
        services.AddHttpClient<ISupabaseHashStore, SupabaseHashStore>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;
            if (options.TimeoutSeconds.HasValue && options.TimeoutSeconds.Value > 0)
            {
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds.Value);
            }
        });
        return services;
    }
}
