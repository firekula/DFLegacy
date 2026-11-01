namespace DFLegacy.Launcher.Updates;
internal enum UpdatePhase { Idle, Checking, Downloading, Ready, Installing, Current, Cancelled, Error }
internal sealed record UpdateViewState(UpdatePhase Phase, string Message, string Version = "", UpdateProgress? Progress = null, bool IsRecovery=false, string AssetName="");
internal interface IUpdateSession
{
    UpdateRecovery? ReadRecovery()=>null;
    Task<UpdateHandoff> PrepareRecoveryAsync(UpdateRecovery recovery,CancellationToken token)=>throw new InvalidOperationException("当前会话不支持恢复。");
    UpdateState? ReadState();
    UpdateApplyResult? ReadLastResult();
    Task<UpdateRelease?> CheckAsync(long installed, CancellationToken token);
    Task<PreparedUpdate> DownloadAsync(UpdateRelease release, string root, IProgress<UpdateProgress> progress, CancellationToken token);
    bool Adopt(PreparedUpdate update);
    Task<UpdateHandoff> PrepareAsync(PreparedUpdate update, CancellationToken token);
    Task<UpdateHelperHandle> StartAsync(UpdateHandoff handoff, CancellationToken token);
}
// Disk and network work run off the dispatcher; the view marshals notifications.
internal sealed class UpdateController(IUpdateSession session, string installation, Action shutdown) : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private Task operation = Task.CompletedTask;
    private bool started, disposed, handedOff;
    private PreparedUpdate? prepared;
    private UpdateRecovery? recovery;
    private UpdateArtifacts? artifacts;
    internal string InstalledVersion { get; private set; } = "旧版安装（未登记）";
    internal UpdateViewState State { get; private set; } = new(UpdatePhase.Idle, "尚未检查更新");
    internal event Action<UpdateViewState>? Changed;
    internal Task StartAsync()
    {
        lock(gate) { if(started || disposed) return operation; started=true; }
        return CheckAsync();
    }
    internal Task CheckAsync()
    {
        lock(gate)
        {
            if(disposed || handedOff || !operation.IsCompleted) return operation;
            cancellation?.Dispose(); cancellation=new(); var token=cancellation.Token;
            operation=Task.Run(()=>CheckCore(token)); return operation;
        }
    }
    private void Set(UpdatePhase phase,string message,string version="",UpdateProgress? progress=null,bool isRecovery=false,string assetName="")
    {
        lock(gate) { if(disposed) return; State=new(phase,message,version,progress,isRecovery,assetName); Changed?.Invoke(State); }
    }
    private void Cleanup() { prepared=null; recovery=null; artifacts?.Dispose(); artifacts=null; }
    private async Task CheckCore(CancellationToken token)
    {
        try
        {
            Cleanup(); Set(UpdatePhase.Checking,"正在检查客户端更新…");
            token.ThrowIfCancellationRequested();
            recovery=session.ReadRecovery();
            if(recovery!=null)
            {
                InstalledVersion=recovery.Version;
                Set(UpdatePhase.Ready,"恢复上次未完成的更新。确认后将退出登录器、恢复原版本的完整文件并重新启动。",recovery.Version,isRecovery:true);
                return;
            }
            UpdateArtifacts.Sweep(installation);
            var state=session.ReadState(); InstalledVersion=state?.Version??"旧版安装（未登记）";
            var release=await session.CheckAsync(state?.ReleaseId??0,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if(release==null) { Set(UpdatePhase.Current,"当前已是最新版本，可返回登录。",InstalledVersion); return; }
            artifacts=UpdateArtifacts.Create(installation);
            var assetName=Path.GetFileName(release.DownloadUrl.AbsolutePath);
            Set(UpdatePhase.Downloading,"发现新版本，正在下载并校验…",release.Version,assetName:assetName);
            prepared=await session.DownloadAsync(release,artifacts.Directory,new ForwardProgress(p=>
            { if(!token.IsCancellationRequested) Set(UpdatePhase.Downloading,"正在下载更新包…",release.Version,p,assetName:assetName); }),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); artifacts.Track(prepared);
            if(session.Adopt(prepared)) { Cleanup(); InstalledVersion=release.Version; Set(UpdatePhase.Current,"文件已是最新版本，无需重新安装。",release.Version); return; }
            token.ThrowIfCancellationRequested();
            Set(UpdatePhase.Ready,"下载及校验完成。点击“重启安装”确认；也可返回登录稍后更新。",release.Version);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { Cleanup(); Set(UpdatePhase.Cancelled,"已取消更新，可重试或返回登录。"); }
        catch(Exception ex) { Cleanup(); Set(UpdatePhase.Error,"更新未完成，可重试或返回登录。\n"+ex.Message); }
    }
    internal Task InstallAsync(bool confirmed)
    {
        lock(gate)
        {
            if(disposed || handedOff || !operation.IsCompleted || prepared==null && recovery==null) return operation;
            if(!confirmed) { Cleanup(); Set(UpdatePhase.Cancelled,"已暂缓安装，可返回登录。"); return Task.CompletedTask; }
            cancellation?.Dispose(); cancellation=new(); var token=cancellation.Token;
            operation=Task.Run(async ()=>
            {
                try
                {
                    Set(UpdatePhase.Installing,recovery!=null?"正在准备安全恢复助手…":"正在准备安全安装助手…",recovery?.Version??prepared!.Release.Version,isRecovery:recovery!=null);
                    artifacts??=UpdateArtifacts.Create(installation);
                    var handoff=recovery!=null?await session.PrepareRecoveryAsync(recovery,token).ConfigureAwait(false):await session.PrepareAsync(prepared!,token).ConfigureAwait(false); artifacts.TrackHelper(handoff);
                    var helper=await session.StartAsync(handoff,token).ConfigureAwait(false);
                    // After authenticated commit a live helper owns its inputs, including on close.
                    handedOff=true; artifacts.MarkStarted(helper); artifacts.Release(); artifacts=null;
                    lock(gate) { if(!disposed) shutdown(); }
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested) { Cleanup(); Set(UpdatePhase.Cancelled,"已取消安装，可返回登录。"); }
                catch(Exception ex) { Cleanup(); Set(UpdatePhase.Error,"暂时无法安装，请关闭游戏后重试。\n"+ex.Message); }
            }); return operation;
        }
    }
    // A close can occur inside the last Ready notification, before the Task is marked
    // complete. Cleanup must therefore run after the operation, not only at cancellation.
    private void CancelOperation()
    {
        var source=cancellation; source?.Cancel();
        if(operation.IsCompleted) { Cleanup(); return; }
        _=operation.ContinueWith(_=>
        {
            lock(gate)
            {
                if(handedOff || !disposed && !ReferenceEquals(cancellation,source)) return;
                Cleanup();
                if(!disposed) Set(UpdatePhase.Cancelled,"已暂缓更新，可返回登录。");
            }
        },CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
    internal void Cancel()
    {
        lock(gate)
        {
            if(handedOff) return; CancelOperation();
            if(operation.IsCompleted) Set(UpdatePhase.Cancelled,"已暂缓更新，可返回登录。");
        }
    }
    public void Dispose()
    {
        lock(gate) { if(disposed) return; disposed=true; if(!handedOff) CancelOperation(); }
    }
    private sealed class ForwardProgress(Action<UpdateProgress> report) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value)=>report(value); }
}
