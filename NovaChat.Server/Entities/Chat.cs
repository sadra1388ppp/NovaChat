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

    // Compatibility-only in-memory projections. ChatMembers is no longer stored in MariaDB.
    [NotMapped]
    public ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    [NotMapped]
    public long? User1Id { get; set; }

    [NotMapped]
    public long? User2Id { get; set; }

    [NotMapped]
    public User? User1 { get; set; }

    [NotMapped]
    public User? User2 { get; set; }

    [NotMapped]
    public ICollection<ChatMember> Members => ChatMembers;
}
