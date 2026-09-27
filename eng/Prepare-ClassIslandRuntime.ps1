param(
    [Parameter(Mandatory = $true)][string]$SeedRoot,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [switch]$AllowDownload
)

$ErrorActionPreference = 'Stop'
$AppFolderName = 'app-2.1.0.1-0'
$LauncherSha256 = '06e69ff08538c3f2c1e650914d3edfd3960f7fdda887e2d654551861f14757a8'
$DesktopSha256 = '9854f9bced74f7213f16345b434a15b1771c9780483364e196fcb3fb64da0ecc'
$SeedArchiveSha256 = 'd0bb33c1e1b79edc147b75f4a79d6c3acc7b6a6965f1b45ee7854626ea351c94'
$OfficialArchiveSha256 = '802773a89bc45881d791d0e5a112e949407d428f3cb2040004dc5bd86fb525ed'
$OfficialArchiveUrl = 'https://github.com/ClassIsland/ClassIsland/releases/download/2.1.0.1/ClassIsland_app_windows_x64_full_folder.zip'

function Get-Sha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return ([BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($stream)) -replace '-', '').ToLowerInvariant()
    }
    finally { $stream.Dispose() }
}

function Assert-Sha256([string]$Path, [string]$Expected, [string]$Name) {
    $actual = Get-Sha256 $Path
    if ($actual -ne $Expected.ToLowerInvariant()) { throw "$Name SHA-256 mismatch." }
}

function Test-PreparedRuntime([string]$Root) {
    return ((Test-Path (Join-Path $Root 'ClassIsland.exe')) -and
            (Test-Path (Join-Path $Root "$AppFolderName\ClassIsland.Desktop.exe")) -and
            (Test-Path (Join-Path $Root "$AppFolderName\PackageType")))
}

function Validate-PreparedRuntime([string]$Root) {
    if (-not (Test-PreparedRuntime $Root)) { throw "Prepared runtime missing." }
    if ((Get-Content (Join-Path $Root "$AppFolderName\PackageType") -Raw).Trim() -ne 'folder') { throw 'PackageType must be folder.' }
    Assert-Sha256 (Join-Path $Root 'ClassIsland.exe') $LauncherSha256 'ClassIsland.exe'
    Assert-Sha256 (Join-Path $Root "$AppFolderName\ClassIsland.Desktop.exe") $DesktopSha256 'ClassIsland.Desktop.exe'
}

$SeedRoot = [IO.Path]::GetFullPath($SeedRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$seedLauncher = Join-Path $SeedRoot 'ClassIsland.exe'
$seedArchive = Join-Path $SeedRoot "$AppFolderName.zip"
$seedAppDir = Join-Path $SeedRoot $AppFolderName

if ((Test-Path $seedLauncher) -and (Test-Path $seedAppDir)) {
    Assert-Sha256 $seedLauncher $LauncherSha256 'seed launcher'
    Copy-Item $seedLauncher (Join-Path $OutputRoot 'ClassIsland.exe') -Force
    Copy-Item $seedAppDir (Join-Path $OutputRoot $AppFolderName) -Recurse -Force
}
elseif ((Test-Path $seedLauncher) -and (Test-Path $seedArchive)) {
    Assert-Sha256 $seedLauncher $LauncherSha256 'seed launcher'
    Assert-Sha256 $seedArchive $SeedArchiveSha256 'seed archive'
    Copy-Item $seedLauncher (Join-Path $OutputRoot 'ClassIsland.exe') -Force
    Expand-Archive $seedArchive (Join-Path $OutputRoot $AppFolderName) -Force
}
elseif ($AllowDownload) {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("classisland-$([guid]::NewGuid())")
    $zip = "$temp.zip"
    try {
        Invoke-WebRequest -Uri $OfficialArchiveUrl -OutFile $zip -TimeoutSec 30
        Assert-Sha256 $zip $OfficialArchiveSha256 'official archive'
        Expand-Archive $zip $temp -Force
        Copy-Item (Join-Path $temp '*') $OutputRoot -Recurse -Force
    }
    finally {
        Remove-Item $zip -Force -ErrorAction SilentlyContinue
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
else {
    throw 'ClassIsland runtime seed missing.'
}

Validate-PreparedRuntime $OutputRoot
