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
            entity.Property(e => e.Id).HasColumnType("bigint(20)");
            entity.Property(e => e.Request).HasColumnType("LONGTEXT").IsRequired();
        });
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
