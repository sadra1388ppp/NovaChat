using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;

namespace NovaChat.Server.Data;

public sealed class HttpRequestLogDbContext(DbContextOptions<HttpRequestLogDbContext> options) : DbContext(options)
{
    public DbSet<HttpRequestLog> HttpRequests => Set<HttpRequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_unicode_ci").HasCharSet("utf8mb4");

        modelBuilder.Entity<HttpRequestLog>(entity =>
        {
            entity.ToTable("HttpRequests");
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.HasIndex(e => e.RequestId, "IX_HttpRequests_RequestId");
            entity.HasIndex(e => e.UserId, "IX_HttpRequests_UserId");
            entity.HasIndex(e => e.StatusCode, "IX_HttpRequests_StatusCode");
            entity.HasIndex(e => e.StartedAt, "IX_HttpRequests_StartedAt");
            entity.HasIndex(e => e.Path, "IX_HttpRequests_Path");

            entity.Property(e => e.Id).HasColumnType("bigint(20)");
            entity.Property(e => e.RequestId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Scheme).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Host).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Path).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.QueryString).HasMaxLength(4096);
            entity.Property(e => e.Protocol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.StatusCode).HasColumnType("int");
            entity.Property(e => e.IsAuthenticated).HasColumnType("tinyint(1)");
            entity.Property(e => e.UserId).HasColumnType("bigint(20)");
            entity.Property(e => e.Username).HasMaxLength(32);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(1024);
            entity.Property(e => e.RequestContentType).HasMaxLength(255);
            entity.Property(e => e.ResponseContentType).HasMaxLength(255);
            entity.Property(e => e.ResponseBody).HasColumnType("LONGTEXT");
            entity.Property(e => e.StartedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.CompletedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.DurationMs).HasColumnType("bigint");
            entity.Property(e => e.Succeeded).HasColumnType("tinyint(1)");
        });
    }

    public async Task EnsureSchemaCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        const string columnExistsSql = @"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'HttpRequests'
              AND COLUMN_NAME = 'ResponseBody';";

        var connection = Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = columnExistsSql;

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var exists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;

        if (exists)
            return;

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = @"
            ALTER TABLE `HttpRequests`
            ADD COLUMN `ResponseBody` LONGTEXT NULL AFTER `ResponseContentLength`;";

        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var added = ChangeTracker.Entries<HttpRequestLog>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        if (added.Count == 0)
            return await base.SaveChangesAsync(cancellationToken);

        var connection = Database.GetDbConnection();
        var tableLock = await DatabaseIdAllocator.AcquireTableLockAsync(connection, "HttpRequests", cancellationToken);

        try
        {
            var reservedIds = added.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();

            foreach (var row in added.Where(x => x.Id == 0))
                row.Id = await DatabaseIdAllocator.GetFirstAvailableIdAsync(
                    connection, "HttpRequests", reservedIds, cancellationToken);

            return await base.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            await tableLock.DisposeAsync();
        }
    }
}
