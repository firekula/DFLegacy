param(
    [string]$Broker,
    [string]$Fixture,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$EvidenceLog
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Specify a new isolated test output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null

function Assert-OrdinaryIdentity([string]$Text) {
    if ([regex]::Matches($Text, '(?m)^ORDINARY_DESKTOP_PROCESS appcontainer=0\r?$').Count -ne 1) {
        throw 'Expected exactly one verified ordinary-process identity marker; old AppContainer output is not accepted.'
    }
    if ($Text -match '(?m)^APPCONTAINER_NO_NETWORK_CAPABILITIES\r?$') { throw 'AppContainer identity is forbidden.' }
}

if ($EvidenceLog) {
    $evidence = (Resolve-Path -LiteralPath $EvidenceLog).Path
    $text = [IO.File]::ReadAllText($evidence)
    $record = "ARCHIVED_RUNTIME_EVIDENCE $evidence`r`nSHA256 $((Get-FileHash -LiteralPath $evidence -Algorithm SHA256).Hash)`r`n" + $text
    [IO.File]::WriteAllText((Join-Path $output 'archived-runtime.log'), $record)
    try { Assert-OrdinaryIdentity $text }
    catch {
        [IO.File]::WriteAllText((Join-Path $output 'result.log'), "RED: $($_.Exception.Message)`r`n")
        throw
    }
    throw 'RED evidence unexpectedly passed the ordinary identity gate.'
}
if (!$Broker -or !$Fixture) { throw 'Broker and Fixture are required for runtime verification.' }
$brokerPath = (Resolve-Path -LiteralPath $Broker).Path
$fixturePath = (Resolve-Path -LiteralPath $Fixture).Path
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class OrdinaryBrokerDesktopProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder value, uint length, out uint needed);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    public static string InputDesktop() {
        IntPtr h = OpenInputDesktop(0, false, 1);
        if (h == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try { var name = new StringBuilder(256); uint needed;
            if (!GetUserObjectInformation(h, 2, name, 512, out needed)) throw new System.ComponentModel.Win32Exception();
            return name.ToString();
        } finally { CloseDesktop(h); }
    }
}
'@
function Read-Desktop {
    $point = New-Object OrdinaryBrokerDesktopProbe+Point
    if (![OrdinaryBrokerDesktopProbe]::GetCursorPos([ref]$point)) { throw 'GetCursorPos failed.' }
    [pscustomobject]@{ Desktop = [OrdinaryBrokerDesktopProbe]::InputDesktop(); Foreground = [OrdinaryBrokerDesktopProbe]::GetForegroundWindow().ToInt64(); X = $point.X; Y = $point.Y }
}
$results = @()
foreach ($mode in @('normal', 'nonzero', 'crash', 'without-broker')) {
    $case = Join-Path $output $mode
    $session = Join-Path $case 'session with spaces'
    New-Item -ItemType Directory -Path $session | Out-Null
    $journal = Join-Path $case 'events.log'
    $prefix = Join-Path $case 'performance'
    $before = Read-Desktop
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $brokerPath
    $start.Arguments = '"' + $fixturePath + '" "' + $mode + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = [IO.Path]::GetDirectoryName($fixturePath)
    foreach ($key in @($start.EnvironmentVariables.Keys)) {
        if ($key -like 'DFL_S1C2_*' -or $key -like 'DFLEGACY_CH2_*' -or $key -like 'DFL_FIXTURE_*') { $start.EnvironmentVariables.Remove($key) }
    }
    $start.EnvironmentVariables['DFL_S1C2_SESSION_DATA'] = $session
    $start.EnvironmentVariables['DFL_FIXTURE_EXPECTED_SESSION'] = $session
    $start.EnvironmentVariables['DFL_FIXTURE_EXPECTED_DESKTOP'] = $before.Desktop
    $start.EnvironmentVariables['DFL_S1C2_PROBE_SECONDS'] = '10'
    $start.EnvironmentVariables['DFLEGACY_CH2_DIAGNOSTIC_EVENTS'] = $journal
    if ($mode -ne 'without-broker') {
        $start.EnvironmentVariables['DFL_S1C2_PROBE_BROKER'] = '1'
        $start.EnvironmentVariables['DFL_S1C2_PERF_LOG_PREFIX'] = $prefix
    }
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    if (!$process.Start()) { throw 'Broker did not start.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(20000)) { $process.Kill(); throw "Broker timeout in $mode" }
    $text = $stdoutTask.Result
    $stderr = $stderrTask.Result
    $exitCode = $process.ExitCode
    $process.Dispose()
    $after = Read-Desktop
    [IO.File]::WriteAllText((Join-Path $case 'stdout.log'), $text)
    [IO.File]::WriteAllText((Join-Path $case 'stderr.log'), $stderr)
    @{ Before = $before; After = $after } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $case 'desktop.json') -Encoding UTF8
    Assert-OrdinaryIdentity $text
    if ($exitCode -ne 0 -or $stderr) { throw "Broker did not preserve normal debugger exit in $mode" }
    if (($before | ConvertTo-Json -Compress) -ne ($after | ConvertTo-Json -Compress)) { throw "Desktop, foreground window or real mouse position changed during $mode; inspect desktop.json." }
    if ($text -notmatch '(?m)^OWNED_PID (\d+)\r?$') { throw 'Missing owned PID.' }
    $owned = $Matches[1]
    if ($mode -eq 'normal') {
        $guildEvents = [regex]::Matches($text, "(?m)^LOCAL_GUILD_OPEN $owned\r?$")
        $afterUnterminatedLine = [regex]::Matches($text, "(?m)^NATIVE_CHANNEL_BODY\r?\nLOCAL_GUILD_OPEN $owned\r?$")
        if ($guildEvents.Count -ne 2 -or $afterUnterminatedLine.Count -ne 1) {
            throw 'Guild browser event was not framed after the child left stdout without a newline.'
        }
    }
    if ($text -notmatch 'FIXTURE_IDENTITY appcontainer=0 desktop=unchanged session=unchanged') { throw 'Child identity/session check failed.' }
    if ($text -notmatch 'FIXTURE_HANDLED_EXCEPTION recovered') { throw 'Handled exception compatibility failed.' }
    $native = [IO.File]::ReadAllText((Join-Path $session 'fixture-native.log'))
    if ($native -ne "fixture pid=$owned`n") { throw 'Child log was not written to the inherited session.' }
    $events = [IO.File]::ReadAllText($journal)
    if ($events -notmatch "(?m)^START $owned`r?`$") { throw 'Missing trusted start event.' }
    $expectedExit = '00000000'
    if ($mode -eq 'nonzero') { $expectedExit = '00000005' }
    if ($mode -eq 'crash') { $expectedExit = 'C0000005' }
    if ($text -notmatch "(?im)^EXIT $expectedExit`r?`$" -or $events -notmatch "(?im)^EXIT $owned $expectedExit`r?`$") { throw "Incorrect exit status in $mode" }
    if ($mode -eq 'crash') {
        if ($events -notmatch "(?m)^EXCEPTION $owned \d+ C0000005 ") { throw 'Missing unhandled exception event.' }
    } elseif ($events -match '(?m)^EXCEPTION ') { throw 'Handled exception became an unhandled exception record.' }
    if ($mode -ne 'without-broker') {
        if ($text -notmatch 'FIXTURE_LOOPBACK bind=127.0.0.2 external=denied wrongport=denied') { throw 'Loopback boundary failed.' }
        if ($text -notmatch 'FIXTURE_FALLBACK connected' -or $text -notmatch 'GAME_BROKER_CONNECT port=47005 to=127.0.0.3:47005') { throw 'Isolated fallback route failed.' }
        foreach ($suffix in @('interval', 'total', 'slowframes')) {
            if ([IO.File]::ReadAllText("$prefix-$suffix.csv") -ne "fixture pid=$owned`n") { throw "Inherited $suffix log handle failed." }
        }
    }
    $results += [pscustomobject]@{ Mode = $mode; Pid = $owned; BrokerExit = $exitCode; ChildExitHex = $expectedExit; Identity = 'ordinary'; DesktopAndCursorUnchanged = $true }
}
$results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding UTF8
$failures = @()
foreach ($failure in @(
    @{ Name = 'missing-executable'; Arguments = '"' + (Join-Path $output 'missing-fixture.exe') + '" "normal"'; Exit = 3 },
    @{ Name = 'removed-sid-mode'; Arguments = '--sid ignored'; Exit = 3 },
    @{ Name = 'removed-cleanup-mode'; Arguments = '--cleanup'; Exit = 2 }
)) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $brokerPath
    $start.Arguments = $failure.Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($key in @($start.EnvironmentVariables.Keys)) {
        if ($key -like 'DFL_S1C2_*' -or $key -like 'DFLEGACY_CH2_*' -or $key -like 'DFL_FIXTURE_*') { $start.EnvironmentVariables.Remove($key) }
    }
    $start.EnvironmentVariables['DFL_S1C2_PROBE_BROKER'] = '1'
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    if (!$process.Start()) { throw 'Broker did not start for failure check.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(10000)) { $process.Kill(); throw "Broker hung in $($failure.Name)" }
    $text = $stdoutTask.Result
    $stderr = $stderrTask.Result
    $exitCode = $process.ExitCode
    $process.Dispose()
    [IO.File]::WriteAllText((Join-Path $output ($failure.Name + '.log')), "exit=$exitCode`r`n" + $text + $stderr)
    if ($exitCode -ne $failure.Exit -or $stderr -or $text -match 'ORDINARY_DESKTOP_PROCESS|OWNED_PID|S-1-15-') { throw "Unexpected failure behavior in $($failure.Name)" }
    if ($failure.Exit -eq 3 -and $text -notmatch '(?m)^CREATE_ERROR \d+\r?$') { throw 'Creation failure diagnostic was lost.' }
    $failures += [pscustomobject]@{ Mode = $failure.Name; BrokerExit = $exitCode; ChildStarted = $false }
}
$failures | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'failure-results.json') -Encoding UTF8
Write-Output "PASS ordinary broker runtime cases=$($results.Count) failure cases=$($failures.Count) output=$output"
