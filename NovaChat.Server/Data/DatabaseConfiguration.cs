using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace NovaChat.Server.Data;

public static class DatabaseConfiguration
{
    public static DbContextOptionsBuilder UseNovaChatDatabase(
        this DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Set ConnectionStrings:DefaultConnection to a MariaDB connection string.");

        var connection = new MySqlConnectionStringBuilder(connectionString)
        {
            // Startup schema validation must fail quickly instead of leaving `dotnet run`
            // apparently frozen for a long time when MariaDB is unavailable.
            ConnectionTimeout = 5
        };
        if (string.IsNullOrWhiteSpace(connection.Database))
            throw new InvalidOperationException("The MariaDB connection string must specify Database.");

        // MariaDB DATETIME has no timezone. Every date in NovaChat is stored as UTC.
        connection.DateTimeKind = MySqlDateTimeKind.Utc;
        var versionText = configuration["Database:ServerVersion"] ?? "11.8.0";
        if (!Version.TryParse(versionText, out var version))
            throw new InvalidOperationException("Database:ServerVersion must be a version such as 11.8.0.");

        // One short retry keeps transient startup hiccups recoverable without allowing
        // several long waits to accumulate before the first HTTP listener starts.
        return options.UseMySql(
            connection.ConnectionString,
            new MariaDbServerVersion(version),
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 1,
                    maxRetryDelay: TimeSpan.FromSeconds(2),
                    errorNumbersToAdd: null);
            });
    }
}
