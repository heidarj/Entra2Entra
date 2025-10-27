using System.Text.Json;
using AdminSync.Models;

namespace AdminSync.Services;

public static class ScimPatchHelper
{
    public static Dictionary<string, object?> Normalize(ScimPatchRequest request)
    {
        var patchPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in request.Operations)
        {
            var op = operation.Operation.ToLowerInvariant();
            if (op is not ("add" or "replace" or "remove"))
            {
                continue;
            }

            var path = operation.Path?.ToLowerInvariant();
            if (string.IsNullOrEmpty(path))
            {
                if (operation.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in operation.Value.EnumerateObject())
                    {
                        MapPatchValue(patchPayload, property.Name, property.Value);
                    }
                }
                continue;
            }

            MapPatchValue(patchPayload, path, operation.Value);
        }

        return patchPayload
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void MapPatchValue(IDictionary<string, object?> patch, string path, JsonElement value)
    {
        switch (path)
        {
            case "username":
                patch["userPrincipalName"] = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                break;
            case "name.givenname":
                patch["givenName"] = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                break;
            case "name.familyname":
                patch["surname"] = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                break;
            case "active":
                patch["accountEnabled"] = value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(value.GetString(), out var b) => b,
                    _ => null
                };
                break;
            default:
                if (path.StartsWith("emails", StringComparison.OrdinalIgnoreCase))
                {
                    var email = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    patch["mail"] = email;
                }
                break;
        }
    }
}
