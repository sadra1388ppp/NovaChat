using System;
using System.Collections.Generic;

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
}
