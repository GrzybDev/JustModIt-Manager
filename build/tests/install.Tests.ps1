BeforeAll {
    Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'Release.psm1') -Force
    $texts = @{
        Title = 'Install Sample App'; AsRoot = 'as root'; Unsupported = 'unsupported'; NoFuse = 'no FUSE'
        NoDownloader = 'no downloader'; Downloading = 'downloading'; Damaged = "the download's damaged"
        Done = 'installed in {file}'; Close = "Enter closes the installer's window."
    }

    # A release folder that curl reads through file://, with the installer files for it, and stand-ins for what starts
    # programs and the user's service manager, which record how they were called.
    function New-Release {
        $test = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $release = Join-Path $test 'release'
        $bin = Join-Path $test 'bin'
        New-Item -ItemType Directory $release, $bin | Out-Null
        $installer = Write-LinuxInstaller -Release "file://$release" -AppImage SampleApp.AppImage `
            -Unit dev.example.SampleApp.service -Slug sample-app -Texts $texts -OutputDir $release
        foreach ($tool in 'setsid', 'systemctl', 'fusermount3') {
            Set-Content (Join-Path $bin $tool) "#!/bin/sh`necho `"$tool `$*`" >> `"`$HOME/calls`"`n" -NoNewline
            chmod 755 (Join-Path $bin $tool)
        }
        [pscustomobject]@{ Folder = $release; Bin = $bin; Script = $installer[0]; Entry = $installer[1]; Archive = $installer[2]
            Home = Join-Path $test 'home' }
    }

    function Publish-AppImage($Release, [string]$Content, [string]$Listed = $Content) {
        Set-Content (Join-Path $Release.Folder 'SampleApp.AppImage') $Content -NoNewline
        $hash = (Get-FileHash -Algorithm SHA256 -InputStream ([System.IO.MemoryStream]::new(
            [System.Text.Encoding]::UTF8.GetBytes($Listed)))).Hash.ToLowerInvariant()
        Set-Content (Join-Path $Release.Folder 'checksums.txt') "$hash  SampleApp.AppImage`n" -NoNewline
    }

    # Runs sh in the release's home with the stand-ins first on PATH, and an empty line for the installer file's pause.
    function Invoke-Shell($Release, [string[]]$Arguments, [string]$Bin = $Release.Bin) {
        New-Item -ItemType Directory -Force $Release.Home | Out-Null
        $output = '' | & env "HOME=$($Release.Home)" "PATH=$Bin`:$env:PATH" sh @Arguments 2>&1
        [pscustomobject]@{ Output = ($output | Out-String).Trim(); ExitCode = $LASTEXITCODE }
    }
}

Describe 'The Linux installer' {
    It 'installs the release from its installer file, extracted from its archive, and install.sh replaces it with the next one' {
        if (-not $IsLinux) { Set-ItResult -Skipped -Because 'install.sh and its installer file run only on Linux' }
        $release = New-Release
        Publish-AppImage $release 'version 1'
        $extracted = Join-Path $release.Home 'Downloads'
        [System.IO.Compression.ZipFile]::ExtractToDirectory($release.Archive, $extracted)
        $file = Get-Item (Join-Path $extracted 'sample-app-install.desktop')
        $entry = Get-Content $file
        # Undoes the Desktop Entry Specification's escaping as a launcher does.
        $exec = ($entry | Where-Object { $_.StartsWith('Exec=') }).Substring(5)
        $command = [regex]::Match($exec, '^sh -c "(.*)"$').Groups[1].Value.Replace('\\', '\').Replace('%%', '%') -replace
            '\\([\\"`$])', '$1'

        $launched = Invoke-Shell $release '-c', $command
        $installed = Join-Path $release.Home 'Applications/SampleApp.AppImage'
        $first = Get-Content $installed -Raw
        Publish-AppImage $release 'version 2'
        $replaced = Invoke-Shell $release $release.Script

        Get-ChildItem $extracted | ForEach-Object Name | Should -Be 'sample-app-install.desktop'
        $file.UnixMode | Should -Be '-rwxr-xr-x'
        Get-Content $file -Raw | Should -Be (Get-Content $release.Entry -Raw)
        $entry | Should -Contain 'Name=Install Sample App'
        $entry | Should -Contain 'Terminal=true'
        $launched.Output | Should -Match 'installed in ~/Applications/SampleApp\.AppImage'
        $launched.Output | Should -Match "Enter closes the installer's window\.$"
        $first | Should -Be 'version 1'
        $replaced.ExitCode | Should -Be 0
        Get-Content $installed -Raw | Should -Be 'version 2'
        (Get-Item $installed).UnixMode | Should -Be '-rwxr-xr-x'
        Get-ChildItem (Split-Path -Parent $installed) -Force | ForEach-Object Name | Should -Be 'SampleApp.AppImage'
        Get-Content (Join-Path $release.Home 'calls') | Should -Be @(
            'systemctl --user try-restart dev.example.SampleApp.service', "setsid -f $installed"
            'systemctl --user try-restart dev.example.SampleApp.service', "setsid -f $installed")
    }

    It 'leaves the installed AppImage as it was when <Case>' -TestCases @(
        @{
            Case = 'the download does not match the release'; Content = 'version 2, damaged'; Root = $false
            Message = "the download's damaged"
        }
        @{ Case = 'it runs as root'; Content = 'version 2'; Root = $true; Message = 'as root' }
    ) {
        if (-not $IsLinux) { Set-ItResult -Skipped -Because 'install.sh runs only on Linux' }
        $release = New-Release
        Publish-AppImage $release 'version 1'
        Invoke-Shell $release $release.Script | Out-Null
        Publish-AppImage $release $Content -Listed 'version 2'
        $bin = $release.Bin
        if ($Root) {
            $bin = Join-Path (Split-Path -Parent $release.Bin) 'root'
            New-Item -ItemType Directory $bin | Out-Null
            Copy-Item (Join-Path $release.Bin '*') $bin
            Set-Content (Join-Path $bin 'id') "#!/bin/sh`necho 0`n" -NoNewline
            chmod 755 (Join-Path $bin 'id')
        }

        $result = Invoke-Shell $release $release.Script -Bin $bin

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match $Message
        $installed = Join-Path $release.Home 'Applications/SampleApp.AppImage'
        Get-Content $installed -Raw | Should -Be 'version 1'
        Get-ChildItem (Split-Path -Parent $installed) -Force | ForEach-Object Name | Should -Be 'SampleApp.AppImage'
    }
}
