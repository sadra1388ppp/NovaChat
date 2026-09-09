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

        var connection = new MySqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.Database))
            throw new InvalidOperationException("The MariaDB connection string must specify Database.");

        // MariaDB DATETIME has no timezone. Every date in NovaChat is stored as UTC.
        connection.DateTimeKind = MySqlDateTimeKind.Utc;
        var versionText = configuration["Database:ServerVersion"] ?? "11.8.0";
        if (!Version.TryParse(versionText, out var version))
            throw new InvalidOperationException("Database:ServerVersion must be a version such as 11.8.0.");

        // An explicit version avoids a network request while building the host.
        return options.UseMySql(
            connection.ConnectionString,
            new MariaDbServerVersion(version),
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            });
    }
}
