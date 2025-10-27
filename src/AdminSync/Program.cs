using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminSync.Data;
using AdminSync.Extensions;
using AdminSync.Models;
using AdminSync.Options;
using AdminSync.Security;
using AdminSync.Services;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);

builder.Services.AddOptions<GraphOptions>()
    .Bind(builder.Configuration.GetSection("Graph"))
    .ValidateDataAnnotations()
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl?.ToString()), "Graph:BaseUrl must be a valid absolute URI.")
    .ValidateOnStart();

builder.Services.AddOptions<ScimOptions>()
    .Bind(builder.Configuration.GetSection("Scim"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ProvisioningBatchOptions>()
    .Bind(builder.Configuration.GetSection("Provisioning:Batches"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<ProvisioningDbContext>(options =>
    options.UseInMemoryDatabase("Provisioning"));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = SharedSecretAuthenticationHandler.SchemeName;
        options.DefaultAuthenticateScheme = SharedSecretAuthenticationHandler.SchemeName;
        options.DefaultChallengeScheme = SharedSecretAuthenticationHandler.SchemeName;
    })
    .AddScheme<AuthenticationSchemeOptions, SharedSecretAuthenticationHandler>(
        SharedSecretAuthenticationHandler.SchemeName,
        configureOptions: _ => { });

builder.Services.AddAuthorization();

builder.Services.AddProblemDetails();

builder.Services.AddSingleton<TokenCredential>(sp =>
{
    var options = sp.GetRequiredService<IOptions<GraphOptions>>().Value;
    return new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
});

builder.Services.AddSingleton<ProvisioningMapper>();

builder.Services.AddSingleton<ProvisioningBackgroundService>();
builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ProvisioningBackgroundService>());
builder.Services.AddSingleton<IProvisioningDispatcher>(sp => sp.GetRequiredService<ProvisioningBackgroundService>());

builder.Services.AddHttpClient<IGraphBulkClient, GraphBulkClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<GraphOptions>>().Value;
        client.BaseAddress = options.BaseUrl;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    })
    .AddPolicyHandler((services, request) =>
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(5, retryAttempt =>
            {
                var backoff = Math.Pow(2, retryAttempt);
                return TimeSpan.FromSeconds(backoff);
            }, (outcome, _, retryAttempt, _) =>
            {
                var logger = services.GetRequiredService<ILogger<GraphBulkClient>>();
                logger.LogWarning("Retrying Graph bulkUpload attempt {Attempt} after {StatusCode}", retryAttempt, outcome.Result?.StatusCode);
            });
    });

var app = builder.Build();

app.UseExceptionHandler();

app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestCorrelation");
    var correlationId = context.Request.Headers["x-ms-correlation-id"].FirstOrDefault()
                        ?? context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString();

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

app.UseAuthentication();
app.UseAuthorization();

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

static string ComputeHash(string payload)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    return Convert.ToHexString(hash);
}

app.MapGet("/scim/v2/ServiceProviderConfig", () =>
    Results.Json(new
    {
        schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig" },
        patch = new { supported = true },
        bulk = new { supported = true, maxOperations = 50, maxPayloadSize = 1048576 },
        filter = new { supported = true, maxResults = 200 },
        changePassword = new { supported = false },
        sort = new { supported = false },
        etag = new { supported = false }
    }, jsonOptions))
    .RequireAuthorization();

app.MapGet("/scim/v2/Schemas", () =>
    Results.Json(new
    {
        Resources = new object[]
        {
            new
            {
                id = "urn:ietf:params:scim:schemas:core:2.0:User",
                name = "User",
                description = "User Account",
                attributes = Array.Empty<object>()
            }
        }
    }, jsonOptions))
    .RequireAuthorization();

app.MapGet("/scim/v2/Users", async ([FromServices] ProvisioningDbContext db) =>
    {
        var users = await db.ProvisionRecords
            .Where(r => r.Type == ProvisionRecordType.Create)
            .OrderBy(r => r.CreatedUtc)
            .Take(25)
            .Select(r => new { r.Id, r.TargetId })
            .ToListAsync();

        return Results.Json(new
        {
            Resources = users,
            totalResults = users.Count,
            itemsPerPage = users.Count,
            startIndex = 1
        }, jsonOptions);
    })
    .RequireAuthorization();

