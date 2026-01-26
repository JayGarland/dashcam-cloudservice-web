namespace ValidatorApi.Services;

public class SupabaseOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public int? TimeoutSeconds { get; set; }
}
