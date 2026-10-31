using System.Net;
using System.Net.Http;
using DFLegacy.Launcher.Updates;
using DFLegacy.Launcher;

internal static class UpdateControllerTests
{
    private sealed class Session : IUpdateSession
    {
        internal Func<CancellationToken, Task<UpdateRelease?>> Feed = _ => Task.FromResult<UpdateRelease?>(null);
        internal int Checks, Downloads, Starts; internal long LastInstalled; internal bool Current = false; internal UpdateInstaller? Installer; internal Exception? StateError;
        internal PreparedUpdate Package = null!; internal string? LastDirectory;
        internal Func<PreparedUpdate,CancellationToken,Task<UpdateHandoff>>? Prepare;
        internal Func<UpdateHandoff,CancellationToken,Task<UpdateHelperHandle>>? Start;
        internal Func<string,IProgress<UpdateProgress>,CancellationToken,Task<PreparedUpdate>>? Download;
        public UpdateState? ReadState() { if (StateError != null) throw StateError; return Installer?.ReadState(); }
        public UpdateApplyResult? ReadLastResult() => null;
        public Task<UpdateRelease?> CheckAsync(long installed, CancellationToken token) { Checks++; LastInstalled=installed; return Feed(token); }
        public Task<PreparedUpdate> DownloadAsync(UpdateRelease release, string root, IProgress<UpdateProgress> progress, CancellationToken token)
        {
            Downloads++; if(Download!=null) return Download(root,progress,token); var destination=Path.Combine(root,"dflegacy-update-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(destination); LastDirectory=destination;
            foreach(var file in Directory.EnumerateFiles(Package.Directory)) File.Copy(file,Path.Combine(destination,Path.GetFileName(file)));
            progress.Report(new(123,456,789)); return Task.FromResult(Package with { Directory=destination });
        }
        public bool Adopt(PreparedUpdate update) => Installer?.RecordCurrentIfMatching(update) ?? Current;
        public Task<UpdateHandoff> PrepareAsync(PreparedUpdate update, CancellationToken token) => Prepare?.Invoke(update,token) ?? throw new IOException("游戏正在占用文件");
        public Task<UpdateHelperHandle> StartAsync(UpdateHandoff handoff, CancellationToken token) { Starts++; return Start?.Invoke(handoff,token) ?? Task.FromResult(new UpdateHelperHandle(1)); }
    }
    internal static async Task Run(string root, Action<bool,string> check)
    {
        var install=Path.Combine(root,"controller"); Directory.CreateDirectory(install);
        var session=new Session(); var closed=0;
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            await controller.StartAsync(); await controller.StartAsync();
            check(session.Checks==1 && controller.State.Phase==UpdatePhase.Current,"startup checks exactly once and no-release remains usable");
            session.Feed=_=>throw new HttpRequestException("429 retry");
            await controller.CheckAsync(); check(controller.State.Phase==UpdatePhase.Error,"manual failure is recoverable state");
            session.Feed=_=>Task.FromResult<UpdateRelease?>(null);
            await controller.CheckAsync(); check(controller.State.Phase==UpdatePhase.Current && session.Checks==3,"manual retry recovers after failure");
            session.StateError=new InvalidDataException("corrupt state"); await controller.CheckAsync();
            check(controller.State.Phase==UpdatePhase.Error && closed==0,"corrupt update state cannot close login"); session.StateError=null;
            session.Feed=async token=>{ await Task.Delay(Timeout.Infinite,token); return null; };
            var pending=controller.CheckAsync();
            await Task.Delay(30);
            check(!pending.IsCompleted,"startup/check is asynchronous while UI remains responsive");
            controller.Cancel(); await pending;
            check(controller.State.Phase==UpdatePhase.Cancelled && closed==0,"cancel unblocks outstanding check without closing login");
        }
        session=new Session();
        using(var previews=UpdateTransportTests.PagedPreviews(_=>{}))
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            var transport=new GitHubUpdateClient(previews,"firekula/DFLegacy",false);
            session.Feed=token=>transport.CheckAsync(cancellationToken:token);
            await controller.CheckAsync();
            check(controller.State.Phase==UpdatePhase.Error && controller.State.Message.Contains("重试") && session.Downloads==0 && closed==0,"truncated stable feed stays retryable without false Current or closing login");
        }
        session=new Session();
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            session.Feed=async token=>{ await Task.Delay(80,token); return null; };
            int changes=0; controller.Changed+=_=>changes++;
            var pending=controller.StartAsync(); controller.Dispose(); var before=changes; await pending;
            check(changes==before,"disposed window ignores late update callbacks");
        }
        // Use the real bounded HTTP transport, package verifier, installed hashes and state writer.
        var bytes=UpdateTransportTests.Package(); var release=UpdateTransportTests.Release(bytes);
        var stage=Path.Combine(install,"stage");
        using var http=UpdateTransportTests.Client(bytes);
        var client=new GitHubUpdateClient(http,"firekula/DFLegacy");
        var package=await client.DownloadAsync(release,stage);
        session=new Session {Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(release)};
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            UpdateProgress? observed=null; controller.Changed+=s=>observed=s.Progress??observed;
            await controller.StartAsync();
            check(controller.State.Phase==UpdatePhase.Ready && observed==new UpdateProgress(123,456,789),"download exposes actual bytes total and speed before confirmation");
            await controller.InstallAsync(false);
            check(session.Starts==0 && closed==0,"declining restart never starts helper or closes login");
        }
        // Fresh package after decline cleanup.
        package=await client.DownloadAsync(release,stage);
        session=new Session {Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(release)};
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            await controller.StartAsync(); await controller.InstallAsync(true);
            check(controller.State.Phase==UpdatePhase.Error && closed==0,"locked game or failed handoff keeps login and allows retry");
        }


        session=new Session {Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(release)};
        var downloading=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Download=async (_,progress,token)=> { progress.Report(new(1,100,1)); downloading.SetResult(); await Task.Delay(Timeout.Infinite,token); return package; };
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            var pending=controller.StartAsync(); await downloading.Task; controller.Cancel(); await pending;
            check(controller.State.Phase==UpdatePhase.Cancelled && closed==0,"download cancellation returns to retry/login without any helper");
        }
        foreach(var disposeAtReady in new[]{false,true})
        {
            session=new Session {Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(release)};
            using var controller=new UpdateController(session,install,()=>closed++);
            controller.Changed+=state=> { if(state.Phase==UpdatePhase.Ready) { if(disposeAtReady) controller.Dispose(); else controller.Cancel(); } };
            await controller.StartAsync();
            await Task.Delay(50);
            check(session.LastDirectory!=null && !Directory.Exists(session.LastDirectory),"cancel/dispose at the final Ready callback cleans the completed attempt: "+disposeAtReady);
        }
        // Controller ordering is tested separately from the engine's real authenticated-pipe suite.
        session=new Session {Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(release)};
        var handoffStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready=new TaskCompletionSource<UpdateHelperHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
        var helperDir=Path.Combine(Path.GetTempPath(),"dflegacy-helper-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(helperDir);
        var helperPath=Path.Combine(helperDir,"DFLegacy.Launcher.exe"); File.WriteAllText(helperPath,"owned controller fixture");
        session.Prepare=(p,t)=>Task.FromResult(new UpdateHandoff(new UpdatePlan(install,p,0,0,"",helperPath,"")));
        session.Start=(_,_)=> { handoffStarted.SetResult(); return ready.Task; };
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            await controller.StartAsync(); var pending=controller.InstallAsync(true); await handoffStarted.Task;
            check(closed==0 && controller.State.Phase==UpdatePhase.Installing,"launcher remains alive until helper readiness succeeds");
            ready.SetResult(new UpdateHelperHandle(int.MaxValue)); await pending;
            check(closed==1,"successful helper readiness triggers shutdown exactly once");
            controller.Cancel(); check(File.Exists(helperPath),"committed handoff ignores cancellation and preserves helper inputs");
        }
        UpdateArtifacts.Sweep(install);
        check(!Directory.Exists(helperDir),"completed owned handoff artifacts are reclaimed on next sweep");
        foreach(var f in package.Manifest.Files) File.Copy(Path.Combine(package.Directory,f.Path),Path.Combine(install,f.Path),true);
        var realInstaller=new UpdateInstaller(install);
        session=new Session {Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(release),Installer=realInstaller};
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            await controller.StartAsync();
            check(controller.State.Phase==UpdatePhase.Current && realInstaller.ReadState()?.ReleaseId==release.Id,"matching installed bytes adopt real release state without restart");
            session.Feed=_=>Task.FromResult<UpdateRelease?>(null); await controller.CheckAsync();
            check(session.LastInstalled==release.Id && session.Downloads==1,"next check uses adopted release ID without downloading same bytes again");
        }
        var beforeExe=File.ReadAllBytes(Path.Combine(install,"DFLegacy.Launcher.exe"));
        session=new Session { Package=package,Installer=realInstaller,Feed=_=>Task.FromResult<UpdateRelease?>(release with {Id=release.Id+1}) };
        using(var lockedState=new FileStream(Path.Combine(install,".dflegacy","state.json"),FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var controller=new UpdateController(session,install,()=>closed++))
        {
            var priorClose=closed; await controller.StartAsync();
            check(controller.State.Phase==UpdatePhase.Error && closed==priorClose && File.ReadAllBytes(Path.Combine(install,"DFLegacy.Launcher.exe")).SequenceEqual(beforeExe),"unwritable update state remains a retryable error and leaves login/installed bytes intact");
        }
        using(var realSession=new UpdateSession(install,new LauncherOptions()))
        using(var gameLock=new FileStream(Path.Combine(install,"ijl15.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try { await realSession.PrepareAsync(package,CancellationToken.None); check(false,"real game DLL lock must defer helper preparation"); }
            catch(IOException) { }
        }
        Console.WriteLine("Update controller lifecycle checks executed");
    }
}
