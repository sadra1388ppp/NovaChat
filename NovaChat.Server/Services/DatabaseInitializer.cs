using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Services;

public sealed class DatabaseInitializer(AppDbContext context, MessageReadService messageReadService)
{
    public async Task<bool> InitializeEmptyDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var created = await context.Database.EnsureCreatedAsync(cancellationToken);
        await messageReadService.EnsureSchemaAsync(cancellationToken);
        await ValidateSchemaAsync(cancellationToken);
        return created;
    }

    public async Task ValidateSchemaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.Users.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Chats.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Messages.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Contacts.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await messageReadService.EnsureSchemaAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "NovaChat could not read its MariaDB schema. Check the connection and permissions. " +
                "For a new database use --initialize-database; for an existing database follow " +
                "docs/MARIADB.md. Startup only adds the MessageReads table when it is missing and never imports existing data.", exception);
        }
    }
}
