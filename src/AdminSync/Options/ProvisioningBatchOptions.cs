using System.ComponentModel.DataAnnotations;

namespace AdminSync.Options;

public class ProvisioningBatchOptions
{
    private const int DefaultMaxOperations = 50;
    private const int DefaultFlushSeconds = 5;

    [Range(1, 50)]
    public int MaxOperations { get; set; } = DefaultMaxOperations;

    [Range(1, 600)]
    public int FlushSeconds { get; set; } = DefaultFlushSeconds;
}
