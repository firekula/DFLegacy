using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DFLegacy.Diagnostics;
using DFLegacy.Server;

if(args.Length==1&&args[0]=="--game-fixture"){await Task.Delay(30000);return 0;}
if(args.Length==4&&args[0]=="--helper-fixture"){await DiagnosticHelper.RunAsync(DiagnosticSession.Open(args[1],args[2],args[3]));return 0;}
if(args.Length==4&&args[0]=="--queue-retry-run") {var retry=DiagnosticSession.Start(args[2],args[3],Guid.NewGuid().ToString("N"),args[1]);var outcome=await new DiagnosticQueue(retry).UploadPendingAsync(new DiagnosticUploadOptions{RetryDelay=TimeSpan.Zero,ExhaustedRetryDelay=TimeSpan.Zero});Console.WriteLine(outcome.Status);return outcome.Status=="uploaded"?0:1;}
var failures=0; var checks=0;
void Check(bool ok,string text) { checks++; if(!ok) { failures++; Console.Error.WriteLine("FAIL: "+text); } }
var root=Path.Combine(Path.GetTempPath(),"dflegacy-diagnostic-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try {
 if(args.Contains("--final-pvf-failsoft"))PvfFailSoft();else if(args.Length==3&&args[0]=="--final-native-wire")await NativeWireTests(args[1],args[2]);else if(args.Contains("--final-fixes-only"))await FinalFixTests();else if(args.Contains("--native-integration-only"))await NativeIntegrationTests();else if(args.Contains("--status-only"))await StatusTests();else {await Run();await StatusTests();await NativeIntegrationTests();}
} catch(Exception ex) { failures++; Console.Error.WriteLine("FAIL: "+ex); }
finally { Directory.Delete(root,true); }
Console.WriteLine($"Diagnostics tests: {checks} assertions, {failures} failures");
return failures==0?0:1;

void PvfFailSoft()
{
 var session=DiagnosticSession.Start(Path.Combine(root,"failsoft-client"),"",Guid.NewGuid().ToString("N"),Path.Combine(root,"failsoft-user"));
 session.SetPvfIdentity("C:/private/not-a-hash");Check(session.Registration.ClientPvfSha256 is null,"invalid PVF identity stays unknown");
 var path=Path.Combine(session.DirectoryPath,"session.json");File.SetAttributes(path,FileAttributes.ReadOnly);
 try {session.SetPvfIdentity(new string('d',64));Check(true,"PVF diagnostic handoff IO failure remains fail-soft for game startup");}
 catch(IOException){Check(false,"PVF diagnostic handoff IO failure remains fail-soft for game startup");}
 catch(UnauthorizedAccessException){Check(false,"PVF diagnostic handoff IO failure remains fail-soft for game startup");}
 finally {File.SetAttributes(path,FileAttributes.Normal);}
}

