using NovaChat.Server.Data;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public sealed class AuditLogService(AuditDbContext db)
{
    private readonly AuditDbContext _db = db;

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
        var row = new AuditLog
        {
            UserId = userId,
            Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "System" : category.Trim(),
            EventType = string.IsNullOrWhiteSpace(eventType) ? "Unknown" : eventType.Trim(),
            TargetType = string.IsNullOrWhiteSpace(targetType) ? null : targetType.Trim(),
            TargetId = string.IsNullOrWhiteSpace(targetId) ? null : targetId.Trim(),
            ChatId = chatId,
            MessageId = messageId,
            DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim(),
            RequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim(),
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim(),
            Succeeded = succeeded,
            Details = details,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
