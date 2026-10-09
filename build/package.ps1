#requires -Version 5.1
<#
.SYNOPSIS
Publishes one app with NativeAOT and packs it into a Velopack release.

.DESCRIPTION
Everything that sets one app apart (its package id, title, authors, icon, update feed, channel and tag prefix) is read
from its project with MSBuild, so the script is the same for every app. The output folder receives:

  publish\           the published app, as the installer lays it out
  symbols\           the program databases, which never go into the package
  releases\          the Velopack release: installer, full and delta packages, release feed
  release-notes.md   the commits since the app's previous tag, leaving out those that touch only other apps
  checksums.txt      the SHA-256 of the installer and the full package

The API key for the app's site comes from the MANAGER_API_KEY environment variable when it is set.

.EXAMPLE
build\package.ps1 -App MenadzerSpolszczen -Version 0.2.0-alpha.1
#>
[CmdletBinding()]
param(
    # The app's project folder under src, or its ManagerAppSlug, which is also the prefix of its tags.
    [Parameter(Mandatory = $true)]
    [string]$App,

    # The release's version, such as 1.2.3 or 1.2.3-alpha.1.
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = 'win-x64',

    # Defaults to artifacts\release\<ManagerAppId>.
    [string]$OutputDir,

    # A GitHub token for reading the app's releases, which avoids the anonymous rate limit.
    [string]$Token,

    # Packs without downloading the previous release, so no delta package is made.
    [switch]$NoDelta,

    # Where the source code of this release can be browsed; the release notes link to it.
    [string]$SourceUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
$root = Split-Path -Parent $PSScriptRoot

# A command-line property ends at a comma or semicolon, and $, @, % and others are MSBuild syntax; every %XX is unescaped.
function ConvertTo-MSBuildValue([string]$Value) {
    return [regex]::Replace($Value, '[^A-Za-z0-9\x80-\uFFFF]', { param($match) '%{0:X2}' -f [int][char]$match.Value })
}

function Write-ReleaseNotes($Target, [string]$Path) {
    $prefix = "$($Target.Slug)/v"
    $previous = Get-NativeOutput 'git' @('-C', $root, 'describe', '--tags', '--abbrev=0', "--match=$prefix*",
        "--exclude=$prefix$Version", 'HEAD')
    $range = if ($previous.ExitCode -eq 0) { "$($previous.Lines[0])..HEAD" } else { 'HEAD' }
    $otherApps = @(Get-AppProjects | Where-Object { $_.FullName -ne $Target.Folder.FullName } |
        ForEach-Object { ":(exclude)src/$($_.Name)" })
    # Written to a file, since Windows PowerShell would read git's UTF-8 in the console's code page.
    $subjects = "$Path.commits"
    $log = Get-NativeOutput 'git' (@('-C', $root, 'log', '--no-merges', '--format=%s', "--output=$subjects", $range, '--', '.') +
        $otherApps)
    if ($log.ExitCode -ne 0) {
        Write-Warning 'The release has no notes: git could not list its commits.'
        return $false
    }

    $lines = @([System.IO.File]::ReadAllLines($subjects) | Where-Object { $_ } | ForEach-Object { "- $_" })
    Remove-Item $subjects
    if ($SourceUrl) { $lines += @('', "Source code: $SourceUrl") }
    if ($lines.Count -eq 0) { return $false }
    [System.IO.File]::WriteAllText($Path, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))
    return $true
}

Confirm-Version $Version

# The NativeAOT linker finds Visual Studio through vswhere, which the Visual Studio installer does not put on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command 'vswhere' -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $installer 'vswhere.exe'))) {
    $env:PATH = "$installer;$env:PATH"
}

$target = Read-AppProject (Find-App $App)
if (-not $OutputDir) { $OutputDir = Join-Path $root "artifacts\release\$($target.AppId)" }
$publish = Join-Path $OutputDir 'publish'
$symbols = Join-Path $OutputDir 'symbols'
$releases = Join-Path $OutputDir 'releases'
$notes = Join-Path $OutputDir 'release-notes.md'
$checksums = Join-Path $OutputDir 'checksums.txt'
foreach ($output in $publish, $symbols, $releases, $notes, $checksums) {
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
}