async Task FinalFixTests()
{
 PvfFailSoft();
 var home=Path.Combine(root,"final-retention");var client=Path.Combine(root,"final-client");
 using var endpoint=new Endpoint(async(ctx,n)=>{ctx.Response.StatusCode=201;await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{receiptId="11111111111111111111111111111111",eventId=ctx.EventId,status="stored"})));});
 var session=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),home,"1.0.0-test.1+aaaaaaaa");var queue=new DiagnosticQueue(session);
 var first=new DiagnosticIncident{Stage="first"};session.WriteIncident(first,"confirmed");Check(session.CollectConfirmed()==1,"first coalesced incident freezes");
 await queue.UploadPendingAsync();
 for(var i=1;i<100;i++)queue.Enqueue(DiagnosticProjection.Create(new DiagnosticIncident(),session.SessionId,"1.0.0-test.1+aaaaaaaa"));
 Check(queue.Enqueue(DiagnosticProjection.Create(new DiagnosticIncident(),session.SessionId,"1.0.0-test.1+aaaaaaaa")),"sent oldest report reclaimed at global 100-record cap");
 Check(!File.Exists(Path.Combine(home,"queue",first.EventId+".json")),"only eligible sent record is reclaimed");
 File.Delete(Path.Combine(home,"queue",first.EventId+".json"));
 var reopened=DiagnosticSession.Open(session.DirectoryPath,session.Registration.Capability,home);
 Check(reopened.CollectConfirmed()==0,"durable collection identity prevents refreezing pruned event after collector restart");
 // Isolate replay from capacity: removing an owned fixture record simulates later queue reclamation.
 File.Delete(Directory.GetFiles(Path.Combine(home,"queue"),"*.json").First());
 Check(reopened.CollectConfirmed()==0,"old consumed ID stays consumed even when queue has free capacity");
 var expired=Directory.GetFiles(Path.Combine(home,"queue"),"*.json").First();var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(expired))!;node["createdAt"]=DateTimeOffset.UtcNow.AddDays(-15);File.WriteAllText(expired,node.ToJsonString());
 var before=File.Exists(Path.Combine(home,"drops.json"))?long.Parse(File.ReadAllText(Path.Combine(home,"drops.json"))):0;
 queue.Enqueue(DiagnosticProjection.Create(new DiagnosticIncident(),session.SessionId,"test"));
 Check(long.Parse(File.ReadAllText(Path.Combine(home,"drops.json")))==before+1,"expired unsent report increments durable drop count");
 // Reuse valid frozen records with legal JSON whitespace to approach the byte
 // budget independently of count. All bodies/IDs/hashes remain intact.
 foreach(var extra in Directory.GetFiles(Path.Combine(home,"queue"),"*.json").Skip(85))File.Delete(extra);
 var byteFiles=Directory.GetFiles(Path.Combine(home,"queue"),"*.json");
 var sentNode=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(byteFiles[0]))!;sentNode["status"]="uploaded";File.WriteAllText(byteFiles[0],sentNode.ToJsonString());
 foreach(var file in byteFiles){var raw=File.ReadAllText(file);File.WriteAllText(file,raw+new string(' ',394757-Encoding.UTF8.GetByteCount(raw)));}
 Check(queue.Enqueue(DiagnosticProjection.Create(new DiagnosticIncident(),session.SessionId,"test")),"sent body reclaimed when byte capacity fills before count capacity");
 Check(Directory.GetFiles(Path.Combine(home,"queue"),"*.json").Sum(p=>new FileInfo(p).Length)<=32L*1024*1024,"reclamation keeps total queue storage within 32 MiB");
 session.Complete();var replacement=DiagnosticSession.Start(client,endpoint.Url,session.SessionId,home,"changed.build");replacement.WriteIncident(first,"confirmed");
 Check(replacement.CollectConfirmed()==0,"same logical producer session retains consumed identity across registration replacement after queue prune");
 var localHome=Path.Combine(root,"final-local");var settings=new DiagnosticSettings(localHome);settings.SetEnabled(false);
 var capture=Path.Combine(localHome,"captures","preserved.dmp");Directory.CreateDirectory(Path.GetDirectoryName(capture)!);File.WriteAllText(capture,"local fixture");
 var succeeded=0;
 for(var i=0;i<115;i++) {try {var local=DiagnosticSession.Start(client,"",Guid.NewGuid().ToString("N"),localHome);local.WriteIncident(new DiagnosticIncident(),"confirmed");local.Complete();succeeded++;}catch(IOException){break;}}
 Check(succeeded==115,"more than 100 completed local-only launches remain capturable without uploader");
 settings.SetEnabled(true);
 try {var enabled=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),localHome);Check(enabled.CollectConfirmed()==0&&new DiagnosticQueue(enabled).ReadPending().Count==0,"opt-in after local-only cleanup never replays historical events");}catch(IOException){Check(false,"opt-in recovers immediately after local-only launches");}
 var live=DiagnosticSession.Start(client,"",Guid.NewGuid().ToString("N"),localHome);using(var self=Process.GetCurrentProcess())live.BindProcess(self);live.Complete();
 var dead=DiagnosticSession.Start(client,"",Guid.NewGuid().ToString("N"),localHome);File.WriteAllText(Path.Combine(dead.DirectoryPath,"session.json"),JsonSerializer.Serialize(dead.Registration with{LauncherCreatedUtcTicks=1,GamePid=Environment.ProcessId,GameCreatedUtcTicks=1},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 var rotations=0;for(var i=0;i<105;i++){settings.SetEnabled(i%2==0);var revoked=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),localHome);revoked.Complete();rotations++;}
 Check(rotations==105&&Directory.Exists(live.DirectoryPath)&&!Directory.Exists(dead.DirectoryPath),"bounded cleanup reclaims dead and revoked sessions while preserving exact live game PID/creation identity");
 Check(File.ReadAllText(capture)=="local fixture","session reclamation preserves separate local captures");
 // The optional hash is frozen by collection, not read back from rich logs.
 var identitySession=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),Path.Combine(root,"identity-wire"),"1.0.0-alpha.1+aaaaaaaa");identitySession.SetPvfIdentity(new string('a',64));
 var identityIncident=new DiagnosticIncident();identitySession.WriteIncident(identityIncident,"confirmed");identitySession.CollectConfirmed();
 var identityBytes=new DiagnosticQueue(identitySession).ReadPending().Single().Body;identitySession.SetPvfIdentity(new string('b',64));
 Check(new DiagnosticQueue(identitySession).ReadPending().Single().Body.SequenceEqual(identityBytes),"PVF identity changes never mutate frozen retries");
 Check(ClientDiagnosticReport.TryParse(identityBytes,out var identityReport)&&identityReport!.ClientPvfSha256==new string('a',64),"known session PVF reaches server-compatible bytes");
 var changedPvf=DiagnosticProjection.Create(identityIncident with{ClientPvfSha256=new string('b',64)},identitySession.SessionId,identitySession.Registration.ClientVersion);
 var changedBuild=changedPvf with{ClientVersion="1.0.0-alpha.2+bbbbbbbb"};
 ClientDiagnosticReport.TryParse(DiagnosticJson.Serialize(changedPvf),out var otherPvf);ClientDiagnosticReport.TryParse(DiagnosticJson.Serialize(changedBuild),out var otherBuild);
 Check(identityReport!.Fingerprint()!=otherPvf!.Fingerprint()&&otherPvf.Fingerprint()!=otherBuild!.Fingerprint(),"different PVF and prerelease/source builds split authoritative server fingerprints");
 await ServerCompatibility(Path.Combine(root,"final-wire"),client,identityIncident with{ClientPvfSha256=new string('b',64)});
}

