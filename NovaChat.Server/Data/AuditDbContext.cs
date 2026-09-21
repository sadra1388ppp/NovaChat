using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;

namespace NovaChat.Server.Data;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_unicode_ci").HasCharSet("utf8mb4");

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.HasIndex(e => e.UserId, "IX_AuditLogs_UserId");
            entity.HasIndex(e => e.EventType, "IX_AuditLogs_EventType");
            entity.HasIndex(e => e.ChatId, "IX_AuditLogs_ChatId");
            entity.HasIndex(e => e.MessageId, "IX_AuditLogs_MessageId");
            entity.HasIndex(e => e.CreatedAt, "IX_AuditLogs_CreatedAt");

            entity.Property(e => e.Id).HasColumnType("bigint(20)");
            entity.Property(e => e.UserId).HasColumnType("bigint(20)");
            entity.Property(e => e.Username).HasMaxLength(32);
            entity.Property(e => e.Category).HasMaxLength(32).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(32);
            entity.Property(e => e.TargetId).HasMaxLength(128);
            entity.Property(e => e.ChatId).HasColumnType("int(11)");
            entity.Property(e => e.MessageId).HasColumnType("int(11)");
            entity.Property(e => e.DeviceId).HasMaxLength(64);
            entity.Property(e => e.RequestId).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.Succeeded).HasColumnType("tinyint(1)").HasDefaultValue(true);
            entity.Property(e => e.Details).HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime(6)");
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var added = ChangeTracker.Entries<AuditLog>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        if (added.Count == 0)
            return await base.SaveChangesAsync(cancellationToken);

        var connection = Database.GetDbConnection();
        var tableLock = await DatabaseIdAllocator.AcquireTableLockAsync(connection, "AuditLogs", cancellationToken);

        try
        {
            var reservedIds = added
                .Where(x => x.Id > 0)
                .Select(x => x.Id)
                .ToHashSet();

            foreach (var auditLog in added.Where(x => x.Id == 0))
                auditLog.Id = await DatabaseIdAllocator.GetFirstAvailableIdAsync(
                    connection, "AuditLogs", reservedIds, cancellationToken);

            return await base.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            await tableLock.DisposeAsync();
        }
    }
}
