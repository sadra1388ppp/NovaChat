using System.Text.Json.Serialization;

namespace NovaChat.Client.Models;

public class ProfileModel
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Bio { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsOnline { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? NewUsername { get; set; }

    // Backward-compatible alias for the existing profile UI. It maps to Username and is not serialized.
    [JsonIgnore]
    public string? NewUserId
    {
        get => NewUsername;
        set => NewUsername = value;
    }
}

public class ProfileActionResponse
{
    public string Message { get; set; } = string.Empty;
    public ProfileModel? User { get; set; }
}