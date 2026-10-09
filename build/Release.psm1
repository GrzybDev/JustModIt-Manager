#requires -Version 5.1

Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
# Semantic Versioning's grammar: with a leading zero, a tag would name a version that its package spells otherwise.
$number = '(0|[1-9]\d*)'
$identifier = "($number|\d*[A-Za-z-][0-9A-Za-z-]*)"
$versionPattern = "^$number\.$number\.$number(-$identifier(\.$identifier)*)?`$"

function Invoke-Native([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

# Windows PowerShell turns anything a native command writes to stderr into an error, which 'Stop' would throw; a failed
# lookup is an answer here, told by the exit code.
function Get-NativeOutput([string]$Command, [string[]]$Arguments) {
    $preference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Command @Arguments 2>$null
        return [pscustomobject]@{ Lines = @($output | ForEach-Object { "$_" }); ExitCode = $LASTEXITCODE }
    }
    finally {
        $ErrorActionPreference = $preference
    }
}

function Get-AppProjects {
    Get-ChildItem (Join-Path $root 'src') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Product.props') }
}

function Read-AppProject([System.IO.DirectoryInfo]$Folder) {
    $project = Get-ChildItem $Folder.FullName -Filter '*.csproj' | Select-Object -First 1
    $properties = 'Product', 'Company', 'AssemblyName', 'ApplicationIcon', 'ManagerAppId', 'ManagerAppSlug', 'ManagerUpdateUrl',
        'ManagerUpdateChannel', 'ManagerUpdatePrereleases'
    $arguments = @('msbuild', $project.FullName, '-p:Configuration=Release', '-getItem:PackageVersion') +
        @($properties | ForEach-Object { "-getProperty:$_" })
    $read = Get-NativeOutput 'dotnet' $arguments
    if ($read.ExitCode -ne 0) { throw "MSBuild could not read $($project.Name):`n$($read.Lines -join "`n")" }
    $evaluation = ($read.Lines -join "`n") | ConvertFrom-Json
    $velopack = @($evaluation.Items.PackageVersion | Where-Object { $_.Identity -eq 'Velopack' })
    if ($velopack.Count -ne 1) { throw 'Directory.Packages.props gives no version of Velopack, which the packaging tool must match.' }

    $values = $evaluation.Properties
    [pscustomobject]@{
        Folder = $Folder
        Project = $project.FullName
        Product = $values.Product
        Company = $values.Company
        MainExe = "$($values.AssemblyName).exe"
        Icon = Join-Path $Folder.FullName $values.ApplicationIcon
        AppId = $values.ManagerAppId
        Slug = $values.ManagerAppSlug
        UpdateUrl = $values.ManagerUpdateUrl.TrimEnd('/')
        Channel = $values.ManagerUpdateChannel
        Prereleases = $values.ManagerUpdatePrereleases -eq 'true'
        VelopackVersion = $velopack[0].Version
    }
}

# Product.props holds plain values, so the slug is read without MSBuild, which version.ps1 runs before the SDK is set up.
function Get-AppSlug([System.IO.DirectoryInfo]$Folder) {
    $props = New-Object System.Xml.XmlDocument
    $props.Load((Join-Path $Folder.FullName 'Product.props'))
    return $props.SelectSingleNode('/Project/PropertyGroup/ManagerAppSlug').InnerText
}

function Find-App([string]$Name) {
    $apps = @(Get-AppProjects)
    $folder = $apps | Where-Object { $_.Name -eq $Name -or (Get-AppSlug $_) -eq $Name } | Select-Object -First 1
    if (-not $folder) {
        throw "No app is called '$Name'; the apps are $(($apps | ForEach-Object Name) -join ', ') (by folder or ManagerAppSlug)."
    }
    return $folder
}

function Confirm-Version([string]$Version) {
    if ($Version -notmatch $versionPattern) { throw "'$Version' is not a version such as 1.2.3 or 1.2.3-alpha.1." }
    # Velopack compares only the numbers, so 0.0.1-ci.1 is accepted.
    if ([version]($Version -split '-', 2)[0] -lt [version]'0.0.1') {
        throw "Velopack packs no version below 0.0.1, so '$Version' cannot be released."
    }
}

# Semantic Versioning precedence, by which Velopack orders releases.
function Compare-Version([string]$Left, [string]$Right) {
    $leftCore, $leftLabel = $Left -split '-', 2
    $rightCore, $rightLabel = $Right -split '-', 2
    $order = ([version]$leftCore).CompareTo([version]$rightCore)
    if ($order -ne 0) { return [Math]::Sign($order) }
    if (-not $leftLabel -or -not $rightLabel) { return [int][bool]$rightLabel - [int][bool]$leftLabel }

    $leftParts = $leftLabel.Split('.')
    $rightParts = $rightLabel.Split('.')
    for ($i = 0; $i -lt [Math]::Min($leftParts.Count, $rightParts.Count); $i++) {
        $leftNumeric = $leftParts[$i] -match '^\d+$'
        $rightNumeric = $rightParts[$i] -match '^\d+$'
        $order = if ($leftNumeric -and $rightNumeric) { ([decimal]$leftParts[$i]).CompareTo([decimal]$rightParts[$i]) }
            elseif ($leftNumeric -ne $rightNumeric) { if ($leftNumeric) { -1 } else { 1 } }
            else { [string]::CompareOrdinal($leftParts[$i], $rightParts[$i]) }
        if ($order -ne 0) { return [Math]::Sign($order) }
    }
    return $leftParts.Count.CompareTo($rightParts.Count)
}

# The versions of the app's tags, <slug>/v<version>, that the ref contains, oldest first.
function Get-AppTagVersions {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Slug,

        [string]$Ref = 'HEAD'
    )

    $prefix = "$Slug/v"
    $tags = Get-NativeOutput 'git' @('-C', $root, 'tag', '--list', "$prefix*", '--merged', $Ref)
    if ($tags.ExitCode -ne 0) { throw "git could not list the tags that $Ref contains." }
    $versions = New-Object System.Collections.Generic.List[string]
    foreach ($tag in $tags.Lines) {
        $version = $tag.Substring($prefix.Length)
        if ($version -match $versionPattern) { $versions.Add($version) }
    }
    $versions.Sort([System.Comparison[string]] { param($left, $right) Compare-Version $left $right })
    return $versions.ToArray()
}

# npm's semver increments, with pre-release numbers starting at 1: a bump without a label releases a pre-release of the
# version it leads to, and one with a label starts a pre-release of the next version.
function Get-NextVersion {
    param(
        [AllowEmptyString()]
        [string]$Current,

        [Parameter(Mandatory = $true)]
        [ValidateSet('patch', 'minor', 'major', 'prerelease', 'release')]
        [string]$Bump,

        [ValidateSet('', 'alpha', 'beta', 'rc')]
        [string]$PreRelease
    )

    if (-not $Current) { $Current = '0.0.0' }
    $core, $label = $Current -split '-', 2
    $major, $minor, $patch = [int[]]$core.Split('.')
    $next = switch ($Bump) {
        'release' {
            if (-not $label) { throw "$Current is no pre-release, so there is nothing to release." }
            if ($PreRelease) { throw 'A release takes no pre-release label.' }
            $core
        }
        'prerelease' {
            if ($label) {
                if (-not $PreRelease) { $PreRelease = $label.Split('.')[0] }
                if ($label -match "^$PreRelease\.(\d+)$") { "$core-$PreRelease.$([int]$Matches[1] + 1)" }
                else { "$core-$PreRelease.1" }
            }
            elseif ($PreRelease) { "$major.$minor.$($patch + 1)-$PreRelease.1" }
            else { throw "$Current is no pre-release, so its next one needs a label: alpha, beta or rc." }
        }
        default {
            $released = switch ($Bump) {
                'patch' { $true }
                'minor' { $patch -eq 0 }
                'major' { $minor -eq 0 -and $patch -eq 0 }
            }
            $bumped = if ($label -and $released -and -not $PreRelease) { $core }
                elseif ($Bump -eq 'major') { "$($major + 1).0.0" }
                elseif ($Bump -eq 'minor') { "$major.$($minor + 1).0" }
                else { "$major.$minor.$($patch + 1)" }
            if ($PreRelease) { "$bumped-$PreRelease.1" } else { $bumped }
        }
    }
    if ((Compare-Version $next $Current) -le 0) { throw "$Bump $PreRelease after $Current gives $next, which is not newer." }
    return $next
}

# Writes the subjects of the commits since the app's previous tag, leaving out those that touch only other apps; false
# when there is nothing to write.
function Write-ReleaseNotes {
    param(
        # The app, as Read-AppProject reads it.
        [Parameter(Mandatory = $true)]
        $Target,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        # Where the source code of this release can be browsed; the notes end with a link to it.
        [string]$SourceUrl
    )

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

Export-ModuleMember -Function Invoke-Native, Get-NativeOutput, Get-AppProjects, Get-AppSlug, Find-App, Read-AppProject,
    Confirm-Version, Compare-Version, Get-AppTagVersions, Get-NextVersion, Write-ReleaseNotes
