using System.ComponentModel.DataAnnotations.Schema;

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

    public string Members { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public long? CreatedByUserId { get; set; }

    public virtual User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    // Compatibility projections for existing private-chat UI/API code.
    // Private chats are persisted through the Members username list.
    [NotMapped]
    public long? User1Id { get; set; }

    [NotMapped]
    public long? User2Id { get; set; }

    [NotMapped]
    public User? User1 { get; set; }

    [NotMapped]
    public User? User2 { get; set; }
}
