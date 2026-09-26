using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NovaChat.Server.Services;

public sealed class ExecutableFileSecurityService
{
    private const int WinVerifyTrustSuccess = 0;
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckChainExcludeRoot = 128;

    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private readonly IConfiguration _configuration;

    public ExecutableFileSecurityService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<ExecutableValidationResult> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ExecutableValidationResult.Rejected(
                "EXE uploads require a Windows server because Authenticode validation uses Windows trust services.");
        }

        if (!File.Exists(filePath))
            return ExecutableValidationResult.Rejected("The uploaded file could not be found.");

        if (!await IsPortableExecutableAsync(filePath, cancellationToken))
            return ExecutableValidationResult.Rejected("The uploaded file is not a valid Windows PE executable.");

        var hash = await ComputeSha256Async(filePath, cancellationToken);

        if (!VerifyAuthenticode(filePath))
            return ExecutableValidationResult.Rejected(
                "The executable does not have a valid trusted Authenticode signature.");

        X509Certificate2 signer;
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            signer = new X509Certificate2(certificate);
        }
        catch (Exception)
        {
            return ExecutableValidationResult.Rejected(
                "The executable signature could not be read.");
        }

        using (signer)
        {
            if (!signer.Verify())
            {
                return ExecutableValidationResult.Rejected(
                    "The executable signing certificate could not be validated against the server's trusted certificate chain.");
            }

            var thumbprint = NormalizeThumbprint(signer.Thumbprint);
            var publisher = signer.GetNameInfo(X509NameType.SimpleName, false);

            var trustedThumbprints = GetTrustedPublisherThumbprints();

            if (trustedThumbprints.Count > 0 &&
                !trustedThumbprints.Contains(thumbprint, StringComparer.OrdinalIgnoreCase))
            {
                return ExecutableValidationResult.Rejected(
                    "The executable is signed, but its publisher is not on NovaChat's trusted publisher allowlist.");
            }

            return ExecutableValidationResult.Accepted(
                hash,
                publisher,
                thumbprint);
        }
    }

    private HashSet<string> GetTrustedPublisherThumbprints()
    {
        var section = _configuration.GetSection(
            "FileSecurity:Executable:TrustedPublisherThumbprints");

        return section
            .GetChildren()
            .Select(child => NormalizeThumbprint(child.Value))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeThumbprint(string? thumbprint) =>
        string.IsNullOrWhiteSpace(thumbprint)
            ? string.Empty
            : new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

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
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
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
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length < 64)
            return false;

        var dosHeader = new byte[64];
        await ReadExactlyAsync(stream, dosHeader, cancellationToken);

        if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
            return false;

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader.AsSpan(0x3C, 4));
        if (peOffset < 64 || peOffset > stream.Length - 4)
            return false;

        stream.Position = peOffset;

        var peHeader = new byte[4];
        await ReadExactlyAsync(stream, peHeader, cancellationToken);

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
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);

            if (read == 0)
                throw new EndOfStreamException();

            offset += read;
        }
    }

    private static bool VerifyAuthenticode(string filePath)
    {
        var filePathPointer = Marshal.StringToCoTaskMemUni(filePath);

        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                PcwszFilePath = filePathPointer
            };

            var fileInfoPointer = Marshal.AllocCoTaskMem(
                Marshal.SizeOf<WinTrustFileInfo>());

            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

                var trustData = new WinTrustData
                {
                    CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    DwUiChoice = WtdUiNone,
                    FdwRevocationChecks = WtdRevokeWholeChain,
                    DwUnionChoice = WtdChoiceFile,
                    PFile = fileInfoPointer,
                    DwStateAction = WtdStateActionVerify,
                    DwProvFlags = WtdRevocationCheckChainExcludeRoot
                };

                var actionGuid = WinTrustActionGenericVerifyV2;
                var status = WinVerifyTrust(
                    IntPtr.Zero,
                    ref actionGuid,
                    ref trustData);

                trustData.DwStateAction = WtdStateActionClose;
                _ = WinVerifyTrust(
                    IntPtr.Zero,
                    ref actionGuid,
                    ref trustData);

                return status == WinVerifyTrustSuccess;
            }
            finally
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(filePathPointer);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        ref Guid pgActionId,
        ref WinTrustData pWinTrustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;
        public IntPtr PcwszFilePath;
        public IntPtr HFile;
        public IntPtr PgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PPolicyCallbackData;
        public IntPtr PSipClientData;
        public uint DwUiChoice;
        public uint FdwRevocationChecks;
        public uint DwUnionChoice;
        public IntPtr PFile;
        public uint DwStateAction;
        public IntPtr HWvtStateData;
        public IntPtr PwszUrlReference;
        public uint DwProvFlags;
        public uint DwUiContext;
        public IntPtr PSignatureSettings;
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
        string publisher,
        string signerThumbprint) =>
        new(true, "Executable accepted.", sha256, publisher, signerThumbprint);

    public static ExecutableValidationResult Rejected(string message) =>
        new(false, message, null, null, null);
}