app.MapPost("/scim/v2/Users", async (
    HttpContext context,
    [FromBody] ScimUser user,
    ProvisioningDbContext db,
    IProvisioningDispatcher dispatcher,
    ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("ScimUsersPost");
        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            return Results.BadRequest(new { detail = "userName is required" });
        }

        var correlationId = context.GetCorrelationId();
        var normalized = new NormalizedProvisioningPayload
        {
            ScimId = user.Id,
            ExternalId = user.ExternalId,
            UserName = user.UserName,
            GivenName = user.Name?.GivenName,
            FamilyName = user.Name?.FamilyName,
            PrimaryEmail = user.Emails.FirstOrDefault(e => e.Primary)?.Value ?? user.Emails.FirstOrDefault()?.Value,
            Active = user.Active ?? true
        };

        var record = new ProvisionRecord
        {
            Type = ProvisionRecordType.Create,
            PayloadJson = JsonSerializer.Serialize(normalized, jsonOptions),
            TargetId = user.Id ?? user.ExternalId,
            CorrelationId = correlationId
        };

        db.ProvisionRecords.Add(record);

        var bodyHash = ComputeHash(JsonSerializer.Serialize(user, jsonOptions));
        db.AuditLogs.Add(new AuditLog
        {
            Direction = AuditDirection.Inbound,
            Endpoint = "POST /scim/v2/Users",
            StatusCode = (int)HttpStatusCode.Accepted,
            BodyHash = bodyHash,
            CorrelationId = correlationId
        });

        await db.SaveChangesAsync();

        await dispatcher.TriggerFlushAsync();

        logger.LogInformation("Enqueued create provisioning record {RecordId}", record.Id);

        return Results.Accepted($"/scim/v2/Users/{record.Id}", new { id = record.Id, meta = new { location = $"/scim/v2/Users/{record.Id}" } });
    })
    .RequireAuthorization();

app.MapPatch("/scim/v2/Users/{id}", async (
    HttpContext context,
    string id,
    [FromBody] ScimPatchRequest request,
    ProvisioningDbContext db,
    IProvisioningDispatcher dispatcher,
    ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("ScimUsersPatch");
        if (request.Operations.Count == 0)
        {
            return Results.BadRequest(new { detail = "At least one operation is required" });
        }

        var correlationId = context.GetCorrelationId();

        var patchPayload = ScimPatchHelper.Normalize(request);

        var normalized = new NormalizedProvisioningPayload
        {
            ScimId = id,
            RawPatch = patchPayload
        };

        if (normalized.RawPatch.Count == 0)
        {
            return Results.BadRequest(new { detail = "No supported patch operations found" });
        }

        var record = new ProvisionRecord
        {
            Type = ProvisionRecordType.Update,
            PayloadJson = JsonSerializer.Serialize(normalized, jsonOptions),
            TargetId = id,
            CorrelationId = correlationId
        };

        db.ProvisionRecords.Add(record);
        db.AuditLogs.Add(new AuditLog
        {
            Direction = AuditDirection.Inbound,
            Endpoint = $"PATCH /scim/v2/Users/{id}",
            StatusCode = (int)HttpStatusCode.Accepted,
            BodyHash = ComputeHash(JsonSerializer.Serialize(request, jsonOptions)),
            CorrelationId = correlationId
        });

        await db.SaveChangesAsync();
        await dispatcher.TriggerFlushAsync();

        logger.LogInformation("Enqueued update provisioning record {RecordId}", record.Id);

        return Results.Accepted($"/scim/v2/Users/{id}");
    })
    .RequireAuthorization();

app.MapDelete("/scim/v2/Users/{id}", async (
    HttpContext context,
    string id,
    ProvisioningDbContext db,
    IProvisioningDispatcher dispatcher,
    ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("ScimUsersDelete");
        var correlationId = context.GetCorrelationId();

        var normalized = new NormalizedProvisioningPayload
        {
            ScimId = id,
            Active = false
        };

        var record = new ProvisionRecord
        {
            Type = ProvisionRecordType.Delete,
            PayloadJson = JsonSerializer.Serialize(normalized, jsonOptions),
            TargetId = id,
            CorrelationId = correlationId
        };

        db.ProvisionRecords.Add(record);
        db.AuditLogs.Add(new AuditLog
        {
            Direction = AuditDirection.Inbound,
            Endpoint = $"DELETE /scim/v2/Users/{id}",
            StatusCode = (int)HttpStatusCode.Accepted,
            BodyHash = ComputeHash(id),
            CorrelationId = correlationId
        });

        await db.SaveChangesAsync();
        await dispatcher.TriggerFlushAsync();

        logger.LogInformation("Enqueued delete provisioning record {RecordId}", record.Id);

        return Results.NoContent();
    })
    .RequireAuthorization();

app.MapPost("/admin/flush", async (IProvisioningDispatcher dispatcher, ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("AdminFlush");
        await dispatcher.TriggerFlushAsync();
        logger.LogInformation("Manual flush requested");
        return Results.Accepted();
    })
    .RequireAuthorization();

app.Run();

partial class Program
{
}