async Task NativeWireTests(string fixture,string helper)
{
 foreach(var mode in new[]{"signature-match","network-normal","module-crash"}) {
  var home=Path.Combine(root,"wire-"+mode);var client=Path.Combine(root,"wire-client");var rich=Path.Combine(root,"rich-"+mode);Directory.CreateDirectory(rich);
  using var endpoint=new Endpoint(async(ctx,n)=>{ctx.Response.StatusCode=201;await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{receiptId="11111111111111111111111111111111",eventId=ctx.EventId,status="stored"})));});
  var session=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),home,"2.0.0-test.native+source");session.SetPvfIdentity(new string('a',64));
  var info=new ProcessStartInfo(fixture){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};info.ArgumentList.Add(mode);info.ArgumentList.Add(rich);session.ApplyEnvironment(info);info.Environment["DFL_TEST_CAPTURE_ROOT"]=home;info.Environment["DFL_DIAGNOSTIC_CAPTURE_HELPER"]=helper;
  var faultModule=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fixture))!,"fault-module",Path.GetFileName(Path.GetDirectoryName(fixture))!,"DFLegacy.Client.dll");if(mode=="module-crash")info.Environment["DFL_TEST_FAULT_MODULE"]=faultModule;
  using var child=Process.Start(info)!;var ready=await child.StandardOutput.ReadLineAsync();Check(ready?.StartsWith("ready ")==true,"actual native fixture starts registered "+mode);session.BindProcess(child);await child.StandardInput.WriteLineAsync();await child.StandardInput.FlushAsync();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
  for(var i=0;i<30;i++){session.CollectConfirmed();if(new DiagnosticQueue(session).ReadPending().Count>=(mode=="signature-match"?2:1))break;await Task.Delay(100);}
  await new DiagnosticQueue(session).UploadPendingAsync();
  Check(endpoint.Bodies.Count==(mode=="signature-match"?2:mode=="module-crash"?1:0),"native typed producer through managed collection sends expected wire count "+mode);
  foreach(var body in endpoint.Bodies) {
   var accepted=ClientDiagnosticReport.TryParse(body,out var report);Check(accepted,"real native automatic wire passes authoritative server validator "+mode);
   if(!accepted)continue;
   Check(report!.ClientPvfSha256==new string('a',64)&&report.ClientVersion=="2.0.0-test.native+source","native wire freezes current session PVF/build identity");
   if(mode=="module-crash")Check(report.Module=="DFLegacy.Client.dll"&&report.ModuleHash==Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(faultModule))),"real native fault module SHA survives actual HTTP wire");
   else Check(report.Kind=="networkerror"&&report.Context.StartsWith("protocol tcp; port 0; socket "),"validated hooks emit only typed bounded network fields");
   await ServerCompatibility(Path.Combine(home,report.EventId),client,JsonSerializer.Deserialize<DiagnosticIncident>(JsonSerializer.Serialize(new {kind=report.Kind,stage=report.Stage,evidence=report.Evidence,module=report.Module,moduleHash=report.ModuleHash,moduleOffset=report.ModuleOffset,exceptionCode=report.ExceptionCode,clientPvfSha256=report.ClientPvfSha256}),new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
  }
 }
}

