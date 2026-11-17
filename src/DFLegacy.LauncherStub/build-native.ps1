[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64')][string] $Architecture = 'x64',
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $RunTests,
    [string] $Version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../DFLegacy.Launcher/LauncherVersion.txt') -Raw).Trim(),
    [string] $Revision,
    [string] $BuildDirectory
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = if (Test-Path -LiteralPath $vswhere) {
    & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json |
        ConvertFrom-Json | Select-Object -First 1
}
$candidates = @(
    $env:CMAKE_EXE,
    $(if ($visualStudio) { Join-Path $visualStudio.installationPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' }),
    (Join-Path $env:ProgramFiles 'CMake/bin/cmake.exe'),
    (Get-Command cmake.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$cmake = $candidates | Select-Object -First 1
if (-not $cmake) { throw 'CMake 3.24 or newer and the Visual Studio C++ tools are required.' }
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $repositoryRoot 'artifacts/native/launcher-stub' }
$buildPath = [IO.Path]::GetFullPath($BuildDirectory)
if ($buildPath -eq [IO.Path]::GetPathRoot($buildPath) -or $buildPath -eq $repositoryRoot) {
    throw 'BuildDirectory must be a dedicated build subdirectory.'
}
$help = & $cmake --help
$generator = @('Visual Studio 18 2026', 'Visual Studio 17 2022', 'Visual Studio 16 2019') |
    Where-Object { $help -match [regex]::Escape($_) } |
    Where-Object { -not $visualStudio -or $_ -match "^Visual Studio $(([version]$visualStudio.installationVersion).Major) " } |
    Select-Object -First 1
if (-not $generator) { throw 'A Visual Studio C++ CMake generator is required.' }
$platform = if ($Architecture -eq 'x86') { 'Win32' } else { 'x64' }
& $cmake -S $PSScriptRoot -B $buildPath -G $generator -A $platform `
    "-DDFL_LAUNCHER_VERSION=$Version" "-DDFL_SOURCE_REVISION=$Revision"
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed: $LASTEXITCODE" }
& $cmake --build $buildPath --config $Configuration
if ($LASTEXITCODE -ne 0) { throw "Launcher stub build failed: $LASTEXITCODE" }
if ($RunTests) {
    & (Join-Path (Split-Path $cmake -Parent) 'ctest.exe') --test-dir $buildPath -C $Configuration --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw "Launcher stub tests failed: $LASTEXITCODE" }
}
