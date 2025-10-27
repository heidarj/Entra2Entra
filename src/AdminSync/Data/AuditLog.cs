using System.ComponentModel.DataAnnotations;

namespace AdminSync.Data;

public enum AuditDirection
{
    Inbound = 0,
    Outbound = 1
}

public class AuditLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public AuditDirection Direction { get; set; }

    [Required]
    [MaxLength(256)]
    public string Endpoint { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    [MaxLength(128)]
    public string BodyHash { get; set; } = string.Empty;

    [MaxLength(128)]
    public string CorrelationId { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
