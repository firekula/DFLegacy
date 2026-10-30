$ErrorActionPreference='Stop'
$taskRun=[IO.Path]::GetFullPath($PSScriptRoot)
if($taskRun -ne 'G:\Game\DNF_ACT1\DFLegacy\.tmp\town-return-perf-20261029'){throw 'Wrong run namespace'}
$taskPython='D:\Program Files\Python312\python.exe'
function Get-TaskSha([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash}
function Save-TaskJson($Value,[string]$Path,[switch]$New){
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 18))
 $mode=if($New){[IO.FileMode]::CreateNew}else{[IO.FileMode]::Create}
 $stream=[IO.File]::Open($Path,$mode,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Get-TaskIdentity($Process,[string]$Role){
 $created=$Process.StartTime.ToUniversalTime();$deadline=[datetime]::UtcNow.AddSeconds(3)
 do{
  $fresh=Get-Process -Id $Process.Id -ErrorAction Stop
  if(($fresh.StartTime.ToUniversalTime()-$created).Duration().TotalMilliseconds -gt 1){throw 'New process identity changed before path resolution'}
  $resolvedPath=$fresh.Path
  if(-not $resolvedPath){Start-Sleep -Milliseconds 25}
 }while(-not $resolvedPath -and [datetime]::UtcNow -lt $deadline)
 if(-not $resolvedPath){throw 'New process image path did not resolve'}
 [pscustomobject]@{Role=$Role;Id=$Process.Id;Path=$resolvedPath;StartTimeUtc=$created.ToString('o')}
}
function Get-TaskOwned($Record){
 if(-not $Record){return $null}
 $current=Get-Process -Id $Record.Id -ErrorAction SilentlyContinue
 if(-not $current){return $null}
 if($current.Path -ne $Record.Path -or ($current.StartTime.ToUniversalTime()-([datetime]$Record.StartTimeUtc).ToUniversalTime()).Duration().TotalMilliseconds -gt 1){throw 'PID identity changed'}
 return $current
}
function Assert-TaskProtected([switch]$AllowNativeConfigs){
 $tooltipNative=$null;$tooltipReceipt='G:\Game\DNF_ACT1\DFLegacy\.tmp\dungeon-tooltip-20261029\native-u01-deployment.json'
 if(Test-Path -LiteralPath $tooltipReceipt){
  if((Get-TaskSha $tooltipReceipt) -ne '155F68768919EB4E7C98A6A6D1109570AD6B58BEE5F021A7ECC94217A4CCD08B'){throw 'U01 native receipt changed'}
  $tooltipNative=Get-Content -LiteralPath $tooltipReceipt -Raw|ConvertFrom-Json
  if((Get-TaskSha $tooltipNative.NativeArtifact) -ne $tooltipNative.NativeSha256 -or (Get-TaskSha $tooltipNative.BuildManifest) -ne $tooltipNative.BuildManifestSha256){throw 'U01 frozen build changed'}
 }

 $nativeUpdatePath='G:\Game\DNF_ACT1\DFLegacy\.tmp\revival-coin-purchase-20261028\native-19C-candidate-deployment.json'
 if((Get-TaskSha $nativeUpdatePath) -ne '4BE613A6465CC75899E4EA0753C9633B4C85646E48C0563D24D929A3B7FAF48C'){throw 'Approved native receipt changed'}
 $nativeUpdate=Get-Content -LiteralPath $nativeUpdatePath -Raw|ConvertFrom-Json
 if((Get-TaskSha $nativeUpdate.BuildManifest) -ne $nativeUpdate.BuildManifestSha256 -or (Get-TaskSha $nativeUpdate.Artifact) -ne $nativeUpdate.NativeSha256){throw 'Native candidate provenance changed'}
 if((Get-TaskSha (Join-Path $taskRun 'preflight/protected-files.json')) -ne $nativeUpdate.ProtectedManifestSha256){throw 'Original protected manifest changed'}
 $dragNative=$null;$dragReceipt=Join-Path $taskRun 'native-drag-deployment.json'
 if(Test-Path -LiteralPath $dragReceipt){
  if((Get-TaskSha $dragReceipt) -ne '7D344EF3AEF68FBC17E2AABA15F9ACE945E79FFE86E015426784F44AA15147DF'){throw 'Native drag receipt changed'}
  $dragNative=Get-Content -LiteralPath $dragReceipt -Raw|ConvertFrom-Json
  if((Get-TaskSha $dragNative.NativeArtifact) -ne $dragNative.NativeSha256 -or (Get-TaskSha $dragNative.BuildManifest) -ne $dragNative.BuildManifestSha256){throw 'Native drag frozen provenance changed'}
 }
 foreach($entry in (Get-Content -LiteralPath (Join-Path $taskRun 'preflight/protected-files.json') -Raw|ConvertFrom-Json)){
  if($entry.Backup -and (Get-TaskSha $entry.Backup) -ne $entry.Sha256){throw 'Original backup changed'}
  if($AllowNativeConfigs -and $entry.Path -in @('G:\Game\DNF_ACT1\DF\Config.ini','G:\Game\DNF_ACT1\Client_test2\Config.ini')){
   $variant=@(Get-Content -Raw -LiteralPath (Join-Path $taskRun 'config-variants.json')|ConvertFrom-Json|Where-Object {$_.Path -eq $entry.Path})
   if($variant.Count -ne 1){throw 'Missing exact configuration variant'}
   $actualHash=Get-TaskSha $entry.Path
   if($actualHash -notin @($variant[0].OriginalSha256,$variant[0].PlannedSha256,$variant[0].NormalPartyCandidateSha256)){throw 'Configuration differs from exact original/planned variant'}
   continue
  }
  $expectedHash=$entry.Sha256
  $nativeTarget=@($nativeUpdate.Targets|Where-Object {$_.Path -eq $entry.Path})
  if($nativeTarget.Count){if($nativeTarget.Count -ne 1 -or $nativeTarget[0].OriginalSha256 -ne $entry.Sha256 -or $nativeTarget[0].Sha256 -ne $nativeUpdate.NativeSha256){throw 'Unexpected native target'};$expectedHash=$nativeUpdate.NativeSha256}
  if($tooltipNative){$updated=@($tooltipNative.Targets|Where-Object {$_.Path -eq $entry.Path});if($updated.Count){if($updated.Count -ne 1 -or $updated[0].BeforeSha256 -ne $nativeUpdate.NativeSha256){throw 'U01 lineage changed'};$expectedHash=$tooltipNative.NativeSha256}}
  if($dragNative){$updated=@($dragNative.Targets|Where-Object {$_.Path -eq $entry.Path});if($updated.Count){if($updated.Count -ne 1 -or -not $tooltipNative -or $updated[0].BeforeSha256 -ne $tooltipNative.NativeSha256){throw 'Native drag lineage changed'};$expectedHash=$dragNative.NativeSha256}}
  if((Get-TaskSha $entry.Path) -ne $expectedHash){throw ('Protected input changed: '+$entry.Path)}
 }
}
function Assert-TaskNoNative {
 if(@(Get-Process -Name DNF,DFLegacy.Server,DFLegacy.Launcher -ErrorAction SilentlyContinue).Count){throw 'Unexpected native/server/launcher already running'}
}
function Stop-TaskOwnedRun {
 $journalPath=Join-Path $taskRun 'deployment.json';$journal=Get-Content -LiteralPath $journalPath -Raw|ConvertFrom-Json
 if($journal.Status -eq 'stopped'){throw 'Already stopped; preserve journal'}
 $records=@($journal.Clients)+@($journal.Launchers)+@($journal.Server)+@($journal.Sink)|Where-Object {$_}
 foreach($p in @(Get-Process -Name DNF,DFLegacy.Server,DFLegacy.Launcher -ErrorAction SilentlyContinue)){
  if(@($records|Where-Object {$_.Id -eq $p.Id -and $_.Path -eq $p.Path}).Count -ne 1){throw 'Unrecorded process; do not stop it'}
 }
 $owned=@($records|ForEach-Object {Get-TaskOwned $_}|Where-Object {$_})
 foreach($record in @($journal.Clients)+@($journal.Launchers)){
  $p=Get-TaskOwned $record;if($p){Stop-Process -Id $p.Id;if(-not $p.WaitForExit(10000)){throw 'Owned client/launcher failed to exit'}}
 }
 if(Get-TaskOwned $journal.Server){
  $deadline=[datetime]::UtcNow.AddSeconds(10);$sessions=$null
  do{
   $readerStream=[IO.FileStream]::new($journal.Stdout,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
   try{$reader=[IO.StreamReader]::new($readerStream,[Text.Encoding]::UTF8,$true);try{$logText=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$readerStream.Dispose()}
   $connected=@([regex]::Matches($logText,'Entrance client ([a-f0-9-]+) connected from [^\r\n]+ on local port 7001')|ForEach-Object {$_.Groups[1].Value})
   $disconnected=@([regex]::Matches($logText,'Entrance client ([a-f0-9-]+) disconnected\.')|ForEach-Object {$_.Groups[1].Value})
   $sessions=@($connected|Where-Object {$_ -notin $disconnected}).Count
   if($sessions -eq 0){break};Start-Sleep -Milliseconds 100
  }while([datetime]::UtcNow -lt $deadline)
  if($sessions -ne 0){throw 'Owned channel cleanup did not finish; preserve server for inspection'}
  $journal|Add-Member -NotePropertyName ObservedActiveChannelSessionsBeforeStop -NotePropertyValue $sessions
  $journal|Add-Member -NotePropertyName ChannelCleanupEvidence -NotePropertyValue 'Every recorded7001 connection has its post-Runtime.Remove disconnected log; no admin authentication attempted'
 }
 foreach($record in @($journal.Server)+@($journal.Sink)|Where-Object {$_}){
  $p=Get-TaskOwned $record;if($p){Stop-Process -Id $p.Id;if(-not $p.WaitForExit(10000)){throw 'Owned server/sink failed to exit'}}
 }
 $journal.Status='stopped';$journal.StoppedAtUtc=[datetime]::UtcNow.ToString('o')
 Save-TaskJson $journal $journalPath
 $closed=Join-Path $taskRun ('closed-'+$journal.Phase)
 New-Item -ItemType Directory -Path $closed|Out-Null
 Copy-Item -LiteralPath $journalPath -Destination (Join-Path $closed 'deployment.json')
 Copy-Item -LiteralPath $journal.State -Destination (Join-Path $closed 'state.json')
 foreach($client in $journal.Clients){$net=Join-Path ([IO.Path]::GetDirectoryName($client.Path)) ('netdump-'+$client.Id+'.log');Copy-Item -LiteralPath $net -Destination (Join-Path $closed ($client.Role+'-netdump.log'))}
 foreach($log in @($journal.Stdout,$journal.Stderr,$journal.SinkLog)|Where-Object {$_}){Copy-Item -LiteralPath $log -Destination $closed}
 $files=@(Get-ChildItem -LiteralPath $closed -File|ForEach-Object {[pscustomobject]@{File=$_.Name;Sha256=Get-TaskSha $_.FullName}})
 Save-TaskJson $files (Join-Path $closed 'manifest.json') -New
 Assert-TaskProtected -AllowNativeConfigs
 return $closed
}
