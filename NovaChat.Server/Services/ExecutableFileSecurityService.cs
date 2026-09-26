using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaChat.Server.Services;

public sealed class ExecutableFileSecurityService
{
    private const int DefaultScanTimeoutSeconds = 180;
    private const int DefaultPollIntervalMilliseconds = 1000;
    private const int MaxPollIntervalMilliseconds = 5000;

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExecutableFileSecurityService> _logger;

    public ExecutableFileSecurityService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<ExecutableFileSecurityService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ExecutableValidationResult> ValidateAsync(
        string filePath,
        string originalFileName,
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

        var scanResult = await ScanWithMetaDefenderAsync(
            filePath,
            originalFileName,
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

    private async Task<MetaDefenderScanResult> ScanWithMetaDefenderAsync(
        string filePath,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration[
            "FileSecurity:Executable:MetaDefender:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return MetaDefenderScanResult.Rejected(
                "MetaDefender API key is not configured on the server.");
        }

        var timeoutSeconds = _configuration.GetValue(
            "FileSecurity:Executable:MetaDefender:ScanTimeoutSeconds",
            DefaultScanTimeoutSeconds);

        timeoutSeconds = Math.Clamp(timeoutSeconds, 30, 900);

        var pollIntervalMilliseconds = _configuration.GetValue(
            "FileSecurity:Executable:MetaDefender:PollIntervalMilliseconds",
            DefaultPollIntervalMilliseconds);

        pollIntervalMilliseconds = Math.Clamp(
            pollIntervalMilliseconds,
            250,
            MaxPollIntervalMilliseconds);

        var client = _httpClientFactory.CreateClient("MetaDefender");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeoutCts.CancelAfter(
            TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var upload = await UploadToMetaDefenderAsync(
                client,
                apiKey,
                filePath,
                originalFileName,
                timeoutCts.Token);

            if (string.IsNullOrWhiteSpace(upload.DataId))
            {
                return MetaDefenderScanResult.Rejected(
                    "MetaDefender did not return a scan identifier.");
            }

            while (true)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();

                var report = await GetMetaDefenderReportAsync(
                    client,
                    apiKey,
                    upload.DataId,
                    timeoutCts.Token);

                if (report == null)
                {
                    return MetaDefenderScanResult.Rejected(
                        "MetaDefender returned an invalid scan report.");
                }

                if (report.ScanResultCode is 254 or 255)
                {
                    await Task.Delay(
                        pollIntervalMilliseconds,
                        timeoutCts.Token);

                    continue;
                }

                return MapFinalScanResult(report);
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return MetaDefenderScanResult.Rejected(
                $"MetaDefender scan timed out after {timeoutSeconds} seconds.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "MetaDefender request failed for executable {FileName}.",
                originalFileName);

            return MetaDefenderScanResult.Rejected(
                "MetaDefender could not be reached to scan the executable.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "MetaDefender returned an unreadable response for executable {FileName}.",
                originalFileName);

            return MetaDefenderScanResult.Rejected(
                "MetaDefender returned an invalid scan response.");
        }
    }

    private async Task<MetaDefenderUploadResponse> UploadToMetaDefenderAsync(
        HttpClient client,
        string apiKey,
        string filePath,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");

        using var form = new MultipartFormDataContent();
        form.Add(
            fileContent,
            "file",
            Path.GetFileName(originalFileName));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "file");

        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.TryAddWithoutValidation(
            "filename",
            Path.GetFileName(originalFileName));

        if (_configuration.GetValue(
                "FileSecurity:Executable:MetaDefender:PrivateScanning",
                false))
        {
            // MetaDefender documents samplesharing=0 for paid private scanning.
            request.Headers.TryAddWithoutValidation(
                "samplesharing",
                "0");
        }

        request.Content = form;

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "MetaDefender upload failed with HTTP {StatusCode}. Response: {Response}",
                response.StatusCode,
                Truncate(body, 500));

