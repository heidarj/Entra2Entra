using Microsoft.EntityFrameworkCore;

namespace AdminSync.Data;

public class ProvisioningDbContext : DbContext
{
    public ProvisioningDbContext(DbContextOptions<ProvisioningDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProvisionRecord> ProvisionRecords => Set<ProvisionRecord>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProvisionRecord>()
            .HasIndex(x => new { x.Status, x.CreatedUtc });

        base.OnModelCreating(modelBuilder);
    }
}
