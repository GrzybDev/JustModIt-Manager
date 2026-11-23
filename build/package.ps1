#requires -Version 5.1
<#
.SYNOPSIS
Publishes one app with NativeAOT for the system it runs on and packs it into a Velopack release.

.DESCRIPTION
Everything that sets one app apart (its package id, title, authors, icons, desktop id, menu categories, update feed,
tag prefix and texts) is read from its project with MSBuild, so the script is the same for every app.
NativeAOT compiles only for the system it runs on, so Windows packs the Windows release and Linux the AppImage. The
output folder receives:

  publish\           the published app, as the installer or the AppImage lays it out
  symbols\           the program databases and debug symbols, which never go into the package
  releases\          the Velopack release: installer or AppImage, full and delta packages, release feed; on Linux also
                     install.sh and <slug>-install.desktop, which install the release's AppImage into ~/Applications,
                     and <slug>-install.zip, the installer file packed for Firefox, which renames a downloaded one
  release-notes.md   the commits since the app's previous tag, leaving out those that touch only other apps
  checksums.txt      the SHA-256 of every file a player downloads: the installer or the AppImage and its installers, and
                     the full package

The API key for the app's site comes from the MANAGER_API_KEY environment variable when it is set, and the address
error reports go to from MANAGER_ERROR_REPORTS_DSN.

.EXAMPLE
build\package.ps1 -App MenadzerSpolszczen -Version 0.2.0-alpha.1
#>
[CmdletBinding()]
param(
    # The app's project folder under src, or its ManagerAppSlug, which is also the prefix of its tags.
    [Parameter(Mandatory = $true)]
    [string]$App,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Defaults to artifacts\release\<ManagerAppId>.
    [string]$OutputDir,

    # A GitHub token for reading the app's releases, which avoids the anonymous rate limit.
    [string]$Token,

    # Packs without downloading the previous release, so no delta package is made.
    [switch]$NoDelta,

    # Where the source code of this release can be browsed; the release notes link to it.
    [string]$SourceUrl,

    # A command that code-signs files in place, which vpk runs through cmd with {{file}} or {{file...}}: first for the
    # app's files and its own executables, then for the installer. Windows only.
    [string]$SignTemplate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
$root = Split-Path -Parent $PSScriptRoot
# Windows PowerShell has no $IsLinux.
$linux = [Environment]::OSVersion.Platform -eq 'Unix'
$runtime = if ($linux) { 'linux-x64' } else { 'win-x64' }

# A command-line property ends at a comma or semicolon, and $, @, % and others are MSBuild syntax; every %XX is unescaped.
function ConvertTo-MSBuildValue([string]$Value) {
    return [regex]::Replace($Value, '[^A-Za-z0-9\x80-\uFFFF]', { param($match) '%{0:X2}' -f [int][char]$match.Value })
}

Confirm-Version $Version

# The NativeAOT linker finds Visual Studio through vswhere, which the Visual Studio installer does not put on PATH.
if (-not $linux) {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (-not (Get-Command 'vswhere' -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $installer 'vswhere.exe'))) {
        $env:PATH = "$installer;$env:PATH"
    }
}

$target = Read-AppProject (Find-App $App)
if (-not $OutputDir) { $OutputDir = [System.IO.Path]::Combine($root, 'artifacts', 'release', $target.AppId) }
$publish = Join-Path $OutputDir 'publish'
$symbols = Join-Path $OutputDir 'symbols'
$releases = Join-Path $OutputDir 'releases'
$notes = Join-Path $OutputDir 'release-notes.md'
$checksums = Join-Path $OutputDir 'checksums.txt'
foreach ($output in $publish, $symbols, $releases, $notes, $checksums) {
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
}

# The packaging tool has to be the version of the Velopack library that the app runs.
$vpk = @('tool', 'exec', "vpk@$($target.VelopackVersion)", '--yes', '--')

$publishArguments = @('publish', $target.Project, '-c', 'Release', '-r', $runtime, "-p:Version=$Version", '-o', $publish)
if ($env:MANAGER_API_KEY) { $publishArguments += "-p:ManagerApiKey=$(ConvertTo-MSBuildValue $env:MANAGER_API_KEY)" }
if ($env:MANAGER_ERROR_REPORTS_DSN) {
    $publishArguments += "-p:ManagerErrorReportsDsn=$(ConvertTo-MSBuildValue $env:MANAGER_ERROR_REPORTS_DSN)"
}
Invoke-Native 'dotnet' $publishArguments

