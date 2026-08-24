namespace NovaChat.Server.Entities;

public class Chat
{
    public int Id { get; set; }

    public string User1Id { get; set; } = string.Empty;
    public User User1 { get; set; } = null!;

    public string User2Id { get; set; } = string.Empty;
    public User User2 { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}