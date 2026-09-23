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
        Log($"Starting validation: {filePath}");

        if (!OperatingSystem.IsWindows())
            return Reject("Executable signature validation is only supported on Windows.");

        if (!File.Exists(filePath))
            return Reject("The uploaded executable could not be found.");

        if (!string.Equals(Path.GetExtension(filePath), ".exe", StringComparison.OrdinalIgnoreCase))
            return Reject("Only EXE files can be checked by this validator.");

        var info = new FileInfo(filePath);
        Log($"Size: {info.Length} bytes");

        var hash = await ComputeSha256Async(filePath, cancellationToken);
        Log($"SHA256: {hash}");

        var signature = VerifyAuthenticodeSignature(filePath);

        Log($"WinVerifyTrust status: 0x{signature.Status:X8}");
        Log($"WinVerifyTrust name: {signature.StatusName}");
        Log($"WinVerifyTrust valid: {signature.IsValid}");

        if (!signature.IsValid)
        {
            var reason =
                $"The EXE Authenticode signature was rejected by Windows. " +
                $"WinVerifyTrust=0x{signature.Status:X8} ({signature.StatusName}).";

            Log($"REJECTED: {reason}");
            return ExecutableSecurityResult.Rejected(reason, hash);
        }

        try
        {
            using var certificateData = X509Certificate.CreateFromSignedFile(filePath);
            using var certificate = new X509Certificate2(certificateData);

            var subject = certificate.Subject;
            var issuer = certificate.Issuer;
            var simpleName = certificate.GetNameInfo(X509NameType.SimpleName, false);

            Log($"Certificate Subject: {subject}");
            Log($"Certificate Issuer: {issuer}");
            Log($"Certificate Publisher: {simpleName}");
            Log($"Certificate Thumbprint: {certificate.Thumbprint}");
            Log($"Certificate NotBefore: {certificate.NotBefore:O}");
            Log($"Certificate NotAfter: {certificate.NotAfter:O}");
            Log("ACCEPTED: trusted Authenticode signature.");

            // Certificate expiry is intentionally not checked independently.
            // WinVerifyTrust evaluates Authenticode timestamps correctly.
            return ExecutableSecurityResult.Accepted(
                hash,
                subject,
                issuer,
                simpleName);
        }
        catch (Exception ex) when (
            ex is CryptographicException or ArgumentException)
        {
            Log($"Certificate extraction failed: {ex.GetType().Name}: {ex.Message}");

            return ExecutableSecurityResult.Rejected(
                "Windows accepted the Authenticode signature, but the signing certificate could not be read.",
                hash);
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
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

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

        var verifyStatus = unchecked((int)0xFFFFFFFF);
        var closeStatus = 0;

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            Log("Calling WinVerifyTrust...");
            verifyStatus = WinVerifyTrust(
                IntPtr.Zero,
                ref action,
                ref trustData);

            Log($"WinVerifyTrust returned 0x{verifyStatus:X8}.");

            // WinTrust may allocate HWVTStateData during VERIFY.
            // Reuse the same structure for the mandatory CLOSE operation.
            trustData.DwStateAction = WtdStateActionClose;

            closeStatus = WinVerifyTrust(
                IntPtr.Zero,
                ref action,
                ref trustData);

            Log($"WinVerifyTrust close returned 0x{closeStatus:X8}.");

            return new SignatureVerificationResult(
                verifyStatus == 0,
                verifyStatus,
                GetWinTrustStatusName(verifyStatus));
        }
        catch (Exception ex)
        {
            Log($"WinVerifyTrust exception: {ex.GetType().FullName}");
            Log($"Message: {ex.Message}");
            Log($"HRESULT: 0x{ex.HResult:X8}");

            return new SignatureVerificationResult(
                false,
                ex.HResult,
                "WINTRUST_EXCEPTION");
        }
        finally
        {
            if (fileInfo.PcwszFilePath != IntPtr.Zero)
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
            unchecked((int)0x80096019) => "TRUST_E_TIME_STAMP",
            unchecked((int)0x80092003) => "CRYPT_E_FILE_ERROR",
            unchecked((int)0x80092026) => "CRYPT_E_REVOCATION_OFFLINE",
            unchecked((int)0x800B0004) => "TRUST_E_ACTION_UNKNOWN",
            unchecked((int)0x800B0003) => "TRUST_E_PROVIDER_UNKNOWN",
            unchecked((int)0x800B0006) => "TRUST_E_SUBJECT_FORM_UNKNOWN",
            _ => "UNKNOWN_WINTRUST_STATUS"
        };

    private static void Log(string message) =>
        Console.WriteLine($"[EXE SECURITY] {message}");

    private static ExecutableSecurityResult Reject(string reason) =>
        ExecutableSecurityResult.Rejected(reason);

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
