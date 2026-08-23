namespace NovaChat.Server.DTOs;

public class RegisterResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public object? User { get; set; }
}