param(
    [Parameter(Mandatory)][string]$PvfPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$pvf = (Resolve-Path -LiteralPath $PvfPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$archive = "$output.zip"
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $archive)) {
    throw 'Choose a new output directory and archive name.'
}
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the source checkout.' }

function Publish-Project([string]$Project, [string]$Destination) {
    dotnet publish (Join-Path $repo "src/$Project/$Project.csproj") -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded `
        -p:PublishTrimmed=false -p:ContinuousIntegrationBuild=true -p:BuildRandomNativeReference=false -o $Destination
    if ($LASTEXITCODE) { throw "Failed to publish $Project." }
}

Push-Location -LiteralPath $repo
try {
    Publish-Project 'DFLegacy.Manager' $output
    $server = Join-Path $output 'runtime/server'
    Publish-Project 'DFLegacy.Server' $server
    $defaultConfig = Join-Path $server 'server.json'
    if (Test-Path -LiteralPath $defaultConfig -PathType Leaf) { Remove-Item -LiteralPath $defaultConfig }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'templates') -Destination (Join-Path $output 'templates') -Recurse
    foreach ($file in @('Start.cmd', 'Start.ps1', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $output $file)
    }
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $output 'LICENSE')
    $resources = New-Item -ItemType Directory -Path (Join-Path $output 'resources')
    Copy-Item -LiteralPath $pvf -Destination (Join-Path $resources.FullName 'Script.pvf')
    @{ version = "第二章 Windows 测试版 $($commit.Substring(0,7))"; sourceCommit = $commit } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding UTF8
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($output, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    Get-FileHash -LiteralPath $archive -Algorithm SHA256
}
finally { Pop-Location }
