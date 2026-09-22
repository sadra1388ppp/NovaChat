namespace NovaChat.Server.Models.Elasticsearch;

public sealed class HttpRequestLogSearchDocument
{
    public long Id { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Scheme { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string? QueryString { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public bool IsAuthenticated { get; init; }
    public long? UserId { get; init; }
    public string? Username { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? RequestContentType { get; init; }
    public long? RequestContentLength { get; init; }
    public string? ResponseContentType { get; init; }
    public long? ResponseContentLength { get; init; }
    public string? ResponseBody { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
    public long DurationMs { get; init; }
    public bool Succeeded { get; init; }
}
