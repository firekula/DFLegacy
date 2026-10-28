param(
    [Parameter(Mandatory)][string]$HelperPath,
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [Parameter(Mandatory)][string]$FailedStartupLog
)
$ErrorActionPreference = 'Stop'
# Load function definitions only. The helper's entrypoint is never invoked.
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($HelperPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Helper parse errors' }
foreach ($name in @('Get-NormalPort', 'New-NormalServerStartPlan', 'Test-NormalServerReadiness', 'Read-NormalServerLog')) {
    $function = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false) | Where-Object Name -EQ $name)
    if ($function.Count -eq 1) { . ([scriptblock]::Create($function[0].Extent.Text)) }
}
if (Test-Path -LiteralPath $FixtureDirectory) { throw 'Preserve existing fixtures' }
[void][IO.Directory]::CreateDirectory($FixtureDirectory)
$runtime = Join-Path $FixtureDirectory 'runtime'
[void][IO.Directory]::CreateDirectory($runtime)
$launcher = Join-Path $runtime 'launcher.json'
$server = Join-Path $runtime 'server.json'
[IO.File]::WriteAllText($launcher, '{"EntrancePort":27001,"StartServer":false}')
[IO.File]::WriteAllText($server, '{"DFLegacy":{"Entrance":{"Enabled":true,"Port":2311},"Channel":{"Port":7001},"DataPath":"data/state.json"}}')
$before = @((Get-FileHash -LiteralPath $launcher).Hash, (Get-FileHash -LiteralPath $server).Hash)
$script:Failures = 0; $script:Checks = 0
function Check([bool]$Value, [string]$Name) {
    $script:Checks++
    if (-not $Value) { $script:Failures++; Write-Output ('FAIL: ' + $Name) }
}
function Rejects([scriptblock]$Action, [string]$Name) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Check $rejected $Name
}
$plan = New-NormalServerStartPlan -RuntimeDirectory $runtime
Check ($plan.EntrancePort -eq 27001) 'launcher EntrancePort27001 overrides original server2311 without editing JSON'
$failedLog = [IO.File]::ReadAllText($FailedStartupLog)
Rejects { Test-NormalServerReadiness -Plan $plan -LogText $failedLog -ErrorText '' -Listeners @([pscustomobject]@{LocalPort=7001;OwningProcess=123}) -ProcessId 123 } 'actual7001-ready then BackgroundService10013/StopHost log must reject startup'
Check (((Get-FileHash -LiteralPath $launcher).Hash -eq $before[0]) -and ((Get-FileHash -LiteralPath $server).Hash -eq $before[1])) 'planning preserves both config byte streams'
Check ($plan.EffectiveDataPath -eq [IO.Path]::GetFullPath((Join-Path $runtime 'data/state.json'))) 'default production DataPath resolves from original server configuration'
Check (-not $plan.ExplicitDataPath -and -not $plan.EnvironmentOverrides.ContainsKey('DFLegacy__DataPath')) 'normal startup does not inject a test DataPath'
$isolated = Join-Path $FixtureDirectory 'isolated-state.json'
$snapshots = Join-Path $FixtureDirectory 'snapshots'
$explicit = New-NormalServerStartPlan -RuntimeDirectory $runtime -DataPath $isolated -SnapshotDirectory $snapshots -InheritedEnvironment @{'DFLegacy__DataPath'='stale-test.json';'DFLegacy__Entrance__Port'='2311';'ASPNETCORE_URLS'='stale';'DOTNET_CONTENTROOT'='stale';'PATH'='kept'}
Check ($explicit.ExplicitDataPath -and $explicit.EffectiveDataPath -eq $isolated -and $explicit.EnvironmentOverrides['DFLegacy__DataPath'] -eq $isolated) 'explicit isolated path replaces inherited stale test path in child plan'
Check ($explicit.EnvironmentOverrides['DFLegacy__Entrance__Port'] -eq '27001') 'child entrance override is actual launcher port'
Check ($explicit.EnvironmentOverrides['DFLegacy__Debug__ClientOverlay__SnapshotDirectory'] -eq $snapshots) 'explicit snapshot path is child scoped'
Check ($explicit.EnvironmentRemovals.Count -eq 4 -and $explicit.EnvironmentRemovals -notcontains 'PATH') 'remove inherited application/host overrides without removing unrelated environment'
Rejects { New-NormalServerStartPlan -RuntimeDirectory $runtime -DataPath 'relative-state.json' } 'relative explicit DataPath refused'
Rejects { New-NormalServerStartPlan -RuntimeDirectory $runtime -SnapshotDirectory 'relative-snapshots' } 'relative explicit SnapshotDirectory refused'
$readyLog = "info: Entrance listener ready at 0.0.0.0:7001`r`ninfo: Entrance listener ready at 0.0.0.0:27001`r`n"
$both = @([pscustomobject]@{LocalPort=7001;OwningProcess=123}, [pscustomobject]@{LocalPort=27001;OwningProcess=123})
Check (Test-NormalServerReadiness $plan $readyLog '' $both 123) 'both readiness messages and both listeners owned by recorded PID pass'
Check (-not (Test-NormalServerReadiness $plan $readyLog '' @($both[0]) 123)) '7001 alone is insufficient even with both log messages'
Check (-not (Test-NormalServerReadiness $plan $readyLog '' @($both[0],[pscustomobject]@{LocalPort=27001;OwningProcess=999}) 123)) 'foreign PID handshake listener refused'
Check (-not (Test-NormalServerReadiness $plan $readyLog '' $both 999)) 'wrong recorded PID refused'
Check (-not (Test-NormalServerReadiness $plan 'info: Entrance listener ready at 0.0.0.0:7001' '' $both 123)) 'listeners without handshake readiness log insufficient'
Rejects { Test-NormalServerReadiness $plan $readyLog 'Unhandled exception: socket bind failed' $both 123 } 'stderr startup failure overrides listeners'
Rejects { Test-NormalServerReadiness $plan ($readyLog + 'Application is shutting down') '' $both 123 } 'shutdown after both readiness messages refused'
foreach ($bad in @('{"EntryPort":27001,"StartServer":false}', '{"EntrancePort":0,"StartServer":false}', '{"EntrancePort":65536,"StartServer":false}', '{"EntrancePort":27001.5,"StartServer":false}', '{"EntrancePort":27001,"StartServer":true}', '{"EntrancePort":7001,"StartServer":false}')) {
    [IO.File]::WriteAllText($launcher, $bad)
    Rejects { New-NormalServerStartPlan -RuntimeDirectory $runtime } ('invalid launcher rejected: ' + $bad)
}
[IO.File]::WriteAllText($launcher, '{"EntrancePort":27001,"StartServer":false}')
foreach ($channel in @('stdout', 'stderr')) {
    $logPath = Join-Path $FixtureDirectory ($channel + '-active-writer.log')
    # Model the real Windows redirect writer: write access remains open while
    # readers are permitted. A reader must also share the existing write access.
    $writer = [IO.FileStream]::new($logPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $initial = [Text.Encoding]::UTF8.GetBytes($channel + '-first')
        $writer.Write($initial, 0, $initial.Length); $writer.Flush($true)
        $legacyRejected = $false
        try { [IO.File]::ReadAllText($logPath) | Out-Null } catch [IO.IOException] { $legacyRejected = $true }
        Check $legacyRejected ($channel + ': original ReadAllText rejects real open writer')
        $read = $null
        try { $read = Read-NormalServerLog $logPath } catch [IO.IOException] { }
        Check ($read -eq ($channel + '-first')) ($channel + ': production reader reads while writer stays open')
        $append = [Text.Encoding]::UTF8.GetBytes('-second')
        $writer.Write($append, 0, $append.Length); $writer.Flush($true)
        $read = $null
        try { $read = Read-NormalServerLog $logPath } catch [IO.IOException] { }
        Check ($read -eq ($channel + '-first-second')) ($channel + ': subsequent production read observes flushed append')
    } finally { $writer.Dispose() }
    $exclusive = [IO.FileStream]::new($logPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $exclusive.Dispose()
    Check $true ($channel + ': no reader handle leaked after writer closes')
}
Write-Output ('normal server startup pure tests: {0} checks, {1} failures; no helper entrypoint/process action' -f $script:Checks, $script:Failures)
if ($script:Failures) { exit 1 }
