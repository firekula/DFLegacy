param(
    [string]$RuntimeDirectory = $PSScriptRoot,
    [string]$DataPath,
    [string]$SnapshotDirectory,
    [string]$LogDirectory,
    [ValidateRange(5,120)][int]$ReadinessTimeoutSeconds = 30
)
# Windows PowerShell 5.1 compatible. Starts the server only, never a game client.
$ErrorActionPreference = 'Stop'
function Get-NormalPort {
    param($Value, [string]$Name)
    $number = 0
    if ([string]$Value -notmatch '^[0-9]+$' -or -not [int]::TryParse([string]$Value, [ref]$number) -or $number -lt 1 -or $number -gt 65535) { throw "Invalid $Name" }
    return $number
}
function New-NormalServerStartPlan {
    param([string]$RuntimeDirectory, [string]$DataPath, [string]$SnapshotDirectory, [System.Collections.IDictionary]$InheritedEnvironment = @{})
    $runtime = [IO.Path]::GetFullPath($RuntimeDirectory)
    $launcherPath = Join-Path $runtime 'launcher.json'
    $serverPath = Join-Path $runtime 'server.json'
    $launcher = Get-Content -LiteralPath $launcherPath -Encoding UTF8 -Raw | ConvertFrom-Json
    $server = Get-Content -LiteralPath $serverPath -Encoding UTF8 -Raw | ConvertFrom-Json
    $entrance = Get-NormalPort $launcher.EntrancePort 'launcher EntrancePort'
    $channel = Get-NormalPort $server.DFLegacy.Channel.Port 'server Channel.Port'
    if ($launcher.StartServer -isnot [bool] -or $launcher.StartServer -ne $false) { throw 'launcher.json must retain StartServer=false for this server-only helper' }
    if ($server.DFLegacy.Entrance.Enabled -ne $true -or $channel -ne 7001 -or $entrance -eq $channel) { throw 'Expected enabled handshake entrance distinct from normal channel7001' }
    $configuredData = [string]$server.DFLegacy.DataPath
    if ([string]::IsNullOrWhiteSpace($configuredData)) { throw 'server.json DataPath is required' }
    $effectiveData = if ([IO.Path]::IsPathRooted($configuredData)) { [IO.Path]::GetFullPath($configuredData) } else { [IO.Path]::GetFullPath((Join-Path $runtime $configuredData)) }
    $overrides = @{ 'DFLegacy__Entrance__Port' = [string]$entrance }
    if ($DataPath) {
        if (-not [IO.Path]::IsPathRooted($DataPath)) { throw 'Explicit DataPath must be absolute' }
        $effectiveData = [IO.Path]::GetFullPath($DataPath)
        $overrides['DFLegacy__DataPath'] = $effectiveData
    }
    if ($SnapshotDirectory) {
        if (-not [IO.Path]::IsPathRooted($SnapshotDirectory)) { throw 'Explicit SnapshotDirectory must be absolute' }
        $overrides['DFLegacy__Debug__ClientOverlay__SnapshotDirectory'] = [IO.Path]::GetFullPath($SnapshotDirectory)
    }
    [pscustomobject]@{
        RuntimeDirectory = $runtime; Executable = (Join-Path $runtime 'DFLegacy.Server.exe')
        LauncherConfig = $launcherPath; ServerConfig = $serverPath
        EntrancePort = $entrance; ChannelPort = $channel
        ConfiguredEntrancePort = (Get-NormalPort $server.DFLegacy.Entrance.Port 'server Entrance.Port')
        EffectiveDataPath = $effectiveData; ExplicitDataPath = [bool]$DataPath
        EnvironmentOverrides = $overrides
        EnvironmentRemovals = @($InheritedEnvironment.Keys | Where-Object { $_ -match '^(DFLegacy|ASPNETCORE_|DOTNET_)' })
    }
}
function Test-NormalServerReadiness {
    param($Plan, [string]$LogText, [string]$ErrorText, $Listeners, [int]$ProcessId)
    if (($LogText + "`n" + $ErrorText) -match '(?im)BackgroundService failed|StopHost|Application is shutting down|\bfail:|\bcrit:|Unhandled exception') { throw 'Server startup failure or shutdown was logged' }
    foreach ($port in @($Plan.ChannelPort, $Plan.EntrancePort)) {
        if ($LogText -notmatch ('(?m)Entrance listener ready at [^\r\n]*:' + $port + '\s*$')) { return $false }
        if (@($Listeners | Where-Object { $_.LocalPort -eq $port -and $_.OwningProcess -eq $ProcessId }).Count -eq 0) { return $false }
    }
    return $true
}
function Read-NormalServerLog {
    param([Parameter(Mandatory)][string]$Path)
    # Windows PowerShell's redirect writer remains open with write access.
    # The reader must permit that existing writer as well as request read access.
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}
function Save-NormalStartJson($Value, [string]$Path) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 8))
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}
function Get-NormalOwnedProcess($Record) {
    $process = Get-Process -Id $Record.Id -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    if ($process.Path -ne $Record.Path -or ($process.StartTime.ToUniversalTime() - ([datetime]$Record.StartTimeUtc).ToUniversalTime()).Duration().TotalMilliseconds -gt 1) { throw 'Recorded PID identity changed; refusing unrelated process' }
    return $process
}
if ($MyInvocation.InvocationName -eq '.') { throw 'Invoke this server-only helper; do not dot-source its entrypoint' }
$inherited = @{}
foreach ($value in Get-ChildItem Env:) { $inherited[$value.Name] = $value.Value }
$plan = New-NormalServerStartPlan $RuntimeDirectory $DataPath $SnapshotDirectory $inherited
if (-not (Test-Path -LiteralPath $plan.Executable -PathType Leaf)) { throw 'DFLegacy.Server.exe is missing beside the normal runtime configuration' }
$configHashes = @((Get-FileHash -LiteralPath $plan.LauncherConfig).Hash, (Get-FileHash -LiteralPath $plan.ServerConfig).Hash)
$lock = [IO.FileStream]::new((Join-Path $plan.RuntimeDirectory '.server-start.lock'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
$transaction = $null; $record = $null; $ready = $false
try {
    if (@(Get-Process -Name DFLegacy.Server -ErrorAction SilentlyContinue).Count) { throw 'A server is already running; inspect it instead of starting another' }
    if (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in @($plan.ChannelPort, $plan.EntrancePort) }).Count) { throw 'A required server port is already listening' }
    $logRoot = if ($LogDirectory) { [IO.Path]::GetFullPath($LogDirectory) } else { Join-Path $plan.RuntimeDirectory 'startup-logs' }
    $transaction = Join-Path $logRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($transaction)
    if ($SnapshotDirectory) { [void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($SnapshotDirectory)) }
    $stdout = Join-Path $transaction 'server.stdout.log'; $stderr = Join-Path $transaction 'server.stderr.log'
    Save-NormalStartJson ([pscustomobject]@{Status='prepared';Runtime=$plan.RuntimeDirectory;Executable=$plan.Executable;Arguments=@();EntrancePort=$plan.EntrancePort;ChannelPort=$plan.ChannelPort;EffectiveDataPath=$plan.EffectiveDataPath;ExplicitDataPath=$plan.ExplicitDataPath;EnvironmentOverrides=$plan.EnvironmentOverrides}) (Join-Path $transaction 'prepared.json')
    $saved = @{}
    $names = @(@($plan.EnvironmentRemovals) + @($plan.EnvironmentOverrides.Keys) | Select-Object -Unique)
    foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        foreach ($name in $plan.EnvironmentRemovals) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
        foreach ($name in $plan.EnvironmentOverrides.Keys) { [Environment]::SetEnvironmentVariable($name, $plan.EnvironmentOverrides[$name], 'Process') }
        $process = Start-Process -FilePath $plan.Executable -WorkingDirectory $plan.RuntimeDirectory -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    } finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    }
    $record = [pscustomobject]@{Id=$process.Id;Path=$process.Path;StartTimeUtc=$process.StartTime.ToUniversalTime().ToString('o')}
    Save-NormalStartJson $record (Join-Path $transaction 'server-process.json')
    if ($record.Path -ne $plan.Executable) { throw 'Started server executable path differs' }
    $deadline = [DateTime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds); $stableSince = $null
    do {
        Start-Sleep -Milliseconds 250
        if (-not (Get-NormalOwnedProcess $record)) { throw 'Server exited before readiness' }
        $logText = Read-NormalServerLog $stdout; $errorText = Read-NormalServerLog $stderr
        $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue)
        if (Test-NormalServerReadiness $plan $logText $errorText $listeners $record.Id) {
            if (-not $stableSince) { $stableSince = [DateTime]::UtcNow }
            $ready = ([DateTime]::UtcNow - $stableSince).TotalSeconds -ge 2
        } else { $stableSince = $null }
    } while (-not $ready -and [DateTime]::UtcNow -lt $deadline)
    if (-not $ready) { throw 'Both handshake and channel listeners failed to become stably ready' }
    if ((Get-FileHash -LiteralPath $plan.LauncherConfig).Hash -ne $configHashes[0] -or (Get-FileHash -LiteralPath $plan.ServerConfig).Hash -ne $configHashes[1]) { throw 'Configuration bytes changed during startup' }
    $result = [pscustomobject]@{Status='ready';Process=$record;Runtime=$plan.RuntimeDirectory;EntrancePort=$plan.EntrancePort;ChannelPort=$plan.ChannelPort;EffectiveDataPath=$plan.EffectiveDataPath;ExplicitDataPath=$plan.ExplicitDataPath;Stdout=$stdout;Stderr=$stderr;Transaction=$transaction;ConfigurationUnchanged=$true;ServerLeftRunning=$true}
    Save-NormalStartJson $result (Join-Path $transaction 'ready.json')
    Write-Output $result
} catch {
    $startupError = $_
    if ($record) {
        $own = Get-NormalOwnedProcess $record
        if ($own) { Stop-Process -InputObject $own -ErrorAction Stop; if (-not $own.WaitForExit(10000)) { throw 'Recorded failed-start server did not exit' } }
    }
    if ($transaction) { Save-NormalStartJson ([pscustomobject]@{Status='failed';Error=$startupError.Exception.Message;Process=$record;AutomaticRetry=$false}) (Join-Path $transaction 'failure.json') }
    throw $startupError
} finally { $lock.Dispose() }
