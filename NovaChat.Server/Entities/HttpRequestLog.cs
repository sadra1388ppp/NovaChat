namespace NovaChat.Server.Entities;

public sealed class HttpRequestLog
{
    public long Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool IsAuthenticated { get; set; }
    public long? UserId { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestContentType { get; set; }
    public long? RequestContentLength { get; set; }
    public string? ResponseContentType { get; set; }
    public long? ResponseContentLength { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public bool Succeeded { get; set; }
    public string? ExceptionType { get; set; }
}