            throw new HttpRequestException(
                $"MetaDefender returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        var result = JsonSerializer.Deserialize<MetaDefenderUploadResponse>(
            body,
            JsonOptions);

        return result ?? new MetaDefenderUploadResponse();
    }

    private static async Task<MetaDefenderReport?> GetMetaDefenderReportAsync(
        HttpClient client,
        string apiKey,
        string dataId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"file/{Uri.EscapeDataString(dataId)}");

        request.Headers.TryAddWithoutValidation("apikey", apiKey);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"MetaDefender returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        return JsonSerializer.Deserialize<MetaDefenderReport>(
            body,
            JsonOptions);
    }

    private static MetaDefenderScanResult MapFinalScanResult(
        MetaDefenderReport report)
    {
        var resultCode = report.ScanResults?.ScanAllResultI;

        if (resultCode == 0)
        {
            var detected = report.ScanResults.TotalDetectedAvs;
            var total = report.ScanResults.TotalAvs;

            return MetaDefenderScanResult.Accepted(
                detected,
                total);
        }

        if (resultCode == 7)
        {
            return MetaDefenderScanResult.Accepted(
                report.ScanResults.TotalDetectedAvs,
                report.ScanResults.TotalAvs);
        }

        var description = resultCode switch
        {
            1 => "A threat was detected.",
            2 => "MetaDefender classified the executable as suspicious.",
            3 => "MetaDefender failed to complete the scan.",
            17 => "MetaDefender detected a file type mismatch.",
            23 => "MetaDefender could not scan this file type.",
            253 => "MetaDefender did not scan the file because the API rate limit was exceeded.",
            254 => "The file is still queued for scanning.",
            255 => "The file is still being scanned.",
            _ => $"MetaDefender returned scan result code {resultCode?.ToString() ?? "unknown"}."
        };

        return MetaDefenderScanResult.Rejected(
            $"Executable rejected by MetaDefender. {description}");
    }

    private static string Truncate(
        string value,
        int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";

    private static (string? Publisher, string? Thumbprint) TryReadSigner(
        string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate =
                X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057

            using var signer = new X509Certificate2(certificate);

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
                thumbprint.Where(Uri.IsHexDigit).ToArray())
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
            options: FileOptions.Asynchronous |
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
            options: FileOptions.Asynchronous |
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
            return false;

        var peOffset =
            BinaryPrimitives.ReadInt32LittleEndian(
                dosHeader.AsSpan(0x3C, 4));

        if (peOffset < 64 ||
            peOffset > stream.Length - 4)
            return false;

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class MetaDefenderUploadResponse
    {
        [JsonPropertyName("data_id")]
        public string? DataId { get; set; }
    }

    private sealed class MetaDefenderReport
    {
        [JsonPropertyName("process_info")]
        public MetaDefenderProcessInfo? ProcessInfo { get; set; }

        [JsonPropertyName("scan_results")]
        public MetaDefenderScanResults ScanResults { get; set; } = new();
    }

    private sealed class MetaDefenderProcessInfo
    {
        [JsonPropertyName("progress_percentage")]
        public int ProgressPercentage { get; set; }
    }

    private sealed class MetaDefenderScanResults
    {
        [JsonPropertyName("scan_all_result_i")]
        public int? ScanAllResultI { get; set; }

        [JsonPropertyName("total_detected_avs")]
        public int TotalDetectedAvs { get; set; }

        [JsonPropertyName("total_avs")]
        public int TotalAvs { get; set; }
    }

    private sealed record MetaDefenderScanResult(
        bool IsAccepted,
        string Message,
        int DetectedEngines,
        int TotalEngines)
    {
        public static MetaDefenderScanResult Accepted(
            int detectedEngines,
            int totalEngines) =>
            new(
                true,
                $"MetaDefender scan passed ({detectedEngines}/{totalEngines} engines detected no threat).",
                detectedEngines,
                totalEngines);

        public static MetaDefenderScanResult Rejected(
            string message) =>
            new(false, message, 0, 0);
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
            "Executable accepted after MetaDefender security scanning.",
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
