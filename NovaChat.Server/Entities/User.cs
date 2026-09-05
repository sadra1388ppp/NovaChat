namespace NovaChat.Server.Entities;

public class User
{
    // Internal database identifier. Never used as a login credential.
    public string Id { get; set; } = string.Empty;

    // Public username used for login and user discovery.
    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}