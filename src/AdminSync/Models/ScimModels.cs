using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdminSync.Models;

public record ScimUser
{
    [JsonPropertyName("schemas")]
    public string[] Schemas { get; init; } = Array.Empty<string>();

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    [Required]
    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName("name")]
    public ScimName? Name { get; init; }

    [JsonPropertyName("emails")]
    public List<ScimEmail> Emails { get; init; } = new();

    [JsonPropertyName("active")]
    public bool? Active { get; init; }
}

public record ScimName
{
    [JsonPropertyName("givenName")]
    public string? GivenName { get; init; }

    [JsonPropertyName("familyName")]
    public string? FamilyName { get; init; }
}

public record ScimEmail
{
    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("primary")]
    public bool Primary { get; init; }
}

public record ScimPatchRequest
{
    [JsonPropertyName("Operations")]
    public List<ScimPatchOperation> Operations { get; init; } = new();
}

public record ScimPatchOperation
{
    [JsonPropertyName("op")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}
