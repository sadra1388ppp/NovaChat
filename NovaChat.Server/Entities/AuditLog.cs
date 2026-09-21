using System;

namespace NovaChat.Server.Entities;

public sealed class AuditLog
{
    public long Id { get; set; }

    public long? UserId { get; set; }

    public string? Username { get; set; }

    public string Category { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    public int? ChatId { get; set; }

    public int? MessageId { get; set; }

    public string? DeviceId { get; set; }

    public string? RequestId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public bool Succeeded { get; set; } = true;

    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }
}
