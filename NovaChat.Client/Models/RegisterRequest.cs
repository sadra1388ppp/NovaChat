using System.Text.Json.Serialization;

namespace NovaChat.Client.Models;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;

    // Backward-compatible alias for the existing registration UI. It is not sent over the wire.
    [JsonIgnore]
    public string Id
    {
        get => Username;
        set => Username = value;
    }

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}