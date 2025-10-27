namespace AdminSync.Services;

public interface IProvisioningDispatcher
{
    Task TriggerFlushAsync(CancellationToken cancellationToken = default);
}
