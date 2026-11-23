BeforeAll {
    $probe = Join-Path (Split-Path -Parent $PSScriptRoot) 'measure-agent.sh'

    # Runs measure-agent.sh from a copy in a home of its own, on a stand-in agent that starts Velopack's log, runs
    # $Start, saves the files the real one saves first, holds its runtime files and sleeps. XDG_STATE_HOME is relative,
    # which the app ignores.
    function Invoke-Probe([string]$Start, [string[]]$Libraries) {
        $test = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $copy = Join-Path $test 'jmi-footprint-test'
        $userHome = Join-Path $test 'home'
        $runtime = Join-Path $test 'run'
        $temp = Join-Path $test 'tmp'
        $shareVendor = Join-Path $userHome '.local/share/Vendor'
        $cacheVendor = Join-Path $test 'cache/Vendor'
        $stateVendor = Join-Path $userHome '.local/state/Vendor'
        $pidFile = Join-Path $test 'agent.pid'
        New-Item -ItemType Directory $copy, $runtime, $temp, (Join-Path $shareVendor 'Other') | Out-Null
        New-Item -ItemType File (Join-Path $runtime 'Other.Agent.lock') | Out-Null
        Copy-Item $probe $copy
        Set-Content (Join-Path $copy 'Agent') @"
#!/bin/sh
echo `$`$ > '$pidFile'
touch '$temp/velopack.log'
$Start
mkdir -p '$shareVendor/App.Footprint/state' '$cacheVendor/App.Footprint/catalogue' '$stateVendor/App.Footprint/logs'
touch '$shareVendor/App.Footprint/state/library.json' '$cacheVendor/App.Footprint/catalogue/index.json'
touch '$runtime/App.Footprint.Agent.lock' '$runtime/App.Footprint.sock'
exec sleep 600
"@

        $output = & env -u XDG_DATA_HOME "HOME=$userHome" "XDG_CACHE_HOME=$(Split-Path -Parent $cacheVendor)" `
            XDG_STATE_HOME=state "XDG_RUNTIME_DIR=$runtime" "TMPDIR=$temp" sh (Join-Path $copy 'measure-agent.sh') Agent `
            Vendor App.Footprint 10 0 1 @Libraries
        [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = @($output)
            Agent = "/proc/$(Get-Content $pidFile)"
            Gone = $copy, (Join-Path $shareVendor 'App.Footprint'), $cacheVendor, $stateVendor,
                (Join-Path $runtime 'App.Footprint.Agent.lock'), (Join-Path $runtime 'App.Footprint.sock'),
                (Join-Path $temp 'velopack.log')
            Kept = (Join-Path $shareVendor 'Other'), (Join-Path $runtime 'Other.Agent.lock')
        }
    }

    function Assert-NothingLeft($Run) {
        Test-Path $Run.Agent | Should -BeFalse
        $Run.Gone | Where-Object { Test-Path $_ } | Should -BeNullOrEmpty
        $Run.Kept | Where-Object { -not (Test-Path $_) } | Should -BeNullOrEmpty
    }
}

Describe 'measure-agent.sh' {
    It 'reads the agent three times and leaves nothing of it behind' {
        if (-not $IsLinux) { Set-ItResult -Skipped -Because 'measure-agent.sh reads /proc, which only Linux has' }

        # The stand-in is sleep, with stdin, stdout and stderr open and what a program this shell starts inherits, such as
        # a CI runner's descriptors.
        $descriptors = [int](sh -c 'ls /proc/$$/fd | wc -l')

        $run = Invoke-Probe -Libraries libSkiaSharp

        $run.ExitCode | Should -Be 0
        $readings = @($run.Output -like '@reading *')
        $readings.Count | Should -Be 3
        foreach ($reading in $readings) {
            $private, $privateWorkingSet, $workingSet, $threads, $handles = [int[]](-split $reading | Select-Object -Skip 1)
            $private | Should -BeGreaterThan 0
            $privateWorkingSet | Should -BeGreaterThan 0
            $workingSet | Should -BeGreaterThan $privateWorkingSet
            $threads | Should -Be 1
            $handles | Should -Be $descriptors
        }
        Assert-NothingLeft $run
    }

    It 'fails when <Case> and leaves nothing of it behind' -TestCases @(
        @{
            Case = 'the agent maps one of the window''s libraries'; Start = ''; Libraries = @('libSkiaSharp', 'libc')
            Messages = @('The agent loaded the window''s native libraries: libc.so.6.')
        }
        @{
            Case = 'the agent stops before its first scan'; Libraries = @()
            Start = 'echo ''A library is missing.'' >&2; logs="$HOME/.local/state/Vendor/App.Footprint/logs"; ' +
                'mkdir -p "$logs"; echo ''The agent does not run here.'' > "$logs/launcher.log"; exit 1'
            Messages = @('A library is missing.', 'The agent does not run here.')
        }
    ) {
        if (-not $IsLinux) { Set-ItResult -Skipped -Because 'measure-agent.sh reads /proc, which only Linux has' }

        $run = Invoke-Probe -Start $Start -Libraries $Libraries

        $run.ExitCode | Should -Be 3
        foreach ($message in $Messages) { $run.Output | Should -Contain $message }
        Assert-NothingLeft $run
    }
}
