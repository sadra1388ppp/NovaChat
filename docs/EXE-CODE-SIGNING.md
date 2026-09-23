# NovaChat EXE Code Signing

NovaChat rejects unsigned .exe uploads with Windows Authenticode validation.

The server uses Windows WinVerifyTrust, so an EXE must have a valid Authenticode signature that Windows trusts. The current rejection TRUST_E_NOSIGNATURE (0x800B0100) means the file has no Authenticode signature.

## Development

For local testing only:

1. Create a development certificate:

   powershell
   .\tools\New-NovaChatDevCertificate.ps1

2. Build the Windows client:

   powershell
   dotnet build NovaChat.Client -c Release

3. Sign the EXE. Use the PFX password requested by the certificate script:

   powershell
   $password = Read-Host "PFX password" -AsSecureString
   .\tools\Sign-NovaChatExe.ps1 -ExePath ".\NovaChat.Client\bin\Release\net10.0-windows\NovaChat.Client.exe" -PfxPath ".\artifacts\signing\NovaChat-Development-CodeSigning.pfx" -PfxPassword $password

4. Verify the result:

   powershell
   Get-AuthenticodeSignature ".\NovaChat.Client\bin\Release\net10.0-windows\NovaChat.Client.exe" | Format-List *

   The status should be Valid.

The development certificate is trusted only on the local Windows user profile. It must never be treated as a production publisher identity.

## Production

Use a real code-signing certificate issued to the NovaChat publisher/company. Keep the private key outside Git and outside the repository.

The release signing command should use SHA-256 and an RFC 3161 timestamp:

   powershell
   signtool sign /f NovaChat-Production.pfx /p "$env:NOVACHAT_SIGNING_PASSWORD" /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 /d "NovaChat" NovaChat.Client.exe
   signtool verify /pa /all /v NovaChat.Client.exe

Do not commit the .pfx, certificate password, or private key.

## Server validation

The server performs these checks before an EXE is moved into permanent upload storage:

1. Windows Authenticode signature verification.
2. SHA-256 calculation for audit/debugging.
3. Signing certificate extraction and logging.
4. Rejection when Windows reports an invalid or missing signature.

The server does not independently reject an otherwise valid timestamped signature merely because the signing certificate is now expired; Windows Authenticode verification is authoritative for that decision.

## Important distinction

Code signing proves publisher identity and file integrity. It is not a software license or activation system. Licensing must be implemented separately if NovaChat will be commercially licensed.
