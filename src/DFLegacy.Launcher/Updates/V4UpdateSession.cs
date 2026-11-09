using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

/// <summary>V4 for explicit/public sources; hosted V3 retains its existing precedence and protocol.</summary>
internal sealed class V4UpdateSession : IUpdateSession, IDisposable
{
    private readonly string root;
    private readonly LauncherOptions options;
    private readonly Core.IReleaseProvider provider;
    private readonly Core.UpdateEngine engine;
    private readonly Core.InstallationStore store;
    private readonly HttpClient? ownedHttp;
    private UpdateSession? legacy;
    private bool legacyActive;
    private Core.UpdateCheck? pending;
    private Core.StagedUpdate? staged;
    private IProgress<UpdateProgress>? checkProgress;
    public string StatusMessage { get; private set; } = "正在检查更新源…";
    public bool ShowStatusNotice { get; private set; }
    internal V4UpdateSession(string installation,LauncherOptions options,Core.IReleaseProvider? provider=null)
    {
        root=Path.GetFullPath(installation); this.options=options; store=new(root);
        var source=SourceFor(root,options);
        if(provider==null) { ownedHttp=Core.ReleaseProvider.CreateHttpClient(); provider=new Core.ReleaseProvider(ownedHttp,source); }
        if(provider.Source!=source) throw new InvalidDataException("测试或更新来源与登录器配置不一致。");
        this.provider=provider; engine=new(root,provider);
        legacyActive=options.UpdateProvider=="github" && !string.IsNullOrWhiteSpace(options.UpdateServiceUrl);
    }
    internal static Core.ReleaseSource SourceFor(string installation,LauncherOptions options)
    {
        var arch=RuntimeInformation.ProcessArchitecture==Architecture.X86?"x86":"x64";
        var flavor=options.UpdateFlavor;
        if(string.IsNullOrWhiteSpace(flavor))
        {
            try { flavor=new Core.InstallationStore(installation).ReadInstalled()?.Release.Manifest.Flavor; } catch(InvalidDataException) { }
            if(string.IsNullOrWhiteSpace(flavor))
            {
                var path=Path.Combine(installation,"client-package.json");
                try
                {
                    Core.PathPolicy.NoLinks(path);
                    if(File.Exists(path) && new FileInfo(path).Length<=65536)
                    {
                        using var json=JsonDocument.Parse(File.ReadAllBytes(path));
                        flavor=json.RootElement.GetProperty("flavor").GetString();
                    }
                }
                catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            }
        }
        var source=new Core.ReleaseSource(options.UpdateProvider,options.UpdateRepository,options.UpdateChannel,arch,string.IsNullOrWhiteSpace(flavor)?"normal":flavor);
        source.Validate(); return source;
    }
    private UpdateSession Legacy=>legacy??=new UpdateSession(root,options);
    public void SetProgress(IProgress<UpdateProgress> progress)=>checkProgress=progress;
    public UpdateState? ReadState()
    {
        if(legacyActive) return Legacy.ReadState();
        try
        {
            var installed=store.ReadInstalled();
            if(installed!=null) { var m=installed.Release.Manifest; return new(m.Sequence,m.Version,m.Files.Select(f=>new UpdateFile(f.Path,f.Sha256,f.Size)).ToArray()); }
        }
        catch(InvalidDataException) { /* A trusted online manifest can repair damaged installed metadata. */ }
        return null;
    }
    public UpdateRecovery? ReadRecovery()
    {
        if(File.Exists(Path.Combine(root,".dflegacy","pending.json"))) { legacyActive=true; return Legacy.ReadRecovery(); }
        if(!Core.InstallTransaction.HasPending(root)) return null;
        var path=Path.Combine(root,"DFLegacy.Launcher.exe"); Core.PathPolicy.NoLinks(path);
        var file=new UpdateFile("DFLegacy.Launcher.exe",UpdateSafety.Hash(path),new FileInfo(path).Length);
        var journal=Path.Combine(store.StateDirectory,"pending.json");
        return new("v4",UpdateSafety.Hash(journal),ReadState()?.Version??"待恢复",file,file,true);
    }
    public UpdateApplyResult? ReadLastResult()=>new UpdateInstaller(root).ReadLastResult();
    public async Task<UpdateRelease?> CheckAsync(long installed,CancellationToken token)
    {
        if(legacyActive)
        {
            StatusMessage="自托管更新源（V3）：已完成现有协议检查。";
            return await Legacy.CheckAsync(installed,token).ConfigureAwait(false);
        }
        if(options.DisableGitHubUpdates)
        {
            var receipt=store.ReadInstalled();
            if(receipt!=null)
            {
                var result=await engine.ValidateInstalledAsync(Forward(checkProgress),token).ConfigureAwait(false);
                if(!result.CanLaunch) throw new InvalidDataException(result.Message);
            }
            else ClientPackageManifest.LoadAndValidate(root);
            StatusMessage="已停用联网更新；本地受管理文件已校验，未确认线上最新版本。"; ShowStatusNotice=true; return null;
        }
        using var report=Program.Incidents.BeginUpdate("v4-check");
        pending=await engine.CheckAsync(Forward(checkProgress),token).ConfigureAwait(false);
        StatusMessage=$"{provider.Source.Provider} / V4：{pending.Message}"; ShowStatusNotice=pending.IsOffline;
        if(pending.Target==null && pending.Installed==null)
        {
            if(provider.Source.Provider!="github") throw new InvalidDataException($"{(provider.Source.Provider=="gitea"?"Gitea":"Gitee")} 测试源尚无对应架构和类型的完整 V4 更新。");
            legacyActive=true; StatusMessage="GitHub 源尚无 V4 清单，使用 V3 桥接协议。";
            report.Complete("legacy-bridge"); return await Legacy.CheckAsync(installed,token).ConfigureAwait(false);
        }
        if(pending.Verification is {} verification)
        {
            var problems=verification.Problems.ToDictionary(p=>p.Path,StringComparer.OrdinalIgnoreCase);
            var checkedManifest=pending.Target?.Manifest??pending.Installed?.Release.Manifest;
            if(checkedManifest!=null) foreach(var file in checkedManifest.Files)
            {
                if(problems.TryGetValue(file.Path,out var problem)) report.AddResult($"{problem.Path}: {problem.Reason}; expected={problem.ExpectedSha256}; actual={problem.ActualSha256??"missing"}");
                else report.AddResult($"{file.Path}: verified; size={file.Size}; sha256={file.Sha256}");
            }
        }
        report.AddResult(StatusMessage); report.Complete(pending.NeedsInstall?"repair-required":pending.CanLaunch?"verified":"blocked");
        if(!pending.NeedsInstall)
        {
            if(!pending.CanLaunch) throw new InvalidDataException(pending.Message);
            return null;
        }
        var release=pending.Target!; var manifest=release.Manifest;
        var origin=provider.Source.Provider switch {"gitea"=>Core.ReleaseSource.GiteaOrigin,"gitee"=>"https://gitee.com",_=>"https://github.com"};
        return new(manifest.Sequence,manifest.Version,DateTimeOffset.UtcNow,true,
            new Uri($"{origin}/{provider.Source.Repository}/releases/download/{release.Tag}/{provider.Source.ManifestAssetName}"),
            manifest.Files.Sum(f=>f.Size),release.ManifestSha256);
    }
    public async Task<PreparedUpdate> DownloadAsync(UpdateRelease release,string attempt,IProgress<UpdateProgress> progress,CancellationToken token)
    {
        if(legacyActive) return await Legacy.DownloadAsync(release,attempt,progress,token).ConfigureAwait(false);
        if(pending?.Target==null || pending.Target.ManifestSha256!=release.Sha256) throw new InvalidDataException("下载目标与已验证清单不一致。");
        using var report=Program.Incidents.BeginUpdate("v4-download");
        try
        {
            staged=await engine.DownloadAsync(pending,Forward(progress),token).ConfigureAwait(false);
            var directory=Path.Combine(attempt,"dflegacy-update-"+Guid.NewGuid().ToString("N")); Core.PathPolicy.NoLinks(directory); Directory.CreateDirectory(directory);
            var m=staged.Release.Manifest; report.AddResult($"target={m.Version}; cache={staged.CacheDirectory}"); report.Complete("download-verified");
            return new(release,directory,new(4,m.Version,m.Commit,m.Files.Select(f=>new UpdateFile(f.Path,f.Sha256,f.Size)).ToArray(),m.Flavor));
        }
        catch(Exception ex) { report.Complete(token.IsCancellationRequested?"cancelled":"failed",token.IsCancellationRequested?null:ex); throw; }
    }
    public bool Adopt(PreparedUpdate update)
    {
        if(legacyActive) return Legacy.Adopt(update);
        RequireStaged(update);
        var m=staged!.Release.Manifest;
        if(!Core.FileVerifier.VerifyAsync(root,m).GetAwaiter().GetResult().IsValid) return false;
        if(m.Migrations.Length>0 || m.Retired.Any(r=>r.Action=="delete")) return false;
        Core.InstallTransaction.Apply(root,staged); return true;
    }
    private void RequireStaged(PreparedUpdate update)
    {
        if(staged==null || staged.Release.ManifestSha256!=update.Release.Sha256) throw new InvalidDataException("暂存更新身份不一致。");
    }
    public Task<UpdateHandoff> PrepareAsync(PreparedUpdate update,CancellationToken token)
    {
        if(legacyActive) return Legacy.PrepareAsync(update,token);
        RequireStaged(update); return new UpdateInstaller(root).PrepareV4Async(staged,null,token);
    }
    public Task<UpdateHandoff> PrepareRecoveryAsync(UpdateRecovery recovery,CancellationToken token)
        =>recovery.IsV4?new UpdateInstaller(root).PrepareV4Async(null,recovery.JournalSha256,token):Legacy.PrepareRecoveryAsync(recovery,token);
    public Task<UpdateHelperHandle> StartAsync(UpdateHandoff handoff,CancellationToken token)=>new UpdateInstaller(root).StartHelperAsync(handoff,token);
    public Task<IReadOnlyList<FileLockOwner>> InspectLocksAsync(CancellationToken token)=>Task.Run<IReadOnlyList<FileLockOwner>>(()=>
    {
        var installed=store.ReadInstalled();
        var manifest=staged?.Release.Manifest??installed?.Release.Manifest;
        var paths=manifest?.Files.Select(f=>Core.PathPolicy.Resolve(root,f.Path))
            ??new[]{Path.Combine(root,"DFLegacy.Launcher.exe"),Path.Combine(root,"DFLegacy.Client.dll"),Path.Combine(root,"Script.pvf")};
        if(manifest!=null)
        {
            var history=new HashSet<string>(installed?.ManagedHistory??[],StringComparer.OrdinalIgnoreCase);
            paths=paths.Concat(manifest.Retired.Where(r=>r.Action=="delete"&&history.Contains(r.Path)).Select(r=>Core.PathPolicy.Resolve(root,r.Path)));
            if(manifest.Migrations.Length>0) paths=paths.Append(Core.PathPolicy.Resolve(root,"launcher.json",true));
        }
        paths=paths.Append(Path.Combine(store.StateDirectory,"installed.json"));
        return FileLockOwners.Find(paths);
    },token);
    internal static IProgress<Core.UpdateProgress> Forward(IProgress<UpdateProgress>? progress)=>new Bridge(progress);
    private sealed class Bridge(IProgress<UpdateProgress>? progress):IProgress<Core.UpdateProgress>
    {
        public void Report(Core.UpdateProgress p)
        {
            var phase=p.Phase switch { "verify"=>"完整校验", "download"=>"下载", "retry"=>"等待重试", _=>p.Phase };
            progress?.Report(new(p.BytesReceived,p.TotalBytes,p.BytesPerSecond,p.Phase,p.Path,p.CompletedFiles,p.TotalFiles,
                $"{phase} {p.CompletedFiles}/{p.TotalFiles}：{p.Path}"+(p.Detail is {} d?"；"+d:"")));
        }
    }
    public void Dispose() { legacy?.Dispose(); ownedHttp?.Dispose(); }
}
