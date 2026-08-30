namespace NovaChat.Client.Models;

public class ChatModel
{
    public int Id { get; set; }
    public string User1Id { get; set; } = string.Empty;
    public string User2Id { get; set; } = string.Empty;
    public string User1Name { get; set; } = string.Empty;
    public string User2Name { get; set; } = string.Empty;
    public string? User1AvatarUrl { get; set; }
    public string? User2AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public MessageModel? LastMessage { get; set; }

    public string OtherUserId(string currentUserId) =>
        string.Equals(User1Id, currentUserId, StringComparison.OrdinalIgnoreCase) ? User2Id : User1Id;

    public string OtherUserName(string currentUserId) =>
        string.Equals(User1Id, currentUserId, StringComparison.OrdinalIgnoreCase) ? User2Name : User1Name;

    public string? OtherUserAvatarUrl(string currentUserId) =>
        string.Equals(User1Id, currentUserId, StringComparison.OrdinalIgnoreCase) ? User2AvatarUrl : User1AvatarUrl;
}

public class MessageModel
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}

public class ChatHistoryResponse
{
    public List<MessageModel> Messages { get; set; } = [];
    public bool HasMore { get; set; }
    public int? NextBeforeMessageId { get; set; }
}

public class CreateChatRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class CreateChatResponse
{
    public string Message { get; set; } = string.Empty;
    public ChatModel? Chat { get; set; }
}