#requires -Version 5.1
<#
.SYNOPSIS
Measures how much memory the agent keeps while it sits in the tray.

.DESCRIPTION
Publishes the app with NativeAOT as a development build with a vendor id and a development name of its own, so it never
meets an installed copy or the developer's own build and starts from an empty data folder, removed afterwards. It reads
the agent's memory once the first library scan and catalogue refresh are saved, a minute later and after -IdleMinutes,
and stops every process it started. It fails when the agent loaded the window's native libraries.

The agent reads the real store libraries and site, read-only: the first-run setup is not finished, so nothing is
downloaded or installed.

With -Linux, the Linux build is published in the SDK's container and copied over SSH to the machine given, where
measure-agent.sh measures it and removes the copy. There, private bytes are the agent's anonymous memory, resident or
swapped out (RssAnon and VmSwap), the private working set is the resident memory no other process maps (Private_Clean
and Private_Dirty), the working set is VmRSS and the handles are the open files.

.EXAMPLE
build\measure-agent.ps1 -App MenadzerSpolszczen -IdleMinutes 10

.EXAMPLE
build\measure-agent.ps1 -App MenadzerSpolszczen -Linux deck@steamdeck
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

    # Defaults to artifacts\footprint\<App>\<runtime>.
    [string]$OutputDir,

    # Measures on this Linux machine instead, reached with ssh and scp, such as user@host.
    [string]$Linux
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Release.psm1') -Force
$root = Split-Path -Parent $PSScriptRoot
$vendor = 'JustModIt.Manager.Footprint'
$developmentName = 'Footprint'
$runtime = if ($Linux) { 'linux-x64' } else { 'win-x64' }
$moments = 'first scan and refresh', '1 min later', "$IdleMinutes min later"

$project = Get-ChildItem (Join-Path $root "src\$App") -Filter '*.csproj' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $project -or -not (Test-Path (Join-Path $project.DirectoryName 'Product.props'))) {
    throw "src\$App is not an app project."
}

if (-not $OutputDir) { $OutputDir = Join-Path $root "artifacts\footprint\$App\$runtime" }

if (-not $NoPublish) {
    if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
    $properties = '-p:ManagerDevelopmentBuild=true', "-p:ManagerVendorId=$vendor", "-p:ManagerDevelopmentName=$developmentName"
    if ($Linux) {
        # NativeAOT compiles only for the system it runs on. The container works on a copy, so the checkout's own build
        # output stays as it is, and its Ubuntu 24.04 binds the build to glibc 2.39, which SteamOS has.
        $sdk = (Get-Content -Raw (Join-Path $root 'global.json') | ConvertFrom-Json).sdk.version
        $out = (New-Item -ItemType Directory $OutputDir).FullName
        $steps = 'apt-get update -qq && apt-get install -y -qq --no-install-recommends clang cmake g++ make meson nasm ' +
            '>/dev/null && mkdir /work && tar -C /repo --exclude=./.git --exclude=./.claude --exclude=./artifacts ' +
            "--exclude=./native/build -cf - . | tar -xf - -C /work && dotnet publish /work/src/$App/$($project.Name) " +
            "-c Release -r $runtime -o /out $properties -nologo -v:q && rm -f /out/*.dbg /out/*.pdb"
        Invoke-Native 'docker' @('run', '--rm', '-v', "${root}:/repo:ro", '-v', "${out}:/out", '-v',
            'justmodit-manager-nuget:/root/.nuget/packages', "mcr.microsoft.com/dotnet/sdk:$sdk", 'sh', '-c', $steps)
    }
    else {
        # The NativeAOT linker finds Visual Studio through vswhere, which the Visual Studio installer does not put on PATH.
        $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
        if (-not (Get-Command 'vswhere' -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $installer 'vswhere.exe'))) {
            $env:PATH = "$installer;$env:PATH"
        }
        Invoke-Native 'dotnet' (@('publish', $project.FullName, '-c', 'Release', '-r', $runtime, '-o', $OutputDir) +
            $properties + @('-nologo', '-v:q'))
    }
}

