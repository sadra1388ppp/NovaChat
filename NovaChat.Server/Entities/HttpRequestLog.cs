namespace NovaChat.Server.Entities;

public sealed class HttpRequestLog
{
    public long Id { get; set; }
    public string Request { get; set; } = string.Empty;
}