New-Item -ItemType Directory -Force $symbols, $releases | Out-Null
# NativeAOT writes the executable's symbols on Linux to <app>.dbg, which vpk would otherwise pack.
Get-ChildItem $publish -File | Where-Object Extension -In '.pdb', '.dbg' | Move-Item -Destination $symbols

if (-not $NoDelta -and $target.UpdateUrl) {
    $downloadArguments = @('download', 'github', '--repoUrl', $target.UpdateUrl, '--outputDir', $releases, '--skip-updates')
    # The delta is made against the newest release of any kind, since testers update to pre-releases too.
    $downloadArguments += '--pre'
    if ($Token) { $downloadArguments += @('--token', $Token) }
    & dotnet @vpk @downloadArguments
    $downloaded = $LASTEXITCODE -eq 0
    $latest = @(Get-ChildItem $releases -Filter '*-full.nupkg' | ForEach-Object {
        Get-PackageVersion $target.AppId $_.Name $linux
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
    '--packAuthors', $target.Company, '--packDir', $publish, '--runtime', $runtime, '--outputDir', $releases, '--delta',
    'BestSpeed', '--skip-updates')
$packArguments += if ($linux) {
    @('--mainExe', $target.AssemblyName, '--icon', $target.Logo, '--categories', $target.Categories)
}
else {
    @('--mainExe', "$($target.AssemblyName).exe", '--icon', $target.Icon, '--shortcuts', 'StartMenuRoot', '--noPortable')
}
if (Write-ReleaseNotes -Target $target -Version $Version -Path $notes -SourceUrl $SourceUrl) {
    $packArguments += @('--releaseNotes', $notes)
}
if ($SignTemplate) {
    # vpk shows the command's output only when it fails, so while signatures are awaited the log says no more than this.
    Write-Host "vpk has the app's files signed, then the installer, with: $SignTemplate"
    $packArguments += @('--signTemplate', $SignTemplate)
}
Invoke-Native 'dotnet' ($vpk + $packArguments)

$assets = Get-ChildItem $releases -Filter 'assets.*.json' | Get-Content -Raw | ConvertFrom-Json
$releaseTag = Get-ReleaseTag $target $Version
# A player downloads the installer or the AppImage and the full package; deltas only reach an installed copy.
$downloads = @($assets | Where-Object Type -NE 'Delta' | ForEach-Object { Join-Path $releases $_.RelativeFileName })
if ($linux) {
    # vpk puts its own AppImage runtime in front of the app, so a Velopack upgrade could ship one the notices do not describe.
    $appImage = Join-Path $releases ($assets | Where-Object Type -EQ 'Portable').RelativeFileName
    $shipped = "$(& $appImage --appimage-version 2>&1)" -replace '^.*/commit/', ''
    $notices = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'THIRD-PARTY-NOTICES.md')
    $described = [regex]::Match($notices, '(?m)^### AppImage runtime\s+- Version: commit ([0-9a-f]+)').Groups[1].Value
    if (-not $shipped -or -not $described.StartsWith($shipped)) {
        throw "The AppImage runtime is $shipped, but THIRD-PARTY-NOTICES.md describes $described."
    }

    # Each release's installer installs that release, so a pre-release's installs the pre-release.
    $downloads += Write-LinuxInstaller -Release "$($target.UpdateUrl)/releases/download/$releaseTag" `
        -AppImage (Split-Path -Leaf $appImage) -Unit "$($target.DesktopId).service" -Slug $target.Slug `
        -Texts (Read-AppTexts $target 'Installer_') -OutputDir $releases
}

$hashes = foreach ($file in $downloads) {
    "$((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path -Leaf $file)"
}
[System.IO.File]::WriteAllText($checksums, ($hashes -join "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))

if ($env:GITHUB_OUTPUT) {
    @(
        "app-id=$($target.AppId)", "update-url=$($target.UpdateUrl)",
        "velopack=$($target.VelopackVersion)", "releases=$releases", "symbols=$symbols", "checksums=$checksums",
        "release-tag=$releaseTag"
    ) | Add-Content -Path $env:GITHUB_OUTPUT -Encoding UTF8
}

Write-Host "$($target.Product) $Version -> $releases (symbols: $symbols)"
