using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NovaChat.Server.Services;

public sealed class ExecutableSecurityService
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;

    public async Task<ExecutableSecurityResult> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ExecutableSecurityResult.Rejected(
                "Executable signature validation is only supported on Windows.");
        }

        if (!File.Exists(filePath))
        {
            return ExecutableSecurityResult.Rejected(
                "The uploaded executable could not be found.");
        }

        if (!string.Equals(Path.GetExtension(filePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutableSecurityResult.Rejected(
                "Only EXE files can be checked by this validator.");
        }

        var hash = await ComputeSha256Async(filePath, cancellationToken);

        var signature = VerifyAuthenticodeSignature(filePath);
        if (!signature.IsValid)
        {
            return ExecutableSecurityResult.Rejected(
                $"The EXE Authenticode signature was rejected by Windows. " +
                $"WinVerifyTrust=0x{signature.Status:X8} ({signature.StatusName}).",
                hash);
        }

        X509Certificate2 certificate;
        try
        {
            using var certificateData = X509Certificate.CreateFromSignedFile(filePath);
            certificate = new X509Certificate2(certificateData);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return ExecutableSecurityResult.Rejected(
                "Windows accepted the Authenticode signature, but the signing certificate could not be read.",
                hash);
        }

        using (certificate)
        {
            // Do not independently reject an expired certificate.
            // Authenticode timestamps can preserve the validity of a signature
            // after the signing certificate itself has expired.
            return ExecutableSecurityResult.Accepted(
                hash,
                certificate.Subject,
                certificate.Issuer,
                certificate.GetNameInfo(X509NameType.SimpleName, false));
        }
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
            bufferSize: 1024 * 64,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static SignatureVerificationResult VerifyAuthenticodeSignature(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var action = WinTrustActionGenericVerifyV2;

        var trustData = new WinTrustData
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            DwUiChoice = WtdUiNone,
            FdwRevocationChecks = WtdRevokeNone,
            DwUnionChoice = WtdChoiceFile,
            PFile = fileInfoPtr,
            DwStateAction = WtdStateActionVerify,
            DwProvFlags = 0,
            DwUiContext = 0
        };

        var verifyStatus = -1;
        var closeStatus = 0;

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            // Pass WINTRUST_DATA directly by reference. This matches the native
            // WinVerifyTrust contract and avoids a second manually-managed copy
            // of the structure.
            verifyStatus = WinVerifyTrust(
                IntPtr.Zero,
                ref action,
                ref trustData);

            // The VERIFY call can populate hWVTStateData. The same structure
            // must then be passed back with STATEACTION_CLOSE to release it.
            trustData.DwStateAction = WtdStateActionClose;
            closeStatus = WinVerifyTrust(
                IntPtr.Zero,
                ref action,
                ref trustData);

            var isValid = verifyStatus == 0;

            return new SignatureVerificationResult(
                isValid,
                verifyStatus,
                GetWinTrustStatusName(verifyStatus));
        }
        finally
        {
            // STATEACTION_CLOSE is the documented way to release WinTrust's
            // verification state. If the first call itself failed, attempting
            // CLOSE is still harmless; WinTrust owns the state handle.
            _ = closeStatus;
            Marshal.FreeHGlobal(fileInfo.PcwszFilePath);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    private static string GetWinTrustStatusName(int status) =>
        status switch
        {
            0x00000000 => "ERROR_SUCCESS",
            unchecked((int)0x800B0100) => "TRUST_E_NOSIGNATURE",
            unchecked((int)0x800B0109) => "CERT_E_UNTRUSTEDROOT",
            unchecked((int)0x800B010C) => "CERT_E_REVOKED",
            unchecked((int)0x800B010A) => "CERT_E_CHAINING",
            unchecked((int)0x800B0101) => "CERT_E_EXPIRED",
            unchecked((int)0x80096010) => "TRUST_E_BAD_DIGEST",
            unchecked((int)0x80096005) => "TRUST_E_SUBJECT_NOT_TRUSTED",
            unchecked((int)0x800B0004) => "TRUST_E_ACTION_UNKNOWN",
            unchecked((int)0x800B0003) => "TRUST_E_PROVIDER_UNKNOWN",
            unchecked((int)0x800B0006) => "TRUST_E_SUBJECT_FORM_UNKNOWN",
            _ => "UNKNOWN_WINTRUST_STATUS"
        };

    private readonly record struct SignatureVerificationResult(
        bool IsValid,
        int Status,
        string StatusName);

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
            new(
                true,
                "Valid trusted Authenticode signature.",
                sha256,
                publisher,
                issuer,
                simpleName);

        public static ExecutableSecurityResult Rejected(
            string reason,
            string? sha256 = null,
            string? publisher = null,
            string? issuer = null) =>
            new(
                false,
                reason,
                sha256 ?? string.Empty,
                publisher,
                issuer,
                null);
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

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        ref Guid pgActionID,
        ref WinTrustData pWvtData);
}
