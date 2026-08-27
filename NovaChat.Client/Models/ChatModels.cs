namespace NovaChat.Client.Models;

public class ChatModel
{
    public int Id { get; set; }

    public string User1Id { get; set; } = string.Empty;

    public string User2Id { get; set; } = string.Empty;

    public string User1Name { get; set; } = string.Empty;

    public string User2Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string OtherUserId(string currentUserId)
    {
        return string.Equals(
            User1Id,
            currentUserId,
            StringComparison.OrdinalIgnoreCase)
            ? User2Id
            : User1Id;
    }

    public string OtherUserName(string currentUserId)
    {
        return string.Equals(
            User1Id,
            currentUserId,
            StringComparison.OrdinalIgnoreCase)
            ? User2Name
            : User1Name;
    }
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

public class CreateChatRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class CreateChatResponse
{
    public string Message { get; set; } = string.Empty;

    public ChatModel? Chat { get; set; }
}