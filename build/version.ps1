#requires -Version 5.1
<#
.SYNOPSIS
Prints the version of an app's next release.

.DESCRIPTION
Versions live only in tags, <ManagerAppSlug>/v<version>, so releasing commits nothing. The next version bumps the newest
of the app's tags that HEAD contains (0.0.0 before the first one). A version whose tag marks another commit is refused.
Under GitHub Actions the version, its tag, whether it is a pre-release and the app's slug also go to the step's outputs.

.EXAMPLE
build\version.ps1 -App menadzer-spolszczen -Bump minor -PreRelease beta
#>
[CmdletBinding(DefaultParameterSetName = 'App')]
param(
    # The app's project folder under src, or its ManagerAppSlug.
    [Parameter(Mandatory = $true, ParameterSetName = 'App')]
    [string]$App,

    # A pushed tag, which names the app and the version and must be spelt as the app's tags are.
    [Parameter(Mandatory = $true, ParameterSetName = 'Tag')]
    [string]$Tag,

    [ValidateSet('patch', 'minor', 'major', 'prerelease', 'release')]
    [string]$Bump = 'patch',

    # 'none' is what the release workflow's choice passes for no label.
    [ValidateSet('none', 'alpha', 'beta', 'rc')]
    [string]$PreRelease = 'none',

    # An exact version, which wins over everything else.
    [string]$Version,

    # A CI run's number, which gives a pre-release of the next patch that is never tagged. Its leading 0 sorts it below
    # every alpha, beta and rc of that version, so the next release replaces an installed CI build.
    [int]$Ci
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
$root = Split-Path -Parent $PSScriptRoot

if ($Tag) { $App, $Version = $Tag -split '/v', 2 }
$slug = Get-AppSlug (Find-App $App)
if (-not $Version) {
    $current = Get-AppTagVersions -Slug $slug | Select-Object -Last 1
    $Version = if ($PSBoundParameters.ContainsKey('Ci')) { "$(Get-NextVersion -Current $current -Bump patch)-0.ci.$Ci" }
        else { Get-NextVersion -Current $current -Bump $Bump -PreRelease ($PreRelease -replace '^none$') }
}
Confirm-Version $Version

$appTag = "$slug/v$Version"
# The folder name finds the app too, but its next versions and release notes follow only the tags spelt with its slug.
if ($Tag -and $Tag -cne $appTag) { throw "$Tag is not one of the app's tags, which are spelt $slug/v<version>." }
# A pushed tag releases the commit it marks.
if ((Get-NativeOutput 'git' @('-C', $root, 'tag', '--list', $appTag)).Lines -and
    (Get-NativeOutput 'git' @('-C', $root, 'rev-list', '-n', '1', $appTag)).Lines[0] -ne
    (Get-NativeOutput 'git' @('-C', $root, 'rev-parse', 'HEAD')).Lines[0]) {
    throw "$appTag already marks another commit, so $Version cannot be released again."
}

if ($env:GITHUB_OUTPUT) {
    @("version=$Version", "tag=$appTag", "prerelease=$($Version.Contains('-').ToString().ToLowerInvariant())", "slug=$slug") |
        Add-Content -Path $env:GITHUB_OUTPUT -Encoding UTF8
}
$Version
