param([switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
Get-ChildItem Env: | Where-Object {
    $_.Name -match '^(DFLegacy|DFL_S1C2_|ASPNETCORE_|Kestrel__|DOTNET_ENVIRONMENT$|DOTNET_BUNDLE_EXTRACT_BASE_DIR$|URLS$)'
} | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null, 'Process') }

$manager = Join-Path $PSScriptRoot 'DFLegacy.Manager.exe'
$data = Join-Path $PSScriptRoot 'data'
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $data 'runtime-cache'
$managerArguments = @('--root', $PSScriptRoot, '--data-dir', $data, '--listen', '127.0.0.2', '--port', '48092')
if ($NoBrowser) { $managerArguments += '--no-browser' }
& $manager @managerArguments
exit $LASTEXITCODE