async Task NativeIntegrationTests()
{
 var hardware=DiagnosticProjection.CollectSystem();
 Check(hardware.ContainsKey("gpuDriverVersion")&&hardware.ContainsKey("gpuMemoryBytes"),"bounded adapter collection explicitly returns driver and memory or unknown");
 var home=Path.Combine(root,"native-user");var client=Path.Combine(root,"native-client");
 var settings=new DiagnosticSettings(home);settings.SetEnabled(false);
 var oldEpoch=settings.Read().Epoch;
 Check(settings.Read().LocalSmallDumps,"local small dumps default on even when automatic reporting is disabled");
 settings.SetLocalSmallDumps(false);
 Check(!settings.Read().Enabled&&!settings.Read().LocalSmallDumps&&settings.Read().Epoch==oldEpoch,"independent dump toggle preserves existing basic opt-out and epoch");
 settings.SetEnabled(true);Check(!settings.Read().LocalSmallDumps,"basic reporting toggle does not reset dump preference");settings.SetEnabled(false);settings.SetLocalSmallDumps(true);
 try {
  var local=DiagnosticSession.Start(client,"",Guid.NewGuid().ToString("N"),home);
  Check(!new DiagnosticQueue(local).IsEligible,"missing endpoint creates local-only session without any upload eligibility");
  Check(DiagnosticSession.Open(local.DirectoryPath,local.Registration.Capability,home).SessionId==local.SessionId,"local-only session remains capability registered");
 }catch(ArgumentException){Check(false,"missing endpoint must retain local-only native diagnostics");}
 settings.SetEnabled(true);
 var session=DiagnosticSession.Start(client,"http://127.0.0.1:32241",Guid.NewGuid().ToString("N"),home);
 var old=session.Registration with { StartedAt=DateTimeOffset.UtcNow.AddDays(-2) };
 var path=Path.Combine(session.DirectoryPath,"session.json");
 File.WriteAllText(path,JsonSerializer.Serialize(old,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 try {Check(DiagnosticSession.Open(session.DirectoryPath,session.Registration.Capability,home).SessionId==session.SessionId,"verified live owner survives 24-hour session age");}
 catch(InvalidDataException){Check(false,"verified live owner must not expire at 24 hours");}
 File.WriteAllText(path,JsonSerializer.Serialize(old with {LauncherCreatedUtcTicks=1},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 try {DiagnosticSession.Open(session.DirectoryPath,session.Registration.Capability,home);Check(false,"stale mismatched PID creation binding must be refused");}catch(InvalidDataException){Check(true,"stale mismatched PID creation binding refused");}
 File.WriteAllText(path,JsonSerializer.Serialize(session.Registration,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 var exit=new DiagnosticIncident{Kind="error",Stage="game-exit",Evidence="abnormal-process-exit"};session.WriteIncident(exit,"confirmed");
 Check(session.CollectConfirmed()==0,"exit summary waits for late native confirmation before freezing upload");
 var native=new DiagnosticIncident{Kind="crash",Stage="game",Evidence="original-exception-context"};session.WriteIncident(native,"confirmed");
 Check(session.CollectConfirmed()==1&&new DiagnosticQueue(session).ReadPending().Count==1,"late native original context suppresses earlier unlinked exit across polls");
 var unknown=DiagnosticSession.Start(Path.Combine(client,"unknown"),"http://127.0.0.1:32241",Guid.NewGuid().ToString("N"),home);unknown.WriteIncident(exit with {EventId=Guid.NewGuid().ToString("N")},"confirmed");
 Check(unknown.CollectConfirmed()==0,"unknown exit also waits for bounded native final drain");
 foreach(var p in Directory.EnumerateFiles(Path.Combine(unknown.DirectoryPath,"events"),"*.json"))File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddSeconds(-7));
 // Registration start predates the deliberately aged fixture event.
 File.WriteAllText(Path.Combine(unknown.DirectoryPath,"session.json"),JsonSerializer.Serialize(unknown.Registration with{StartedAt=DateTimeOffset.UtcNow.AddMinutes(-1)},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 unknown=DiagnosticSession.Open(unknown.DirectoryPath,unknown.Registration.Capability,home);
 Check(unknown.CollectConfirmed()==1,"unknown exit eventually reports as error once native drain ends");
 unknown.WriteIncident(native with{EventId=Guid.NewGuid().ToString("N")},"confirmed");
 Check(unknown.CollectConfirmed()==0&&new DiagnosticQueue(unknown).ReadPending().Count==1,"exceptional late native record cannot double-count an already frozen unknown exit");
 var childInfo=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};childInfo.ArgumentList.Add(typeof(Endpoint).Assembly.Location);childInfo.ArgumentList.Add("--game-fixture");
 using var live=Process.Start(childInfo)!;
 try {
  var closing=DiagnosticSession.Start(Path.Combine(client,"closing"),"http://127.0.0.1:32241",Guid.NewGuid().ToString("N"),home);
  var observer=DiagnosticSession.Open(closing.DirectoryPath,closing.Registration.Capability,home);closing.BindProcess(live);closing.Complete();
  await DiagnosticHelper.RunAsync(observer).WaitAsync(TimeSpan.FromSeconds(10));
  Check(!live.HasExited&&File.Exists(Path.Combine(closing.DirectoryPath,"session.json"))&&Directory.Exists(Path.Combine(closing.DirectoryPath,"events")),"closed uploader cannot delete a verified live game's registration");
 }finally{if(!live.HasExited)live.Kill();await live.WaitForExitAsync();}
}

async Task Run()
{
 var home=Path.Combine(root,"user"); var client=Path.Combine(root,"client"); Directory.CreateDirectory(client);
 var settings=new DiagnosticSettings(home);
 Check(settings.Read().Enabled,"current-user default enables automatic basic reporting without interaction");
 var session=DiagnosticSession.Start(client,"http://127.0.0.1:32191",Guid.NewGuid().ToString("N"),home);
 var incident=new DiagnosticIncident { Kind="crash",Stage="game",Evidence="confirmed-unhandled-exception",ExceptionCode="0xC0000005",Module="game.exe",ModuleOffset="0x1234",Stack=["game.exe+0x1234"] };
 var report=DiagnosticProjection.Create(incident,session.SessionId,"test-1",new Dictionary<string,string>{{"cpuModel","password=FAKE_PASSWORD C:\\Users\\secret"},{"gpuModel","Bearer FAKE_TOKEN"},{"username","FAKE_ACCOUNT"}});
 var bytes=DiagnosticJson.Serialize(report); var body=Encoding.UTF8.GetString(bytes);
 Check(ClientDiagnosticReport.TryParse(bytes,out _),"projected report satisfies authoritative server validator");
 if(OperatingSystem.IsWindows())Check(DiagnosticProjection.CollectSystem().ContainsKey("totalMemoryBytes"),"bounded native physical-memory query supplies hardware snapshot");
 Check(!body.Contains("FAKE_")&&!body.Contains("Users")&&!body.Contains("username"),"unsafe hardware values and arbitrary metadata never reach wire");
 var dirty=DiagnosticProjection.Create(incident with { Module="C:\\secret\\game.exe",Stage="password=secret",Stack=["C:\\secret\\game.exe+0x12"],ExceptionCode="Bearer secret" },session.SessionId,"bad version token");
 Check(ClientDiagnosticReport.TryParse(DiagnosticJson.Serialize(dirty),out _)&&dirty.Module=="unknown"&&dirty.Stack.Length==0,"unsafe structured fields become unknown and unsafe frames are omitted");
 var queue=new DiagnosticQueue(session);
 Check(queue.Enqueue(report),"registered session queues first report");
 Check(!queue.Enqueue(report with { Occurrences=9 }),"same event ID cannot replace frozen first body");
 Check(queue.ReadPending().Single().Body.SequenceEqual(bytes),"first serialized bytes remain frozen");
 var copied=DiagnosticSession.Start(Path.Combine(root,"copied-client"),"http://127.0.0.1:32191",Guid.NewGuid().ToString("N"),home);
 Check(new DiagnosticQueue(copied).ReadPending().Count==0,"copied client directory cannot inherit installation queue");
 settings.SetEnabled(false);
 Check(!queue.Enqueue(report with { EventId=Guid.NewGuid().ToString("N") }),"disable blocks existing session enqueue");
 settings.SetEnabled(true);
 Check(new DiagnosticQueue(DiagnosticSession.Start(client,"http://127.0.0.1:32191",Guid.NewGuid().ToString("N"),home)).ReadPending().Count==0,"re-enable starts epoch without silently resuming old reports");
 Check(!queue.IsEligible,"old session stays revoked after re-enable");
 Check(new DiagnosticSettings(home).Read().Enabled,"preference reload persists");
 settings.SetEnabled(false); Check(!new DiagnosticSettings(home).Read().Enabled,"closed preference survives a new settings instance"); settings.SetEnabled(true);
 var current=DiagnosticSession.Start(client,"http://127.0.0.1:32191",Guid.NewGuid().ToString("N"),home);
 incident=incident with {EventId=Guid.NewGuid().ToString("N")};
 current.WriteIncident(incident with {EventId=Guid.NewGuid().ToString("N")},"provisional"); current.WriteIncident(incident with {EventId=Guid.NewGuid().ToString("N")},"cancelled");
 File.WriteAllText(Path.Combine(client,"launcher.log"),"password=SHARED_OLD_SECRET");
 Check(current.CollectConfirmed()==0,"provisional cancelled and shared logs are never upload ready");
 current.WriteIncident(incident,"confirmed");
 Check(current.CollectConfirmed()==1&&current.CollectConfirmed()==0,"confirmed new session event is consumed once");
 current.WriteIncident(incident with { EventId=Guid.NewGuid().ToString("N"),Kind="crash",Evidence="abnormal-process-exit",RelatedEventId=incident.EventId },"confirmed");
 Check(current.CollectConfirmed()==0,"linked game exit does not count confirmed native event twice");
 var bad=Path.Combine(current.DirectoryPath,"events",Guid.NewGuid().ToString("N")+".json"); File.WriteAllBytes(bad,new byte[262145]);
 Check(current.CollectConfirmed()==0,"oversized local record is not collected");
 var target=Path.Combine(root,"outside.json"); File.WriteAllBytes(target,bytes);
 try { File.CreateSymbolicLink(Path.Combine(current.DirectoryPath,"events",Guid.NewGuid().ToString("N")+".json"),target); Check(current.CollectConfirmed()==0,"symbolic-link local record is not collected"); }
 catch(Exception e) when(e is UnauthorizedAccessException or IOException) { Console.WriteLine("SKIP: file symbolic link requires Windows privilege"); }
 if(OperatingSystem.IsWindows()) {
  var junction=Path.Combine(root,"linked-user");var link=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};link.ArgumentList.Add("/c");link.ArgumentList.Add("mklink");link.ArgumentList.Add("/J");link.ArgumentList.Add(junction);link.ArgumentList.Add(home);
  using var command=Process.Start(link)!;await command.WaitForExitAsync();Check(command.ExitCode==0,"junction fixture is created without symbolic-link privileges");
  if(command.ExitCode==0){try {new DiagnosticSettings(junction).Read();Check(false,"linked user directory must be refused");}catch(IOException){Check(true,"reparse ancestor cannot enter user queue");}finally{Directory.Delete(junction);}}
 }
 var changed=DiagnosticSession.Start(client,"http://127.0.0.1:32192",Guid.NewGuid().ToString("N"),home);
 Check(!new DiagnosticQueue(current).IsEligible&&new DiagnosticQueue(changed).ReadPending().Count==0,"switching site revokes old queue and cannot redirect it");
 var back=DiagnosticSession.Start(client,"http://127.0.0.1:32191",Guid.NewGuid().ToString("N"),home);
 Check(new DiagnosticQueue(back).ReadPending().Count==0,"switching back does not resurrect prior site epoch");
 Check(DiagnosticSession.Open(back.DirectoryPath,back.Registration.Capability,home).SessionId==back.SessionId,"helper opens current-user capability registration");
 try {DiagnosticSession.Open(back.DirectoryPath,Guid.NewGuid().ToString("N"),home);Check(false,"wrong capability must be refused");}catch(InvalidDataException){Check(true,"wrong capability is refused");}
 var spoof=new{sessionId=back.SessionId,capability=back.Registration.Capability,state="confirmed",incident=incident with{EventId=Guid.NewGuid().ToString("N")},producerPid=123,producerCreatedUtcTicks=1};
 File.WriteAllText(Path.Combine(back.DirectoryPath,"events","spoof.json"),JsonSerializer.Serialize(spoof));Check(back.CollectConfirmed()==0,"foreign process identity cannot contribute automatic events");
 await HelperLifetime(home,client);
 var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 using(var blocked=new Endpoint(async(ctx,n)=>await release.Task)) {
  var closing=DiagnosticSession.Start(client,blocked.Url,Guid.NewGuid().ToString("N"),home);new DiagnosticQueue(closing).Enqueue(DiagnosticProjection.Create(incident with{EventId=Guid.NewGuid().ToString("N")},closing.SessionId,"test-1"));closing.Complete();
  using var stop=new CancellationTokenSource();var drain=DiagnosticHelper.RunAsync(closing,stop.Token,TimeSpan.FromMilliseconds(200));
  var completed=await Task.WhenAny(drain,Task.Delay(2000));Check(completed==drain,"own-session stop bounds final drain even with a stalled receiver");stop.Cancel();await drain;release.TrySetResult();
 }
 var boundedHome=Path.Combine(root,"bounded-user");var bounded=DiagnosticSession.Start(client,"http://127.0.0.1:32194",Guid.NewGuid().ToString("N"),boundedHome);var boundedQueue=new DiagnosticQueue(bounded);
 for(var i=0;i<100;i++)boundedQueue.Enqueue(DiagnosticProjection.Create(incident with{EventId=Guid.NewGuid().ToString("N")},bounded.SessionId,"test-1"));
 var nextSite=DiagnosticSession.Start(client,"http://127.0.0.1:32195",Guid.NewGuid().ToString("N"),boundedHome);
 Check(!new DiagnosticQueue(nextSite).Enqueue(DiagnosticProjection.Create(incident with{EventId=Guid.NewGuid().ToString("N")},nextSite.SessionId,"test-1")),"100 report cap applies globally across site and epoch changes");
 Check(File.ReadAllText(Path.Combine(boundedHome,"drops.json"))=="1","global queue admission records bounded dropped count");
 await TransportTests(home,client,incident);
 await ServerCompatibility(home,client,incident);
}

async Task StatusTests()
{
 var home=Path.Combine(root,"status-user");var client=Path.Combine(root,"status-client");var settings=new DiagnosticSettings(home);
 var empty=settings.GetStatus(client,"http://127.0.0.1:32201/");
 Check(empty.Receiver=="http://127.0.0.1:32201"&&empty.Enabled&&empty.Pending==0&&empty.Held==0&&empty.LastStatus=="never","empty read-only status resolves actual origin without inventing a send result");
 Check(!Directory.Exists(home),"inspecting status never creates diagnostic storage");
 var one=DiagnosticSession.Start(client,"http://127.0.0.1:32201",Guid.NewGuid().ToString("N"),home);new DiagnosticQueue(one).Enqueue(DiagnosticProjection.Create(new DiagnosticIncident(),one.SessionId,"test-1"));
 var queued=settings.GetStatus(client,one.Registration.Site);Check(queued.Pending==1&&queued.Held==0&&queued.LastStatus=="never","status exposes actual unsent queue count");
 var two=DiagnosticSession.Start(client,"http://127.0.0.1:32202",Guid.NewGuid().ToString("N"),home);new DiagnosticQueue(two).Enqueue(DiagnosticProjection.Create(new DiagnosticIncident(),two.SessionId,"test-1"));
 var before=Directory.GetFiles(home,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
 var active=settings.GetStatus(client,two.Registration.Site);var prior=settings.GetStatus(client,one.Registration.Site);
 Check(active.Pending==1&&active.Held==1&&prior.Pending==0&&prior.Held==2,"status distinguishes current queue from held prior site records");
 Check(before.Count==Directory.GetFiles(home,"*",SearchOption.AllDirectories).Length&&before.All(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))==p.Value)&&new DiagnosticQueue(two).IsEligible&&!new DiagnosticQueue(one).IsEligible,"inspecting another destination does not write or rotate any scope");
 settings.SetEnabled(false);var disabled=settings.GetStatus(client,two.Registration.Site);Check(!disabled.Enabled&&disabled.Pending==0&&disabled.Held==2,"disabled status shows held records without implying they will send");
 foreach(var (code,want) in new[]{(404,"unsupported"),(503,"exhausted"),(201,"uploaded")}) {
  using var endpoint=new Endpoint(async(ctx,n)=>{ctx.Response.StatusCode=code;if(code==201)await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{receiptId="11111111111111111111111111111111",eventId=ctx.EventId,status="stored"})));});
  var sendHome=Path.Combine(root,"status-send-"+code);var s=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),sendHome);var q=new DiagnosticQueue(s);q.Enqueue(DiagnosticProjection.Create(new DiagnosticIncident(),s.SessionId,"test-1"));
  var read=new DiagnosticSettings(sendHome);read.GetStatus(client,endpoint.Url);Check(endpoint.Bodies.Count==0,"status inspection performs no HTTP request");
  await q.UploadPendingAsync(new DiagnosticUploadOptions{RetryDelay=TimeSpan.Zero});var sent=read.GetStatus(client,endpoint.Url);
  Check(sent.LastStatus==want&&sent.Receiver==endpoint.Url&&sent.Available,$"status exposes actual persisted HTTP {code} result");
  if(code==201)Check(sent.ReceiptId=="11111111111111111111111111111111"&&sent.Uploaded==1&&sent.Pending==0,"status exposes validated successful receipt");
  if(code==503)Check(sent.Pending==1&&sent.Held==0,"exhausted run remains pending a future launcher retry");
  if(code==404)Check(sent.Pending==0&&sent.Held==1,"unsupported receive route remains held rather than shown as sending");
 }
 var corrupt=Path.Combine(root,"status-corrupt");Directory.CreateDirectory(Path.Combine(corrupt,"queue"));File.WriteAllBytes(Path.Combine(corrupt,"queue",Guid.NewGuid().ToString("N")+".json"),new byte[400001]);
 Check(!new DiagnosticSettings(corrupt).GetStatus(client,"http://127.0.0.1:32201").Available,"oversized local status record returns bounded unavailable status");
}

