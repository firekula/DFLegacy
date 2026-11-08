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
    }
}
