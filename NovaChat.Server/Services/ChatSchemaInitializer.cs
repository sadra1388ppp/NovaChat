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
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);

        async Task<bool> ColumnExistsAsync(string table, string column)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column";
            var p1 = cmd.CreateParameter(); p1.ParameterName = "@table"; p1.Value = table; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@column"; p2.Value = column; cmd.Parameters.Add(p2);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
        }

        async Task ExecuteAsync(string sql)
        {
            await using var cmd = connection.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await ColumnExistsAsync("Chats", "Type")) await ExecuteAsync("ALTER TABLE Chats ADD COLUMN Type INT NOT NULL DEFAULT 0");
        if (!await ColumnExistsAsync("Chats", "Name")) await ExecuteAsync("ALTER TABLE Chats ADD COLUMN Name VARCHAR(128) NOT NULL DEFAULT ''");
        if (!await ColumnExistsAsync("Chats", "CreatedByUserId")) await ExecuteAsync("ALTER TABLE Chats ADD COLUMN CreatedByUserId VARCHAR(255) NULL");

        await ExecuteAsync("ALTER TABLE Chats MODIFY COLUMN User1Id VARCHAR(255) NULL");
        await ExecuteAsync("ALTER TABLE Chats MODIFY COLUMN User2Id VARCHAR(255) NULL");

        await ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ChatMembers (
            Id INT NOT NULL AUTO_INCREMENT,
            ChatId INT NOT NULL,
            UserId VARCHAR(255) NOT NULL,
            Role INT NOT NULL DEFAULT 0,
            JoinedAt DATETIME(6) NOT NULL,
            PRIMARY KEY (Id),
            UNIQUE KEY IX_ChatMembers_ChatId_UserId (ChatId, UserId),
            INDEX IX_ChatMembers_UserId (UserId)
        ) ENGINE=InnoDB");

        await ExecuteAsync(@"UPDATE Chats SET Type = 0, Name = '' WHERE Type IS NULL");
        await ExecuteAsync(@"UPDATE Chats SET CreatedByUserId = User1Id WHERE CreatedByUserId IS NULL AND User1Id IS NOT NULL");
        await ExecuteAsync(@"INSERT INTO ChatMembers (ChatId, UserId, Role, JoinedAt)
            SELECT c.Id, c.User1Id, 2, c.CreatedAt FROM Chats c
            WHERE c.User1Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ChatMembers m WHERE m.ChatId = c.Id AND m.UserId = c.User1Id)");
        await ExecuteAsync(@"INSERT INTO ChatMembers (ChatId, UserId, Role, JoinedAt)
            SELECT c.Id, c.User2Id, 0, c.CreatedAt FROM Chats c
            WHERE c.User2Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ChatMembers m WHERE m.ChatId = c.Id AND m.UserId = c.User2Id)");
    }
}
