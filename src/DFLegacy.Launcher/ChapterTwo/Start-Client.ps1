param(
    [Parameter(Mandatory)][string]$Game,
    [Parameter(Mandatory)][string]$HostFile,
    [ValidateSet('legacy','sharp','point','linear')][string]$Filter = 'legacy',
    [int]$Width = 640,
    [int]$Height = 480,
    [ValidateSet('12','14','16','18')][int]$DialogueFontSize = 12,
    [ValidateRange(0,4)][int]$DialogueLineGap = 3
)
$ErrorActionPreference = 'Stop'
$trace = Join-Path $PSScriptRoot 'dnf_startup_trace.exe'
$logs = Join-Path $Game 'Logs\DFLegacy'
$run = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$credentials = [Console]::In.ReadLine() | ConvertFrom-Json
$arguments = '99?127.0.0.2?42311?{0}?{1}?0?0?0?0?0?0?0?0' -f $credentials.Account, $credentials.Password
$credentials = $null

function Initialize-ChapterTwoLoginInfo([string]$GameDirectory) {
    # The RX game profile cannot create this native startup file itself.
    # Never copy credentials or reopen/truncate an existing login file.
    $path = Join-Path $GameDirectory 'loginInfo.inf'
    if ([IO.File]::Exists($path)) { return }
    try {
        $file = [IO.File]::Open($path, 'CreateNew', 'Write', 'None')
        $file.Dispose()
    } catch [IO.IOException] {
        # Another instance may have created it after the existence check.
        # Only an actual file-exists collision is safe to ignore.
        $errorCode = $_.Exception.HResult -band 0xffff
        if ($errorCode -in @(80,183) -and [IO.File]::Exists($path)) { return }
        throw
    }
}

function Set-ProfileAcl([string]$Operation, [string]$Entry) {
    $aclMutex = [Threading.Mutex]::new($false, 'Local\DFLegacy.S1C2.FixedHostProbe.Acl')
    try {
        try { $null = $aclMutex.WaitOne() } catch [Threading.AbandonedMutexException] {}
        try {
            & "$env:SystemRoot\System32\icacls.exe" $Game $Operation $Entry | Add-Content -LiteralPath (Join-Path $logs "chapter-two-acl-$instance-$run.log")
            if ($LASTEXITCODE) { throw 'Could not update the local game profile permissions.' }
        } finally { $aclMutex.ReleaseMutex() }
    } finally { $aclMutex.Dispose() }
}

