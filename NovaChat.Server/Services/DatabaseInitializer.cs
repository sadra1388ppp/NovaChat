using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Services;

public sealed class DatabaseInitializer(AppDbContext context)
{
    public async Task<bool> InitializeEmptyDatabaseAsync(CancellationToken cancellationToken = default)
    {
        // Explicit setup only. EnsureCreated never upgrades existing tables.
        var created = await context.Database.EnsureCreatedAsync(cancellationToken);
        await ValidateSchemaAsync(cancellationToken);
        return created;
    }

    public async Task ValidateSchemaAsync(CancellationToken cancellationToken = default)
    {
        // Read all mapped columns without changing schema or user data.
        try
        {
            await context.Users.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Chats.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.ChatMembers.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Messages.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Contacts.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "NovaChat could not read its MariaDB schema. Check the connection and permissions. " +
                "For a new database use --initialize-database; for an existing database follow " +
                "docs/MARIADB.md. Startup does not alter tables or import existing data.", exception);
        }
    }
}
