param([Parameter(Mandatory)][string]$BuildPath,[Parameter(Mandatory)][string]$ManifestPath,
 [Parameter(Mandatory)][string]$ManifestSha256,[ValidateSet('return-green')][string]$Phase='return-green')
. (Join-Path $PSScriptRoot 'native-common.ps1')
Assert-TaskNoNative
Assert-TaskProtected -AllowNativeConfigs
if((Get-TaskSha $ManifestPath) -ne $ManifestSha256){throw 'Selected frozen manifest changed'}
$manifest=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
if([IO.Path]::GetFullPath($manifest.Build) -ne [IO.Path]::GetFullPath($BuildPath)){throw 'Frozen build path mismatch'}
foreach($entry in $manifest.Files){if((Get-TaskSha (Join-Path $BuildPath $entry.File)) -ne $entry.Sha256){throw 'Frozen artifact changed'}}
$journalPath=Join-Path $taskRun 'deployment.json'
if(Test-Path -LiteralPath $journalPath){$prior=Get-Content -LiteralPath $journalPath -Raw|ConvertFrom-Json;if($prior.Status -ne 'stopped'){throw 'Prior phase is not stopped'}}
$fixture=Get-Content -LiteralPath (Join-Path $taskRun 'native-fixture.json') -Raw|ConvertFrom-Json
$initial=Join-Path $taskRun 'fixture-return-green.json'
if((Get-TaskSha $initial) -ne '414B70DB31F6E25D57830127BA89A39A12320E4A7C68F7BB23561282935F7C28'){throw 'Fixture changed'}
$runtime=Join-Path $taskRun ('runtime-'+$Phase);if(Test-Path -LiteralPath $runtime){throw 'Preserve phase runtime'}
New-Item -ItemType Directory -Path $runtime|Out-Null
foreach($entry in $manifest.Files){$dest=Join-Path $runtime $entry.File;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $BuildPath $entry.File) -Destination $dest}
Copy-Item -LiteralPath (Join-Path $taskRun 'preflight/05-server.json') -Destination (Join-Path $runtime 'server.json') -Force
$state=Join-Path $taskRun ('state-'+$Phase+'.json');Copy-Item -LiteralPath $initial -Destination $state
$snapshots=Join-Path $taskRun ('snapshots-'+$Phase);New-Item -ItemType Directory -Path $snapshots|Out-Null
$isFallback=$Phase -like 'fallback*'
$journal=[pscustomobject]@{Schema='party-native-phase/v1';Phase=$Phase;Status='starting';StartedAtUtc=[datetime]::UtcNow.ToString('o');StoppedAtUtc=$null;BuildManifest=$ManifestPath;BuildManifestSha256=$ManifestSha256;Runtime=$runtime;State=$state;InitialFixtureSha256='414B70DB31F6E25D57830127BA89A39A12320E4A7C68F7BB23561282935F7C28';Server=$null;Clients=@();Launchers=@();Sink=$null;SinkLog=$null;Stdout=(Join-Path $taskRun ('server-'+$Phase+'.log'));Stderr=(Join-Path $taskRun ('server-'+$Phase+'.err.log'));PeerSink=$isFallback}
$journal | Add-Member -NotePropertyName RoleAccounts -NotePropertyValue @{A='shizuku';B='test'}
Save-TaskJson $journal $journalPath
foreach($directory in @('G:\Game\DNF_ACT1\DF','G:\Game\DNF_ACT1\Client_test2')){
 $config=Join-Path $directory 'Config.ini';$text=[IO.File]::ReadAllText($config)
 if(Test-Path -LiteralPath (Join-Path $directory 'auto-input.json')){throw 'Pending native input plan'}
 $variant=@(Get-Content -Raw -LiteralPath (Join-Path $taskRun 'config-variants.json')|ConvertFrom-Json|Where-Object {$_.Path -eq $config})
 if($variant.Count -ne 1 -or (Get-TaskSha $config) -ne $variant[0].PlannedSha256){throw 'Exact planned config not activated'}
}
if($isFallback){
 $journal.SinkLog=Join-Path $taskRun ('peer-sink-'+$Phase+'.log')
 $sink=Start-Process -FilePath $taskPython -ArgumentList @('-u',(Join-Path $taskRun 'peer-sink.py')) -WorkingDirectory $taskRun -WindowStyle Hidden -RedirectStandardOutput $journal.SinkLog -RedirectStandardError (Join-Path $taskRun ('peer-sink-'+$Phase+'.err.log')) -PassThru
 $journal.Sink=Get-TaskIdentity $sink 'sink';Save-TaskJson $journal $journalPath
 $sinkDeadline=[datetime]::UtcNow.AddSeconds(5)
 do{
  if(-not(Get-TaskOwned $journal.Sink)){throw 'Owned peer sink exited'}
  $sinkReady=@(Get-NetUDPEndpoint -LocalPort 27006 -ErrorAction SilentlyContinue|Where-Object {$_.OwningProcess -eq $journal.Sink.Id}).Count -eq 1
  if(-not $sinkReady){Start-Sleep -Milliseconds 100}
 }while(-not $sinkReady -and [datetime]::UtcNow -lt $sinkDeadline)
 if(-not $sinkReady){throw 'Peer sink did not bind its owned discard endpoint'}
}
$saved=@{};$names=@(Get-ChildItem Env:|Where-Object {$_.Name -match '^(DFLegacy|ASPNETCORE_|DOTNET_)'}|Select-Object -ExpandProperty Name)
$overrides=@{'DFLegacy__DataPath'=$state;'DFLegacy__ScriptPvfPath'='G:\Game\DNF_ACT1\Client_test2\Script.pvf';'DFLegacy__Entrance__Port'='27001';'DFLegacy__Debug__ClientOverlay__SnapshotDirectory'=$snapshots;'DFLEGACY_UDP_AUDIT'='1';'Logging__LogLevel__DFLegacy.Server.FallbackRelayService'='Debug'}
if($isFallback){$overrides['DFLEGACY_UDP_PEER_SINK']='127.0.0.1:27006'}
$names=@(@($names)+@($overrides.Keys)|Select-Object -Unique)
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
 foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$null,'Process')}
 foreach($name in $overrides.Keys){[Environment]::SetEnvironmentVariable($name,$overrides[$name],'Process')}
 $server=Start-Process -FilePath (Join-Path $runtime 'DFLegacy.Server.exe') -WorkingDirectory $runtime -WindowStyle Hidden -RedirectStandardOutput $journal.Stdout -RedirectStandardError $journal.Stderr -PassThru
 $journal.Server=Get-TaskIdentity $server 'server';Save-TaskJson $journal $journalPath
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
$deadline=[datetime]::UtcNow.AddSeconds(30)
do{
 Start-Sleep -Milliseconds 250
 if(-not(Get-TaskOwned $journal.Server)){throw 'Isolated server exited; preserve journal'}
 $ready=Select-String -LiteralPath $journal.Stdout -SimpleMatch 'Entrance listener ready at 0.0.0.0:7001' -Quiet
 $relayReady=Select-String -LiteralPath $journal.Stdout -SimpleMatch 'Native fallback relay ready:' -Quiet
}while((-not $ready -or -not $relayReady) -and [datetime]::UtcNow -lt $deadline)
if(-not $ready -or -not $relayReady){throw 'Actual game and relay readiness failed'}
foreach($role in @('A','B')){
 $directory=if($role -eq 'A'){'G:\Game\DNF_ACT1\DF'}else{'G:\Game\DNF_ACT1\Client_test2'}
 $launcherDirectory=if($role -eq 'A'){'G:\Game\DNF_ACT1\DF\town-test'}else{Join-Path $directory 'DFLegacy-launcher'}
 $account=if($role -eq 'A'){'shizuku'}else{'test'}
 $launcher=Start-Process -FilePath (Join-Path $launcherDirectory 'DFLegacy.Launcher.exe') -ArgumentList @('--headless','--account',$account,'--no-dialog') -WorkingDirectory $launcherDirectory -WindowStyle Hidden -PassThru
 $journal.Launchers+=Get-TaskIdentity $launcher ('launcher-'+$role);Save-TaskJson $journal $journalPath
 $deadline=[datetime]::UtcNow.AddSeconds(20)
 do{Start-Sleep -Milliseconds 250;$clients=@(Get-Process -Name DNF -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq (Join-Path $directory 'DNF.exe')})}while($clients.Count -ne 1 -and [datetime]::UtcNow -lt $deadline)
 if($clients.Count -ne 1){throw 'Expected unique owned native client'}
 $journal.Clients+=Get-TaskIdentity $clients[0] $role;Save-TaskJson $journal $journalPath
}
$journal.Status='running';Save-TaskJson $journal $journalPath
Assert-TaskProtected -AllowNativeConfigs
$journal|Select-Object Phase,Status,Server,Clients,PeerSink|ConvertTo-Json -Depth 6
