#requires -Version 5.1

Set-StrictMode -Version Latest

# A store is s3://<bucket>[/<prefix>], reached through the AWS CLI with its AWS_* settings, or a local folder.
function Get-S3Location([string]$Store) {
    if ($Store -match '^s3://(?<bucket>[^/]+)/*(?<prefix>.*?)/*$') {
        $prefix = if ($Matches.prefix) { "$($Matches.prefix)/" } else { '' }
        return [pscustomobject]@{ Bucket = $Matches.bucket; Prefix = $prefix }
    }
}

function Invoke-Aws([string[]]$Arguments) {
    if (-not (Get-Command 'aws' -ErrorAction SilentlyContinue)) { throw 'An s3:// store needs the AWS CLI (aws).' }
    $output = & aws @Arguments
    if ($LASTEXITCODE -ne 0) { throw "aws $($Arguments[0]) $($Arguments[1]) failed with exit code $LASTEXITCODE." }
    return $output
}

function Send-StoreFile([string]$Store, [string]$Key, [string]$Path) {
    $s3 = Get-S3Location $Store
    if ($s3) {
        Invoke-Aws @('s3', 'cp', $Path, "s3://$($s3.Bucket)/$($s3.Prefix)$Key", '--only-show-errors') | Out-Null
        return
    }
    $target = Join-Path $Store $Key
    # Moved into place from the same volume, so a file is never listed while it is still being written.
    $partial = Join-Path $Store ".partial\$([guid]::NewGuid())"
    New-Item -ItemType Directory -Force (Split-Path -Parent $partial), (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $Path $partial
    # Dated by the upload, as S3 dates an object, since a copy keeps its source's time.
    (Get-Item -LiteralPath $partial).LastWriteTimeUtc = [DateTime]::UtcNow
    Move-Item -LiteralPath $partial $target -Force
}

function Receive-StoreFile([string]$Store, [string]$Key, [string]$Path) {
    $s3 = Get-S3Location $Store
    if ($s3) { Invoke-Aws @('s3', 'cp', "s3://$($s3.Bucket)/$($s3.Prefix)$Key", $Path, '--only-show-errors') | Out-Null }
    else { Copy-Item -LiteralPath (Join-Path $Store $Key) $Path -Force }
}

# Each file's key and the UTC time it was uploaded.
function Get-StoreFiles([string]$Store, [string]$Prefix) {
    $s3 = Get-S3Location $Store
    if ($s3) {
        $objects = (Invoke-Aws @('s3api', 'list-objects-v2', '--bucket', $s3.Bucket, '--prefix', "$($s3.Prefix)$Prefix",
            '--query', 'Contents[].{Key: Key, Modified: LastModified}', '--output', 'json')) -join "`n" | ConvertFrom-Json
        # PowerShell 7 reads the time as a date, Windows PowerShell as text; the cast takes either.
        return $objects | Where-Object { $_ } | ForEach-Object {
            [pscustomobject]@{
                Key = $_.Key.Substring($s3.Prefix.Length); Modified = ([datetime]$_.Modified).ToUniversalTime()
            }
        }
    }
    $folder = Join-Path $Store $Prefix
    if (-not (Test-Path -LiteralPath $folder)) { return }
    $base = [System.IO.Path]::GetFullPath($Store).TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $folder -Recurse -File | ForEach-Object {
        [pscustomobject]@{ Key = $_.FullName.Substring($base.Length).Replace('\', '/'); Modified = $_.LastWriteTimeUtc }
    }
}

function Test-StoreFile([string]$Store, [string]$Key) {
    if (Get-S3Location $Store) { return @(Get-StoreFiles $Store $Key | ForEach-Object { $_.Key }) -contains $Key }
    return Test-Path -LiteralPath (Join-Path $Store $Key)
}

# Like S3's, removing a file that is not there succeeds.
function Remove-StoreFile([string]$Store, [string]$Key) {
    $s3 = Get-S3Location $Store
    if ($s3) {
        Invoke-Aws @('s3', 'rm', "s3://$($s3.Bucket)/$($s3.Prefix)$Key", '--only-show-errors') | Out-Null
        return
    }
    $path = Join-Path $Store $Key
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

# Removes every file under the folder; the slash keeps v1.2.3 from taking v1.2.30 with it.
function Remove-StoreFolder([string]$Store, [string]$Folder) {
    $prefix = "$($Folder.Trim('/'))/"
    $s3 = Get-S3Location $Store
    if ($s3) {
        $uri = "s3://$($s3.Bucket)/$($s3.Prefix)$prefix"
        Invoke-Aws @('s3', 'rm', $uri, '--recursive', '--only-show-errors') | Out-Null
        return
    }
    $path = Join-Path $Store $prefix
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

function Confirm-CodeSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string]$Thumbprint,

        [string]$Subject,

        # A certificate made for tests chains to no trusted root, which Windows reports as an unknown error.
        [switch]$AllowUntrustedRoot
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $name = Split-Path -Leaf $Path
    if (-not ($signature.Status -eq 'Valid' -or ($AllowUntrustedRoot -and $signature.Status -eq 'UnknownError' -and
        $signature.SignerCertificate))) {
        throw "$name carries no valid signature: $($signature.StatusMessage)"
    }
    $signer = $signature.SignerCertificate
    if (($Thumbprint -and $signer.Thumbprint -ne $Thumbprint) -or ($Subject -and $signer.Subject -ne $Subject)) {
        throw "$name is not signed by the expected certificate but by $($signer.Subject) ($($signer.Thumbprint))."
    }
}

# Signing a PE file changes only its checksum and the certificate table's directory entry, and appends the table after
# padding to 8 bytes, so any other difference means that the signed file was made from another file.
function Test-SignedCopy([string]$Unsigned, [string]$Signed) {
    $original = [System.IO.File]::ReadAllBytes($Unsigned)
    $copy = [System.IO.File]::ReadAllBytes($Signed)
    $length = $original.Length
    if ($copy.Length -le $length) { return $false }
    $optionalHeader = [BitConverter]::ToInt32($original, 0x3C) + 24
    $pe32Plus = [BitConverter]::ToUInt16($original, $optionalHeader) -eq 0x20B
    $directory = $optionalHeader + $(if ($pe32Plus) { 144 } else { 128 })
    $table = [BitConverter]::ToUInt32($copy, $directory)
    $tableEnd = $table + [BitConverter]::ToUInt32($copy, $directory + 4)
    foreach ($bytes in $original, $copy) {
        [Array]::Clear($bytes, $optionalHeader + 64, 4)
        [Array]::Clear($bytes, $directory, 8)
    }
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $same = [Convert]::ToBase64String($sha256.ComputeHash($original)) -eq
        [Convert]::ToBase64String($sha256.ComputeHash($copy, 0, $length))
    return $same -and $table -ge $length -and $table -lt $length + 8 -and $tableEnd -eq $copy.Length
}

Export-ModuleMember -Function Send-StoreFile, Receive-StoreFile, Get-StoreFiles, Test-StoreFile, Remove-StoreFile,
    Remove-StoreFolder, Confirm-CodeSignature, Test-SignedCopy
