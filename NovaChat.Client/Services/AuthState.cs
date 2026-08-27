namespace NovaChat.Client.Services;

public static class AuthState
{
    public static string Token { get; private set; } = string.Empty;

    public static string UserId { get; private set; } = string.Empty;

    public static string DisplayName { get; private set; } = string.Empty;

    public static string Email { get; private set; } = string.Empty;

    public static bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(Token);

    public static void Set(
        string token,
        string userId,
        string displayName,
        string email)
    {
        Token = token;
        UserId = userId;
        DisplayName = displayName;
        Email = email;
    }

    public static void Clear()
    {
        Token = string.Empty;
        UserId = string.Empty;
        DisplayName = string.Empty;
        Email = string.Empty;
    }
}