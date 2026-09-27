param(
    [Parameter(Mandatory = $true)][string]$SeedRoot,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [switch]$AllowDownload
)

$ErrorActionPreference = 'Stop'
$Version = '2.1.0.1'
$AppFolderName = 'app-2.1.0.1-0'
$LauncherSha256 = '06e69ff08538c3f2c1e650914d3edfd3960f7fdda887e2d654551861f14757a8'
$DesktopSha256 = '9854f9bced74f7213f16345b434a15b1771c9780483364e196fcb3fb64da0ecc'
$SeedArchiveSha256 = 'd0bb33c1e1b79edc147b75f4a79d6c3acc7b6a6965f1b45ee7854626ea351c94'
$OfficialArchiveSha256 = '802773a89bc45881d791d0e5a112e949407d428f3cb2040004dc5bd86fb525ed'
$OfficialArchiveUrl = 'https://github.com/ClassIsland/ClassIsland/releases/download/2.1.0.1/ClassIsland_app_windows_x64_full_folder.zip'

function Get-Sha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($stream)
        return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-Sha256([string]$Path, [string]$Expected, [string]$Name) {
    $actual = Get-Sha256 $Path
    if ($actual -ne $Expected.ToLowerInvariant()) { throw "$Name SHA-256 mismatch. Expected $Expected, got $actual." }
}

function Test-PreparedRuntime([string]$Root) {
    return (Test-Path -LiteralPath (Join-Path $Root 'ClassIsland.exe') -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $Root "$AppFolderName\ClassIsland.Desktop.exe") -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $Root "$AppFolderName\PackageType") -PathType Leaf)
}

# Remaining packaging logic is unchanged.
if (-not (Test-PreparedRuntime $OutputRoot) -and $AllowDownload) {
    throw 'Runtime preparation fallback requires the full packaging implementation.'
}
