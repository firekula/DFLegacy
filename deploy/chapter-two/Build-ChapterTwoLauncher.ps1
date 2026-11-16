param(
    [Parameter(Mandatory)][string]$NativeAssetsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$PresentationHost,
    [Parameter(Mandatory)][string]$StartupBroker,
    [string]$D3D11Bridge,
    [string]$Version,
    [switch]$LocalTest
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if (-not $Version) {
    $Version = (Get-Content -LiteralPath (Join-Path $root 'src/DFLegacy.Launcher/LauncherVersion.txt') -Raw).Trim()
}
$native = (Resolve-Path -LiteralPath $NativeAssetsDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new isolated output directory.' }
$files = @{
    'DNF-rebuilt-v3-probe.exe' = '9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB'
    's1c2_fixed_loader-weight-55c1cb8-runA.exe' = 'B72C2C7DFF350BCE0F8E5A178740CB5BE703BC9559956951BDBBF8E573A71139'
    'Chapter2Launcher/dnf_startup_trace.exe' = '751AA409EC440FE987AB4462EEA057BD9BB3F650BA93FC6C1498D541F55F5079'
}
foreach ($entry in $files.GetEnumerator()) {
    $file = Join-Path $native $entry.Key
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Chapter Two input identity differs. $file"
    }
}
$nativeProperties = @()
if ($LocalTest) { $nativeProperties += '-p:ChapterTwoLocalTest=true' }
$optionalAssets = @()
if ($PresentationHost) {
    $preview = (Resolve-Path -LiteralPath $PresentationHost).Path
    $nativeProperties += "-p:ChapterTwoPresentationHost=$preview"
    $optionalAssets += $preview
}
if ($StartupBroker) {
    $broker = (Resolve-Path -LiteralPath $StartupBroker).Path
    $nativeProperties += "-p:ChapterTwoStartupBroker=$broker"
    $optionalAssets += $broker
}
if ($D3D11Bridge) {
    $bridge = (Resolve-Path -LiteralPath $D3D11Bridge).Path
    if ((Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash -ne '25A0CEEB85FCC4675C6F964442EECC14FEDD504EFFD39C931A627249AD4685E2') {
        throw 'Specify the unmodified dgVoodoo2 2.87.6 MS/x86/D3D8.dll.'
    }
    $nativeProperties += "-p:ChapterTwoD3D11Bridge=$bridge"
    $optionalAssets += $bridge
}

$package = Join-Path $output 'package'
New-Item -ItemType Directory -Path $package | Out-Null
$resourceDirectory = Join-Path $output 'native'
& pwsh -NoProfile -File (Join-Path $root 'src/DFLegacy.Startup/build-native.ps1') `
    -Architecture x64 -Version $Version -BuildManagedResources -BuildDirectory $resourceDirectory *> (Join-Path $output 'native-build.log')
if ($LASTEXITCODE) { throw "Chapter Two Windows resources build failed. See $output/native-build.log" }
$resource = Join-Path $resourceDirectory 'standalone.res'
Push-Location -LiteralPath $root
try {
    $build = @('publish', 'src/DFLegacy.Launcher/DFLegacy.Launcher.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded', '-p:PublishTrimmed=false',
        '-p:ContinuousIntegrationBuild=true', "-p:ArtifactsPath=$output/obj", "-p:ChapterTwoPayloadDir=$native",
        "-p:Version=$Version", "-p:DFLegacyLauncherWin32Resource=$resource", '-o', $package)
    & dotnet @build @nativeProperties *> (Join-Path $output 'build.log')
    if ($LASTEXITCODE) { throw "Chapter Two launcher build failed. See $output/build.log" }
} finally { Pop-Location }
if ($optionalAssets.Count) {
    Get-FileHash -LiteralPath $optionalAssets -Algorithm SHA256 | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $output 'native-inputs.json') -Encoding utf8
}

$exe = Join-Path $package 'DFLegacy.Chapter2.Launcher.exe'
Move-Item -LiteralPath (Join-Path $package 'DFLegacy.Launcher.exe') -Destination $exe
$metadata = (Get-Item -LiteralPath $exe).VersionInfo
if ($metadata.FileVersion -ne "$Version.0" -or $metadata.OriginalFilename -ne (Split-Path -Leaf $exe)) {
    throw 'The published executable has incorrect Windows version information.'
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-Launcher.md') -Destination (Join-Path $package 'README.md')
$packageName = if ($LocalTest) { 'DFLegacy-Chapter2-LocalTest-Windows-x64.zip' } else { 'DFLegacy-Chapter2-Launcher-GUI-Windows-x64.zip' }
$zip = Join-Path $output $packageName
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Fastest, $false)
Get-FileHash -LiteralPath $zip, $exe -Algorithm SHA256 | ForEach-Object {
    '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path)
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output $zip
