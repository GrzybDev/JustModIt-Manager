#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Rid = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root "build\$Rid"
$bin = Join-Path $root "bin\$Rid"

if (-not (Test-Path (Join-Path $root 'xdelta\xdelta3\xdelta3.h')) -or -not (Test-Path (Join-Path $root 'xz\CMakeLists.txt'))) {
    git -C $root submodule update --init --depth 1 -- xdelta xz
    if ($LASTEXITCODE -ne 0) { throw 'git submodule update failed' }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio with the C++ x64 toolset was not found' }

$arch = if ($Rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
$cmakeDir = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake'
# A batch file sidesteps PowerShell 5.1 mangling nested quotes when passing a command line to cmd.exe.
$batch = Join-Path ([IO.Path]::GetTempPath()) "xd3shim-build-$PID.cmd"
@(
    '@echo off',
    "call `"$vs\VC\Auxiliary\Build\vcvarsall.bat`" $arch >nul || exit /b 1",
    "set `"PATH=$cmakeDir\CMake\bin;$cmakeDir\Ninja;%PATH%`"",
    "cmake -S `"$root\xd3shim`" -B `"$build`" -G Ninja -DCMAKE_BUILD_TYPE=$Configuration || exit /b 1",
    "cmake --build `"$build`" || exit /b 1"
) | Set-Content -Path $batch -Encoding Ascii

try {
    cmd /c "`"$batch`""
    if ($LASTEXITCODE -ne 0) { throw "native build failed ($LASTEXITCODE)" }
}
finally {
    Remove-Item $batch -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item (Join-Path $build 'xd3shim.dll') $bin -Force
Write-Host "xd3shim.dll -> $bin"
