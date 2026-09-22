using System.Text.Json.Serialization;

namespace NovaChat.Server.Models.Elasticsearch;

/// <summary>
/// Metadata for a NovaChat message stored in Elasticsearch.
/// Message content is intentionally excluded because NovaChat requires E2EE.
/// </summary>
public sealed class MessageSearchDocument
{
    [JsonPropertyName("messageId")]
    public int MessageId { get; init; }

    public int ChatId { get; init; }

    public string SenderId { get; init; } = string.Empty;

    public DateTime SentAt { get; init; }
}