async Task HelperLifetime(string home,string client)
{
 Process Child(params string[] arguments){var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(typeof(Endpoint).Assembly.Location);foreach(var value in arguments)info.ArgumentList.Add(value);return Process.Start(info)!;}
 using var game=Child("--game-fixture");
 var session=DiagnosticSession.Start(client,"http://127.0.0.1:32193",Guid.NewGuid().ToString("N"),home);
 var early=new{sessionId=session.SessionId,capability=session.Registration.Capability,state="confirmed",incident=new DiagnosticIncident{Kind="crash",Architecture="x86",Evidence="confirmed-unhandled-exception"},producerPid=game.Id,producerCreatedUtcTicks=game.StartTime.ToUniversalTime().Ticks};
 File.WriteAllText(Path.Combine(session.DirectoryPath,"events","early-native.json"),JsonSerializer.Serialize(early,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 Check(session.CollectConfirmed()==0,"early native event is held until game identity is registered");
 session.BindProcess(game);Check(session.CollectConfirmed()==1,"held native event becomes eligible after matching PID and creation time are bound");
 using var helper=Child("--helper-fixture",session.DirectoryPath,session.Registration.Capability,home);
 try {await Task.Delay(800);Check(!helper.HasExited,"helper remains alive while registered owners run");if(!helper.HasExited)helper.Kill();await helper.WaitForExitAsync();Check(!game.HasExited,"helper exit does not terminate the registered game");}
 finally {if(!helper.HasExited)helper.Kill();if(!game.HasExited)game.Kill();await game.WaitForExitAsync();}
}

async Task TransportTests(string home,string client,DiagnosticIncident incident)
{
 foreach(var (code,expected,calls) in new[]{(201,"uploaded",1),(429,"retry",1),(503,"exhausted",3),(404,"unsupported",1),(410,"deleted",1),(302,"redirect-rejected",1),(409,"conflict",1)}) {
  using var endpoint=new Endpoint(async (ctx,n)=> { ctx.Response.StatusCode=code; if(code==429)ctx.Response.Headers["Retry-After"]="60"; if(code==302)ctx.Response.RedirectLocation="http://127.0.0.1:1/leak"; if(code==201)await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {receiptId="11111111111111111111111111111111",eventId=ctx.EventId,status="stored"}))); });
  var s=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),home); var q=new DiagnosticQueue(s); q.Enqueue(DiagnosticProjection.Create(incident with { EventId=Guid.NewGuid().ToString("N") },s.SessionId,"test-1"));
  var result=await q.UploadPendingAsync(new DiagnosticUploadOptions { RetryDelay=TimeSpan.Zero });
  Check(result.Status==expected&&endpoint.Bodies.Count==calls,$"HTTP {code} yields {expected} with {calls} bounded attempts");
  Check(endpoint.Bodies.All(b=>b.SequenceEqual(endpoint.Bodies[0])),"HTTP retry reuses frozen body bytes");
  if(code==429) {q.Enqueue(DiagnosticProjection.Create(incident with{EventId=Guid.NewGuid().ToString("N")},s.SessionId,"test-1"));await q.UploadPendingAsync();Check(endpoint.Bodies.Count==1,"Retry-After pauses other reports for the same site too");}
 }
 using(var endpoint=new Endpoint(async (ctx,n)=> { await Task.Delay(150);ctx.Response.StatusCode=503; })) {
  var s=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),home);var q=new DiagnosticQueue(s);q.Enqueue(DiagnosticProjection.Create(incident with {EventId=Guid.NewGuid().ToString("N")},s.SessionId,"test-1"));
  var one=q.UploadPendingAsync(new DiagnosticUploadOptions{RetryDelay=TimeSpan.Zero}); await Task.Delay(40);
  var two=await new DiagnosticQueue(s).UploadPendingAsync(); Check(two.Status=="busy","cross-instance filesystem lease enforces single upload");
  new DiagnosticSettings(home).SetEnabled(false); await one; Check(endpoint.Bodies.Count==1,"opt-out cancels in-flight upload and prevents retries");new DiagnosticSettings(home).SetEnabled(true);
 }
 using(var endpoint=new Endpoint((ctx,n)=> {ctx.Response.Abort();return Task.CompletedTask;})) {
  var s=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),home);var q=new DiagnosticQueue(s);q.Enqueue(DiagnosticProjection.Create(incident with {EventId=Guid.NewGuid().ToString("N")},s.SessionId,"test-1"));
  var result=await q.UploadPendingAsync(new DiagnosticUploadOptions{RetryDelay=TimeSpan.Zero}); Check(result.Status=="exhausted"&&endpoint.Bodies.Count==3,"lost response retries exactly three attempts");
 }
 // A plain HTTP listener contacted using TLS cannot present a valid certificate; no HTTP fallback is allowed.
 using(var endpoint=new Endpoint((ctx,n)=>Task.CompletedTask)) {
  var s=DiagnosticSession.Start(client,endpoint.Url.Replace("http:","https:"),Guid.NewGuid().ToString("N"),home);var q=new DiagnosticQueue(s);q.Enqueue(DiagnosticProjection.Create(incident with {EventId=Guid.NewGuid().ToString("N")},s.SessionId,"test-1"));
  var result=await q.UploadPendingAsync(new DiagnosticUploadOptions{RetryDelay=TimeSpan.Zero,RequestTimeout=TimeSpan.FromMilliseconds(250)});Check(result.Status=="exhausted"&&endpoint.Bodies.Count==0,"TLS failure cannot downgrade to HTTP");
 }
 using(var tls=new InvalidCertificateEndpoint()) {
  var s=DiagnosticSession.Start(client,tls.Url,Guid.NewGuid().ToString("N"),home);var q=new DiagnosticQueue(s);q.Enqueue(DiagnosticProjection.Create(incident with {EventId=Guid.NewGuid().ToString("N")},s.SessionId,"test-1"));
  var result=await q.UploadPendingAsync(new DiagnosticUploadOptions{RetryDelay=TimeSpan.Zero,RequestTimeout=TimeSpan.FromSeconds(2)});
  Check(result.Status=="exhausted"&&tls.Attempts==3&&tls.HttpBytes==0,"untrusted self-signed certificate is rejected before any report HTTP bytes");
 }
 using(var endpoint=new Endpoint(async(ctx,n)=> {ctx.Response.StatusCode=n<=3?503:201;if(n>3)await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{receiptId="11111111111111111111111111111111",eventId=ctx.EventId,status="stored"})));})) {
  var s=DiagnosticSession.Start(client,endpoint.Url,Guid.NewGuid().ToString("N"),home);var q=new DiagnosticQueue(s);q.Enqueue(DiagnosticProjection.Create(incident with{EventId=Guid.NewGuid().ToString("N")},s.SessionId,"test-1"));
  await q.UploadPendingAsync(new DiagnosticUploadOptions{RetryDelay=TimeSpan.Zero,ExhaustedRetryDelay=TimeSpan.Zero});await q.UploadPendingAsync();Check(endpoint.Bodies.Count==3,"same launcher cannot reset exhausted budget on the next poll");
  var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{typeof(Endpoint).Assembly.Location,"--queue-retry-run",home,client,endpoint.Url})info.ArgumentList.Add(arg);using var child=Process.Start(info)!;var log=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();
  Check(child.ExitCode==0&&endpoint.Bodies.Count==4,"new genuine launcher run retries prior failure and succeeds: "+await log+await errors);Check(endpoint.Bodies.All(b=>b.SequenceEqual(endpoint.Bodies[0])),"cross-launcher recovery preserves frozen ID and bytes");
 }
}

