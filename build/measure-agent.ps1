#requires -Version 5.1
<#
.SYNOPSIS
Measures how much memory the agent keeps while it sits in the tray.

.DESCRIPTION
Publishes one app with NativeAOT as a development build, so its mutexes and pipe never meet an installed copy, with a
vendor id of its own, so it starts from an empty data folder that is removed afterwards. It runs the build as the agent
(no window) and reads the agent's memory three times: as soon as the first library scan and catalogue refresh have
saved their results, a minute later and after it idled. Every process the script starts is stopped at the end.

The agent reads the real store libraries and the app's real site, with read-only requests only: with an empty data
folder the first-run setup is not finished, so nothing is downloaded or installed.

.EXAMPLE
build\measure-agent.ps1 -App MenadzerSpolszczen -IdleMinutes 10
#>
[CmdletBinding()]
param(
    # The app's project folder under src.
    [string]$App = 'MenadzerSpolszczen',

    # When the last reading is taken, counted from the first scan and refresh.
    [double]$IdleMinutes = 5,

    # How long the first scan and refresh may take.
    [int]$TimeoutSeconds = 300,

    # Runs the build published by an earlier run instead of publishing again.
    [switch]$NoPublish,

    # Defaults to artifacts\footprint\<App>.
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$vendor = 'JustModIt.Manager.Footprint'

$project = Get-ChildItem (Join-Path $root "src\$App") -Filter '*.csproj' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $project -or -not (Test-Path (Join-Path $project.DirectoryName 'Product.props'))) {
    throw "src\$App is not an app project."
}

if (-not $OutputDir) { $OutputDir = Join-Path $root "artifacts\footprint\$App" }

# The NativeAOT linker finds Visual Studio through vswhere, which the Visual Studio installer does not put on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command 'vswhere' -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $installer 'vswhere.exe'))) {
    $env:PATH = "$installer;$env:PATH"
}

if (-not $NoPublish) {
    if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
    & dotnet publish $project.FullName -c Release -r win-x64 -o $OutputDir -p:ManagerDevelopmentBuild=true "-p:ManagerVendorId=$vendor" `
        -nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}

$exe = Get-ChildItem $OutputDir -Filter '*.exe' | Select-Object -First 1
if (-not $exe) { throw "$OutputDir holds no published app." }

$data = Join-Path $env:LOCALAPPDATA "$vendor\$($exe.BaseName).Dev"
$index = Join-Path $data 'cache\catalogue\index.json'
$library = Join-Path $data 'state\library.json'
if (Test-Path $data) { Remove-Item $data -Recurse -Force }

function Read-Footprint([System.Diagnostics.Process]$Process, [string]$Moment) {
    $Process.Refresh()
    $counters = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess = $($Process.Id)"
    [pscustomobject]@{
        Moment = $Moment
        'Private (MB)' = [Math]::Round($Process.PrivateMemorySize64 / 1MB, 1)
        'Private working set (MB)' = [Math]::Round([double]$counters.WorkingSetPrivate / 1MB, 1)
        'Working set (MB)' = [Math]::Round($Process.WorkingSet64 / 1MB, 1)
        Threads = $Process.Threads.Count
        Handles = $Process.HandleCount
    }
}

function Wait-Until([datetime]$Moment) {
    $left = $Moment - (Get-Date)
    if ($left -gt [TimeSpan]::Zero) { Start-Sleep -Milliseconds ([int]$left.TotalMilliseconds) }
}

$agent = $null
try {
    $agent = Start-Process -FilePath $exe.FullName -ArgumentList '--agent' -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not ((Test-Path $index) -and (Test-Path $library))) {
        if ($agent.HasExited) {
            throw "The agent exited with code $($agent.ExitCode); another development build of $($exe.BaseName) may be running."
        }
        if ((Get-Date) -gt $deadline) { throw "The first scan and catalogue refresh took longer than $TimeoutSeconds s." }
        Start-Sleep -Milliseconds 250
    }

    $ready = Get-Date
    $readings = @(Read-Footprint $agent 'first scan and refresh')
    Wait-Until $ready.AddMinutes(1)
    $readings += Read-Footprint $agent '1 min later'
    Wait-Until $ready.AddMinutes($IdleMinutes)
    $readings += Read-Footprint $agent "$IdleMinutes min later"
    $readings | Format-Table -AutoSize
}
finally {
    if ($agent -and -not $agent.HasExited) {
        Stop-Process -Id $agent.Id -Force
        $agent.WaitForExit()
    }
    Get-CimInstance Win32_Process -Filter "Name = '$($exe.Name)'" |
        Where-Object { $_.ExecutablePath -eq $exe.FullName } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    $vendorFolder = Split-Path -Parent $data
    if ((Test-Path $vendorFolder) -and -not (Get-ChildItem $vendorFolder)) { Remove-Item $vendorFolder }
}
