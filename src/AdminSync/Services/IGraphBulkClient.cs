using AdminSync.Models;

namespace AdminSync.Services;

public interface IGraphBulkClient
{
    Task<GraphBulkResponse> UploadAsync(GraphBulkRequest request, CancellationToken cancellationToken);
}
