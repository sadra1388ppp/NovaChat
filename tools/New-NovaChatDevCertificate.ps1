[CmdletBinding()]
param(
    [string]$Subject = "CN=NovaChat Development Code Signing",
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\signing"
)

$ErrorActionPreference = "Stop"
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -FriendlyName "NovaChat Development Code Signing" -CertStoreLocation "Cert:\CurrentUser\My" -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(3)

$pfxPath = Join-Path $resolvedOutput "NovaChat-Development-CodeSigning.pfx"
$cerPath = Join-Path $resolvedOutput "NovaChat-Development-CodeSigning.cer"
$password = Read-Host "Enter a password for the development PFX" -AsSecureString

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPublisher" | Out-Null

Write-Host ""
Write-Host "Development certificate created."
Write-Host "PFX: $pfxPath"
Write-Host "CER: $cerPath"
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host ""
Write-Host "This certificate is for local development/testing only. Do not use it for production releases."
