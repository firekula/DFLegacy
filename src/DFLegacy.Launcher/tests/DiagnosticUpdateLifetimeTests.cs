using System.Diagnostics;
using DFLegacy.Diagnostics;
using DFLegacy.Launcher.Diagnostics;
using DFLegacy.Launcher.Updates;
namespace DFLegacy.Launcher;
internal static class DiagnosticUpdateLifetimeTests
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var user=new DiagnosticSettings(Path.Combine(root,"update-owned-user"));
        var closed=DiagnosticSession.Start(Path.Combine(root,"closed-fixture"),"",Guid.NewGuid().ToString("N"),user.Root);
        closed.Complete();
        using(var reader=new FileStream(Path.Combine(closed.DirectoryPath,"closed.json"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try {await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(closed.Complete)));check(true,"repeated own-session completion never replaces an already valid close marker");}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){check(false,"repeated own-session completion never replaces an already valid close marker");}
        }
        File.WriteAllText(Path.Combine(closed.DirectoryPath,"closed.json"),"\"wrong-capability\"");closed.Complete();
        check(closed.IsClosed,"wrong capability marker is replaced with this owned session capability");
        File.WriteAllText(Path.Combine(closed.DirectoryPath,"closed.json"),"{");closed.Complete();
        check(closed.IsClosed,"malformed marker is repaired rather than mistaken for owned completion");
        var install=Path.Combine(root,"update-owned-install");Directory.CreateDirectory(install);
        var helper=Path.Combine(install,"DFLegacy.Diagnostics.exe");File.WriteAllText(helper,"owned helper replacement fixture");
        var options=new LauncherOptions{ClientPath=Path.Combine(install,"DNF.exe"),AccountServiceUrl="http://127.0.0.1:1"};
        Process? active=null;
        Process? Start(DiagnosticSession s)
        {
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
            foreach(var a in new[]{"--diagnostic-helper-fixture",s.DirectoryPath,s.Registration.Capability,user.Root,helper})info.ArgumentList.Add(a);
            return active=Process.Start(info);
        }
        AutomaticDiagnostics.Configure(options,user,Start);
        var first=active!;var firstId=first.Id;using var observed=Process.GetProcessById(firstId);using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while(!File.Exists(helper+".ready"))await Task.Delay(20,deadline.Token);
        check(!first.HasExited,"default-on pre-update startup owns a live helper");
        try {using var locked=new FileStream(helper,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);check(false,"active helper must lock managed payload before handoff");}catch(IOException){check(true,"active helper really locks payload");}
        var release=new UpdateRelease(1,"fixture-update",DateTimeOffset.UtcNow,false,new Uri("http://127.0.0.1:1/update.zip"),1,new string('a',64));
        var updateSession=new UpdateControllerTests.Session{Feed=_=>Task.FromResult<UpdateRelease?>(release)};
        updateSession.Download=(attempt,progress,token)=>
        {
            var stage=Path.Combine(attempt,"dflegacy-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            return Task.FromResult(new PreparedUpdate(release,stage,new UpdateManifest(2,"fixture-update",new string('a',40),[],"normal")));
        };
        var preflight=false;
        updateSession.Prepare=(package,token)=>
        {
            using var writable=new FileStream(helper,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
            preflight=true;throw new IOException("deliberate test handoff failure after actual preflight");
        };
        using(var controller=new UpdateController(updateSession,install,()=>check(false,"failed handoff cannot close launcher")))
        {
            await controller.StartAsync();
            check(!observed.HasExited&&controller.State.Phase==UpdatePhase.Ready,"real update check/download leaves default-on helper active");
            await controller.InstallAsync(true);
            check(preflight&&controller.State.Phase==UpdatePhase.Error,"install preflight obtains writable helper after cooperative owned exit");
        }
        check(observed.HasExited,"cooperative own-session close waits for retained helper exit");

        var second=active!;check(second.Id!=firstId&&!second.HasExited,"failed/cancelled update resumes reporting with a new owned helper");
        var session=AutomaticDiagnostics.Session!;
        async Task<bool> PendingStage(string stage)
        {
            for(var attempt=0;attempt<100;attempt++)
            {
                session.CollectConfirmed();
                if(new DiagnosticQueue(session).ReadPending().Any(p=>System.Text.Encoding.UTF8.GetString(p.Body).Contains(stage)))return true;
                await Task.Delay(50);
            }
            return false;
        }
        check(await PendingStage("update-install"),"handoff failure is retained by the resumed diagnostic session");
        session.WriteIncident(new DiagnosticIncident{Stage="after-failed-update"},"confirmed");
        check(await PendingStage("after-failed-update"),"new session still freezes reports after failed handoff");
        await AutomaticDiagnostics.SuspendForUpdateAsync(deadline.Token);
        user.SetEnabled(false);AutomaticDiagnostics.ResumeAfterUpdate();AutomaticDiagnostics.Configure(options,user,Start);
        check(!options.Diagnostics.Enabled&&AutomaticDiagnostics.Session!.Registration.LocalOnly,"package defaults preserve durable disabled choice");
    }
}
