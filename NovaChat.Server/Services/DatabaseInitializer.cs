using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Services;

public sealed class DatabaseInitializer(AppDbContext context, MessageReadService messageReadService, E2eeDeviceService e2eeDeviceService, ChatRequestService chatRequestService)
{
    public async Task<bool> InitializeEmptyDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var created = await context.Database.EnsureCreatedAsync(cancellationToken);
        await messageReadService.EnsureSchemaAsync(cancellationToken);
        await e2eeDeviceService.EnsureSchemaAsync(cancellationToken);
        await chatRequestService.EnsureSchemaAsync(cancellationToken);
        await ValidateSchemaAsync(cancellationToken);
        return created;
    }

    private async Task EnsureAuditLogSchemaAsync(CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id BIGINT NOT NULL AUTO_INCREMENT,
                UserId BIGINT NULL,
                Username VARCHAR(32) NULL,
                Category VARCHAR(32) NOT NULL,
                EventType VARCHAR(64) NOT NULL,
                TargetType VARCHAR(32) NULL,
                TargetId VARCHAR(128) NULL,
                ChatId INT NULL,
                MessageId INT NULL,
                DeviceId VARCHAR(128) NULL,
                RequestId VARCHAR(64) NULL,
                IpAddress VARCHAR(45) NULL,
                UserAgent VARCHAR(512) NULL,
                Succeeded TINYINT(1) NOT NULL DEFAULT 1,
                Details TEXT NULL,
                CreatedAt DATETIME(6) NOT NULL,
                PRIMARY KEY (Id),
                INDEX IX_AuditLogs_UserId (UserId),
                INDEX IX_AuditLogs_EventType (EventType),
                INDEX IX_AuditLogs_ChatId (ChatId),
                INDEX IX_AuditLogs_MessageId (MessageId),
                INDEX IX_AuditLogs_CreatedAt (CreatedAt)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
            """,
            cancellationToken);
    }

    private async Task EnsureMessageEditSchemaAsync(CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Messages ADD COLUMN IF NOT EXISTS EditedAt DATETIME(6) NULL;",
            cancellationToken);
    }

    public async Task ValidateSchemaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await chatRequestService.EnsureSchemaAsync(cancellationToken);
            await EnsureAuditLogSchemaAsync(cancellationToken);
            await context.Users.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Chats.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await EnsureMessageEditSchemaAsync(cancellationToken);
            await context.Messages.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await context.Contacts.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await messageReadService.EnsureSchemaAsync(cancellationToken);
            await e2eeDeviceService.EnsureSchemaAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "NovaChat could not read its MariaDB schema. Check the connection and permissions. " +
                "For a new database use --initialize-database; for an existing database follow docs/MARIADB.md. " +
                "Startup only adds the MessageReads, EncryptionDevices and ChatRequests schema when missing and never imports existing data.", exception);
        }
    }
}
