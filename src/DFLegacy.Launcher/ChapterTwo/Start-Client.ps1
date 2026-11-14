param(
    [Parameter(Mandatory)][string]$Game,
    [Parameter(Mandatory)][string]$HostFile,
    [ValidateSet('legacy','sharp','point','linear')][string]$Filter = 'legacy',
    [ValidateSet('none','crt','xbrz')][string]$PostProcess = 'none',
    [int]$Width = 640,
    [int]$Height = 480,
    [ValidateSet('12','14','16','18')][int]$DialogueFontSize = 12,
    [ValidateRange(0,4)][int]$DialogueLineGap = 3,
    [string]$RemoteIpv4,
    [string]$RelayIpv4,
    [switch]$DetailedDiagnostics
)
$ErrorActionPreference = 'Stop'
$trace = Join-Path $PSScriptRoot 'dnf_startup_trace.exe'
$logs = Join-Path $Game 'Logs\DFLegacy'
$run = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$credentials = [Console]::In.ReadLine() | ConvertFrom-Json
$connectHost = '127.0.0.2'
if ($RelayIpv4) {
    if ($RemoteIpv4 -or $RelayIpv4 -cnotmatch '\A127\.73\.(?:[1-9][0-9]{0,2})\.(?:[1-9][0-9]{0,2})\z') { throw 'Invalid Chapter Two relay mode.' }
    $relayAddress = $null
    if (![Net.IPAddress]::TryParse($RelayIpv4, [ref]$relayAddress) -or $relayAddress.ToString() -cne $RelayIpv4) { throw 'Invalid Chapter Two relay IPv4.' }
    $connectHost = '127.0.0.1'
}
if ($RemoteIpv4) {
    $address = $null
    if (![Net.IPAddress]::TryParse($RemoteIpv4, [ref]$address) -or $address.AddressFamily -ne 'InterNetwork' -or
        $address.ToString() -cne $RemoteIpv4 -or [Net.IPAddress]::IsLoopback($address) -or
        $address.GetAddressBytes()[0] -eq 0 -or $address.GetAddressBytes()[0] -ge 224) { throw 'Invalid Chapter Two remote IPv4.' }
    $connectHost = $RemoteIpv4
}
$arguments = '99?{0}?42311?{1}?{2}?0?0?0?0?0?0?0?0' -f $connectHost, $credentials.Account, $credentials.Password
$credentials = $null

