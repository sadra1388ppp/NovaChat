namespace NovaChat.Server.Entities;

public class Contact
{
    public int Id { get; set; }
    public long OwnerUserId { get; set; }
    public long ContactUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User OwnerUser { get; set; } = null!;
    public User ContactUser { get; set; } = null!;
}
