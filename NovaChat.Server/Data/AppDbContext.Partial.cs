using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;

namespace NovaChat.Server.Data;

public partial class AppDbContext
{
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
                locks.Add(await DatabaseIdAllocator.AcquireTableLockAsync(connection, group.Key, cancellationToken));

            foreach (var group in byTable)
            {
                var reservedIds = new HashSet<long>();

                foreach (var item in group)
                {
                    switch (item.Entity)
                    {
                        case User user when user.Id > 0:
                            reservedIds.Add(user.Id);
                            break;
                        case Chat chat when chat.Id > 0:
                            reservedIds.Add(chat.Id);
                            break;
                        case Contact contact when contact.Id > 0:
                            reservedIds.Add(contact.Id);
                            break;
                        case Message message when message.Id > 0:
                            reservedIds.Add(message.Id);
                            break;
                    }
                }

                foreach (var item in group)
                {
                    switch (item.Entity)
                    {
                        case User user when user.Id == 0:
                            user.Id = await DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken);
                            break;

                        case Chat chat when chat.Id == 0:
                            chat.Id = checked((int)await DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken));
                            break;

                        case Contact contact when contact.Id == 0:
                            contact.Id = checked((int)await DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken));
                            break;

                        case Message message when message.Id == 0:
                            message.Id = checked((int)await DatabaseIdAllocator.GetFirstAvailableIdAsync(connection, group.Key, reservedIds, cancellationToken));
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
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.DeviceId)
                .HasMaxLength(64);

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