$mutex = $null
foreach ($instance in @('A','B')) {
    $candidateMutex = [Threading.Mutex]::new($false, "Local\DFLegacy.S1C2.PartyProbe.$instance")
    try { $locked = $candidateMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if ($locked) { $mutex = $candidateMutex; break }
    $candidateMutex.Dispose()
}
if (!$mutex) { throw 'Both Chapter Two client instances are already in use.' }

try {
    Initialize-ChapterTwoLoginInfo $Game
    Get-ChildItem Env: | Where-Object Name -like 'DFL_S1C2_*' |
        ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null, 'Process') }
    $settings = @{
        DFL_S1C2_REGIONAL_PROBE = '1'; DFL_S1C2_INSTANCE = $instance; DFL_S1C2_DUAL_UDP_PROBE = '1'
        DFL_S1C2_PROBE_BROKER = '1'; DFL_S1C2_VENDOR_STACK = '4'; DFL_S1C2_PROBE_SECONDS = '0'
        DFL_S1C2_TRIAL_CHANNEL_CIPHER = '1'; DFL_S1C2_VENDOR_RESULT_ONE = '1'
        DFL_S1C2_TRIAL_SECOND_CALLBACK_ONE = '1'; DFL_S1C2_TRIAL_FOLLOWUP_CALLBACK_ONE = '1'
    }
    foreach ($name in $settings.Keys) {
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    $env:DFL_S1C2_DIALOGUE_SIZE = [string]$DialogueFontSize
    $env:DFL_S1C2_DIALOGUE_GAP = [string]$DialogueLineGap
    if ($Filter -ne 'legacy') {
        $env:DFL_S1C2_PRESENTATION_FILTER = $Filter
        $env:DFL_S1C2_WINDOW_WIDTH = [string]$Width
        $env:DFL_S1C2_WINDOW_HEIGHT = [string]$Height
    }
    $sid = (& $trace --sid ignore | Out-String).Trim()
    if ($LASTEXITCODE -or $sid -notmatch '^S-1-15-2(-[0-9]+){7}$') {
        throw 'Could not create the isolated Windows game profile.'
    }
    try {
        Set-ProfileAcl '/grant' "*$($sid):(OI)(CI)(RX)"
        $raw = [IO.File]::ReadAllBytes($hostFile)
        $offset = [BitConverter]::ToInt32($raw, 0x3c) + 8
        for ($attempt = 1; $attempt -le 48; $attempt++) {
            $candidate = Join-Path $Game "s1c2_chapter2_host-$instance-$run-$attempt.exe"
            $log = Join-Path $logs "chapter-two-client-$instance-$run-$attempt.log"
            $performancePrefix = Join-Path $logs "chapter-two-perf-$instance-$run-$attempt"
            # Vary Windows image placement only when the required address was occupied.
            $tag = [BitConverter]::ToUInt32([guid]::NewGuid().ToByteArray(), 0)
            [BitConverter]::GetBytes($tag).CopyTo($raw, $offset)
            $file = [IO.File]::Open($candidate, 'CreateNew', 'Write', 'None')
            try {
                try { $file.Write($raw, 0, $raw.Length) }
                finally { $file.Dispose() }
                $writer = [IO.StreamWriter]::new($log, $false, [Text.UTF8Encoding]::new($false))
                try {
                    $env:DFL_S1C2_PERF_LOG_PREFIX = $performancePrefix
                    & $trace $candidate $arguments 2>&1 | ForEach-Object {
                        $line = [string]$_
                        if (!$line.StartsWith('LOCAL_PAY_REQUEST ', [StringComparison]::Ordinal)) { $writer.WriteLine($line) }
                        if ($line -cmatch '\A(?:OWNED_PID [1-9][0-9]{0,9}|LOCAL_PAY_REQUEST [1-9][0-9]{0,9} [1-9][0-9]{0,9} [a-z-]{1,32} (?:[0-9a-fA-F]{2,2048}|-))\z') {
                            [Console]::WriteLine($line)
                            [Console]::Out.Flush()
                        }
                    }
                    $traceExit = $LASTEXITCODE
                } finally { $writer.Dispose() }
                if ($traceExit) { throw "Could not start Chapter Two. See $log" }
                if (Select-String -LiteralPath $log -SimpleMatch 'PREFERRED_BASE_UNAVAILABLE' -Quiet) { continue }
                if (Select-String -LiteralPath $log -Pattern '^LOAD_IMPORT |^MISSING_IMPORT |^CREATE_ERROR |^STOP_BEFORE_PROTECTION_MODULE ' -Quiet) {
                    throw "The Chapter Two startup log reported a failure. See $log"
                }
                # The broker can exit successfully after the game has crashed.
                $targetExits = [regex]::Matches([IO.File]::ReadAllText($log), '(?m)^EXIT ([^\r\n]*)\r?$')
                if (!$targetExits.Count) { throw "The Chapter Two game did not report its exit status. See $log" }
                foreach ($targetExit in $targetExits) {
                    $exitCode = $targetExit.Groups[1].Value
                    if ($exitCode -cnotmatch '\A[0-9a-fA-F]{1,8}[ \t]*\z') {
                        throw "The Chapter Two game reported an invalid exit status. See $log"
                    }
                    if ([Convert]::ToUInt32($exitCode.Trim(), 16) -ne 0) {
                        throw "The Chapter Two game exited with code $exitCode. See $log"
                    }
                }
                Write-Output "Chapter Two instance $instance log: $log"
                return
            } finally {
                Remove-Item -LiteralPath $candidate -ErrorAction SilentlyContinue
                foreach ($scope in 'interval','total','slowframes') {
                    $emptyLog = Get-Item -LiteralPath "$performancePrefix-$scope.csv" -ErrorAction SilentlyContinue
                    if ($null -ne $emptyLog -and $emptyLog.Length -eq 0) {
                        Remove-Item -LiteralPath $emptyLog.FullName -ErrorAction SilentlyContinue
                    }
                }
            }
        }
        throw "Windows could not allocate the required startup address. See $logs"
    } finally {
        try { Set-ProfileAcl '/remove:g' "*$sid" }
        finally { & $trace --cleanup }
    }
} finally {
    $arguments = $null
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
