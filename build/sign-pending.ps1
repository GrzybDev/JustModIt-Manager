#requires -Version 5.1
<#
.SYNOPSIS
Code-signs the release files that wait in the signing store, with this machine's certificate.

.DESCRIPTION
Signs every file a release left under unsigned/ in the store (build\sign-remote.ps1) with one signtool call, so a smart
card asks for its PIN once, checks the signatures and puts the files under signed/. A release sends two rounds, its
files and then the installer, so run it with -Watch until stopped with Ctrl+C. Files older than the longest release run
are removed instead of signed.

The certificate is the one given by -CertificateThumbprint, otherwise the newest trusted code-signing certificate with a
private key in the current user's store, where proCertum CardManager registers the certificate of a Certum card.

.EXAMPLE
build\sign-pending.ps1 -Watch
#>
[CmdletBinding()]
param(
    # s3://<bucket>[/<prefix>], read with the AWS CLI and its AWS_* settings, or a folder.
    [string]$Store = $(if ($env:SIGNING_STORE) { $env:SIGNING_STORE } else { 's3://codesign' }),

    [switch]$Watch,

    [int]$IntervalSeconds = 10,

    # A release uploads a round's files within seconds of each other; signing once none came for this long takes them all.
    [int]$SettleSeconds = 15,

    # The SHA-1 thumbprint of the certificate in the current user's store.
    [string]$CertificateThumbprint,

    # Empty signs without a timestamp, so the signatures expire with the certificate.
    [string]$TimestampUrl = 'http://time.certum.pl',

    [string]$SignToolPath,

    # Accepts a certificate that chains to no trusted root, as one made for tests does.
    [switch]$AllowUntrustedRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Signing.psm1') -Force

# release.yml's timeout-minutes: no run still waits for a file uploaded longer ago, so signing it would sign a file that
# no release asked for.
$abandonedAfterMinutes = 150

# Tried once, so that a key that may not delete warns once instead of on every pass until the lifecycle rule takes it.
$unremovable = @{}

function Find-SignTool {
    if ($SignToolPath) {
        if (-not (Test-Path -LiteralPath $SignToolPath)) { throw "$SignToolPath does not exist." }
        return $SignToolPath
    }
    $architecture = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -find "**\$architecture\signtool.exe" | Select-Object -First 1
        if ($found) { return $found }
    }
    foreach ($key in 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots') {
        $kits = Get-ItemProperty $key -Name KitsRoot10 -ErrorAction SilentlyContinue
        if (-not $kits) { continue }
        $found = Get-ChildItem (Join-Path $kits.KitsRoot10 'bin') -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -as [version] } | Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName "$architecture\signtool.exe" } | Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($found) { return $found }
    }
    throw 'signtool.exe was not found: install the Windows SDK with its signing tools, or pass -SignToolPath.'
}

function Find-Certificate {
    if ($CertificateThumbprint) {
        $certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
        if (-not $certificate) { throw "The current user's certificate store holds no certificate $CertificateThumbprint." }
        return $certificate
    }
    # A certificate left by an aborted test run is valid and newer than the card's, but every file it signs fails its check.
    $certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.HasPrivateKey -and (Test-TrustedCertificate $_) } |
        Sort-Object NotBefore -Descending | Select-Object -First 1
    if (-not $certificate) {
        throw ('No trusted code-signing certificate with a private key is in the current user''s store: insert the card ' +
            'and register its certificate with proCertum CardManager.')
    }
    return $certificate
}

# Also tells whether the certificate is valid now; revocation is not checked, so the card signs offline too.
function Test-TrustedCertificate([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    try {
        $chain.ChainPolicy.RevocationMode = 'NoCheck'
        return $chain.Build($Certificate)
    }
    finally {
        $chain.Dispose()
    }
}

# Other projects may keep files in the same store in another layout, which this script leaves alone.
function Get-PendingEntries {
    $cutoff = [DateTime]::UtcNow.AddMinutes(-$abandonedAfterMinutes)
    $fresh = @{ unsigned = @(); signed = @() }
    foreach ($side in 'unsigned', 'signed') {
        foreach ($file in @(Get-StoreFiles $Store "$side/")) {
            $entry = $file.Key.Substring($side.Length + 1)
            if ($entry -notmatch '^[^/]+/[^/]+/v[^/]+/[0-9a-f]{64}/[^/]+$') { continue }
            if ($file.Modified -ge $cutoff) {
                $fresh[$side] += $entry
                continue
            }
            if ($unremovable.Contains($file.Key)) { continue }
            $name = $file.Key -replace '/[0-9a-f]{64}/', '/'
            try {
                Remove-StoreFile $Store $file.Key
                Write-Host ("Removed $name, uploaded over $abandonedAfterMinutes min ago: no release run waits for it " +
                    'any more.')
            }
            catch {
                $unremovable[$file.Key] = $true
                Write-Warning ("$name, uploaded over $abandonedAfterMinutes min ago, is not signed, but it could not be " +
                    "removed: $($_.Exception.Message)")
            }
        }
    }
    $fresh.unsigned | Where-Object { $fresh.signed -notcontains $_ }
}

function Invoke-SigningRound([string[]]$Entries) {
    $work = Join-Path ([System.IO.Path]::GetTempPath()) "sign-pending-$PID"
    try {
        $files = @{}
        foreach ($entry in $Entries) {
            $path = Join-Path $work "$($files.Count)\$(Split-Path -Leaf $entry)"
            New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
            Receive-StoreFile $Store "unsigned/$entry" $path
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Split('/')[3]) {
                Write-Warning "unsigned/$entry does not hold the content its name gives, so it is left unsigned."
                continue
            }
            $files[$entry] = $path
        }
        if (-not $files.Count) { return }

        # Listed before the card asks for its PIN, so that a file that no running release asked for is noticed in time.
        Write-Host "Signing $($files.Count) file(s) with one signtool call:"
        $files.Keys | Sort-Object | ForEach-Object { Write-Host "  $($_ -replace '/[0-9a-f]{64}/', '/')" }
        & $signTool sign /sha1 $certificate.Thumbprint /s My /fd SHA256 @timestamp @($files.Values)
        if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE, so nothing was uploaded." }
        foreach ($path in $files.Values) {
            Confirm-CodeSignature $path -Thumbprint $certificate.Thumbprint -AllowUntrustedRoot:$AllowUntrustedRoot
        }
        foreach ($entry in $files.Keys) { Send-StoreFile $Store "signed/$entry" $files[$entry] }
        Write-Host "Uploaded the signed files to $Store."
    }
    finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$signTool = Find-SignTool
$certificate = Find-Certificate
$timestamp = if ($TimestampUrl) { @('/tr', $TimestampUrl, '/td', 'SHA256') } else { @() }
Write-Host "Signing as $($certificate.Subject) ($($certificate.Thumbprint)) with $signTool."
if ($Watch) { Write-Host "Watching $Store for files to sign; stop with Ctrl+C." }

do {
    try {
        $pending = @(Get-PendingEntries)
        if ($pending.Count) {
            do {
                $seen = $pending.Count
                Start-Sleep -Seconds $SettleSeconds
                $pending = @(Get-PendingEntries)
            } while ($pending.Count -gt $seen)
            Invoke-SigningRound $pending
        }
        elseif (-not $Watch) {
            Write-Host "No file waits for a signature in $Store."
        }
    }
    catch {
        if (-not $Watch) { throw }
        Write-Warning "$($_.Exception.Message) Trying again in $IntervalSeconds s."
    }
    if ($Watch) { Start-Sleep -Seconds $IntervalSeconds }
} while ($Watch)
