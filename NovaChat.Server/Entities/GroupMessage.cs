namespace NovaChat.Server.Entities;

public class GroupMessage
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public string SenderId { get; set; } = string.Empty;
    public User Sender { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}