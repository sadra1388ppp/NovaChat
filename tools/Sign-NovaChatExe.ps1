[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$PfxPath,

    [Parameter(Mandatory = $true)]
    [securestring]$PfxPassword,

    [string]$TimestampUrl = "https://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$exe = (Resolve-Path $ExePath).Path
$pfx = (Resolve-Path $PfxPath).Path

$signtool = Get-ChildItem "$env:ProgramFiles(x86)\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    throw "signtool.exe was not found. Install the Windows SDK."
}

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
$plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

try {
    & $signtool.FullName sign /f $pfx /p $plainPassword /fd SHA256 /tr $TimestampUrl /td SHA256 /d "NovaChat" $exe
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with exit code $LASTEXITCODE." }

    & $signtool.FullName verify /pa /all /v $exe
    if ($LASTEXITCODE -ne 0) { throw "signtool verification failed with exit code $LASTEXITCODE." }

    Write-Host ""
    Write-Host "Signed and verified: $exe"
}
finally {
    $plainPassword = $null
}
