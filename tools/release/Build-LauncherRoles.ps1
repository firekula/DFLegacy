[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$Revision,
    [Parameter(Mandatory)][string]$NativeInputs,
    [Parameter(Mandatory)][string]$PresentationHost,
    [Parameter(Mandatory)][string]$StartupBroker,
    [Parameter(Mandatory)][string]$D3D8Bridge,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
$null = New-Item -ItemType Directory -Path $output
$buildTemp = Join-Path $output 'temp'
$null = New-Item -ItemType Directory -Path $buildTemp
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$version = (Get-Content -LiteralPath (Join-Path $root 'src/DFLegacy.Launcher/LauncherVersion.txt') -Raw).Trim()
if ($version -cnotmatch '\A\d+\.\d+\.\d+\z') { throw 'Invalid launcher version.' }
$head = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $head -cne $Revision) { throw 'Revision must identify the repository checkout.' }
$changes = @(& git -C $root status --porcelain=v1)
if ($LASTEXITCODE -or $changes.Count) { throw 'The repository checkout must be clean.' }
$native = (Resolve-Path -LiteralPath $NativeInputs).Path
$hostExe = (Resolve-Path -LiteralPath $PresentationHost).Path
$brokerExe = (Resolve-Path -LiteralPath $StartupBroker).Path
$d3d8 = (Resolve-Path -LiteralPath $D3D8Bridge).Path
$pins = [ordered]@{
    (Join-Path $native 'DNF-rebuilt-v3-probe.exe') = '9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB'
    (Join-Path $native 's1c2_fixed_loader-weight-55c1cb8-runA.exe') = 'B72C2C7DFF350BCE0F8E5A178740CB5BE703BC9559956951BDBBF8E573A71139'
    (Join-Path $native 'Chapter2Launcher/dnf_startup_trace.exe') = '751AA409EC440FE987AB4462EEA057BD9BB3F650BA93FC6C1498D541F55F5079'
    $hostExe = '6AC7DBA3FBD4F20C83E75DDEEF3C07A6EBC894A33EF95A54366FB7CA49AA9E22'
    $brokerExe = '0AA08275D9FD539B2E96DE6C2958343489ABF8C2B325405D4A334B1554725849'
    $d3d8 = '25A0CEEB85FCC4675C6F964442EECC14FEDD504EFFD39C931A627249AD4685E2'
}
foreach ($file in $pins.Keys) {
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne $pins[$file]) { throw "Input identity differs: $file" }
}
$nativeOutput = Join-Path $output 'native'
$nativeArgs = @('-Architecture', 'x64', '-Version', $version, '-BuildManagedResources', '-BuildDirectory', $nativeOutput, '-Revision', $Revision)
& pwsh -NoLogo -NoProfile -NonInteractive -File (Join-Path $root 'src/DFLegacy.Startup/build-native.ps1') @nativeArgs *> (Join-Path $output 'native-build.log')
if ($LASTEXITCODE) { throw 'Native build failed. See native-build.log' }
$nugetConfig = Join-Path $output 'NuGet.Config'
[IO.File]::WriteAllText($nugetConfig, '<configuration><packageSources><clear /></packageSources></configuration>')

function Publish-Role([string]$role, [string]$resource, [string]$expectedOriginal, [string]$expectedDescription) {
    $publish = Join-Path $output "publish-$role"
    Push-Location -LiteralPath $root
    try {
        $arguments = @('publish', 'src/DFLegacy.Launcher/DFLegacy.Launcher.csproj',
            '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publish,
            '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded', '-p:PublishTrimmed=false',
            '-p:ContinuousIntegrationBuild=true', '-p:NuGetAudit=false', "-p:RestoreConfigFile=$nugetConfig",
            "-p:ArtifactsPath=$output/obj-$role", "-p:Version=$version", "-p:SourceRevisionId=$Revision",
            "-p:ChapterTwoPayloadDir=$native",
            "-p:ChapterTwoPresentationHost=$hostExe", "-p:ChapterTwoStartupBroker=$brokerExe",
            "-p:ChapterTwoD3D11Bridge=$d3d8",
            "-p:DFLegacyLauncherWin32Resource=$(Join-Path $nativeOutput $resource)")
        & dotnet @arguments *> (Join-Path $output "$role-build.log")
        if ($LASTEXITCODE) { throw "$role publish failed. See $role-build.log" }
    } finally { Pop-Location }
    $app = Get-Item -LiteralPath (Join-Path $publish 'DFLegacy.Launcher.exe')
    $info = $app.VersionInfo
    if ($info.OriginalFilename -cne $expectedOriginal -or $info.FileDescription -cne $expectedDescription -or
        $info.FileVersion -cne "$version.0" -or $info.ProductVersion -cne "$version+$Revision" -or
        $info.ProductName -cne 'DFLegacy' -or $info.CompanyName -cne 'FireKula') {
        throw "Incorrect $role metadata: $($info.OriginalFilename) / $($info.FileDescription) / $($info.FileVersion) / $($info.ProductVersion) / $($info.CompanyName)"
    }
    [pscustomobject]@{
        Role = $role; Version = $version; Revision = $Revision; Path = $app.FullName; Size = $app.Length
        Sha256 = (Get-FileHash -LiteralPath $app.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$results = @()
$results += Publish-Role 'app' 'app.res' 'DFLegacy.Launcher.App.exe' 'DFLegacy Launcher'
$results += Publish-Role 'updater' 'updater.res' 'DFLegacy.Updater.exe' 'DFLegacy Client Updater'
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'roles-evidence.json') -Encoding utf8NoBOM
Write-Output (Join-Path $output 'roles-evidence.json')



