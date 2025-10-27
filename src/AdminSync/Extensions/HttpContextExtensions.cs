using Microsoft.AspNetCore.Http;

namespace AdminSync.Extensions;

public static class HttpContextExtensions
{
    private const string CorrelationKey = "CorrelationId";

    public static string GetCorrelationId(this HttpContext context)
        => context.Items.TryGetValue(CorrelationKey, out var value) && value is string id
            ? id
            : context.TraceIdentifier;
}
