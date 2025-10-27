using System.ComponentModel.DataAnnotations;

namespace AdminSync.Options;

public class GraphOptions
{
    private const string DefaultBaseUrl = "https://graph.microsoft.com/v1.0/";

    [Required]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    [Required]
    public string ServicePrincipalId { get; set; } = string.Empty;

    [Required]
    public string SyncJobId { get; set; } = string.Empty;

    public Uri BaseUrl { get; set; } = new(DefaultBaseUrl, UriKind.Absolute);
}