function Initialize-ChapterTwoLoginInfo([string]$GameDirectory) {
    # Retain the verified native startup precondition in ordinary desktop mode.
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

function Initialize-ChapterTwoNativeLogInterop {
    if ('DFLegacy.ChapterTwoNativeLogFiles' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace DFLegacy {
    public static class ChapterTwoNativeLogFiles {
        [StructLayout(LayoutKind.Sequential)] struct FileTime { public uint Low, High; }
        [StructLayout(LayoutKind.Sequential)] struct FileInfo {
            public uint Attributes; public FileTime Created, Accessed, Written;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
        public static void Copy(string source,string destination,DateTime notBefore) {
            bool created=false,complete=false;
            try {
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    FileInfo info;
                    if(!GetFileInformationByHandle(input.SafeFileHandle,out info)) throw new IOException("Cannot inspect native log handle.");
                    if((info.Attributes & 0x410)!=0 || info.Links!=1 || info.SizeHigh!=0 || info.SizeLow>16*1024*1024)
                        throw new IOException("Native log is not a bounded ordinary file.");
                    long written=((long)info.Written.High<<32)|info.Written.Low;
                    if(DateTime.FromFileTimeUtc(written)<notBefore) throw new IOException("Native log predates this process attempt.");
                    var actual=new StringBuilder(32768);
                    uint length=GetFinalPathNameByHandle(input.SafeFileHandle,actual,(uint)actual.Capacity,0);
                    if(length==0 || length>=actual.Capacity || !String.Equals(actual.ToString(),@"\\?\"+Path.GetFullPath(source),StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Native log handle resolved outside its expected path.");
                    using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                        created=true; input.CopyTo(output); output.Flush(true); complete=true;
                    }
                }
            } finally { if(created&&!complete) File.Delete(destination); }
        }
    }
}
'@
}

function Initialize-ChapterTwoSessionDirectory([string]$GameDirectory, [string]$Name) {
    if ($Name -cnotmatch '\A[AB]-[0-9]{8}-[0-9]{6}-[0-9]{3}-[1-9][0-9]?\z') {
        throw 'Invalid Chapter Two session name.'
    }
    $root = [IO.Path]::GetFullPath($GameDirectory)
    $ancestor = [IO.DirectoryInfo]::new($root)
    while ($null -ne $ancestor) {
        if (!$ancestor.Exists -or ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Chapter Two client directory must be an ordinary existing directory.'
        }
        $ancestor = $ancestor.Parent
    }
    foreach ($part in @('Logs','DFLegacy','Sessions')) {
        $root = Join-Path $root $part
        $null = [IO.Directory]::CreateDirectory($root)
        if ([IO.File]::GetAttributes($root) -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Chapter Two session parent is a reparse point.'
        }
    }
    $session = Join-Path $root $Name
    # New-Item (without Force) rejects an old session instead of mixing evidence.
    $null = New-Item -ItemType Directory -Path $session -ErrorAction Stop
    return $session
}

function Copy-ChapterTwoNativeLogs([string]$SessionFolder, [string]$Events, [string]$DestinationPrefix, [DateTime]$AttemptStartedUtc) {
    # Only the broker-owned journal selects the PID. Client stdout/path messages
    # never become filesystem input. Archiving must not replace a game failure.
    try {
        if (!$SessionFolder -or ![IO.File]::Exists($Events)) { return }
        $journal = Get-Item -LiteralPath $Events
        if ($journal.Length -gt 4096 -or ($journal.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid native log process journal.' }
        $text = [IO.File]::ReadAllText($Events)
        $starts = [regex]::Matches($text, '(?m)^START ([1-9][0-9]{0,9})\r?$')
        [uint32]$processId = 0
        if ($starts.Count -ne 1 -or !$text.StartsWith($starts[0].Value, [StringComparison]::Ordinal) -or
            ![uint32]::TryParse($starts[0].Groups[1].Value, [ref]$processId)) { throw 'Missing unique trusted native log PID.' }
        $directory = [IO.Path]::GetFullPath($SessionFolder)
        foreach ($component in @('', 'Temp', 'NativeLogs', [string]$processId)) {
            if ($component) { $directory = Join-Path $directory $component }
            if (![IO.Directory]::Exists($directory)) { return }
            $attributes = [IO.File]::GetAttributes($directory)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Native log directory is a reparse point.' }
        }
        Initialize-ChapterTwoNativeLogInterop
        foreach ($name in @('DNF.trc','NeopleEngine.LOG')) {
            $source = Join-Path $directory $name
            if (![IO.File]::Exists($source)) { continue }
            try {
                [DFLegacy.ChapterTwoNativeLogFiles]::Copy($source, "$DestinationPrefix-$name", $AttemptStartedUtc)
            } catch { Write-Warning "Could not archive Chapter Two native log $name`: $($_.Exception.Message)" }
        }
    } catch { Write-Warning "Could not archive Chapter Two native logs: $($_.Exception.Message)" }
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
    if ($RemoteIpv4) { $env:DFL_S1C2_REMOTE_IPV4 = $RemoteIpv4 }
    if ($RelayIpv4) { $env:DFL_S1C2_LOCAL_RELAY_IPV4 = $RelayIpv4 }
    if ($DetailedDiagnostics) { $env:DFL_S1C2_DETAILED_DIAGNOSTICS = '1' }
    $env:DFL_S1C2_DIALOGUE_SIZE = [string]$DialogueFontSize
    $env:DFL_S1C2_DIALOGUE_GAP = [string]$DialogueLineGap
    $env:DFL_S1C2_POST_PROCESS = $PostProcess
    if ($Filter -ne 'legacy' -or $PostProcess -ne 'none') {
        $env:DFL_S1C2_PRESENTATION_FILTER = $Filter
        $env:DFL_S1C2_WINDOW_WIDTH = [string]$Width
        $env:DFL_S1C2_WINDOW_HEIGHT = [string]$Height
    }
    try {
        $raw = [IO.File]::ReadAllBytes($hostFile)
        $offset = [BitConverter]::ToInt32($raw, 0x3c) + 8
        for ($attempt = 1; $attempt -le 48; $attempt++) {
            $candidate = Join-Path $Game "s1c2_chapter2_host-$instance-$run-$attempt.exe"
            $log = Join-Path $logs "chapter-two-client-$instance-$run-$attempt.log"
            $events = Join-Path $logs "chapter-two-events-$instance-$run-$attempt.log"
            $nativeLogPrefix = Join-Path $logs "chapter-two-native-$instance-$run-$attempt"
            $attemptStartedUtc = [DateTime]::UtcNow
            $nativeLogSession = Initialize-ChapterTwoSessionDirectory $Game "$instance-$run-$attempt"
            $env:DFL_S1C2_SESSION_DATA = $nativeLogSession
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
                    $env:DFLEGACY_CH2_DIAGNOSTIC_EVENTS = $events
                    [Console]::WriteLine(('CHAPTER_TWO_ATTEMPT {0} {1} {2}' -f [IO.Path]::GetFileName($candidate), [IO.Path]::GetFileName($log), [IO.Path]::GetFileName($events)))
                    [Console]::Out.Flush()
                    & $trace $candidate $arguments 2>&1 | ForEach-Object {
                        $line = [string]$_
                        if (!$line.StartsWith('LOCAL_PAY_REQUEST ', [StringComparison]::Ordinal)) { $writer.WriteLine($line) }
                        if ($line -cmatch '\A(?:OWNED_PID [1-9][0-9]{0,9}|LOCAL_PAY_REQUEST [1-9][0-9]{0,9} [1-9][0-9]{0,9} [a-z-]{1,32} (?:[0-9a-fA-F]{2,2048}|-))\z') {
                            [Console]::WriteLine($line)
                            [Console]::Out.Flush()
                        }
                    }
                    $traceExit = $LASTEXITCODE
                } finally {
                    $writer.Dispose()
                    $env:DFLEGACY_CH2_DIAGNOSTIC_EVENTS = $null
                }
                if (!$traceExit -and @((Select-String -LiteralPath $log -Pattern '^ORDINARY_DESKTOP_PROCESS appcontainer=0$')).Count -ne 1) {
                    throw "The Chapter Two startup component did not confirm an ordinary desktop process. See $log"
                }
                $retry = !$traceExit -and (Select-String -LiteralPath $log -SimpleMatch 'PREFERRED_BASE_UNAVAILABLE' -Quiet)
                [Console]::WriteLine(('CHAPTER_TWO_FINISH {0}' -f $(if ($retry) { 'retry' } else { 'final' })))
                [Console]::Out.Flush()
                if ($traceExit) { throw "Could not start Chapter Two. See $log" }
                if ($retry) { continue }
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
                Copy-ChapterTwoNativeLogs $nativeLogSession $events $nativeLogPrefix $attemptStartedUtc
                $env:DFL_S1C2_SESSION_DATA = $null
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
        $env:DFL_S1C2_SESSION_DATA = $null
    }
} finally {
    $arguments = $null
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
