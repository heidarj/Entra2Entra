using System.Threading.Channels;
using AdminSync.Data;
using AdminSync.Models;
using AdminSync.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdminSync.Services;

public class ProvisioningBackgroundService : BackgroundService, IProvisioningDispatcher
{
    private const int MaxAttempts = 5;
    private readonly Channel<bool> _flushChannel = Channel.CreateUnbounded<bool>();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProvisioningBatchOptions _options;
    private readonly ILogger<ProvisioningBackgroundService> _logger;
    private readonly ProvisioningMapper _mapper;
    private readonly IGraphBulkClient _graphClient;

    public ProvisioningBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ProvisioningBatchOptions> batchOptions,
        ILogger<ProvisioningBackgroundService> logger,
        ProvisioningMapper mapper,
        IGraphBulkClient graphClient)
    {
        _scopeFactory = scopeFactory;
        _options = batchOptions.Value;
        _logger = logger;
        _mapper = mapper;
        _graphClient = graphClient;
    }

    public async Task TriggerFlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_flushChannel.Writer.TryWrite(true))
        {
            await _flushChannel.Writer.WriteAsync(true, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.FlushSeconds));
        var signalTask = _flushChannel.Reader.ReadAsync(stoppingToken).AsTask();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var timerTask = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                var completed = await Task.WhenAny(signalTask, timerTask);
                if (completed == signalTask)
                {
                    _ = signalTask.Result;
                    signalTask = _flushChannel.Reader.ReadAsync(stoppingToken).AsTask();
                }

                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running provisioning batch loop");
            }
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProvisioningDbContext>();

        var pending = await db.ProvisionRecords
            .Where(r => r.Status == ProvisionRecordStatus.Pending)
            .OrderBy(r => r.CreatedUtc)
            .Take(_options.MaxOperations)
            .ToListAsync(cancellationToken);

        if (!pending.Any())
        {
            return;
        }

        var utcNow = DateTime.UtcNow;
        foreach (var record in pending)
        {
            record.Status = ProvisionRecordStatus.InProgress;
            record.Attempts += 1;
            record.LastAttemptUtc = utcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var operations = pending.Select(_mapper.MapToOperation).ToArray();
            var request = new GraphBulkRequest { Value = operations };
            var response = await _graphClient.UploadAsync(request, cancellationToken);

            var responseLookup = response.Value.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
            foreach (var record in pending)
            {
                if (responseLookup.TryGetValue(record.Id.ToString(), out var result))
                {
                    if (result.Status >= 200 && result.Status < 300)
                    {
                        record.Status = ProvisionRecordStatus.Completed;
                        record.CompletedUtc = DateTime.UtcNow;
                        continue;
                    }

                    _logger.LogWarning("Graph bulk operation failed for record {RecordId} with status {Status}", record.Id, result.Status);
                }
                else
                {
                    _logger.LogWarning("Graph bulk response missing status for record {RecordId}", record.Id);
                }

                if (record.Attempts >= MaxAttempts)
                {
                    record.Status = ProvisionRecordStatus.Failed;
                }
                else
                {
                    record.Status = ProvisionRecordStatus.Pending;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send provisioning batch to Graph");
            foreach (var record in pending)
            {
                if (record.Attempts >= MaxAttempts)
                {
                    record.Status = ProvisionRecordStatus.Failed;
                }
                else
                {
                    record.Status = ProvisionRecordStatus.Pending;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
