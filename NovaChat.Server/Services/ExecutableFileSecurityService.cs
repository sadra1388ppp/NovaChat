using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NovaChat.Server.Services;

public sealed class ExecutableFileSecurityService
{
    private const int DefaultScanTimeoutSeconds = 120;

    private readonly IConfiguration _configuration;
    private readonly ILogger<ExecutableFileSecurityService> _logger;

    public ExecutableFileSecurityService(
        IConfiguration configuration,
        ILogger<ExecutableFileSecurityService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ExecutableValidationResult> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ExecutableValidationResult.Rejected(
                "EXE upload validation requires a Windows server.");
        }

        if (!File.Exists(filePath))
        {
            return ExecutableValidationResult.Rejected(
                "The uploaded file could not be found.");
        }

        if (!await IsPortableExecutableAsync(filePath, cancellationToken))
        {
            return ExecutableValidationResult.Rejected(
                "The uploaded file is not a valid Windows PE executable.");
        }

        var sha256 = await ComputeSha256Async(
            filePath,
            cancellationToken);

        var scanResult = await ScanWithWindowsDefenderAsync(
            filePath,
            cancellationToken);

        if (!scanResult.IsAccepted)
        {
            return ExecutableValidationResult.Rejected(
                scanResult.Message);
        }

        var signer = TryReadSigner(filePath);

        return ExecutableValidationResult.Accepted(
            sha256,
            signer.Publisher,
            signer.Thumbprint);
    }

    private async Task<DefenderScanResult> ScanWithWindowsDefenderAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var defenderPath = FindMpCmdRun();

        if (defenderPath == null)
        {
            return DefenderScanResult.Rejected(
                "Microsoft Defender command-line scanner (MpCmdRun.exe) was not found on the server.");
        }

        var timeoutSeconds = _configuration.GetValue(
            "FileSecurity:Executable:ScanTimeoutSeconds",
            DefaultScanTimeoutSeconds);

        timeoutSeconds = Math.Clamp(
            timeoutSeconds,
            10,
            900);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = defenderPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.StartInfo.ArgumentList.Add("-Scan");
        process.StartInfo.ArgumentList.Add("-ScanType");
        process.StartInfo.ArgumentList.Add("3");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(filePath);

        try
        {
            if (!process.Start())
            {
                return DefenderScanResult.Rejected(
                    "Windows Defender could not start the EXE security scan.");
            }

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync();

            var stderrTask =
                process.StandardError.ReadToEndAsync();

            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutCts.CancelAfter(
                TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(
                    timeoutCts.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                TryKill(process);

                return DefenderScanResult.Rejected(
                    $"Windows Defender scan timed out after {timeoutSeconds} seconds.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                return DefenderScanResult.Accepted();
            }

            var details = string.IsNullOrWhiteSpace(stderr)
                ? stdout
                : stderr;

            details = NormalizeScanDetails(details);

            return DefenderScanResult.Rejected(
                string.IsNullOrWhiteSpace(details)
                    ? $"Windows Defender rejected the executable scan (exit code {process.ExitCode})."
                    : $"Windows Defender rejected the executable scan (exit code {process.ExitCode}). {details}");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);

            _logger.LogError(
                exception,
                "Windows Defender scan failed for executable {FilePath}.",
                filePath);

            return DefenderScanResult.Rejected(
                "Windows Defender scan failed.");
        }
    }

    private static string? FindMpCmdRun()
    {
        var platformRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows Defender",
            "Platform");

        if (Directory.Exists(platformRoot))
        {
            var platformPath = Directory
                .GetDirectories(platformRoot)
                .OrderByDescending(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                    Path.Combine(path, "MpCmdRun.exe"))
                .FirstOrDefault(File.Exists);

            if (platformPath != null)
                return platformPath;
        }

        var programFilesPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "Windows Defender",
            "MpCmdRun.exe");

        return File.Exists(programFilesPath)
            ? programFilesPath
            : null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string NormalizeScanDetails(
        string details)
    {
        var singleLine = details
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return singleLine.Length > 500
            ? singleLine[..500] + "..."
            : singleLine;
    }

    private static (string? Publisher, string? Thumbprint) TryReadSigner(
        string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate =
                X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057

            using var signer =
                new X509Certificate2(certificate);

            var publisher = signer.GetNameInfo(
                X509NameType.SimpleName,
                forIssuer: false);

            var thumbprint =
                NormalizeThumbprint(signer.Thumbprint);

            return (
                string.IsNullOrWhiteSpace(publisher)
                    ? null
                    : publisher,
                string.IsNullOrWhiteSpace(thumbprint)
                    ? null
                    : thumbprint);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string NormalizeThumbprint(
        string? thumbprint) =>
        string.IsNullOrWhiteSpace(thumbprint)
            ? string.Empty
            : new string(
                thumbprint
                    .Where(Uri.IsHexDigit)
                    .ToArray())
                .ToUpperInvariant();

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options:
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }

    private static async Task<bool> IsPortableExecutableAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options:
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        if (stream.Length < 64)
            return false;

        var dosHeader = new byte[64];

        await ReadExactlyAsync(
            stream,
            dosHeader,
            cancellationToken);

        if (dosHeader[0] != (byte)'M' ||
            dosHeader[1] != (byte)'Z')
        {
            return false;
        }

        var peOffset =
            BinaryPrimitives.ReadInt32LittleEndian(
                dosHeader.AsSpan(0x3C, 4));

        if (peOffset < 64 ||
            peOffset > stream.Length - 4)
        {
            return false;
        }

        stream.Position = peOffset;

        var peHeader = new byte[4];

        await ReadExactlyAsync(
            stream,
            peHeader,
            cancellationToken);

        return peHeader[0] == (byte)'P' &&
               peHeader[1] == (byte)'E' &&
               peHeader[2] == 0 &&
               peHeader[3] == 0;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(
                    offset,
                    buffer.Length - offset),
                cancellationToken);

            if (read == 0)
                throw new EndOfStreamException();

            offset += read;
        }
    }

    private sealed record DefenderScanResult(
        bool IsAccepted,
        string Message)
    {
        public static DefenderScanResult Accepted() =>
            new(
                true,
                "Windows Defender found no blocked threats.");

        public static DefenderScanResult Rejected(
            string message) =>
            new(false, message);
    }
}

public sealed record ExecutableValidationResult(
    bool IsAccepted,
    string Message,
    string? Sha256,
    string? Publisher,
    string? SignerThumbprint)
{
    public static ExecutableValidationResult Accepted(
        string sha256,
        string? publisher,
        string? signerThumbprint) =>
        new(
            true,
            "Executable accepted after Windows Defender scanning.",
            sha256,
            publisher,
            signerThumbprint);

    public static ExecutableValidationResult Rejected(
        string message) =>
        new(
            false,
            message,
            null,
            null,
            null);
}
