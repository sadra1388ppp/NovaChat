using NovaChat.Server.Services;

namespace NovaChat.Server.DTOs;

public class MessageDto
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public string MessageType { get; set; } = "text";
    public string? AttachmentUrl { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSize { get; set; }
    public double? DurationSeconds { get; set; }
}

public class ChatHistoryResponseDto
{
    public List<MessageDto> Messages { get; set; } = [];
    public bool HasMore { get; set; }
    public int? NextBeforeMessageId { get; set; }
}

public class ChatListDto
{
    public int Id { get; set; }
    public string User1Id { get; set; } = string.Empty;
    public string User2Id { get; set; } = string.Empty;
    public string User1Name { get; set; } = string.Empty;
    public string User2Name { get; set; } = string.Empty;
    public string? User1AvatarUrl { get; set; }
    public string? User2AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public MessageDto? LastMessage { get; set; }
}

public static class MessageDtoMapper
{
    public static MessageDto Map(NovaChat.Server.Entities.Message message, string baseUrl)
    {
        var dto = new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            SenderName = message.Sender?.DisplayName ?? string.Empty,
            Content = message.Content,
            SentAt = message.SentAt
        };

        if (MediaMessageEnvelope.TryParse(message.Content, out var media) && media != null)
        {
            dto.MessageType = media.Type;
            dto.FileName = media.FileName;
            dto.ContentType = media.ContentType;
            dto.FileSize = media.Size;
            dto.DurationSeconds = media.DurationSeconds;
            dto.AttachmentUrl = $"{baseUrl.TrimEnd('/')}/api/ChatMedia/{message.Id}";
            dto.Content = media.Type switch
            {
                "image" => $"📷 {media.FileName}",
                "voice" => "🎙 Voice message",
                _ => $"📎 {media.FileName}"
            };
        }
        return dto;
    }
}
