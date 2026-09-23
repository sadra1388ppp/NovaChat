using System.Collections.Frozen;

namespace NovaChat.Server.Services;

public sealed class UploadStorageService
{
    private static readonly FrozenSet<string> DangerousExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ade", ".adp", ".apk", ".app", ".appx", ".bat", ".bin", ".cab", ".cmd", ".com",
        ".cpl", ".dll", ".dmg", ".exe", ".hta", ".inf", ".ins", ".iso", ".jar", ".js",
        ".jse", ".lnk", ".msi", ".msp", ".mst", ".ocx", ".ps1", ".psd1", ".psm1",
        ".reg", ".scr", ".sys", ".vb", ".vbe", ".vbs", ".vhd", ".vhdx", ".wsc", ".wsf",
        ".wsh"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public UploadStorageService(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public string RootPath
    {
        get
        {
            var configured = _configuration["NovaChat:UploadRoot"];
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(configured);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = _environment.ContentRootPath;

            return Path.Combine(localAppData, "NovaChat", "uploads");
        }
    }

    public string LegacyRootPath
    {
        get
        {
            var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            return Path.Combine(webRoot, "uploads");
        }
    }

    public string GetWriteDirectory(string category)
    {
        ValidateCategory(category);
        var path = Path.Combine(RootPath, category);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetWritePath(string category, string storageName)
    {
        ValidateCategory(category);

        var safeName = Path.GetFileName(storageName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            !string.Equals(safeName, storageName, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage file name.");

        return Path.Combine(GetWriteDirectory(category), safeName);
    }

    public string? FindExistingPath(string category, string storageName)
    {
        ValidateCategory(category);

        var safeName = Path.GetFileName(storageName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            !string.Equals(safeName, storageName, StringComparison.Ordinal))
            return null;

        var newPath = Path.Combine(RootPath, category, safeName);
        if (File.Exists(newPath))
            return newPath;

        var legacyPath = Path.Combine(LegacyRootPath, category, safeName);
        return File.Exists(legacyPath) ? legacyPath : null;
    }

    public string? FindExistingChatPath(string storageName)
    {
        if (string.IsNullOrWhiteSpace(storageName))
            return null;

        var normalized = storageName.Replace('\\', '/').TrimStart('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
            return null;

        var category = parts[0];
        var safeName = parts[1];

        if (!string.Equals(category, Path.GetFileName(category), StringComparison.Ordinal) ||
            !string.Equals(safeName, Path.GetFileName(safeName), StringComparison.Ordinal))
            return null;

        return FindExistingPath(Path.Combine("chat", category), safeName);
    }

    public static string GetSafeOriginalFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe))
            return "file";

        safe = safe.Replace("\0", string.Empty);
        safe = new string(safe.Where(c => !char.IsControl(c)).ToArray());

        return safe.Length <= 180 ? safe : safe[..180];
    }

    public static bool HasDangerousExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) && DangerousExtensions.Contains(extension);
    }

    public static bool IsSafeUploadedFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (fileName.IndexOfAny(['\\', '/', ':']) >= 0)
            return false;

        return !HasDangerousExtension(fileName);
    }

    private static void ValidateCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Invalid upload category.", nameof(category));

        var normalized = category.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Invalid upload category.", nameof(category));

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment =>
                segment is "." or ".." ||
                segment.IndexOfAny([':', '\\', '\0']) >= 0))
            throw new ArgumentException("Invalid upload category.", nameof(category));
    }
}
