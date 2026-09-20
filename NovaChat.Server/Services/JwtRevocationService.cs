using System.Collections.Concurrent;

namespace NovaChat.Server.Services;

public sealed class JwtRevocationService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new(StringComparer.Ordinal);

    public void Revoke(string jti, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(jti) || expiresAt <= DateTimeOffset.UtcNow)
            return;

        CleanupExpired();
        _revokedTokens[jti] = expiresAt;
    }

    public bool IsRevoked(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return false;

        if (!_revokedTokens.TryGetValue(jti, out var expiresAt))
            return false;

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            _revokedTokens.TryRemove(jti, out _);
            return false;
        }

        return true;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _revokedTokens)
        {
            if (pair.Value <= now)
                _revokedTokens.TryRemove(pair.Key, out _);
        }
    }
}
