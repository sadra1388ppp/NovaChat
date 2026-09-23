using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NovaChat.Server.Services;

public sealed class ExecutableSecurityService
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;

    public async Task<ExecutableSecurityResult> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return ExecutableSecurityResult.Rejected(
                "Executable signature validation is only supported on Windows.");

        if (!File.Exists(filePath))
            return ExecutableSecurityResult.Rejected(
                "The uploaded executable could not be found.");

        if (!string.Equals(Path.GetExtension(filePath), ".exe", StringComparison.OrdinalIgnoreCase))
            return ExecutableSecurityResult.Rejected(
                "Only EXE files can be checked by this validator.");

        var hash = await ComputeSha256Async(filePath, cancellationToken);

        // WinVerifyTrust is the Windows Authenticode trust decision.
        // It understands normal Authenticode rules, including timestamped
        // signatures whose signing certificate has since expired.
        if (!VerifyAuthenticodeSignature(filePath))
        {
            return ExecutableSecurityResult.Rejected(
                "The EXE does not have a valid trusted Authenticode signature.",
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
                "The EXE signature could not be read.",
                hash);
        }

        using (certificate)
        {
            // Do not independently reject an expired certificate here.
            // Authenticode can remain valid when the signature has a trusted
            // timestamp from when the certificate was valid. WinVerifyTrust
            // is responsible for that policy decision.
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

    private static bool VerifyAuthenticodeSignature(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var action = WinTrustActionGenericVerifyV2;

        var trustData = new WinTrustData
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            DwUiChoice = WtdUiNone,
            FdwRevocationChecks = 0,
            DwUnionChoice = WtdChoiceFile,
            PFile = fileInfoPtr,
            DwStateAction = WtdStateActionVerify,
            DwProvFlags = 0,
            DwUiContext = 0
        };

        IntPtr dataPtr = IntPtr.Zero;

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, dataPtr, false);

            var status = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

            // The verification call may populate HWvtStateData. Re-read it
            // before asking WinTrust to close its state.
            var verifiedTrustData = Marshal.PtrToStructure<WinTrustData>(dataPtr);
            verifiedTrustData.DwStateAction = WtdStateActionClose;
            Marshal.StructureToPtr(verifiedTrustData, dataPtr, false);
            _ = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

            return status == 0;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(dataPtr);

            Marshal.FreeHGlobal(fileInfo.PcwszFilePath);
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
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        ref Guid pgActionID,
        IntPtr pWvtData);
}
