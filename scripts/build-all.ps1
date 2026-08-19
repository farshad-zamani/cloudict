<#
.SYNOPSIS
    Builds every Cloudict package this machine is able to produce.

.DESCRIPTION
    Each package needs tooling that only exists on its own operating system, so a single machine
    can rarely build all of them:

      Windows installer   Inno Setup 6
      .deb / .rpm         dpkg-deb / rpmbuild  (available under WSL on a Windows box)
      AppImage            appimagetool
      .app / .dmg         macOS only - hdiutil, iconutil and codesign have no equivalent elsewhere

    Anything unavailable is reported and skipped rather than failing the run. The GitHub Actions
    workflow builds the full set by using one runner per platform.

.PARAMETER Version
    Version to stamp on the packages. Defaults to VersionPrefix in src/Directory.Build.props.

.PARAMETER SkipDrivers
    Skip refreshing the bundled ChromeDriver builds.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $SkipDrivers
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$distDir  = Join-Path $repoRoot 'dist'

if (-not $Version) {
    $props = Get-Content (Join-Path $repoRoot 'src\Directory.Build.props') -Raw
    if ($props -match '<VersionPrefix>([^<]+)</VersionPrefix>') { $Version = $Matches[1] }
    else { throw 'Could not read VersionPrefix from src/Directory.Build.props' }
}

Write-Host "Cloudict $Version" -ForegroundColor Cyan
New-Item -ItemType Directory -Force $distDir | Out-Null

if (-not $SkipDrivers) {
    Write-Host "`n=== refreshing bundled drivers ===" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'fetch-chromedriver.ps1')
}

Write-Host "`n=== tests ===" -ForegroundColor Cyan
dotnet test (Join-Path $repoRoot 'src\Cloudict.Core.Tests\Cloudict.Core.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

function Publish-Rid([string] $rid) {
    Write-Host "`n=== publish $rid ===" -ForegroundColor Cyan
    dotnet publish (Join-Path $repoRoot 'src\Cloudict.App\Cloudict.App.csproj') `
        -c Release -r $rid --self-contained true --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
    return Join-Path $repoRoot "src\Cloudict.App\bin\Release\net10.0\$rid\publish"
}

# ------------------------------------------------------------------ Windows
$winPublish = Publish-Rid 'win-x64'

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "`n=== Windows installer ===" -ForegroundColor Cyan
    & $iscc (Join-Path $repoRoot 'packaging\windows\Cloudict.iss') | Select-String 'Successful compile|Error'
} else {
    Write-Warning 'Inno Setup 6 not found; skipping the Windows installer. https://jrsoftware.org/isdl.php'
}

# ------------------------------------------------------------------ Linux (through WSL)
$linuxPublish = Publish-Rid 'linux-x64'

$wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
if ($wsl) {
    Write-Host "`n=== Linux packages (via WSL) ===" -ForegroundColor Cyan
    $wslRepo = (wsl.exe wslpath -a ($repoRoot -replace '\\','/')).Trim()
    $wslPub  = (wsl.exe wslpath -a ($linuxPublish -replace '\\','/')).Trim()
    $wslDist = (wsl.exe wslpath -a ($distDir -replace '\\','/')).Trim()
    wsl.exe bash "$wslRepo/packaging/linux/build-linux-packages.sh" "$wslPub" $Version "$wslDist"
} else {
    Write-Warning 'WSL not available; skipping the Linux packages.'
}

# ------------------------------------------------------------------ macOS
Publish-Rid 'osx-arm64' | Out-Null
Publish-Rid 'osx-x64'   | Out-Null
Write-Warning 'The .app bundle and .dmg can only be built on macOS. Use the GitHub Actions workflow, or run packaging/macos/build-macos-package.sh on a Mac.'

Write-Host "`n=== packages in $distDir ===" -ForegroundColor Cyan
Get-ChildItem $distDir -File | ForEach-Object { "{0,-46} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB) }
