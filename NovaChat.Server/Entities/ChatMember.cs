namespace NovaChat.Server.Entities;

public enum ChatMemberRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}

public class ChatMember
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public Chat Chat { get; set; } = null!;
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public ChatMemberRole Role { get; set; } = ChatMemberRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
