<#
.SYNOPSIS
  Authenticode-signs Cloudict's Windows binaries, when a certificate is available.

.DESCRIPTION
  Signing is what makes an antivirus stop asking about every file in the install folder. An
  unsigned executable that spawns Chrome, injects keystrokes and writes to the registry looks,
  to a heuristic, exactly like the things it exists to catch; the same executable carrying a
  publisher's signature is a known quantity, and the prompts go away. This script is the
  mechanism; the certificate is the only missing piece.

  It is safe to run without one. When no certificate is configured it prints a line and exits
  with success, so the build pipeline is identical with and without credentials — signing
  switches on the day the secrets exist, and nothing else changes.

  The certificate comes from the environment, as CI supplies it:

    WINDOWS_SIGN_PFX_BASE64    the .pfx, base64-encoded
    WINDOWS_SIGN_PFX_PASSWORD  its password

  or from -PfxPath / -PfxPassword for a local run.

.PARAMETER Path
  A folder, in which case every .exe and .dll that does not already carry a valid signature is
  signed (files signed by Microsoft or others are left alone), or a single file. Inno Setup
  calls this with a single file to sign the installer and the uninstaller it generates.

.EXAMPLE
  pwsh packaging/windows/sign.ps1 -Path src/Cloudict.App/bin/Release/net10.0/win-x64/publish
  pwsh packaging/windows/sign.ps1 -Path dist/Cloudict-3.2.1-Setup.exe
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $Path,
  [string] $PfxPath,
  [string] $PfxPassword,
  [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------- credentials
$pfxFromEnv = $false
if (-not $PfxPath) {
  $b64 = $env:WINDOWS_SIGN_PFX_BASE64
  if (-not $b64) {
    Write-Host "sign.ps1: no certificate configured (WINDOWS_SIGN_PFX_BASE64 unset) - leaving files unsigned."
    exit 0
  }
  $PfxPath = Join-Path ([IO.Path]::GetTempPath()) ("cloudict-sign-" + [guid]::NewGuid().ToString("N") + ".pfx")
  [IO.File]::WriteAllBytes($PfxPath, [Convert]::FromBase64String($b64))
  $pfxFromEnv = $true
}
if (-not $PfxPassword) { $PfxPassword = $env:WINDOWS_SIGN_PFX_PASSWORD }
if (-not (Test-Path $PfxPath)) { throw "sign.ps1: certificate not found at $PfxPath" }

try {
  # ---------------------------------------------------------------- signtool
  # Part of the Windows SDK, which every GitHub Windows runner and every Visual Studio install
  # carries. The newest SDK on the machine wins.
  $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
  if (-not $signtool) {
    $kits = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")
    # bin\ holds version folders (10.0.22621.0) beside architecture ones (x64, arm); only the
    # former are candidates.
    $signtool = $kits |
      Where-Object { $_ -and (Test-Path $_) } |
      ForEach-Object { Get-ChildItem $_ -Directory } |
      Where-Object { $_.Name -match '^\d+(\.\d+){1,3}$' } |
      Sort-Object { [version]$_.Name } -Descending |
      ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
      Where-Object { Test-Path $_ } |
      Select-Object -First 1
  }
  if (-not $signtool) { throw "sign.ps1: signtool.exe not found; install the Windows SDK." }

  # ---------------------------------------------------------------- targets
  $item = Get-Item $Path
  if ($item.PSIsContainer) {
    $targets = Get-ChildItem $item.FullName -Recurse -Include *.exe, *.dll |
      Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne "Valid" }
  } else {
    $targets = @($item)
  }

  if (-not $targets) {
    Write-Host "sign.ps1: nothing to sign under $Path"
    exit 0
  }

  # ---------------------------------------------------------------- sign
  # SHA-256 digest and an RFC 3161 timestamp, so the signature outlives the certificate: a
  # timestamped signature stays valid after the certificate expires, an untimestamped one does
  # not, and a release should not stop being trusted on a date printed in the certificate.
  $failed = 0
  foreach ($file in $targets) {
    & $signtool sign /fd SHA256 /td SHA256 /tr $TimestampUrl /f $PfxPath /p $PfxPassword /q $file.FullName
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "sign.ps1: failed on $($file.Name) (exit $LASTEXITCODE)"
      $failed++
    }
  }

  $signed = $targets.Count - $failed
  Write-Host "sign.ps1: signed $signed file(s)$(if ($failed) { ", $failed FAILED" })"
  if ($failed) { exit 1 }
}
finally {
  if ($pfxFromEnv -and (Test-Path $PfxPath)) { Remove-Item $PfxPath -Force }
}
