using System.Diagnostics;
using System.Text.Json;
using DFLegacy.Diagnostics;
using DFLegacy.Launcher.Diagnostics;

namespace DFLegacy.Launcher;

internal static class NativeDiagnosticLifetimeTests
{
    internal static async Task Run(string root,Action<bool,string> check,string nativeDirectory)
    {
        var settings=new DiagnosticSettings(Path.Combine(root,"live-native-user"));
        Program.UserDiagnosticSettings=settings;
        var options=new LauncherOptions{ClientPath=Path.Combine(root,"live-native-client","DNF.exe"),AccountServiceUrl="http://127.0.0.1:32243"};
        AutomaticDiagnostics.Configure(options,settings);
        var session=AutomaticDiagnostics.Session??throw new InvalidOperationException("Missing initial diagnostic session.");
        var rich=Path.Combine(root,"native-rich");Directory.CreateDirectory(rich);
        var start=new ProcessStartInfo(Path.Combine(nativeDirectory,"native_incident_tests.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("crash");start.ArgumentList.Add(rich);session.ApplyEnvironment(start);
        start.Environment["DFL_TEST_CAPTURE_ROOT"]=settings.Root;
        start.Environment["DFL_DIAGNOSTIC_CAPTURE_HELPER"]=Path.Combine(nativeDirectory,"native_capture_helper_fixture.exe");
        using var game=Process.Start(start)!;
        using var stop=new CancellationTokenSource();Task uploader=Task.CompletedTask;
        try
        {
            var ready=(await game.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))?.Split(' ');
            check(ready is {Length:3}&&ready[0]=="ready"&&ready[1]==game.Id.ToString()&&ready[2]==game.StartTime.ToUniversalTime().Ticks.ToString(),"actual native fixture agrees with launcher PID/creation identity");
            session.BindProcess(game);uploader=DiagnosticHelper.RunAsync(session,stop.Token);
            // Exact setting-save sequence used by ConfigWindow: basic reporting
            // is still enabled; only the next-start dump checkbox changes.
            settings.SetEnabled(true);settings.SetLocalSmallDumps(false);AutomaticDiagnostics.Configure(options,settings);
            await Task.Delay(7500); // Exceeds the old helper's 6.5s cleanup drain.
            check(ReferenceEquals(session,AutomaticDiagnostics.Session),"saving next-start dump preference retains the bound live session");
            check(File.Exists(Path.Combine(session.DirectoryPath,"session.json"))&&Directory.Exists(Path.Combine(session.DirectoryPath,"events")),"live game's registration survives any uploader drain");
            check(session.Registration.LocalSmallDumps&&!settings.Read().LocalSmallDumps,"running game keeps original dump preference while next launch sees the new value");
            stop.Cancel();await uploader; // Avoid any HTTP: inspect local capture and frozen queue only.
            await game.StandardInput.WriteLineAsync();await game.StandardInput.FlushAsync();await game.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var events=Path.Combine(session.DirectoryPath,"events");string? confirmed=null;
            for(var attempt=0;attempt<60&&confirmed is null;attempt++){confirmed=Directory.Exists(events)?Directory.EnumerateFiles(events,"*-confirmed.json").FirstOrDefault():null;if(confirmed is null)await Task.Delay(50);}
            check(confirmed is not null,"same bound native game still captures its real fault after the setting save");
            if(confirmed is not null)
            {
                using var document=JsonDocument.Parse(File.ReadAllText(confirmed));var value=document.RootElement;var incident=value.GetProperty("incident");var id=incident.GetProperty("eventId").GetString()!;
                check(value.GetProperty("producerPid").GetInt32()==game.Id&&incident.GetProperty("exceptionCode").GetString()=="0xC0000005","captured event belongs to the original live game and actual AV");
                var dump=Path.Combine(settings.Root,"captures",id+".dmp");check(File.Exists(dump)&&new FileInfo(dump).Length is >0 and <=16*1024*1024,"running game retains its original enabled local dump setting");
                check(session.CollectConfirmed()==1,"basic reporting remains eligible after changing only local dump preference");
            }
            AutomaticDiagnostics.Configure(options,settings);
            check(AutomaticDiagnostics.Session is { } next&&!ReferenceEquals(next,session)&&!next.Registration.LocalSmallDumps,"after verified game exit the next session uses the saved dump preference");
        }
        finally
        {
            stop.Cancel();await uploader;
            if(!game.HasExited){game.Kill();await game.WaitForExitAsync();}
            settings.SetEnabled(false);
        }
    }
}
