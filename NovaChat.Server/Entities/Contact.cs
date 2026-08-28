namespace NovaChat.Server.Entities;

public class Contact
{
    public int Id { get; set; }

    public string OwnerUserId { get; set; } = string.Empty;

    public string ContactUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User OwnerUser { get; set; } = null!;

    public User ContactUser { get; set; } = null!;
}
