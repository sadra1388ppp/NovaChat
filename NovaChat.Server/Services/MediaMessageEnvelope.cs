using System.Text.Json;

namespace NovaChat.Server.Services;

public sealed class MediaMessageEnvelope
{
    public const string Prefix = "__NOVACHAT_MEDIA__";

    public string Type { get; set; } = "file";
    public string StorageName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public double? DurationSeconds { get; set; }

    public string Serialize() => Prefix + JsonSerializer.Serialize(this);

    public static bool TryParse(string? content, out MediaMessageEnvelope? media)
    {
        media = null;
        if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            media = JsonSerializer.Deserialize<MediaMessageEnvelope>(content[Prefix.Length..]);
            return media != null && !string.IsNullOrWhiteSpace(media.StorageName) && !string.IsNullOrWhiteSpace(media.FileName);
        }
        catch { return false; }
    }
}
