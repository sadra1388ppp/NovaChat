using System;
using System.Collections.Generic;

namespace NovaChat.Server.Entities;

public partial class Message
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    // Kept as the immutable relational/audit identifier for the sender.
    public long SenderId { get; set; }

    // Human-readable sender username stored with the message for audit/history display.
    public string SenderUsername { get; set; } = string.Empty;

    public string Content { get; set; } = null!;

    public DateTime SentAt { get; set; }

    public bool DeletedForEveryone { get; set; }

    public string DeletedForUserIds { get; set; } = null!;

    public virtual Chat Chat { get; set; } = null!;

    public virtual User Sender { get; set; } = null!;
}
