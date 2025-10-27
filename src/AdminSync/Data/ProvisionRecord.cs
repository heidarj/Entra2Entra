using System.ComponentModel.DataAnnotations;

namespace AdminSync.Data;

public enum ProvisionRecordType
{
    Create = 1,
    Update = 2,
    Delete = 3
}

public enum ProvisionRecordStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

public class ProvisionRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public ProvisionRecordType Type { get; set; }

    [Required]
    public ProvisionRecordStatus Status { get; set; } = ProvisionRecordStatus.Pending;

    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? TargetId { get; set; }

    [MaxLength(128)]
    public string CorrelationId { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }
}
