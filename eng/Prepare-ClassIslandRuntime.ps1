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

function Assert-Sha256([string]$Path, [string]$Expected, [string]$Name) {
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    if ($actual -ne $Expected.ToLowerInvariant()) { throw "$Name SHA-256 mismatch. Expected $Expected, got $actual." }
}
function Test-PreparedRuntime([string]$Root) {
    return (Test-Path -LiteralPath (Join-Path $Root 'ClassIsland.exe') -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $Root "$AppFolderName\ClassIsland.Desktop.exe") -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $Root "$AppFolderName\PackageType") -PathType Leaf)
}
function Validate-PreparedRuntime([string]$Root) {
    if (-not (Test-PreparedRuntime $Root)) { throw "Prepared ClassIsland runtime is incomplete at $Root." }
    if ((Get-Content -LiteralPath (Join-Path $Root "$AppFolderName\PackageType") -Raw).Trim() -ne 'folder') {
        throw "ClassIsland PackageType must stay 'folder'."
    }
    Assert-Sha256 (Join-Path $Root 'ClassIsland.exe') $LauncherSha256 'ClassIsland.exe'
    Assert-Sha256 (Join-Path $Root "$AppFolderName\ClassIsland.Desktop.exe") $DesktopSha256 'ClassIsland.Desktop.exe'
}

$SeedRoot = [IO.Path]::GetFullPath($SeedRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$seedLauncher = Join-Path $SeedRoot 'ClassIsland.exe'
$seedArchive = Join-Path $SeedRoot "$AppFolderName.zip"
$seedExpanded = Join-Path $SeedRoot $AppFolderName

if (Test-PreparedRuntime $OutputRoot) {
    try { Validate-PreparedRuntime $OutputRoot; Write-Host "ClassIsland runtime already prepared at $OutputRoot"; exit 0 }
    catch { Remove-Item -LiteralPath $OutputRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

Remove-Item -LiteralPath $OutputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$source = ''

if ((Test-Path -LiteralPath $seedLauncher -PathType Leaf) -and (Test-Path -LiteralPath $seedArchive -PathType Leaf)) {
    Assert-Sha256 $seedLauncher $LauncherSha256 'seed ClassIsland.exe'
    Assert-Sha256 $seedArchive $SeedArchiveSha256 'seed app archive'
    Copy-Item -LiteralPath $seedLauncher -Destination (Join-Path $OutputRoot 'ClassIsland.exe')
    Expand-Archive -LiteralPath $seedArchive -DestinationPath (Join-Path $OutputRoot $AppFolderName) -Force
    $source = 'local-seed'
}
elseif ((Test-Path -LiteralPath $seedLauncher -PathType Leaf) -and (Test-Path -LiteralPath $seedExpanded -PathType Container)) {
    Assert-Sha256 $seedLauncher $LauncherSha256 'seed ClassIsland.exe'
    Copy-Item -LiteralPath $seedLauncher -Destination (Join-Path $OutputRoot 'ClassIsland.exe')
    Copy-Item -LiteralPath $seedExpanded -Destination (Join-Path $OutputRoot $AppFolderName) -Recurse
    $source = 'local-expanded-seed'
}
elseif ($AllowDownload) {
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("exusiai-classisland-" + [guid]::NewGuid().ToString('N'))
    $archive = "$temporary.zip"
    try {
        Write-Host "Local seed absent; fetching immutable upstream ClassIsland $Version release for build-time packaging."
        Invoke-WebRequest -Uri $OfficialArchiveUrl -OutFile $archive
        Assert-Sha256 $archive $OfficialArchiveSha256 'official release archive'
        Expand-Archive -LiteralPath $archive -DestinationPath $temporary -Force
        $launcher = Get-ChildItem -LiteralPath $temporary -Filter 'ClassIsland.exe' -File -Recurse |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.Directory.FullName $AppFolderName) -PathType Container } |
            Select-Object -First 1
        if ($null -eq $launcher) { throw 'Official ClassIsland archive does not contain expected launcher + app folder layout.' }
        Copy-Item -Path (Join-Path $launcher.Directory.FullName '*') -Destination $OutputRoot -Recurse -Force
        $source = 'official-release-fallback'
    }
    finally {
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
}
else {
    throw "ClassIsland runtime seed is missing. Put ClassIsland.exe and $AppFolderName.zip under $SeedRoot."
}

Validate-PreparedRuntime $OutputRoot
@{
    version = $Version
    appFolder = $AppFolderName
    releaseCommit = '15273f82c9d2d55929df83b5fb806e68ee4547c0'
    mishaBranch = 'develop/v2/misha-alpha'
    mishaBaselineCommit = '08808615899d1a4abb8e0ef576bf1e247adde10f'
    source = $source
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot '.exusiai-runtime.json') -Encoding UTF8
Write-Host "Prepared ClassIsland $Version runtime at $OutputRoot from $source"
