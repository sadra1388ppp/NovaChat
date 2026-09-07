namespace NovaChat.Server.Entities;

public enum ChatType
{
    Private = 0,
    Group = 1
}

public class Chat
{
    public int Id { get; set; }
    public ChatType Type { get; set; } = ChatType.Private;
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public long? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public long? User1Id { get; set; }
    public User? User1 { get; set; }
    public long? User2Id { get; set; }
    public User? User2 { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ChatMember> Members { get; set; } = new List<ChatMember>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
