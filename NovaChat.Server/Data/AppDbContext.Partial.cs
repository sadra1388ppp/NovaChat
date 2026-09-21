using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Data;

public partial class AppDbContext
{
    public virtual DbSet<AuditLog> AuditLogs { get; set; }

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
