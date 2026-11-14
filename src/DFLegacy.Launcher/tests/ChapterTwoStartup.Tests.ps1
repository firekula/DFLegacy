param(
    [string]$Source = (Join-Path $PSScriptRoot '../ChapterTwo/Start-Client.ps1'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../dist/chapter-two-main-sync-20261110/startup-file'),
    [string]$RunLabel = 'tests'
)
$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$testRoot = Join-Path $outputRoot ($RunLabel + '-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$script:checks = 0
$script:failures = 0
$script:results = [Collections.Generic.List[object]]::new()

function Check([bool]$Condition, [string]$Message) {
    $script:checks++
    if (!$Condition) { $script:failures++; Write-Output "FAIL: $Message" }
}

function Run-Case([string]$Name, [scriptblock]$Body) {
    $before = $script:failures
    try { & $Body }
    catch { Check $false "$Name threw unexpectedly: $($_.Exception.Message)" }
    $script:results.Add([pscustomobject]@{ Name=$Name; Passed=($before -eq $script:failures) })
}

# These records are copied literally from the provenance-listed native logs.
# The fake broker never executes the candidate: it reports only these fixtures.
# The identity marker is the new ordinary-process contract; other records retain
# the original fixture values. The fixture does not execute a real game.
$ordinary = 'ORDINARY_DESKTOP_PROCESS appcontainer=0'
$normal = @($ordinary, 'OWNED_PID 47568', 'EXIT 00000000')
$crash = @($ordinary, 'OWNED_PID 40032', 'EXCEPTION code=c0000005 first=0 at=0000000077112600 count=2 info=1,24', 'EXIT c0000005')
$preferred = @($ordinary, 'OWNED_PID 9652', 'PREFERRED_BASE_UNAVAILABLE error=487', 'EXIT 00000005')

function New-Case([string]$Name) {
    $root = Join-Path $testRoot $Name
    $game = Join-Path $root 'Game'
    $null = New-Item -ItemType Directory -Path (Join-Path $game 'Logs/DFLegacy')
    $helper = Join-Path $root 'Start-Client.ps1'
    # Only mutex names change, so test instances cannot claim real game slots.
    $code = [IO.File]::ReadAllText($sourcePath).Replace('Local\DFLegacy.S1C2.', ('Local\DFLegacy.StartupTest.' + [guid]::NewGuid().ToString('N') + '.'))
    [IO.File]::WriteAllText($helper, $code)
    $hostFile = Join-Path $root 'nonexecutable-fixture.bin'
    $raw = [byte[]]::new(128)
    [BitConverter]::GetBytes([int]64).CopyTo($raw, 0x3c)
    [IO.File]::WriteAllBytes($hostFile, $raw)
    $ini = Join-Path $game 'DFighter.ini'
    [IO.File]::WriteAllBytes($ini, [byte[]](0,255,17,128,10,13))
    return [pscustomobject]@{ Root=$root; Game=$game; Helper=$helper; Host=$hostFile; Ini=$ini }
}

function Invoke-Case($Case, [object[]]$Attempts, [int]$BrokerExit = 0, [switch]$DetailedDiagnostics, [string]$PostProcess = '') {
    $state = [pscustomobject]@{
        Launches=0; LoginAtLaunch=[Collections.Generic.List[bool]]::new()
        CandidateAtLaunch=[Collections.Generic.List[bool]]::new()
        Acls=[Collections.Generic.List[string]]::new(); Cleanups=0; Attempts=$Attempts; BrokerExit=$BrokerExit
        DetailedAtLaunch=[Collections.Generic.List[bool]]::new(); EventsAtLaunch=[Collections.Generic.List[string]]::new()
        Sessions=[Collections.Generic.List[string]]::new(); ProfileCalls=0
        PostProcesses=[Collections.Generic.List[string]]::new()
        DialogueSizes=[Collections.Generic.List[string]]::new()
    }
    $broker = Join-Path $Case.Root 'dnf_startup_trace.exe'
    $acl = Join-Path $env:SystemRoot 'System32/icacls.exe'
    $brokerFunction = 'Function:\' + $broker
    $aclFunction = 'Function:\' + $acl
    $originalInput = [Console]::In
    $environment = @{}
    Get-ChildItem Env: | Where-Object Name -like 'DFL_S1C2_*' | ForEach-Object { $environment[$_.Name] = $_.Value }
    $beforeIni = [Convert]::ToBase64String([IO.File]::ReadAllBytes($Case.Ini))
    $gamePath = $Case.Game
    Set-Item -LiteralPath $brokerFunction -Value {
        param([string]$Candidate, [string]$Arguments)
        if ($Candidate -eq '--sid') { $state.ProfileCalls++; 'S-1-15-2-1-2-3-4-5-6-7'; $global:LASTEXITCODE=0; return }
        if ($Candidate -eq '--cleanup') { $state.Cleanups++; $global:LASTEXITCODE=0; return }
        $state.LoginAtLaunch.Add([IO.File]::Exists((Join-Path $gamePath 'loginInfo.inf')))
        $state.CandidateAtLaunch.Add([IO.File]::Exists($Candidate))
        $state.DetailedAtLaunch.Add($env:DFL_S1C2_DETAILED_DIAGNOSTICS -eq '1')
        $state.EventsAtLaunch.Add($env:DFLEGACY_CH2_DIAGNOSTIC_EVENTS)
        $state.Sessions.Add($env:DFL_S1C2_SESSION_DATA)
        $state.PostProcesses.Add($env:DFL_S1C2_POST_PROCESS)
        $state.DialogueSizes.Add($env:DFL_S1C2_DIALOGUE_SIZE)
        $index = [Math]::Min($state.Launches, $state.Attempts.Count - 1)
        $state.Launches++
        foreach ($line in $state.Attempts[$index]) { $line }
        $global:LASTEXITCODE=$state.BrokerExit
    }.GetNewClosure()
    Set-Item -LiteralPath $aclFunction -Value {
        param([string]$Path, [string]$Operation, [string]$Entry)
        $state.Acls.Add("$Operation $Entry")
        $global:LASTEXITCODE=0
    }.GetNewClosure()
    $errorText = $null
    $output = @()
    try {
        [Console]::SetIn([IO.StringReader]::new('{"Account":"startup-fixture","Password":"synthetic-test-only"}'))
        $extra = @{}
        if ($PostProcess) { $extra.PostProcess = $PostProcess }
        try { $output = @(& $Case.Helper -Game $Case.Game -HostFile $Case.Host -DetailedDiagnostics:$DetailedDiagnostics @extra) }
        catch { $errorText = $_.Exception.Message }
    } finally {
        [Console]::SetIn($originalInput)
        Remove-Item -LiteralPath $brokerFunction,$aclFunction
        Get-ChildItem Env: | Where-Object Name -like 'DFL_S1C2_*' | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null, 'Process') }
        foreach ($entry in $environment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
    }
    Check (([Convert]::ToBase64String([IO.File]::ReadAllBytes($Case.Ini))) -ceq $beforeIni) 'startup preserves DFighter.ini bytes'
    Check ($state.Acls.Count -eq 0 -and $state.ProfileCalls -eq 0 -and $state.Cleanups -eq 0) 'ordinary launch does not create container profiles or change permissions'
    Check (@(Get-ChildItem -LiteralPath $Case.Game -Filter 's1c2_chapter2_host-*.exe').Count -eq 0) 'candidate files are cleaned'
    Check (!$env:DFLEGACY_CH2_DIAGNOSTIC_EVENTS) 'diagnostic journal path is cleared after the broker returns'
    foreach ($enabled in $state.DetailedAtLaunch) { Check ($enabled -eq [bool]$DetailedDiagnostics) 'detailed broker collection requires the explicit launch switch' }
    foreach ($path in $state.EventsAtLaunch) { Check ($path.StartsWith((Join-Path $Case.Game 'Logs\DFLegacy\chapter-two-events-'))) 'diagnostic journal stays in the selected client log directory' }
    foreach ($path in $state.Sessions) {
        Check (!!$path -and $path.StartsWith((Join-Path $Case.Game 'Logs\DFLegacy\Sessions\')) -and [IO.Directory]::Exists($path)) 'each attempt supplies an existing session root inside the selected client logs'
    }
    Check (@($state.Sessions | Select-Object -Unique).Count -eq $state.Sessions.Count) 'retry attempts cannot share scratch or native log directories'
    Check (!$env:DFL_S1C2_SESSION_DATA) 'session path is cleared after attempt'
    return [pscustomobject]@{ Error=$errorText; Output=$output; State=$state }
}

Run-Case 'post-process-default' {
    $case = New-Case 'post-process-default'
    $r = Invoke-Case $case @(,$normal)
    Check (!$r.Error -and $r.State.PostProcesses[0] -eq 'none') 'missing effect explicitly selects no post-process'
    Check ($r.State.DialogueSizes[0] -eq '16') 'missing dialogue size selects size 16 for the native host'
}
Run-Case 'post-process-legacy' {
    $case = New-Case 'post-process-legacy'
    $r = Invoke-Case $case @(,$normal) -PostProcess 'crt'
    Check (!$r.Error -and $r.State.PostProcesses[0] -eq 'crt') 'CRT reaches the native host with legacy scaling'
}
Run-Case 'post-process-xbrz' {
    $case = New-Case 'post-process-xbrz'
    $r = Invoke-Case $case @(,$normal) -PostProcess 'xbrz'
    Check (!$r.Error -and $r.State.PostProcesses[0] -eq 'xbrz') 'xBRZ reaches the native host as a distinct effect'
}

Run-Case 'missing-login-info' {
    $case = New-Case 'missing-login-info'
    $r = Invoke-Case $case @(,$normal)
    Check (!$r.Error) 'normal target exit succeeds'
    Check (@($r.Output | Where-Object { $_ -like 'Chapter Two instance * log: *' }).Count -eq 1) 'normal target exit emits one successful result log'
    Check ($r.State.Launches -eq 1 -and $r.State.LoginAtLaunch[0]) 'missing loginInfo.inf exists before target launch'
    Check ([IO.File]::Exists((Join-Path $case.Game 'loginInfo.inf')) -and (Get-Item -LiteralPath (Join-Path $case.Game 'loginInfo.inf')).Length -eq 0) 'new loginInfo.inf is empty'
    Check ($r.State.CandidateAtLaunch[0]) 'broker boundary receives an existing fixture candidate'
}

Run-Case 'detailed-diagnostics-opt-in' {
    $case = New-Case 'detailed-diagnostics-opt-in'
    $r = Invoke-Case $case @(,$normal) -DetailedDiagnostics
    Check (!$r.Error -and $r.State.DetailedAtLaunch[0]) 'explicit detailed diagnostics reaches the broker'
}

Run-Case 'missing-ordinary-identity' {
    $case = New-Case 'missing-ordinary-identity'
    $r = Invoke-Case $case @(,@('OWNED_PID 47568','EXIT 00000000'))
    Check (!!$r.Error) 'old broker without ordinary process proof cannot report successful launch'
}

Run-Case 'duplicate-ordinary-identity' {
    $case = New-Case 'duplicate-ordinary-identity'
    $r = Invoke-Case $case @(,@($ordinary,$ordinary,'OWNED_PID 47568','EXIT 00000000'))
    Check (!!$r.Error) 'duplicate process identity proof is rejected'
}

Run-Case 'existing-login-info' {
    $case = New-Case 'existing-login-info'
    $login = Join-Path $case.Game 'loginInfo.inf'
    $bytes = [byte[]](0,255,128,13,10,65,0,66)
    [IO.File]::WriteAllBytes($login, $bytes)
    # An existing file is not opened for writing, even if someone holds it exclusively.
    $held = [IO.File]::Open($login, 'Open', 'Read', 'None')
    try { $r = Invoke-Case $case @(,$normal) } finally { $held.Dispose() }
    Check (!$r.Error -and $r.State.Launches -eq 1) 'existing exclusive loginInfo.inf permits launch'
    Check (([Convert]::ToBase64String([IO.File]::ReadAllBytes($login))) -ceq 'AP+ADQpBAEI=') 'existing loginInfo.inf retains exact bytes'
}

Run-Case 'login-info-directory' {
    $case = New-Case 'login-info-directory'
    $null = New-Item -ItemType Directory -Path (Join-Path $case.Game 'loginInfo.inf')
    $r = Invoke-Case $case @(,$normal)
    Check (!!$r.Error -and $r.State.Launches -eq 0) 'directory collision fails before target launch'
}

Run-Case 'login-info-create-denied' {
    $case = New-Case 'login-info-create-denied'
    $originalAcl = Get-Acl -LiteralPath $case.Game
    $deniedAcl = Get-Acl -LiteralPath $case.Game
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($identity, [Security.AccessControl.FileSystemRights]::CreateFiles, [Security.AccessControl.AccessControlType]::Deny)
    $deniedAcl.AddAccessRule($rule)
    try {
        Set-Acl -LiteralPath $case.Game -AclObject $deniedAcl
        $denied = $false
        try { $probe = [IO.File]::Open((Join-Path $case.Game 'denied-control'), 'CreateNew', 'Write', 'None'); $probe.Dispose() }
        catch [UnauthorizedAccessException] { $denied=$true }
        Check $denied 'owned-directory denial control really rejects creation'
        $r = Invoke-Case $case @(,$normal)
        Check (!!$r.Error -and $r.State.Launches -eq 0) 'unwritable loginInfo.inf fails before target launch'
    } finally { Set-Acl -LiteralPath $case.Game -AclObject $originalAcl }
}

Run-Case 'native-crash-broker-zero' {
    $case = New-Case 'native-crash-broker-zero'
    $r = Invoke-Case $case @(,$crash)
    Check (!!$r.Error -and $r.State.Launches -eq 1) 'native c0000005 is rejected when broker exits zero'
    Check (@($r.Output | Where-Object { $_ -like 'Chapter Two instance * log: *' }).Count -eq 0) 'native crash never emits successful result log'
}

Run-Case 'no-target-exit' {
    $case = New-Case 'no-target-exit'
    $r = Invoke-Case $case @(,@($ordinary, 'OWNED_PID 40032', 'DEBUG target stopped without exit record'))
    Check (!!$r.Error) 'broker zero without target EXIT is rejected'
}

Run-Case 'broker-failure-no-exit' {
    $case = New-Case 'broker-failure-no-exit'
    $r = Invoke-Case $case @(,@('CREATE_ERROR 5')) 1
    Check (!!$r.Error -and $r.State.Launches -eq 1) 'failed broker without target EXIT is rejected'
}

Run-Case 'malformed-target-exit' {
    $case = New-Case 'malformed-target-exit'
    $r = Invoke-Case $case @(,@($ordinary, 'EXIT 100000000'))
    Check (!!$r.Error) 'target exit outside uint32 range is rejected'
}

Run-Case 'malformed-exit-before-zero' {
    $case = New-Case 'malformed-exit-before-zero'
    $r = Invoke-Case $case @(,@($ordinary, 'EXIT 100000000', 'EXIT 00000000'))
    Check (!!$r.Error) 'normal exit cannot hide a malformed target EXIT record'
}

Run-Case 'crash-before-zero' {
    $case = New-Case 'crash-before-zero'
    $r = Invoke-Case $case @(,@($ordinary, 'EXIT c0000005', 'EXIT 00000000'))
    Check (!!$r.Error) 'normal exit cannot hide an earlier target crash'
}

Run-Case 'preferred-base-retry' {
    $case = New-Case 'preferred-base-retry'
    $r = Invoke-Case $case @($preferred,$normal)
    Check (!$r.Error -and $r.State.Launches -eq 2) 'native preferred-base exit 5 retries then normal exit succeeds'
}

Run-Case 'preferred-base-exhaustion' {
    $case = New-Case 'preferred-base-exhaustion'
    $r = Invoke-Case $case @(,$preferred)
    Check (!!$r.Error -and $r.State.Launches -eq 48) 'preferred-base retry remains bounded at 48'
}

Run-Case 'reported-import-failure' {
    $case = New-Case 'reported-import-failure'
    $r = Invoke-Case $case @(,@($ordinary, 'MISSING_IMPORT fixture.dll fixture', 'EXIT 00000000'))
    Check (!!$r.Error) 'startup import failure is rejected even with target exit zero'
}

# Concurrent callers execute the real production function in independent runspaces.
# The whole-script cases above ensure it is wired before the fake broker boundary.
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($sourcePath, [ref]$tokens, [ref]$parseErrors)
Check ($parseErrors.Count -eq 0) 'production helper parses without errors'
$ensure = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Initialize-ChapterTwoLoginInfo' }, $true)
if ($ensure) { . ([scriptblock]::Create($ensure.Extent.Text)) }
$sessionInitializer = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Initialize-ChapterTwoSessionDirectory' }, $true)
Check (!!$sessionInitializer) 'production session initializer is available'
if ($sessionInitializer) { . ([scriptblock]::Create($sessionInitializer.Extent.Text)) }

Run-Case 'session-identity-and-reuse' {
    $case = New-Case 'session-identity-and-reuse'
    $name = 'A-20261112-120000-123-1'
    $session = Initialize-ChapterTwoSessionDirectory $case.Game $name
    [IO.File]::WriteAllText((Join-Path $session 'sentinel.txt'), 'prior-run')
    $caught = $false
    try { $null = Initialize-ChapterTwoSessionDirectory $case.Game $name } catch { $caught=$true }
    Check $caught 'existing session cannot be reused'
    Check ([IO.File]::ReadAllText((Join-Path $session 'sentinel.txt')) -ceq 'prior-run') 'old session contents stay intact'
    foreach ($invalid in @('../outside','A-20261112-120000-123-0','C-20261112-120000-123-1', "A-20261112-120000-123-1`n")) {
        $caught = $false
        try { $null = Initialize-ChapterTwoSessionDirectory $case.Game $invalid } catch { $caught=$true }
        Check $caught 'invalid session name is refused'
    }
}

Run-Case 'session-parent-file' {
    $case = New-Case 'session-parent-file'
    $blocked = Join-Path $case.Game 'Logs/DFLegacy/Sessions'
    [IO.File]::WriteAllText($blocked, 'not-a-directory')
    $caught = $false
    try { $null = Initialize-ChapterTwoSessionDirectory $case.Game 'B-20261112-120000-123-1' } catch { $caught=$true }
    Check $caught 'file in session ancestry is refused'
    Check ([IO.File]::ReadAllText($blocked) -ceq 'not-a-directory') 'blocking file is preserved'
}

Run-Case 'session-junction-ancestor' {
    $case = New-Case 'session-junction-ancestor'
    $link = Join-Path $case.Root 'linked-game'
    $null = New-Item -ItemType Junction -Path $link -Target $case.Game
    $caught = $false
    try { $null = Initialize-ChapterTwoSessionDirectory $link 'A-20261112-120000-123-1' } catch { $caught=$true }
    Check $caught 'game directory junction is refused'
    Check (!(Test-Path -LiteralPath (Join-Path $case.Game 'Logs/DFLegacy/Sessions'))) 'refused junction leaves target unchanged'
}

Run-Case 'session-junction-log-parent' {
    $case = New-Case 'session-junction-log-parent'
    $destination = Join-Path $case.Root 'outside-sessions'
    $null = New-Item -ItemType Directory -Path $destination
    $null = New-Item -ItemType Junction -Path (Join-Path $case.Game 'Logs/DFLegacy/Sessions') -Target $destination
    $caught = $false
    try { $null = Initialize-ChapterTwoSessionDirectory $case.Game 'A-20261112-120000-123-1' } catch { $caught=$true }
    Check $caught 'session parent junction is refused'
    Check (@(Get-ChildItem -LiteralPath $destination -Force).Count -eq 0) 'refused log junction target stays empty'
}

Run-Case 'initializer-missing-parent' {
    if (!$ensure) { return }
    $missing = Join-Path $testRoot 'missing-parent/Game'
    $caught = $false
    try { Initialize-ChapterTwoLoginInfo $missing }
    catch [IO.DirectoryNotFoundException] { $caught=$true }
    Check $caught 'initializer does not swallow a missing parent IOException'
    Check (!(Test-Path -LiteralPath $missing)) 'initializer does not create unexpected parent directories'
}

Run-Case 'initializer-write-denied' {
    if (!$ensure) { return }
    $case = New-Case 'initializer-write-denied'
    $originalAcl = Get-Acl -LiteralPath $case.Game
    $deniedAcl = Get-Acl -LiteralPath $case.Game
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $deniedAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, [Security.AccessControl.FileSystemRights]::CreateFiles, [Security.AccessControl.AccessControlType]::Deny))
    $caught = $false
    try {
        Set-Acl -LiteralPath $case.Game -AclObject $deniedAcl
        try { Initialize-ChapterTwoLoginInfo $case.Game }
        catch [UnauthorizedAccessException] { $caught=$true }
        Check $caught 'initializer propagates a real write denial'
        Check (![IO.File]::Exists((Join-Path $case.Game 'loginInfo.inf'))) 'denied initializer leaves no login file'
    } finally { Set-Acl -LiteralPath $case.Game -AclObject $originalAcl }
}

Run-Case 'concurrent-create-new' {
    Check (!!$ensure) 'production login file initializer is available for concurrent callers'
    if (!$ensure) { return }
    $case = New-Case 'concurrent-create-new'
    $barrier = [Threading.Barrier]::new(8)
    $workers = @()
    try {
        foreach ($i in 1..8) {
            $ps = [PowerShell]::Create()
            $null = $ps.AddScript($ensure.Extent.Text)
            $null = $ps.AddScript('param($gate,$game) if (!$gate.SignalAndWait(10000)) { throw "Concurrent caller barrier timed out" }; Initialize-ChapterTwoLoginInfo $game').AddArgument($barrier).AddArgument($case.Game)
            $workers += [pscustomobject]@{ PS=$ps; Pending=$ps.BeginInvoke() }
        }
        foreach ($worker in $workers) {
            $null = $worker.PS.EndInvoke($worker.Pending)
            Check (!$worker.PS.HadErrors) 'concurrent CreateNew caller has no error'
        }
        $login = Join-Path $case.Game 'loginInfo.inf'
        Check ([IO.File]::Exists($login) -and (Get-Item -LiteralPath $login).Length -eq 0) 'concurrent startup creates one empty loginInfo.inf'
    } finally {
        foreach ($worker in $workers) { $worker.PS.Dispose() }
        $barrier.Dispose()
    }
}

$summary = [pscustomobject]@{ Source=$sourcePath; SourceSha256=(Get-FileHash -LiteralPath $sourcePath).Hash; Checks=$checks; Failures=$failures; NativeGameStarted=$false; TestRoot=$testRoot; Cases=$results }
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testRoot 'summary.json') -Encoding utf8
Write-Output "Chapter Two startup: $checks checks, $failures failures. Evidence: $testRoot"
if ($failures) { exit 1 }
