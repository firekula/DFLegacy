# Run only in an isolated game copy after extracting the matching starter ZIP.
# Compatible with Windows PowerShell 5.1 and PowerShell 7. No network requests.
[CmdletBinding()]
param([string]$ClientDirectory = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Runtime.Serialization
$root = (Resolve-Path -LiteralPath $ClientDirectory).ProviderPath
$configPath = Join-Path $root 'launcher.json'
$manifestPath = Join-Path $root 'client-package.json'
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if ($manifest.flavor -notin @('normal', 'test')) { throw 'Unrecognized starter package flavor.' }

# Use the platform JSON reader's typed XML mapping to retain JSON scalar types,
# arbitrary nested objects, large numbers, and date strings without conversion.
$existed = [IO.File]::Exists($configPath)
$original = if ($existed) { [IO.File]::ReadAllBytes($configPath) } else {
    [Text.Encoding]::UTF8.GetBytes('{"Account":"","Password":"","StartServer":false,"ClientPath":"DNF.exe"}')
}
if ($original.Length -gt 4MB) { throw 'launcher.json is too large; original file was preserved.' }
$reader = [Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader(
    $original, [Xml.XmlDictionaryReaderQuotas]::Max)
$document = New-Object Xml.XmlDocument
try { $document.Load($reader) } finally { $reader.Dispose() }
if ($document.DocumentElement.GetAttribute('type') -ne 'object') {
    throw 'launcher.json must be a JSON object; original file was preserved.'
}
$seen = @{}
foreach ($child in $document.DocumentElement.ChildNodes) {
    $name = if ($child.HasAttribute('item')) { $child.GetAttribute('item') } else { $child.LocalName }
    if ($seen.ContainsKey($name)) { throw 'Duplicate configuration key; original file was preserved.' }
    $seen[$name] = $child
}
$changes = [ordered]@{
    UpdateProvider = 'gitee'; UpdateRepository = 'firekula/DFLegacy'; UpdateChannel = 'gitee-test'
    UpdateFlavor = [string]$manifest.flavor; DisableGitHubUpdates = $false
}
foreach ($name in $changes.Keys) {
    $node = $document.CreateElement($name)
    $node.SetAttribute('type', $(if ($changes[$name] -is [bool]) { 'boolean' } else { 'string' }))
    $node.InnerText = $(if ($changes[$name] -is [bool]) { 'false' } else { $changes[$name] })
    if ($seen.ContainsKey($name)) { [void]$document.DocumentElement.ReplaceChild($node, $seen[$name]) }
    else { [void]$document.DocumentElement.AppendChild($node) }
}
$buffer = New-Object IO.MemoryStream
$writer = [Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonWriter($buffer, [Text.Encoding]::UTF8, $false, $true)
try { $document.Save($writer); $writer.Flush(); $updated = $buffer.ToArray() }
finally { $writer.Dispose(); $buffer.Dispose() }
$temporary = Join-Path $root ('launcher.json.enable-' + [Guid]::NewGuid().ToString('N') + '.tmp')
$backup = $configPath + '.backup-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
try {
    [IO.File]::WriteAllBytes($temporary, $updated)
    if ($existed) {
        [IO.File]::Replace($temporary, $configPath, $backup)
        Write-Output ('Original bytes backed up to: ' + $backup)
    } else { [IO.File]::Move($temporary, $configPath) }
} finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
Write-Output ('Gitee test channel enabled (' + $manifest.flavor + '). Existing credentials and other settings were preserved.')
Write-Output 'The next launcher update check will contact firekula/DFLegacy on Gitee. Use only an isolated game copy.'
