[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $EvidenceDirectory,
    [ValidateSet('CMake', 'MSBuild')]
    [string] $BuildSystem = 'CMake',
    [string] $CMake = $env:CMAKE_EXE,
    [string] $MSBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

function Invoke-Logged([string] $Executable, [string[]] $Arguments, [string] $Log) {
    & $Executable @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $Log -Tail 30
        throw "Build command failed ($LASTEXITCODE); see $Log"
    }
}

if ($BuildSystem -eq 'CMake' -and -not $CMake) {
    $CMake = (Get-Command cmake.exe -ErrorAction Stop).Source
}
if ($BuildSystem -eq 'MSBuild' -and -not $MSBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $MSBuild = Join-Path $installation 'MSBuild/Current/Bin/MSBuild.exe'
}

$projects = @(
    @{ Name = 'random'; Directory = 'DFLegacy.RandomNative'; Platform = 'x64'; Files = @('DFLegacy.RandomNative.dll') },
    @{ Name = 'ijl15'; Directory = 'DFLegacy.Ijl15'; Platform = 'Win32'; Files = @('ijl15.dll', 'debug.dll') }
)
if ($BuildSystem -eq 'MSBuild') {
    $projects[1].Files = @('ijl15.dll')
    $projects += @{ Name = 'debug'; Directory = 'DFLegacy.Debug'; Platform = 'Win32'; Files = @('debug.dll') }
}

$results = @()
foreach ($project in $projects) {
    $build = Join-Path $evidenceRoot "$BuildSystem/$($project.Name)"
    New-Item -ItemType Directory -Force -Path $build | Out-Null
    $source = Join-Path $repositoryRoot "src/$($project.Directory)"
    if ($BuildSystem -eq 'CMake') {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $version = [version](& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion)
        $generator = switch ($version.Major) { 18 { 'Visual Studio 18 2026' } 17 { 'Visual Studio 17 2022' } 16 { 'Visual Studio 16 2019' } default { throw "Unsupported VS version: $version" } }
        Invoke-Logged $CMake @('-S', $source, '-B', $build, '-G', $generator, '-A', $project.Platform) (Join-Path $build 'configure.log')
    }
    $rounds = @()
    foreach ($round in 1, 2) {
        $log = Join-Path $build "clean-build-$round.log"
        if ($BuildSystem -eq 'CMake') {
            Invoke-Logged $CMake @('--build', $build, '--config', 'Release', '--clean-first', '--parallel', '4') $log
            $output = Join-Path $build 'Release'
        }
        else {
            $output = Join-Path $build 'out'
            $intermediate = Join-Path $build 'obj'
            Invoke-Logged $MSBuild @((Join-Path $source "$($project.Directory).vcxproj"), '/t:Rebuild', '/p:Configuration=Release', "/p:Platform=$($project.Platform)", "/p:OutDir=$output/", "/p:IntDir=$intermediate/", '/m', '/v:minimal') $log
        }
        $artifacts = @{}
        foreach ($file in $project.Files) {
            $path = Join-Path $output $file
            $snapshot = Join-Path $build "round-$round-$file"
            Copy-Item -LiteralPath $path -Destination $snapshot -Force
            $bytes = [IO.File]::ReadAllBytes($snapshot)
            $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
            $artifacts[$file] = @{sha256 = (Get-FileHash -LiteralPath $snapshot -Algorithm SHA256).Hash; peTimeDateStamp = [BitConverter]::ToUInt32($bytes, $peOffset + 8)}
        }
        $rounds += $artifacts
    }
    foreach ($file in $project.Files) {
        $result = [ordered]@{buildSystem = $BuildSystem; project = $project.Name; file = $file; first = $rounds[0][$file]; second = $rounds[1][$file]; identical = $rounds[0][$file].sha256 -eq $rounds[1][$file].sha256}
        $results += $result
        Write-Host "$BuildSystem $file identical=$($result.identical) first=$($result.first.sha256) second=$($result.second.sha256)"
    }
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidenceRoot "$BuildSystem-results.json") -Encoding utf8
if ($results.Where({ -not $_.identical }).Count -gt 0) {
    throw "Native output changed between clean builds; see $evidenceRoot/$BuildSystem-results.json"
}
