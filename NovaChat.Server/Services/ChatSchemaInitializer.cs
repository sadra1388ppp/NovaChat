using System.Data;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Services;

public sealed class ChatSchemaInitializer
{
    private readonly AppDbContext _db;

    public ChatSchemaInitializer(AppDbContext db) => _db = db;

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        async Task<bool> TableExistsAsync(string table)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @table";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = table;
            cmd.Parameters.Add(parameter);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
        }

        async Task<bool> ColumnExistsAsync(string table, string column)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column";
            var tableParameter = cmd.CreateParameter();
            tableParameter.ParameterName = "@table";
            tableParameter.Value = table;
            cmd.Parameters.Add(tableParameter);
            var columnParameter = cmd.CreateParameter();
            columnParameter.ParameterName = "@column";
            columnParameter.Value = column;
            cmd.Parameters.Add(columnParameter);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
        }

        async Task ExecuteAsync(string sql)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        async Task DropForeignKeysForColumnAsync(string table, string column)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT CONSTRAINT_NAME
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @table
                  AND COLUMN_NAME = @column
                  AND REFERENCED_TABLE_NAME IS NOT NULL";

            var tableParameter = cmd.CreateParameter();
            tableParameter.ParameterName = "@table";
            tableParameter.Value = table;
            cmd.Parameters.Add(tableParameter);

            var constraints = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                constraints.Add(reader.GetString(0));

            foreach (var constraint in constraints)
            {
                var safeName = constraint.Replace("`", "``", StringComparison.Ordinal);
                await ExecuteAsync($"ALTER TABLE `{table}` DROP FOREIGN KEY `{safeName}`");
            }
        }

        if (!await TableExistsAsync("Chats"))
            throw new InvalidOperationException("NovaChat requires the Chats table to exist before chat schema initialization.");

        if (!await ColumnExistsAsync("Chats", "Type"))
            await ExecuteAsync("ALTER TABLE `Chats` ADD COLUMN `Type` INT NOT NULL DEFAULT 0");

        if (!await ColumnExistsAsync("Chats", "Name"))
            await ExecuteAsync("ALTER TABLE `Chats` ADD COLUMN `Name` VARCHAR(128) NOT NULL DEFAULT ''");

        if (!await ColumnExistsAsync("Chats", "AvatarUrl"))
            await ExecuteAsync("ALTER TABLE `Chats` ADD COLUMN `AvatarUrl` VARCHAR(512) NULL");

        if (!await ColumnExistsAsync("Chats", "CreatedByUserId"))
            await ExecuteAsync("ALTER TABLE `Chats` ADD COLUMN `CreatedByUserId` VARCHAR(255) NULL");

        if (!await ColumnExistsAsync("Chats", "CreatedAt"))
            await ExecuteAsync("ALTER TABLE `Chats` ADD COLUMN `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)");

        await ExecuteAsync(@"CREATE TABLE IF NOT EXISTS `ChatMembers` (
            `Id` INT NOT NULL AUTO_INCREMENT,
            `ChatId` INT NOT NULL,
            `UserId` VARCHAR(255) NOT NULL,
            `Role` INT NOT NULL DEFAULT 0,
            `JoinedAt` DATETIME(6) NOT NULL,
            PRIMARY KEY (`Id`),
            UNIQUE KEY `IX_ChatMembers_ChatId_UserId` (`ChatId`, `UserId`),
            INDEX `IX_ChatMembers_UserId` (`UserId`),
            CONSTRAINT `FK_ChatMembers_Chats_ChatId`
                FOREIGN KEY (`ChatId`) REFERENCES `Chats` (`Id`) ON DELETE CASCADE,
            CONSTRAINT `FK_ChatMembers_Users_UserId`
                FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
        ) ENGINE=InnoDB");

        await ExecuteAsync("UPDATE `Chats` SET `Type` = 0 WHERE `Type` IS NULL");
        await ExecuteAsync("UPDATE `Chats` SET `Name` = '' WHERE `Name` IS NULL");

        if (await ColumnExistsAsync("Chats", "User1Id"))
        {
            await ExecuteAsync("ALTER TABLE `Chats` MODIFY COLUMN `User1Id` VARCHAR(255) NULL");
            await ExecuteAsync(@"UPDATE `Chats`
                SET `CreatedByUserId` = `User1Id`
                WHERE `CreatedByUserId` IS NULL AND `User1Id` IS NOT NULL");

            await ExecuteAsync(@"INSERT INTO `ChatMembers` (`ChatId`, `UserId`, `Role`, `JoinedAt`)
                SELECT c.`Id`, c.`User1Id`, 2, c.`CreatedAt`
                FROM `Chats` c
                WHERE c.`User1Id` IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM `ChatMembers` m
                      WHERE m.`ChatId` = c.`Id` AND m.`UserId` = c.`User1Id`
                  )");
        }

        if (await ColumnExistsAsync("Chats", "User2Id"))
        {
            await ExecuteAsync("ALTER TABLE `Chats` MODIFY COLUMN `User2Id` VARCHAR(255) NULL");

            await ExecuteAsync(@"INSERT INTO `ChatMembers` (`ChatId`, `UserId`, `Role`, `JoinedAt`)
                SELECT c.`Id`, c.`User2Id`, 0, c.`CreatedAt`
                FROM `Chats` c
                WHERE c.`User2Id` IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM `ChatMembers` m
                      WHERE m.`ChatId` = c.`Id` AND m.`UserId` = c.`User2Id`
                  )");
        }

        // The old direct participant columns are no longer part of the Chat model.
        // Drop their foreign keys first, then remove the columns after membership data
        // has been copied into ChatMembers.
        if (await ColumnExistsAsync("Chats", "User1Id"))
        {
            await DropForeignKeysForColumnAsync("Chats", "User1Id");
            await ExecuteAsync("ALTER TABLE `Chats` DROP COLUMN `User1Id`");
        }

        if (await ColumnExistsAsync("Chats", "User2Id"))
        {
            await DropForeignKeysForColumnAsync("Chats", "User2Id");
            await ExecuteAsync("ALTER TABLE `Chats` DROP COLUMN `User2Id`");
        }
    }
}