async Task ServerCompatibility(string home,string client,DiagnosticIncident incident)
{
 var serverRoot=Path.Combine(root,"server"); Directory.CreateDirectory(Path.Combine(serverRoot,"creature")); File.WriteAllText(Path.Combine(serverRoot,"creature","exptable.tbl"),"10\n100\n");
 var port=Endpoint.FreePort();var config=Path.Combine(serverRoot,"server.json");
 File.WriteAllText(config,JsonSerializer.Serialize(new{DFLegacy=new{DataPath=Path.Combine(serverRoot,"state.json"),SeedTestAccount=false,ScriptPvfPath="",SkillScriptPath=serverRoot,Admin=new{Host="127.0.0.1",Port=port},Entrance=new{Enabled=false},CharacterDatagram=new{Enabled=false},GameplayDatagram=new{Enabled=false},FallbackRelay=new{Enabled=false},GameProbe=new{Enabled=false},WebPanels=new{Gm=new{Enabled=false},Player=new{Enabled=false}}}}));
 var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(typeof(JsonGameStore).Assembly.Location)!};
 start.ArgumentList.Add(typeof(JsonGameStore).Assembly.Location);start.ArgumentList.Add("--config");start.ArgumentList.Add(config);
 using var host=Process.Start(start)!;var output=host.StandardOutput.ReadToEndAsync();var error=host.StandardError.ReadToEndAsync();
 try {
  using var http=new HttpClient{Timeout=TimeSpan.FromMilliseconds(400)};var ready=false;
  for(var i=0;i<100&&!host.HasExited;i++){try{using var health=await http.GetAsync($"http://127.0.0.1:{port}/health");if(health.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}catch(TaskCanceledException){}await Task.Delay(100);}
  if(!ready)throw new Exception("real server startup failed "+(host.HasExited?await error:"timeout"));
  var s=DiagnosticSession.Start(client,$"http://127.0.0.1:{port}",Guid.NewGuid().ToString("N"),home);var q=new DiagnosticQueue(s);var r=DiagnosticProjection.Create(incident with{EventId=Guid.NewGuid().ToString("N")},s.SessionId,"test-1");q.Enqueue(r);var frozen=q.ReadPending().Single().Body;
  var result=await q.UploadPendingAsync();Check(result.Status=="uploaded"&&result.ReceiptId?.Length==32,"real Task1 account listener accepts client frozen JSON and returns receipt");
  using var content=new ByteArrayContent(frozen);content.Headers.ContentType=new("application/json");using var duplicate=await http.PostAsync($"http://127.0.0.1:{port}/api/client-diagnostics/reports",content);
  var receipt=JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync()).RootElement;Check(duplicate.StatusCode==HttpStatusCode.OK&&receipt.GetProperty("receiptId").GetString()==result.ReceiptId,"real Task1 duplicate POST preserves original receipt and exact bytes");
 } finally {if(!host.HasExited)host.Kill(true);await host.WaitForExitAsync();await output;await error;}
}

