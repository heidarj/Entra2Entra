using System.Text.Json.Serialization;

namespace AdminSync.Models;

public record GraphBulkRequest
{
    [JsonPropertyName("value")]
    public IReadOnlyList<GraphBulkOperation> Value { get; init; } = Array.Empty<GraphBulkOperation>();
}

public record GraphBulkOperation
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("method")]
    public string Method { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("bulkId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BulkId { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }
}

public record GraphBulkOperationResult
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("response")]
    public JsonElement Response { get; init; }
}

public record GraphBulkResponse
{
    [JsonPropertyName("value")]
    public IReadOnlyList<GraphBulkOperationResult> Value { get; init; } = Array.Empty<GraphBulkOperationResult>();
}
