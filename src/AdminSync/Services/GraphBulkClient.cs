using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminSync.Models;
using AdminSync.Options;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdminSync.Services;

public class GraphBulkClient : IGraphBulkClient
{
    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];
    private readonly HttpClient _httpClient;
    private readonly GraphOptions _options;
    private readonly TokenCredential _credential;
    private readonly ILogger<GraphBulkClient> _logger;
    private readonly JsonSerializerOptions _serializerOptions;

    public GraphBulkClient(
        HttpClient httpClient,
        IOptions<GraphOptions> options,
        ILogger<GraphBulkClient> logger,
        TokenCredential credential)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credential = credential;
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task<GraphBulkResponse> UploadAsync(GraphBulkRequest request, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"servicePrincipals/{_options.ServicePrincipalId}/synchronization/jobs/{_options.SyncJobId}/bulkUpload");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = JsonContent.Create(request, options: _serializerOptions);

        _logger.LogInformation("Dispatching {OperationCount} provisioning operations to Graph sync job", request.Value.Count);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return new GraphBulkResponse();
        }

        response.EnsureSuccessStatusCode();

        var graphResponse = await response.Content.ReadFromJsonAsync<GraphBulkResponse>(_serializerOptions, cancellationToken);
        return graphResponse ?? new GraphBulkResponse();
    }
}