sealed class Endpoint : IDisposable
{
 readonly HttpListener listener=new();readonly Task loop;public string Url {get;}public List<byte[]> Bodies {get;}=[];
 public Endpoint(Func<Context,int,Task> action){Url=$"http://127.0.0.1:{FreePort()}";listener.Prefixes.Add(Url+"/");listener.Start();loop=Task.Run(async()=>{while(listener.IsListening){HttpListenerContext c;try{c=await listener.GetContextAsync();}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}using var memory=new MemoryStream();await c.Request.InputStream.CopyToAsync(memory);var bytes=memory.ToArray();Bodies.Add(bytes);try{await action(new Context(c,bytes),Bodies.Count);c.Response.Close();}catch(HttpListenerException){}catch(ObjectDisposedException){}}});}
 public static int FreePort(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();var p=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}
 public void Dispose(){listener.Close();loop.GetAwaiter().GetResult();}
 public sealed class Context(HttpListenerContext value,byte[] body){public HttpListenerResponse Response=>value.Response;public string EventId=>JsonDocument.Parse(body).RootElement.GetProperty("eventId").GetString()!;}
}

sealed class InvalidCertificateEndpoint:IDisposable
{
 readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource stopped=new();readonly X509Certificate2 certificate;readonly Task loop;
 public string Url {get;}public int Attempts;public int HttpBytes;
 public InvalidCertificateEndpoint(){using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddMinutes(10));listener.Start();Url=$"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";loop=Task.Run(async()=>{while(!stopped.IsCancellationRequested){try{using var socket=await listener.AcceptTcpClientAsync(stopped.Token);Interlocked.Increment(ref Attempts);using var tls=new SslStream(socket.GetStream());try{await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=certificate},stopped.Token);var buffer=new byte[1024];HttpBytes+=await tls.ReadAsync(buffer,stopped.Token);}catch(AuthenticationException){}catch(IOException){}}catch(OperationCanceledException){break;}}});}
 public void Dispose(){stopped.Cancel();listener.Stop();loop.GetAwaiter().GetResult();certificate.Dispose();stopped.Dispose();}
}
