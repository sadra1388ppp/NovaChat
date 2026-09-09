using System;
using System.Collections.Generic;

namespace NovaChat.Server.Entities;

public partial class Message
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    public long SenderId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime SentAt { get; set; }

    public bool DeletedForEveryone { get; set; }

    public string DeletedForUserIds { get; set; } = null!;

    public virtual Chat Chat { get; set; } = null!;

    public virtual User Sender { get; set; } = null!;
}
