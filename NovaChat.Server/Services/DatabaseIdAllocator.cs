using System.Data.Common;

namespace NovaChat.Server.Services;

public static class DatabaseIdAllocator
{
    public static async Task<IAsyncDisposable> AcquireTableLockAsync(
        DbConnection connection, string tableName, CancellationToken cancellationToken = default)
    {
        ValidateTable(tableName);
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        var lockName = "NovaChat.IdAllocator." + tableName;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@lockName, 15);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@lockName";
        parameter.Value = lockName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt32(result) != 1)
        {
            if (wasClosed)
                await connection.CloseAsync();
            throw new TimeoutException("Could not acquire the ID allocation lock for " + tableName + ".");
        }

        return new TableLockHandle(connection, lockName, wasClosed);
    }

    public static async Task<long> GetFirstAvailableIdAsync(
        DbConnection connection, string tableName, CancellationToken cancellationToken = default)
    {
        ValidateTable(tableName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CASE " +
            "WHEN NOT EXISTS (SELECT 1 FROM `" + tableName + "` WHERE `Id` = 1) THEN 1 " +
            "ELSE COALESCE((SELECT t.`Id` + 1 FROM `" + tableName + "` t " +
            "WHERE t.`Id` >= 1 AND NOT EXISTS (SELECT 1 FROM `" + tableName + "` nextRow " +
            "WHERE nextRow.`Id` = t.`Id` + 1) ORDER BY t.`Id` LIMIT 1), 1) END;";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public static async Task<long> GetFirstAvailableIdAsync(
        DbConnection connection, string tableName, ISet<long> reservedIds, CancellationToken cancellationToken = default)
    {
        var candidate = await GetFirstAvailableIdAsync(connection, tableName, cancellationToken);
        while (reservedIds.Contains(candidate) || await ExistsAsync(connection, tableName, candidate, cancellationToken))
            candidate++;
        reservedIds.Add(candidate);
        return candidate;
    }

    private static async Task<bool> ExistsAsync(
        DbConnection connection, string tableName, long id, CancellationToken cancellationToken)
    {
        ValidateTable(tableName);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM `" + tableName + "` WHERE `Id` = @id);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void ValidateTable(string tableName)
    {
        switch (tableName)
        {
            case "Users":
            case "Chats":
            case "Contacts":
            case "Messages":
            case "HttpRequests":
            case "ChatRequests":
            case "GroupAddRequests":
                return;
            default:
                throw new ArgumentException("Unsupported ID allocation table: " + tableName, nameof(tableName));
        }
    }

    private sealed class TableLockHandle(DbConnection connection, string lockName, bool closeConnectionWhenDisposed) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT RELEASE_LOCK(@lockName);";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@lockName";
                parameter.Value = lockName;
                command.Parameters.Add(parameter);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                if (closeConnectionWhenDisposed)
                    await connection.CloseAsync();
            }
        }
    }
}