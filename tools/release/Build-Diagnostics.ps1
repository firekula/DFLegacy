[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$Revision,
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
$sdkRoot = (Get-ItemProperty -LiteralPath 'HKLM:/SOFTWARE/Microsoft/Windows Kits/Installed Roots').KitsRoot10
if (-not $sdkRoot) { throw '找不到 Windows SDK（诊断程序需要 rc.exe）。' }
$rc = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'bin') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/rc.exe' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if (-not $rc) { throw '找不到 rc.exe（Windows SDK 10）。' }
$icon = Join-Path $root 'src/DFLegacy.Launcher/DFLegacy.Launcher.ico'
if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) { throw "缺少图标：$icon" }
$template = Get-Content -LiteralPath (Join-Path $root 'src/DFLegacy.Diagnostics/DFLegacy.Diagnostics.rc.in') -Raw
$components = $version -replace '\.', ','
$product = "$version+$Revision"
$rendered = $template.Replace('@DFL_VERSION_COMPONENTS@', $components).
    Replace('@DFL_LAUNCHER_VERSION@', $version).
    Replace('@DFL_PRODUCT_VERSION@', $product).
    Replace('@DFL_DIAGNOSTICS_ICON@', ($icon -replace '\\', '/'))
$rcFile = Join-Path $output 'DFLegacy.Diagnostics.rc'
[IO.File]::WriteAllText($rcFile, $rendered)
$resource = Join-Path $output 'DFLegacy.Diagnostics.res'
& $rc /nologo /fo $resource $rcFile
if ($LASTEXITCODE) { throw "rc.exe failed: $LASTEXITCODE" }
$nugetConfig = Join-Path $output 'NuGet.Config'
[IO.File]::WriteAllText($nugetConfig, '<configuration><packageSources><clear /></packageSources></configuration>')
Push-Location -LiteralPath $root
try {
    & dotnet publish 'src/DFLegacy.Diagnostics/DFLegacy.Diagnostics.csproj' -c Release -r win-x64 --self-contained true -o $output `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:ContinuousIntegrationBuild=true -p:NuGetAudit=false "-p:RestoreConfigFile=$nugetConfig" `
        "-p:ArtifactsPath=$output/obj" -p:Version=$version "-p:SourceRevisionId=$Revision" `
        "-p:DiagnosticsWin32Resource=$resource" *> (Join-Path $output 'diagnostics-build.log')
    if ($LASTEXITCODE) { throw 'Diagnostics publish failed. See diagnostics-build.log' }
} finally { Pop-Location }
$exe = Get-Item -LiteralPath (Join-Path $output 'DFLegacy.Diagnostics.exe')
$info = $exe.VersionInfo
$expected = @{
    OriginalFilename = 'DFLegacy.Diagnostics.exe'
    FileDescription = 'DFLegacy Diagnostics'
    InternalName = 'DFLegacy.Diagnostics'
    ProductName = 'DFLegacy'
    CompanyName = 'FireKula'
    LegalCopyright = 'Copyright (C) 2026 FireKula'
    FileVersion = "$version.0"
    ProductVersion = "$version+$Revision"
}
foreach ($name in $expected.Keys) {
    if ($info.$name -cne $expected[$name]) { throw "Incorrect diagnostics metadata: $name=$($info.$name), expected $($expected[$name])" }
}
$evidence = [pscustomobject]@{
    Role = 'diagnostics'; Version = $version; Revision = $Revision; Path = $exe.FullName; Size = $exe.Length
    Sha256 = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    FileDescription = $info.FileDescription; OriginalFilename = $info.OriginalFilename
    InternalName = $info.InternalName; ProductName = $info.ProductName; CompanyName = $info.CompanyName
    LegalCopyright = $info.LegalCopyright; FileVersion = $info.FileVersion; ProductVersion = $info.ProductVersion
}
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'diagnostics-evidence.json') -Encoding utf8NoBOM
Write-Output (Join-Path $output 'diagnostics-evidence.json')
