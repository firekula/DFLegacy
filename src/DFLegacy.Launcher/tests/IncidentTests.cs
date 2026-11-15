using DFLegacy.Launcher.Diagnostics;

namespace DFLegacy.Launcher;

internal static class IncidentTests
{
    internal static void Run(string root, Action<bool,string> check)
    {
        var folder = Path.Combine(root,"incidents");
        var reporter = new IncidentReporter(folder,"test-version+commit","x86", maxFiles:8, maxBytes:32768);
        var pvf=Path.Combine(root,"Script.pvf");File.WriteAllText(pvf,"abc");
        check(reporter.CapturePvfIdentity(pvf)=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","PVF identity is hashed from the actual local bytes");
        reporter.RegisterSecret("private-user"); reporter.RegisterSecret("private-password");
        reporter.AddContext("login private-user password=private-password Authorization: Bearer secret-token");
        var first = reporter.Report(IncidentKind.Error,"startup",new InvalidOperationException("password=secret-one token=secret-two https://example.test/?key=secret-three", new IOException("inner")));
        check(first is not null && Path.GetFileName(first).StartsWith("error-"),"error creates an individual report");
        var report = File.ReadAllText(first!);
        check(!report.Contains("private-") && !report.Contains("secret-"),"context/exception/URL credentials are redacted");
        check(report.Contains("test-version+commit") && report.Contains("x86") && report.Contains("sessionId=") && report.Contains("eventId=") && report.Contains("installation=") && report.Contains("timezone=") && report.Contains("IOException"),"identity, local time and exception chain survive");
        check(report.Contains("clientPvfSha256=ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),"PVF identity is retained outside the context ring");
        var repeats=Enumerable.Range(0,30).Select(_=>reporter.Report(IncidentKind.Error,"startup",new InvalidOperationException("same"))).ToArray();
        check(repeats.Distinct().Count()==1 && File.ReadAllText(repeats[0]!).Contains("occurrences=30"),"repeated event grouped with count");
        var names = new System.Collections.Concurrent.ConcurrentBag<string?>();
        Parallel.For(0,20,i=>names.Add(reporter.Report(IncidentKind.Error,"concurrent-"+i,new Exception("distinct"))));
        check(names.Distinct().Count()==20,"concurrent reports have unique event names");
        check(Directory.GetFiles(folder,"*.log").Length<=8 && Directory.GetFiles(folder,"*.log").Sum(p=>new FileInfo(p).Length)<=32768,"count and byte retention bounds hold");
        var unrelated=Path.Combine(folder,"player.txt"); File.WriteAllText(unrelated,"preserve");
        using(var update=reporter.BeginUpdate("verify")) { update.AddResult("nested/file.npk sha256=expected result=valid"); update.Complete("success"); }
        var success=Directory.GetFiles(folder,"update-*.log").Select(File.ReadAllText).Single();
        check(success.Contains("result=success") && success.Contains("nested/file.npk"),"successful update includes per-file result");
        using(var update=reporter.BeginUpdate("download")) { update.Complete("failed",new IOException("disk")); }
        check(Directory.GetFiles(folder,"error-*.log").Select(File.ReadAllText).Any(s=>s.Contains("updateId=")),"update failure links its separate error report");
        var before=Directory.GetFiles(folder).Length;
        check(reporter.Report(IncidentKind.Error,"cancel",new OperationCanceledException()) is null && Directory.GetFiles(folder).Length==before,"ordinary cancellation is not an error");
        check(reporter.ReportExit(42,0,false) is null,"normal process exit suppressed");
        var exit=reporter.ReportExit(42,5,false);
        check(exit is not null && File.ReadAllText(exit).Contains("evidence=abnormal-process-exit") && File.ReadAllText(exit).Contains("nativeCause=unknown"),"unexpected exit never invents exception code");
        var native=Path.Combine(folder,"crash-20261108-120000-000-43-"+Guid.NewGuid().ToString("N")+".log");
        File.WriteAllText(native,$"sessionId={reporter.SessionId}\npid=43\nevidence=unhandled-exception-filter\nexceptionCode=0xC0000005\n");
        var linked=reporter.ReportExit(43,unchecked((int)0xc0000005),false);
        check(linked is not null && File.ReadAllText(linked).Contains("nativeReport="+Path.GetFileName(native)),"abnormal exit associates same-session native evidence");
        check(File.Exists(unrelated),"retention leaves unrelated files intact");
        var eof=new DFLegacy.Relay.RelayTermination(DFLegacy.Relay.RelayTerminationReason.Eof,"TCP","transfer",7001,1,"server->client",7,3,10);
        check(reporter.ReportNetwork(eof) is null && reporter.ReportNetwork(eof with { Reason=DFLegacy.Relay.RelayTerminationReason.ExpectedHandoff }) is null,"EOF and expected handoff do not become network reports");
        var network=reporter.ReportNetwork(eof with { Reason=DFLegacy.Relay.RelayTerminationReason.SocketError, SocketError=System.Net.Sockets.SocketError.ConnectionReset });
        check(network is not null && Path.GetFileName(network).StartsWith("networkerror-") && File.ReadAllText(network).Contains("clientToServerBytes=7"),"typed network report preserves transport evidence");
        var retry=reporter.ReportNetwork(eof with { Reason=DFLegacy.Relay.RelayTerminationReason.SocketError, SocketError=System.Net.Sockets.SocketError.ConnectionReset, ConnectionId=2 });
        check(retry==network && File.ReadAllText(network!).Contains("occurrences=2"),"reconnect failures are grouped across different connections");
        var large=new IncidentReporter(Path.Combine(root,"large-update"));
        using(var update=large.BeginUpdate("verify")) { for(var i=0;i<426;i++) update.AddResult($"resource-{i}.npk expected=0123456789abcdef actual=0123456789abcdef result=valid"); update.Complete("success"); }
        check(File.ReadAllText(Directory.GetFiles(Path.Combine(root,"large-update"),"update-*.log").Single()).Contains("resource-425.npk"),"ordinary 426-resource installation retains every per-file result");
        var logClient=Path.Combine(root,"bounded-main-log","DNF.exe");
        var logDir=ClientLogPaths.GetDirectory(logClient);Directory.CreateDirectory(logDir);File.WriteAllText(Path.Combine(logDir,"launcher.log"),new string('a',2*1024*1024));
        check(ClientLogPaths.TryWrite(logClient,"after rotation") && new FileInfo(Path.Combine(logDir,"launcher.log")).Length<1024 && File.Exists(Path.Combine(logDir,"launcher.previous.log")),"main log rotates at the byte budget and continues writing");
        check(DetailedCrashCapture.CreateStartInfo("C:/capture/DFLegacy.CrashCapture.exe",42,folder,false) is null,"debugger capture never starts without explicit opt-in");
        var capture=DetailedCrashCapture.CreateStartInfo("C:/capture/DFLegacy.CrashCapture.exe",42,folder,true);
        check(capture is not null && capture.ArgumentList.Contains("42") && !capture.UseShellExecute,"opt-in capture uses a distinct process with structured arguments");
        var captureRoot=Path.Combine(root,"detailed-budget");
        DetailedCrashCapture.PrepareDirectory(captureRoot,42);DetailedCrashCapture.PrepareDirectory(captureRoot,43);
        try { DetailedCrashCapture.PrepareDirectory(captureRoot,44);check(false,"third detailed session must require clearing earlier local captures"); }
        catch(IOException) { check(Directory.GetDirectories(captureRoot).Length==2,"detailed capture session budget is bounded without deleting evidence"); }
        var automatic=new List<DFLegacy.Diagnostics.DiagnosticIncident>();
        var safe=new IncidentReporter(Path.Combine(root,"auto-incidents"),"test-1","x86") { AutomaticReport=automatic.Add };
        safe.Report(IncidentKind.Error,"startup",new IOException("password=DO_NOT_UPLOAD C:\\Users\\private"),details:"token=DO_NOT_UPLOAD");
        check(automatic.Count==1&&automatic[0].Stage=="startup"&&automatic[0].ExceptionCode.StartsWith("0x"),"managed error emits structured automatic evidence");
        check(!System.Text.Json.JsonSerializer.Serialize(automatic).Contains("DO_NOT_UPLOAD"),"automatic evidence does not transport raw details or exception text");
        var autoCount=automatic.Count;
        safe.Report(IncidentKind.Error,"cancel",new OperationCanceledException());safe.Report(IncidentKind.Update,"success",details:"result=success");
        check(automatic.Count==autoCount,"normal cancellation and successful updates do not emit automatic reports");
        safe.ReportNetwork(eof with {Reason=DFLegacy.Relay.RelayTerminationReason.SocketError,SocketError=System.Net.Sockets.SocketError.ConnectionReset});
        check(automatic.Count==autoCount+1&&automatic[^1].Protocol=="tcp"&&automatic[^1].Port==7001&&automatic[^1].SocketCode==10054,"relay emits only typed protocol port and socket evidence");
        var pvfFixture=Path.Combine(root,"known-pvf");File.WriteAllText(pvfFixture,"pvf version one");var pvfHash=safe.CapturePvfIdentity(pvfFixture);
        safe.Report(IncidentKind.Error,"pvf-known");
        using(var projected=System.Text.Json.JsonDocument.Parse(DFLegacy.Diagnostics.DiagnosticJson.Serialize(DFLegacy.Diagnostics.DiagnosticProjection.Create(automatic[^1],safe.SessionId,"test"))))
            check(projected.RootElement.TryGetProperty("clientPvfSha256",out var wirePvf)&&wirePvf.GetString()==pvfHash,"already acquired PVF hash reaches sanitized automatic wire bytes");
        var groupedId=automatic[^1].EventId;safe.Report(IncidentKind.Error,"pvf-known");
        check(automatic[^1].EventId==groupedId&&DFLegacy.Diagnostics.DiagnosticProjection.Create(automatic[^1],safe.SessionId,"test").Occurrences==1,"minute coalescing preserves one immutable incident count despite repeated notifications");
        check(new LauncherOptions().Diagnostics.Enabled,"launcher default enables automatic reporting without consent prompt");
        var userSettings=new DFLegacy.Diagnostics.DiagnosticSettings(Path.Combine(root,"pregame-diagnostic-user"));
        AutomaticDiagnostics.Configure(new LauncherOptions {ClientPath=Path.Combine(root,"pregame-client","DNF.exe"),AccountServiceUrl="http://127.0.0.1:32196"},userSettings);
        Program.ReportError("update-check-download",new IOException("password=DO_NOT_UPLOAD"));
        var pregame=AutomaticDiagnostics.Session;
        check(pregame is not null&&pregame.Registration.GamePid==0&&pregame.CollectConfirmed()==1,"configured launcher update error is handed off before a game process exists");
        if(pregame is not null) {
            var queued=new DFLegacy.Diagnostics.DiagnosticQueue(pregame).ReadPending().Single();
            check(!System.Text.Encoding.UTF8.GetString(queued.Body).Contains("DO_NOT_UPLOAD"),"pre-game error handoff excludes raw credentials");
            var helper=AutomaticDiagnostics.CreateHelperStartInfo("C:/helper/DFLegacy.Diagnostics.exe",pregame);
            check(helper.ArgumentList.Count==4&&helper.ArgumentList.Contains(pregame.Registration.Capability)&&!helper.UseShellExecute,"automatic helper uses structured session capability arguments without game credentials");
            AutomaticDiagnostics.Configure(new LauncherOptions {ClientPath=Path.Combine(root,"pregame-client","DNF.exe"),AccountServiceUrl="invalid-site"},userSettings);
            Program.ReportError("invalid-site-validation",new IOException("local only"));
            check(pregame.CollectConfirmed()==0,"invalid new site cannot send its error through the previous site's session");
        }
        userSettings.SetEnabled(false);
        var nullPath=Path.Combine(root,"null-diagnostics.json");File.WriteAllText(nullPath,"{\"Diagnostics\":null}");
        var nullOptions=new LauncherAccountStore(nullPath).Load();
        check(nullOptions.Diagnostics is not null&&nullOptions.Diagnostics.Enabled,"explicit null diagnostics loads default enabled options");
        using(var nullLease=new LauncherAccountStore(nullPath).AcquireReadLease())check(nullLease.Options.Diagnostics is not null,"read lease normalizes null diagnostics too");
        nullOptions.ClientPath=Path.Combine(root,"null-client","DNF.exe");nullOptions.AccountServiceUrl="http://127.0.0.1:32197";nullOptions.Diagnostics=null!;
        userSettings.SetEnabled(true);
        try {AutomaticDiagnostics.Configure(nullOptions,userSettings);check(AutomaticDiagnostics.Session is not null,"direct null diagnostics cannot break automatic configuration");}
        catch(NullReferenceException){check(false,"direct null diagnostics cannot break automatic configuration");}
        userSettings.SetEnabled(false);
    }
}
