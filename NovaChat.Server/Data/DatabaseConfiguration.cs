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

        // The development database is a local MariaDB instance. Some local MariaDB
        // installations advertise SSL but close the TLS handshake unexpectedly, which
        // produces SocketException 10054 before the application can query the database.
        // Disable SSL only for loopback hosts; remote deployments keep their configured
        // SSL mode unchanged.
        if (connection.Server is "localhost" or "127.0.0.1" or "::1")
            connection.SslMode = MySqlSslMode.None;

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
