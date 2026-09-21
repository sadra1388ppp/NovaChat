using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Data;

public partial class AppDbContext
{
    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var added = ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        var byTable = added
            .Select(entity => (Entity: entity, Table: entity switch
            {
                User => "Users",
                Chat => "Chats",
                Contact => "Contacts",
                Message => "Messages",
                AuditLog => "AuditLogs",
                _ => null
            }))
            .Where(x => x.Table != null)
            .GroupBy(x => x.Table!)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToList();

        if (byTable.Count == 0)
            return await base.SaveChangesAsync(cancellationToken);

        var connection = Database.GetDbConnection();
        var locks = new List<IAsyncDisposable>();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;

        try
        {
            foreach (var group in byTable)
                locks.Add(await Services.DatabaseIdAllocator.AcquireTableLockAsync(connection, group.Key, cancellationToken));

            foreach (var group in byTable)
            {
                var reservedIds = new HashSet<long>();
                foreach (var item in group)
                {
                    switch (item.Entity)
                    {
                        case User user when user.Id == 0:
                            user.Id = await Services.DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken);
                            break;

                        case Chat chat when chat.Id == 0:
                            chat.Id = checked((int)await Services.DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken));
                            break;

                        case Contact contact when contact.Id == 0:
                            contact.Id = checked((int)await Services.DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken));
                            break;

                        case Message message when message.Id == 0:
                            message.Id = checked((int)await Services.DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken));
                            break;

                        case AuditLog auditLog when auditLog.Id == 0:
                            auditLog.Id = await Services.DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken);
                            break;
                    }
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            for (var index = locks.Count - 1; index >= 0; index--)
                await locks[index].DisposeAsync();

            if (wasClosed && connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
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
            entity.Property(e => e.DeviceId).HasMaxLength(128);
            entity.Property(e => e.RequestId).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.Succeeded).HasColumnType("tinyint(1)").HasDefaultValue(true);
            entity.Property(e => e.Details).HasColumnType("text");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime(6)");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.MessagePrivacy)
                .HasMaxLength(32)
                .HasDefaultValue("Everybody");

            // The database stores the value as the literal text "true" or "false",
            // while the generated entity keeps the application-facing bool type.
            entity.Property(e => e.AllowGroupAdds)
                .HasColumnType("varchar(5)")
                .HasMaxLength(5)
                .HasConversion(
                    value => value ? "true" : "false",
                    value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                .HasDefaultValue("true");
        });
    }
}