$vpkFolder = Join-Path $root "artifacts\tools\vpk\$($target.VelopackVersion)"
$vpk = Join-Path $vpkFolder 'vpk.exe'
if (-not (Test-Path $vpk)) {
    Invoke-Native 'dotnet' @('tool', 'install', 'vpk', '--version', $target.VelopackVersion, '--tool-path', $vpkFolder)
}

$publishArguments = @('publish', $target.Project, '-c', 'Release', '-r', $Runtime, "-p:Version=$Version", '-o', $publish)
if ($env:MANAGER_API_KEY) { $publishArguments += "-p:ManagerApiKey=$(ConvertTo-MSBuildValue $env:MANAGER_API_KEY)" }
Invoke-Native 'dotnet' $publishArguments

New-Item -ItemType Directory -Force $symbols, $releases | Out-Null
Get-ChildItem $publish -Filter '*.pdb' | Move-Item -Destination $symbols

if (-not $NoDelta -and $target.UpdateUrl) {
    $downloadArguments = @('download', 'github', '--repoUrl', $target.UpdateUrl, '--outputDir', $releases, '--skip-updates')
    if ($target.Channel) { $downloadArguments += @('--channel', $target.Channel) }
    if ($target.Prereleases) { $downloadArguments += '--pre' }
    if ($Token) { $downloadArguments += @('--token', $Token) }
    & $vpk @downloadArguments
    $downloaded = $LASTEXITCODE -eq 0
    $latest = @(Get-ChildItem $releases -Filter '*-full.nupkg' | ForEach-Object {
        if ($_.Name -match "^$([regex]::Escape($target.AppId))-(?<version>.+)-full\.nupkg$") { $Matches.version }
    })
    if (-not $downloaded -or $latest.Count -eq 0) {
        Write-Warning "No earlier release came from $($target.UpdateUrl), so this one has no delta package."
    }
    # Velopack refuses to pack next to a release that is not older, such as a pre-release that is out while an older
    # line gets a fix.
    elseif ($latest | Where-Object { (Compare-Version $_ $Version) -ge 0 }) {
        Write-Warning "$($target.UpdateUrl) already has $($latest -join ', '), not older than $Version, so this one has no delta package."
        Get-ChildItem $releases | Remove-Item -Recurse -Force
    }
}

$packArguments = @('pack', '--packId', $target.AppId, '--packVersion', $Version, '--packTitle', $target.Product,
    '--packAuthors', $target.Company, '--packDir', $publish, '--mainExe', $target.MainExe, '--icon', $target.Icon,
    '--runtime', $Runtime, '--outputDir', $releases, '--shortcuts', 'StartMenuRoot', '--noPortable', '--delta', 'BestSpeed',
    '--skip-updates')
if ($target.Channel) { $packArguments += @('--channel', $target.Channel) }
if (Write-ReleaseNotes $target $notes) { $packArguments += @('--releaseNotes', $notes) }
Invoke-Native $vpk $packArguments

$assets = Get-ChildItem $releases -Filter 'assets.*.json' | Get-Content -Raw | ConvertFrom-Json
$setup = Join-Path $releases ($assets | Where-Object Type -eq 'Installer').RelativeFileName
$hashes = foreach ($file in $setup, (Join-Path $releases ($assets | Where-Object Type -eq 'Full').RelativeFileName)) {
    "$((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path -Leaf $file)"
}
[System.IO.File]::WriteAllText($checksums, ($hashes -join "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))

if ($env:GITHUB_OUTPUT) {
    @(
        "app-id=$($target.AppId)", "product=$($target.Product)", "update-url=$($target.UpdateUrl)",
        "channel=$($target.Channel)", "vpk=$vpk", "releases=$releases", "symbols=$symbols", "setup=$setup",
        "checksums=$checksums"
    ) | Add-Content -Path $env:GITHUB_OUTPUT -Encoding UTF8
}

Write-Host "$($target.Product) $Version -> $releases (symbols: $symbols)"
