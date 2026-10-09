BeforeAll {
    $build = Split-Path -Parent $PSScriptRoot
    $shell = (Get-Process -Id $PID).Path
    # An aborted run leaves its certificate behind.
    foreach ($leftover in Get-ChildItem Cert:\CurrentUser\My, Cert:\CurrentUser\CA |
        Where-Object { $_.Subject -eq 'CN=Build Script Tests' -and $_.Issuer -eq $_.Subject }) {
        if ($leftover.HasPrivateKey) { Remove-Item $leftover.PSPath -DeleteKey } else { Remove-Item $leftover.PSPath }
    }
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Build Script Tests' `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddMinutes(15)
    $store = Join-Path $TestDrive 'store'
    $release = @{
        SIGNING_STORE = $store; SIGNING_REPOSITORY = 'sample-repository'; SIGNING_APP = 'sample-app'; SIGNING_VERSION = '1.2.3'
        SIGNING_TIMEOUT_MINUTES = '1'; SIGNING_SUBJECT = $null
    }

    function New-UnsignedFile([string]$Name) {
        $path = Join-Path $TestDrive $Name
        Add-Type -TypeDefinition "public static class Sample$([guid]::NewGuid().ToString('N')) { }" -OutputAssembly $path
        return $path
    }

    function Get-Signer([string]$Path) {
        $signature = Get-AuthenticodeSignature -LiteralPath $Path
        if ($signature.SignerCertificate) { return $signature.SignerCertificate.Thumbprint }
    }

    function Start-Script([string]$Script, [string[]]$Arguments) {
        $log = Join-Path $TestDrive "$([guid]::NewGuid()).log"
        $process = Start-Process $shell -ArgumentList (@('-NoProfile', '-File', "`"$build\$Script`"") + $Arguments) `
            -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
        # Without the handle taken while the process runs, its exit code is lost.
        $null = $process.Handle
        return [pscustomobject]@{ Process = $process; Log = $log }
    }

    function Start-SignRemote([string[]]$Files, [hashtable]$Settings = @{}) {
        $environment = $release.Clone()
        foreach ($name in $Settings.Keys) { $environment[$name] = $Settings[$name] }
        $previous = @{}
        foreach ($name in $environment.Keys) {
            $previous[$name] = [Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name, $environment[$name])
        }
        try {
            return Start-Script 'sign-remote.ps1' (@('-AllowUntrustedRoot') + @($Files | ForEach-Object { "`"$_`"" }))
        }
        finally {
            foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
        }
    }

    function Wait-Script($Started) {
        $Started.Process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $Started.Process.ExitCode
            Output = (Get-Content $Started.Log, "$($Started.Log).err" -Raw) -join "`n"
        }
    }

    function Add-StoreEntry([string]$Store, [string]$Key, [string]$File, [switch]$Abandoned) {
        $path = Join-Path $Store $Key
        New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
        if ($File) { Copy-Item $File $path } else { Set-Content $path 'content' }
        if ($Abandoned) { (Get-Item $path).LastWriteTimeUtc = [DateTime]::UtcNow.AddHours(-3) }
    }

    # Answers the upload of a file as the signing machine would, with any content.
    function Publish-Counterpart([string]$File, [string]$Content = $File, [switch]$Unsigned) {
        $hash = (Get-FileHash $File).Hash.ToLowerInvariant()
        $key = "sample-repository/sample-app/v1.2.3/$hash/$(Split-Path -Leaf $File)"
        $deadline = [DateTime]::UtcNow.AddMinutes(1)
        while (-not (Test-Path (Join-Path $store "unsigned/$key"))) {
            if ([DateTime]::UtcNow -gt $deadline) { throw "$File was never uploaded." }
            Start-Sleep -Milliseconds 100
        }
        $signed = Join-Path $store "signed/$key"
        New-Item -ItemType Directory -Force (Split-Path -Parent $signed) | Out-Null
        Copy-Item $Content $signed
        if (-not $Unsigned) {
            Set-AuthenticodeSignature -FilePath $signed -Certificate $certificate -HashAlgorithm SHA256 | Out-Null
        }
    }
}

AfterAll {
    Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -DeleteKey
    # New-SelfSignedCertificate also puts a copy, without the key, among the intermediate authorities.
    Remove-Item "Cert:\CurrentUser\CA\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
}

Describe 'Signing through a store' {
    It "signs a round's files with one signtool call, and the same content again for a later run" {
        $first = New-UnsignedFile 'first.dll'
        $second = New-UnsignedFile "second caf$([char]0xE9).dll"
        $again = Join-Path $TestDrive 'again\first.dll'
        New-Item -ItemType Directory (Split-Path -Parent $again) | Out-Null
        Copy-Item $first $again
        $signer = Start-Script 'sign-pending.ps1' @('-Watch', '-Store', "`"$store`"", '-CertificateThumbprint',
            $certificate.Thumbprint, '-TimestampUrl', '""', '-IntervalSeconds', '1', '-SettleSeconds', '1', '-AllowUntrustedRoot')
        try {
            $result = Wait-Script (Start-SignRemote $first, $second)
            $againResult = Wait-Script (Start-SignRemote $again)
        }
        finally {
            Stop-Process -Id $signer.Process.Id
        }

        $result.ExitCode | Should -Be 0
        Get-Signer $first | Should -Be $certificate.Thumbprint
        Get-Signer $second | Should -Be $certificate.Thumbprint
        $againResult.ExitCode | Should -Be 0
        Get-Signer $again | Should -Be $certificate.Thumbprint
        @(Get-Content $signer.Log | Where-Object { $_ -like 'Signing * file(s)*' }) |
            Should -Be 'Signing 2 file(s) with one signtool call:', 'Signing 1 file(s) with one signtool call:'
    }

    It 'never picks a certificate the system does not trust' {
        $result = Wait-Script (Start-Script 'sign-pending.ps1' @('-Store', "`"$(Join-Path $TestDrive 'empty')`"",
            '-TimestampUrl', '""'))

        $result.Output | Should -Match 'Signing as |No trusted code-signing certificate'
        $result.Output | Should -Not -Match $certificate.Thumbprint
    }

    It 'signs nothing whose content differs from its key' {
        $swapped = Join-Path $TestDrive 'swapped'
        $file = New-UnsignedFile 'swapped.dll'
        Add-StoreEntry $swapped "unsigned/sample-repository/sample-app/v1.2.3/$('0' * 64)/swapped.dll" $file
        Add-StoreEntry $swapped 'unsigned/other-layout/swapped.dll' $file

        $result = Wait-Script (Start-Script 'sign-pending.ps1' @('-Store', "`"$swapped`"", '-CertificateThumbprint',
            $certificate.Thumbprint, '-TimestampUrl', '""', '-SettleSeconds', '0', '-AllowUntrustedRoot'))

        $result.ExitCode | Should -Be 0
        # The console wraps a warning wider than its window.
        $result.Output -replace '\r?\n', '' | Should -Match 'v1\.2\.3/0{64}/swapped\.dll does not hold the content its name gives'
        $result.Output | Should -Not -Match 'Signing \d+ file'
        Test-Path (Join-Path $swapped 'signed') | Should -BeFalse
    }

    It 'takes a signature only from the certificate that SIGNING_SUBJECT names' {
        $refused = New-UnsignedFile 'refused.dll'
        $accepted = New-UnsignedFile 'accepted.dll'

        $runs = (Start-SignRemote $refused @{ SIGNING_SUBJECT = 'CN=Someone Else' }),
            (Start-SignRemote $accepted @{ SIGNING_SUBJECT = 'CN=Build Script Tests' })
        Publish-Counterpart $refused
        Publish-Counterpart $accepted
        $refusal, $acceptance = $runs | ForEach-Object { Wait-Script $_ }

        $refusal.ExitCode | Should -Not -Be 0
        $refusal.Output | Should -Match 'refused\.dll\.signed is not signed by the expected certificate'
        Get-Signer $refused | Should -BeNullOrEmpty
        $acceptance.ExitCode | Should -Be 0
        Get-Signer $accepted | Should -Be $certificate.Thumbprint
    }

    It 'takes from the store only the uploaded file with a valid signature added' {
        $unsigned = New-UnsignedFile 'unsigned.dll'
        $substituted = New-UnsignedFile 'substituted.dll'

        $runs = (Start-SignRemote $unsigned), (Start-SignRemote $substituted)
        Publish-Counterpart $unsigned -Unsigned
        Publish-Counterpart $substituted -Content (New-UnsignedFile 'other.dll')
        $unsignedResult, $substitutedResult = $runs | ForEach-Object { Wait-Script $_ }

        $unsignedResult.ExitCode | Should -Not -Be 0
        $unsignedResult.Output | Should -Match 'unsigned\.dll\.signed carries no valid signature'
        Get-Signer $unsigned | Should -BeNullOrEmpty
        $substitutedResult.ExitCode | Should -Not -Be 0
        $substitutedResult.Output | Should -Match 'substituted\.dll\.signed is not substituted\.dll with a signature added'
        Get-Signer $substituted | Should -BeNullOrEmpty
    }

    It 'gives up when no signature comes in time' {
        $file = New-UnsignedFile 'late.dll'

        $result = Wait-Script (Start-SignRemote $file @{ SIGNING_TIMEOUT_MINUTES = '0.01' })

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'Not signed within 0\.01 min: late\.dll'
        Get-Signer $file | Should -BeNullOrEmpty
    }
}

Describe 'Clearing the signing store' {
    BeforeAll {
        Import-Module (Join-Path $build 'Signing.psm1') -Force

        function Get-EntryKey([string]$Side, [string]$File, [string]$Version = '1.2.3') {
            $hash = (Get-FileHash $File).Hash.ToLowerInvariant()
            "$Side/sample-repository/sample-app/v$Version/$hash/$(Split-Path -Leaf $File)"
        }

        function Get-StoreContent([string]$Store) {
            @('unsigned/', 'signed/' | ForEach-Object { Get-StoreFiles $Store $_ } | ForEach-Object Key | Sort-Object)
        }
    }

    It "removes a release's unsigned and signed files as its run ends and leaves other releases alone" {
        $cleared = Join-Path $TestDrive 'cleared'
        $hash = 'a' * 64
        $release = "unsigned/sample-repository/sample-app/v1.2.3/$hash/app.dll",
            "unsigned/sample-repository/sample-app/v1.2.3/$('b' * 64)/setup.exe",
            "signed/sample-repository/sample-app/v1.2.3/$hash/app.dll"
        $others = "unsigned/sample-repository/sample-app/v1.2.30/$hash/app.dll",
            "signed/sample-repository/other-app/v1.2.3/$hash/app.dll", 'unsigned/other-repository/v1.2.3/tool.exe'
        foreach ($key in $release + $others) { Add-StoreEntry $cleared $key }

        foreach ($side in 'unsigned', 'signed') {
            Remove-StoreFolder $cleared "$side/sample-repository/sample-app/v1.2.3"
        }

        Get-StoreContent $cleared | Should -Be @($others | Sort-Object)
    }

    It 'removes files that no release run can wait for any more instead of signing them, and signs the fresh ones' {
        $aged = Join-Path $TestDrive 'aged'
        $fresh = New-UnsignedFile 'fresh.dll'
        $abandoned = New-UnsignedFile 'abandoned.dll'
        $earlier = New-UnsignedFile 'earlier.dll'
        $kept = New-UnsignedFile 'kept.dll'
        Add-StoreEntry $aged (Get-EntryKey 'unsigned' $fresh) $fresh
        Add-StoreEntry $aged (Get-EntryKey 'unsigned' $abandoned) $abandoned -Abandoned
        Add-StoreEntry $aged (Get-EntryKey 'signed' $earlier '1.2.2') $earlier -Abandoned
        Add-StoreEntry $aged (Get-EntryKey 'unsigned' $kept) $kept -Abandoned
        Add-StoreEntry $aged 'unsigned/other-repository/v1.0.0/tool.exe' $earlier -Abandoned

        # Held open without delete sharing, it cannot be removed, as with a store key that may not delete.
        $lock = [System.IO.File]::Open((Join-Path $aged (Get-EntryKey 'unsigned' $kept)), 'Open', 'Read', 'Read')
        try {
            $result = Wait-Script (Start-Script 'sign-pending.ps1' @('-Store', "`"$aged`"", '-CertificateThumbprint',
                $certificate.Thumbprint, '-TimestampUrl', '""', '-SettleSeconds', '0', '-AllowUntrustedRoot'))
        }
        finally {
            $lock.Dispose()
        }

        $result.ExitCode | Should -Be 0
        $result.Output | Should -Match 'Removed unsigned/sample-repository/sample-app/v1\.2\.3/abandoned\.dll'
        $result.Output | Should -Match 'Removed signed/sample-repository/sample-app/v1\.2\.2/earlier\.dll'
        [regex]::Matches($result.Output, 'WARNING: unsigned/sample-repository/sample-app/v1\.2\.3/kept\.dll, ').Count |
            Should -Be 1
        $result.Output | Should -Match 'Signing 1 file\(s\)'
        Get-StoreContent $aged | Should -Be @((Get-EntryKey 'signed' $fresh), (Get-EntryKey 'unsigned' $fresh),
            (Get-EntryKey 'unsigned' $kept), 'unsigned/other-repository/v1.0.0/tool.exe' | Sort-Object)
        Get-Signer (Join-Path $aged (Get-EntryKey 'signed' $fresh)) | Should -Be $certificate.Thumbprint
    }
}
