using NovaChat.Server.Data;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public sealed class AuditLogService(AppDbContext context)
{
    public async Task LogAsync(
        string category,
        string eventType,
        long? userId = null,
        string? username = null,
        string? targetType = null,
        string? targetId = null,
        int? chatId = null,
        int? messageId = null,
        string? deviceId = null,
        string? requestId = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool succeeded = true,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditLog
        {
            UserId = userId,
            Username = Normalize(username, 32),
            Category = NormalizeRequired(category, 32),
            EventType = NormalizeRequired(eventType, 64),
            TargetType = Normalize(targetType, 32),
            TargetId = Normalize(targetId, 128),
            ChatId = chatId,
            MessageId = messageId,
            DeviceId = Normalize(deviceId, 128),
            RequestId = Normalize(requestId, 64),
            IpAddress = Normalize(ipAddress, 45),
            UserAgent = Normalize(userAgent, 512),
            Succeeded = succeeded,
            Details = Normalize(details, 8000),
            CreatedAt = DateTime.UtcNow
        };

        context.AuditLogs.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeRequired(string value, int maxLength)
        => Normalize(value, maxLength) ?? throw new ArgumentException("Audit log value is required.", nameof(value));

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
