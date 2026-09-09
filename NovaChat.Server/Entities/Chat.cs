namespace NovaChat.Server.Entities;

public enum ChatType
{
    Private = 0,
    Group = 1
}

public partial class Chat
{
    public int Id { get; set; }

    public int Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public long? CreatedByUserId { get; set; }

    public virtual User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
