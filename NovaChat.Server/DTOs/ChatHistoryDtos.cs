namespace NovaChat.Server.DTOs;

public class MessageDto
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    public string SenderId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }
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

    public DateTime CreatedAt { get; set; }

    public MessageDto? LastMessage { get; set; }
}