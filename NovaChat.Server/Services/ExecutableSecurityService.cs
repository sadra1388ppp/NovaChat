using System.Security.Cryptography;

namespace NovaChat.Server.Services;

public sealed class ExecutableSecurityService
{
    private readonly HashSet<string> _allowedHashes;

    public ExecutableSecurityService(IConfiguration configuration)
    {
        _allowedHashes = configuration
            .GetSection("Security:AllowedExecutableSha256")
            .Get<string[]>()?
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Select(hash => hash.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    public async Task<ExecutableSecurityResult> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Log($"Starting validation: {filePath}");

        if (!OperatingSystem.IsWindows())
            return Reject("EXE upload validation is only supported on Windows.");

        if (!File.Exists(filePath))
            return Reject("The uploaded executable could not be found.");

        if (!string.Equals(Path.GetExtension(filePath), ".exe", StringComparison.OrdinalIgnoreCase))
            return Reject("Only EXE files can be checked by this validator.");

        var info = new FileInfo(filePath);
        Log($"Size: {info.Length} bytes");

        var hash = await ComputeSha256Async(filePath, cancellationToken);
        Log($"SHA256: {hash}");

        if (_allowedHashes.Count == 0)
        {
            const string reason = "No trusted EXE SHA-256 hashes are configured on the server.";
            Log($"REJECTED: {reason}");
            return Reject(reason, hash);
        }

        if (!_allowedHashes.Contains(hash))
        {
            var reason = "The EXE SHA-256 hash is not trusted by the server.";
            Log($"REJECTED: {reason}");
            return Reject(reason, hash);
        }

        Log("ACCEPTED: trusted EXE SHA-256 hash.");
        return ExecutableSecurityResult.Accepted(hash);
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void Log(string message) =>
        Console.WriteLine($"[EXE SECURITY] {message}");

    private static ExecutableSecurityResult Reject(string reason, string? sha256 = null) =>
        ExecutableSecurityResult.Rejected(reason, sha256);

    public sealed record ExecutableSecurityResult(
        bool Allowed,
        string Reason,
        string Sha256,
        string? Publisher = null,
        string? Issuer = null,
        string? SimpleName = null)
    {
        public static ExecutableSecurityResult Accepted(string sha256) =>
            new(
                true,
                "Trusted EXE SHA-256 hash.",
                sha256);

        public static ExecutableSecurityResult Rejected(
            string reason,
            string? sha256 = null) =>
            new(
                false,
                reason,
                sha256 ?? string.Empty);
    }
}
