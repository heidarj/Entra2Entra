using System.Text.Json.Serialization;

namespace AdminSync.Models;

public record NormalizedProvisioningPayload
{
    [JsonPropertyName("scimId")]
    public string? ScimId { get; init; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName("givenName")]
    public string? GivenName { get; init; }

    [JsonPropertyName("familyName")]
    public string? FamilyName { get; init; }

    [JsonPropertyName("primaryEmail")]
    public string? PrimaryEmail { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    [JsonPropertyName("rawPatch")]
    public Dictionary<string, object?>? RawPatch { get; init; }
}
