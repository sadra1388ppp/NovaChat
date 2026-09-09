using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace NovaChat.Server.Entities;

public partial class Chat
{
    public int Id { get; set; }
    public int Type { get; set; }
    public string Name { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();
    public virtual User? CreatedByUser { get; set; }
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    // Compatibility-only projections. They are NOT database columns.
    [NotMapped]
    public long? User1Id { get; set; }
    [NotMapped]
    public long? User2Id { get; set; }
    [NotMapped]
    public User? User1 { get; set; }
    [NotMapped]
    public User? User2 { get; set; }
}
