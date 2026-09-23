using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NovaChat.Server.Services;

public sealed class ExecutableSecurityService
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckChainExcludeRoot = 128;
    private const uint WtdLifetimeSigningFlag = 2048;

    public async Task<ExecutableSecurityResult> ValidateAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return ExecutableSecurityResult.Rejected("Executable signature validation is only supported on Windows.");

        if (!File.Exists(filePath))
            return ExecutableSecurityResult.Rejected("The uploaded executable could not be found.");

        var extension = Path.GetExtension(filePath);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            return ExecutableSecurityResult.Rejected("Only EXE files can be checked by this validator.");

        var hash = await ComputeSha256Async(filePath, cancellationToken);

        if (!VerifyAuthenticodeSignature(filePath))
            return ExecutableSecurityResult.Rejected(
                "The EXE does not have a valid trusted Authenticode signature.",
                hash);

        X509Certificate2 certificate;
        try
        {
            using var certificateData = X509Certificate.CreateFromSignedFile(filePath);
            certificate = new X509Certificate2(certificateData);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return ExecutableSecurityResult.Rejected(
                "The EXE signature could not be read.",
                hash);
        }

        using (certificate)
        {
            if (certificate.NotBefore > DateTime.UtcNow || certificate.NotAfter < DateTime.UtcNow)
            {
                return ExecutableSecurityResult.Rejected(
                    "The EXE signing certificate is outside its validity period.",
                    hash,
                    certificate.Subject,
                    certificate.Issuer);
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            if (!chain.Build(certificate))
            {
                var reason = chain.ChainStatus.Length == 0
                    ? "The EXE signing certificate chain could not be validated."
                    : $"The EXE signing certificate chain is not trusted: {chain.ChainStatus[0].StatusInformation.Trim()}";

                return ExecutableSecurityResult.Rejected(
                    reason,
                    hash,
                    certificate.Subject,
                    certificate.Issuer);
            }

            return ExecutableSecurityResult.Accepted(
                hash,
                certificate.Subject,
                certificate.Issuer,
                certificate.GetNameInfo(X509NameType.SimpleName, false));
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 64,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool VerifyAuthenticodeSignature(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var action = WinTrustActionGenericVerifyV2;
        var trustData = new WinTrustData
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            DwUiChoice = WtdUiNone,
            FdwRevocationChecks = WtdRevokeWholeChain,
            DwUnionChoice = WtdChoiceFile,
            PFile = fileInfoPtr,
            DwStateAction = WtdStateActionVerify,
            DwProvFlags = WtdRevocationCheckChainExcludeRoot | WtdLifetimeSigningFlag,
            DwUiContext = 0
        };

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());

            try
            {
                Marshal.StructureToPtr(trustData, dataPtr, false);
                var status = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

                trustData.DwStateAction = WtdStateActionClose;
                Marshal.StructureToPtr(trustData, dataPtr, false);
                _ = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

                return status == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(dataPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    public sealed record ExecutableSecurityResult(
        bool Allowed,
        string Reason,
        string Sha256,
        string? Publisher,
        string? Issuer,
        string? SimpleName)
    {
        public static ExecutableSecurityResult Accepted(
            string sha256,
            string? publisher,
            string? issuer,
            string? simpleName) =>
            new(true, "Valid trusted Authenticode signature.", sha256, publisher, issuer, simpleName);

        public static ExecutableSecurityResult Rejected(
            string reason,
            string? sha256 = null,
            string? publisher = null,
            string? issuer = null) =>
            new(false, reason, sha256 ?? string.Empty, publisher, issuer, null);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;
        public IntPtr PcwszFilePath;
        public IntPtr HFile;
        public IntPtr PgKnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            PcwszFilePath = Marshal.StringToHGlobalUni(filePath);
            HFile = IntPtr.Zero;
            PgKnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
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

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern long WinVerifyTrust(
        IntPtr hwnd,
        ref Guid pgActionID,
        IntPtr pWvtData);
}
