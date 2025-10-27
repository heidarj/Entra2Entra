using System.Text.Json;
using AdminSync.Data;
using AdminSync.Models;
using Microsoft.Extensions.Logging;

namespace AdminSync.Services;

public class ProvisioningMapper
{
    private readonly ILogger<ProvisioningMapper> _logger;

    public ProvisioningMapper(ILogger<ProvisioningMapper> logger)
    {
        _logger = logger;
    }

    public GraphBulkOperation MapToOperation(ProvisionRecord record)
    {
        var payload = JsonSerializer.Deserialize<NormalizedProvisioningPayload>(record.PayloadJson) ?? new NormalizedProvisioningPayload();

        var method = record.Type switch
        {
            ProvisionRecordType.Create => "POST",
            ProvisionRecordType.Update => "PATCH",
            ProvisionRecordType.Delete => "DELETE",
            _ => "POST"
        };

        object? data = null;
        if (record.Type is ProvisionRecordType.Create or ProvisionRecordType.Update)
        {
            data = new Dictionary<string, object?>
            {
                ["userPrincipalName"] = payload.UserName,
                ["givenName"] = payload.GivenName,
                ["surname"] = payload.FamilyName,
                ["mail"] = payload.PrimaryEmail,
                ["accountEnabled"] = payload.Active ?? true
            };
        }

        if (payload.RawPatch is not null && payload.RawPatch.Count > 0)
        {
            data = payload.RawPatch;
        }

        var path = record.Type == ProvisionRecordType.Delete && payload.ScimId is not null
            ? $"/Users/{payload.ScimId}"
            : "/Users";

        if (record.Type is ProvisionRecordType.Update && payload.ScimId is not null)
        {
            path = $"/Users/{payload.ScimId}";
        }

        return new GraphBulkOperation
        {
            Id = record.Id.ToString(),
            BulkId = payload.ScimId ?? record.Id.ToString(),
            Method = method,
            Path = path,
            Data = data
        };
    }
}
