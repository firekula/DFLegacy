param(
    [string]$Source = (Join-Path $PSScriptRoot '../ChapterTwo/Start-Client.ps1'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../dist/c2-parity-next-20261112/native-log/archive-tests')
)
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $root
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $Source),[ref]$tokens,[ref]$errors)
$script:checks=0;$script:failures=0
function Check([bool]$ok,[string]$message){$script:checks++;if(!$ok){$script:failures++;Write-Output "FAIL: $message"}}
foreach($name in 'Initialize-ChapterTwoNativeLogInterop','Copy-ChapterTwoNativeLogs'){
    $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    Check ($null -ne $node) "production $name exists"
    if($node){. ([scriptblock]::Create($node.Extent.Text))}
}
if($failures){exit 1}
Initialize-ChapterTwoNativeLogInterop
function Case([string]$name,[string]$journal="START 12345`nEXIT 12345 00000000`n"){
    $folder=Join-Path $root $name;$container=Join-Path $folder 'Container';$source=Join-Path $container 'Temp/NativeLogs/12345'
    $null=New-Item -ItemType Directory -Path $source
    $events=Join-Path $folder 'events.log';[IO.File]::WriteAllText($events,$journal)
    $prefix=Join-Path $folder 'archived'
    [pscustomobject]@{Container=$container;Source=$source;Events=$events;Prefix=$prefix;Since=[DateTime]::UtcNow.AddSeconds(-5)}
}
function Archive($c){Copy-ChapterTwoNativeLogs $c.Container $c.Events $c.Prefix $c.Since -WarningVariable warning 3>$null}
$c=Case 'normal'
[IO.File]::WriteAllBytes((Join-Path $c.Source 'DNF.trc'),[byte[]](0,65,66,13,10))
[IO.File]::WriteAllText((Join-Path $c.Source 'NeopleEngine.LOG'),'engine')
[IO.File]::WriteAllText((Join-Path $c.Source 'other.log'),'excluded')
Archive $c
Check ([IO.File]::Exists($c.Prefix+'-DNF.trc') -and [IO.File]::Exists($c.Prefix+'-NeopleEngine.LOG')) 'both native logs are archived'
Check (([Convert]::ToBase64String([IO.File]::ReadAllBytes($c.Prefix+'-DNF.trc'))) -eq 'AEFCDQo=') 'binary framing and plaintext bytes remain exact'
Check (![IO.File]::Exists($c.Prefix+'-other.log')) 'only the two named native logs are copied'
[IO.File]::WriteAllText((Join-Path $c.Source 'DNF.trc'),'replacement');Archive $c
Check (([Convert]::ToBase64String([IO.File]::ReadAllBytes($c.Prefix+'-DNF.trc'))) -eq 'AEFCDQo=') 'existing archived evidence is not overwritten'
foreach($journal in @("OWNED_PID 12345`n","START ../../elsewhere`n","START 4294967296`n","START 12345`nSTART 54321`n")){
    $c=Case ([guid]::NewGuid().ToString('N')) $journal;[IO.File]::WriteAllText((Join-Path $c.Source 'DNF.trc'),'text');Archive $c
    Check (![IO.File]::Exists($c.Prefix+'-DNF.trc')) 'untrusted or malformed process identities cannot select a folder'
}
$c=Case 'stale';$file=Join-Path $c.Source 'DNF.trc';[IO.File]::WriteAllText($file,'old');[IO.File]::SetLastWriteTimeUtc($file,$c.Since.AddMinutes(-1));Archive $c
Check (![IO.File]::Exists($c.Prefix+'-DNF.trc')) 'reused PID cannot archive stale output'
$c=Case 'oversized';$file=[IO.File]::Open((Join-Path $c.Source 'DNF.trc'),'CreateNew','Write','None');$file.SetLength(16MB+1);$file.Dispose();Archive $c
Check (![IO.File]::Exists($c.Prefix+'-DNF.trc')) 'oversized logs are rejected before copying'
$c=Case 'locked';$file=[IO.File]::Open((Join-Path $c.Source 'DNF.trc'),'CreateNew','Write','None');try{Archive $c}finally{$file.Dispose()}
Check (![IO.File]::Exists($c.Prefix+'-DNF.trc')) 'an active writer cannot be mistaken for stable final evidence'
$c=Case 'wrong-pid' "START 23456`nEXIT 23456 00000000`n";[IO.File]::WriteAllText((Join-Path $c.Source 'DNF.trc'),'other process');Archive $c
Check (![IO.File]::Exists($c.Prefix+'-DNF.trc')) 'only the trusted journal PID is read'
$c=Case 'junction';$outside=Join-Path $root 'outside';$null=New-Item -ItemType Directory -Path $outside
[IO.File]::WriteAllText((Join-Path $outside 'DNF.trc'),'outside')
[IO.Directory]::Delete($c.Source);$null=New-Item -ItemType Junction -Path $c.Source -Target $outside;Archive $c
Check (![IO.File]::Exists($c.Prefix+'-DNF.trc')) 'directory reparse points cannot redirect an archive read'
$c=Case 'hardlink';$target=Join-Path $root 'hardlink-source';[IO.File]::WriteAllText($target,'linked');$null=New-Item -ItemType HardLink -Path (Join-Path $c.Source 'DNF.trc') -Target $target;Archive $c
Check (![IO.File]::Exists($c.Prefix+'-DNF.trc')) 'multiple-link files cannot expose unrelated content'
$c=Case 'crash' "START 12345`nEXCEPTION 12345 22 C0000005 0000000000001234`nEXIT 12345 C0000005`n"
[IO.File]::WriteAllText((Join-Path $c.Source 'NeopleEngine.LOG'),'crash evidence');Archive $c
Check ([IO.File]::Exists($c.Prefix+'-NeopleEngine.LOG')) 'nonzero game exits still preserve native logs'
$summary=[pscustomobject]@{Checks=$checks;Failures=$failures;TestRoot=$root;NativeGameStarted=$false}
$summary|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'summary.json')
Write-Output "Native log archive: $checks checks, $failures failures. $root"
if($failures){exit 1}