function New-Reading([string]$Moment, [double]$Private, [double]$PrivateWorkingSet, [double]$WorkingSet, [int]$Threads,
    [int]$Handles) {
    [pscustomobject]@{
        Moment = $Moment
        'Private (MB)' = [Math]::Round($Private / 1MB, 1)
        'Private working set (MB)' = [Math]::Round($PrivateWorkingSet / 1MB, 1)
        'Working set (MB)' = [Math]::Round($WorkingSet / 1MB, 1)
        Threads = $Threads
        Handles = $Handles
    }
}

if ($Linux) {
    $exe = Get-ChildItem $OutputDir -File | Where-Object { -not $_.Extension } | Select-Object -First 1
    if (-not $exe) { throw "$OutputDir holds no published app." }
    Copy-Item (Join-Path $PSScriptRoot 'measure-agent.sh') $OutputDir
    # A folder of its own per run, so scp never copies into one that an interrupted run left behind.
    $remote = "/tmp/jmi-footprint-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    Invoke-Native 'scp' @('-r', '-q', $OutputDir, "${Linux}:$remote")
    # With a terminal, a lost connection or Ctrl+C ends measure-agent.sh too, which then stops the agent and removes
    # the copy. On Linux the window's platform loads libX11 where Windows loads ANGLE.
    $output = & ssh -tt $Linux sh "$remote/measure-agent.sh" $exe.Name $vendor "$($exe.Name).$developmentName" `
        $TimeoutSeconds 60 ([int]($IdleMinutes * 60)) libSkiaSharp libHarfBuzzSharp libX11 libavifshim
    $code = $LASTEXITCODE
    $lines = @($output | ForEach-Object { "$_".TrimEnd() })
    if ($code -ne 0) { throw "measure-agent.sh failed with exit code ${code}:`n$(($lines -notlike '@reading *') -join "`n")" }

    $found = @($lines -like '@reading *')
    $readings = for ($i = 0; $i -lt $found.Count; $i++) {
        $kilobytes = [double[]](-split $found[$i] | Select-Object -Skip 1)
        New-Reading $moments[$i] ($kilobytes[0] * 1KB) ($kilobytes[1] * 1KB) ($kilobytes[2] * 1KB) $kilobytes[3] $kilobytes[4]
    }
    $readings | Format-Table -AutoSize
    Write-Host 'These are the bare executable''s figures: an installed AppImage adds the process that serves its mount.'
    return
}

$exe = Get-ChildItem $OutputDir -Filter '*.exe' | Select-Object -First 1
if (-not $exe) { throw "$OutputDir holds no published app." }

$data = Join-Path $env:LOCALAPPDATA "$vendor\$($exe.BaseName).$developmentName"
$index = Join-Path $data 'cache\catalogue\index.json'
$library = Join-Path $data 'state\library.json'
if (Test-Path $data) { Remove-Item $data -Recurse -Force }

function Read-Footprint([System.Diagnostics.Process]$Process, [string]$Moment) {
    $Process.Refresh()
    $counters = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess = $($Process.Id)"
    New-Reading $Moment $Process.PrivateMemorySize64 ([double]$counters.WorkingSetPrivate) $Process.WorkingSet64 `
        $Process.Threads.Count $Process.HandleCount
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
    $readings = @(Read-Footprint $agent $moments[0])
    # The agent role touches no Avalonia type, so the window's native libraries must never load in the tray.
    $windowLibraries = @($agent.Modules |
        Where-Object { $_.ModuleName -in 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'av_libglesv2.dll' })
    if ($windowLibraries.Count) {
        throw "The agent loaded the window's native libraries: $(($windowLibraries | ForEach-Object ModuleName) -join ', ')."
    }
    Wait-Until $ready.AddMinutes(1)
    $readings += Read-Footprint $agent $moments[1]
    Wait-Until $ready.AddMinutes($IdleMinutes)
    $readings += Read-Footprint $agent $moments[2]
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
