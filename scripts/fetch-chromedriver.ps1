<#
.SYNOPSIS
    Refreshes the ChromeDriver that Cloudict ships inside its installer.

.DESCRIPTION
    Cloudict bundles a ChromeDriver so the app can open its helper browser on a machine that has
    never been online. Chrome keeps moving, so re-run this before cutting a release to bundle the
    driver for the current Chrome stable.

    Google's own download host answers 403 Forbidden in a number of regions, so mirrors are tried
    first and Google last — the same order the app itself uses at runtime.

    The driver lands in src\Cloudict\Drivers\<version>\chromedriver.exe and any previously bundled
    version is removed, so only one driver ever ships.

.PARAMETER Version
    Full driver version to fetch (e.g. 151.0.7922.77). Defaults to the Chrome-for-Testing Stable
    release, which is what the vast majority of users are running.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\fetch-chromedriver.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\fetch-chromedriver.ps1 -Version 152.0.8000.10
#>
[CmdletBinding()]
param(
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$driversDir = Join-Path $repoRoot 'src\Cloudict\Drivers'

if (-not $Version) {
    Write-Host 'Resolving the current Chrome for Testing stable version...'
    $meta = Invoke-RestMethod -TimeoutSec 60 `
        -Uri 'https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions.json'
    $Version = $meta.channels.Stable.version
}
Write-Host "Target driver version: $Version"

# Mirrors first: storage.googleapis.com is the host that is blocked for many of this app's users.
$sources = @(
    "https://cdn.npmmirror.com/binaries/chrome-for-testing/$Version/win64/chromedriver-win64.zip",
    "https://registry.npmmirror.com/-/binary/chrome-for-testing/$Version/win64/chromedriver-win64.zip",
    "https://storage.googleapis.com/chrome-for-testing-public/$Version/win64/chromedriver-win64.zip"
)

$temp = Join-Path ([IO.Path]::GetTempPath()) ("cloudict-driver-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null
$zip = Join-Path $temp 'chromedriver-win64.zip'

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
    throw "Could not download ChromeDriver $Version from any source."
}

Expand-Archive -Path $zip -DestinationPath $temp -Force
$exe = Get-ChildItem -Recurse -Path $temp -Filter 'chromedriver.exe' | Select-Object -First 1
if (-not $exe) {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
    throw 'The downloaded archive did not contain chromedriver.exe.'
}

$actual = $exe.VersionInfo.ProductVersion
Write-Host "Downloaded chromedriver $actual"

# One bundled driver at a time: leaving old ones behind would bloat every installer build.
if (Test-Path $driversDir) { Remove-Item -Recurse -Force $driversDir }
$target = Join-Path $driversDir $actual
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $exe.FullName (Join-Path $target 'chromedriver.exe') -Force

Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Bundled driver updated: src\Cloudict\Drivers\$actual\chromedriver.exe"
Write-Host "Commit the change, then run scripts\build-installer.bat to produce a new setup."
