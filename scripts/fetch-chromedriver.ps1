<#
.SYNOPSIS
    Downloads the ChromeDriver builds Cloudict ships inside its installers.

.DESCRIPTION
    Cloudict bundles a driver so the app can open its helper browser on a machine that has never
    been online. Chrome keeps moving, so re-run this before cutting a release.

    Google's own download host answers 403 Forbidden in a number of regions, so mirrors are tried
    first and Google last: the same order the app itself uses at runtime.

    Drivers land in src/Cloudict.App/Drivers/<platform>/<version>/, and the packaging scripts copy
    only the folder matching the target platform into each installer. A previously bundled driver
    for the same platform is removed, so only one version per platform ever ships.

.PARAMETER Version
    Full driver version (e.g. 151.0.7922.77). Defaults to the current Chrome-for-Testing Stable.

.PARAMETER Platform
    Which builds to fetch: win64, linux64, mac-x64, mac-arm64, or All (the default).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\fetch-chromedriver.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\fetch-chromedriver.ps1 -Platform linux64
#>
[CmdletBinding()]
param(
    [string] $Version,
    [ValidateSet('All', 'win64', 'linux64', 'mac-x64', 'mac-arm64')]
    [string] $Platform = 'All'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$driversDir = Join-Path $repoRoot 'src\Cloudict.App\Drivers'

if (-not $Version) {
    Write-Host 'Resolving the current Chrome for Testing stable version...'
    $meta = Invoke-RestMethod -TimeoutSec 60 `
        -Uri 'https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions.json'
    $Version = $meta.channels.Stable.version
}
Write-Host "Target driver version: $Version"

$platforms = if ($Platform -eq 'All') { @('win64', 'linux64', 'mac-x64', 'mac-arm64') } else { @($Platform) }

foreach ($target in $platforms) {
    Write-Host ""
    Write-Host "=== $target ==="

    # Mirrors first: storage.googleapis.com is the host blocked for many of this app's users.
    $sources = @(
        "https://cdn.npmmirror.com/binaries/chrome-for-testing/$Version/$target/chromedriver-$target.zip",
        "https://registry.npmmirror.com/-/binary/chrome-for-testing/$Version/$target/chromedriver-$target.zip",
        "https://storage.googleapis.com/chrome-for-testing-public/$Version/$target/chromedriver-$target.zip"
    )

    $temp = Join-Path ([IO.Path]::GetTempPath()) ("cloudict-driver-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $temp | Out-Null
    $zip = Join-Path $temp "chromedriver.zip"

    $ok = $false
    foreach ($url in $sources) {
        try {
            Write-Host "  trying $url"
            Invoke-WebRequest -Uri $url -OutFile $zip -TimeoutSec 300
            $ok = $true
            break
        } catch {
            Write-Warning "  failed: $($_.Exception.Message)"
        }
    }

    if (-not $ok) {
        Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
        throw "Could not download ChromeDriver $Version for $target from any source."
    }

    Expand-Archive -Path $zip -DestinationPath $temp -Force

    $name = if ($target -eq 'win64') { 'chromedriver.exe' } else { 'chromedriver' }
    $exe = Get-ChildItem -Recurse -Path $temp -Filter $name | Select-Object -First 1
    if (-not $exe) {
        Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
        throw "The archive for $target did not contain $name."
    }

    # One bundled driver per platform; leaving old ones behind would bloat every installer.
    $platformDir = Join-Path $driversDir $target
    if (Test-Path $platformDir) { Remove-Item -Recurse -Force $platformDir }

    $targetDir = Join-Path $platformDir $Version
    New-Item -ItemType Directory -Force $targetDir | Out-Null
    Copy-Item $exe.FullName (Join-Path $targetDir $name) -Force

    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
    Write-Host "  bundled: src\Cloudict.App\Drivers\$target\$Version\$name"
}

Write-Host ""
Write-Host "Done. Commit the change, then run scripts\build-all.ps1 to produce the packages."
