#requires -Version 5.1
<#
.SYNOPSIS
Has files code-signed on the machine that holds the certificate, through a signing store.

.DESCRIPTION
package.ps1 runs this for each round of files a release signs (its -SignTemplate). Each file goes to the store as
unsigned/<repository>/<app>/v<version>/<SHA-256 of the file>/<name in ASCII>; build\sign-pending.ps1, run where the
certificate is, signs it and puts it under signed/ with the same name. This script waits for them, checks that each one
is the uploaded file with a valid signature added and replaces the files in place.

The environment sets the store, SIGNING_STORE (s3://<bucket>[/<prefix>], with the AWS CLI's AWS_* variables, or a
folder); the release, SIGNING_REPOSITORY, SIGNING_APP and SIGNING_VERSION; how long to wait, SIGNING_TIMEOUT_MINUTES (60
by default); and, optionally, the subject that the signing certificate must have, SIGNING_SUBJECT.

.EXAMPLE
build\package.ps1 -App MenadzerSpolszczen -Version 1.2.3 -SignTemplate "pwsh -NoProfile -File build\sign-remote.ps1 {{file...}}"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$File,

    # Accepts a certificate that chains to no trusted root, as one made for tests does.
    [switch]$AllowUntrustedRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Signing.psm1') -Force

$store = $env:SIGNING_STORE
if (-not ($store -and $env:SIGNING_REPOSITORY -and $env:SIGNING_APP -and $env:SIGNING_VERSION)) {
    throw 'SIGNING_STORE, SIGNING_REPOSITORY, SIGNING_APP and SIGNING_VERSION must name the store and the release.'
}
$timeout = if ($env:SIGNING_TIMEOUT_MINUTES) { [double]$env:SIGNING_TIMEOUT_MINUTES } else { 60 }

$files = @(foreach ($path in $File) {
    $name = Split-Path -Leaf $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    # The AWS CLI's listings reach PowerShell in the console's code page, so keys stay ASCII; the hash tells files apart.
    $key = "$env:SIGNING_REPOSITORY/$env:SIGNING_APP/v$env:SIGNING_VERSION/$hash/$($name -replace '[^A-Za-z0-9._-]', '_')"
    [pscustomobject]@{ Path = $path; Name = $name; Key = $key }
})

foreach ($entry in $files) {
    # Not taken from an earlier run: sign-pending.ps1 removes an old signed file even while this run waits for others.
    Remove-StoreFile $store "signed/$($entry.Key)"
    Send-StoreFile $store "unsigned/$($entry.Key)" $entry.Path
}
Write-Host "Waiting up to $timeout min for $($files.Name -join ', ') to be signed by build\sign-pending.ps1 -Watch."
$waiting = $files
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$reported = 1
while ($waiting.Count) {
    $left = $timeout * 60 - $clock.Elapsed.TotalSeconds
    if ($left -le 0) { throw "Not signed within $timeout min: $($waiting.Name -join ', ')." }
    if ($clock.Elapsed.TotalMinutes -ge $reported) {
        Write-Host "Still waiting for $($waiting.Name -join ', ') to be signed, $reported of $timeout min."
        $reported++
    }
    Start-Sleep -Seconds ([Math]::Ceiling([Math]::Min(10, $left)))
    $waiting = @($waiting | Where-Object { -not (Test-StoreFile $store "signed/$($_.Key)") })
}

foreach ($entry in $files) {
    $signed = "$($entry.Path).signed"
    Receive-StoreFile $store "signed/$($entry.Key)" $signed
    try {
        Confirm-CodeSignature $signed -Subject $env:SIGNING_SUBJECT -AllowUntrustedRoot:$AllowUntrustedRoot
        if (-not (Test-SignedCopy $entry.Path $signed)) {
            throw "$(Split-Path -Leaf $signed) is not $($entry.Name) with a signature added."
        }
    }
    catch {
        Remove-Item -LiteralPath $signed
        throw
    }
    Move-Item -LiteralPath $signed $entry.Path -Force
}
Write-Host "Signed $($files.Name -join ', ')."
