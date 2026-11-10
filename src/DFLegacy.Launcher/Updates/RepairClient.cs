using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Core=DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal sealed record RepairRequest(string Directory,string? Provider=null,string? Channel=null,string? Flavor=null,bool NoDialog=false);
internal sealed record RepairSettings(Core.ReleaseSource Source,bool NetworkDisabled);
internal sealed record RepairOutcome(bool Success,bool Offline,string Message);

// The external copy owns this pipeline. It never executes the installed launcher
// or delegates recovery to binaries that may already be damaged.
internal static class RepairClient
{
    internal static bool IsRequested(string[] args,string? executable)=>args.Contains("--repair-client",StringComparer.OrdinalIgnoreCase)
        ||string.Equals(Path.GetFileNameWithoutExtension(executable),"DFLegacy.Repair",StringComparison.OrdinalIgnoreCase);

    internal static RepairRequest ParseArguments(string[] args)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);bool quiet=false;
        for(var i=0;i<args.Length;i++)
        {
            var key=args[i];
            if(key.Equals("--no-dialog",StringComparison.OrdinalIgnoreCase)||key.Equals("--headless",StringComparison.OrdinalIgnoreCase)){quiet=true;continue;}
            if(key.ToLowerInvariant() is not ("--repair-client" or "--update-provider" or "--update-channel" or "--update-flavor"))throw new ArgumentException("修复入口不支持参数："+key);
            if(i+1==args.Length||args[i+1].StartsWith("--",StringComparison.Ordinal)||!values.TryAdd(key,args[++i]))throw new ArgumentException("修复参数缺少值或重复："+key);
        }
        values.TryGetValue("--repair-client",out var directory);values.TryGetValue("--update-provider",out var provider);
        values.TryGetValue("--update-channel",out var channel);values.TryGetValue("--update-flavor",out var flavor);
        return new(directory is null ? "" : Path.GetFullPath(directory),provider,channel,flavor,quiet);
    }

    internal static void EnsureExternal(string executable,string directory)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));Core.PathPolicy.NoLinks(root);
        Core.PathPolicy.NoLinks(Path.GetFullPath(executable));
        var relative=Path.GetRelativePath(root,Path.GetFullPath(executable));
        if(!Path.IsPathRooted(relative)&&relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new InvalidOperationException("请将独立 DFLegacy.Repair.exe 放到客户端目录以外，再选择需要修复的客户端目录。修复工具不能覆盖自身或其正在使用的依赖。");
    }

    internal static RepairSettings ReadSettings(RepairRequest request)
    {
        using var lease=Core.InstallationLease.Acquire(request.Directory);
        var path=Path.Combine(request.Directory,"launcher.json");Core.PathPolicy.NoLinks(path);
        var explicitKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(File.Exists(path))
        {
            if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("配置文件超过修复入口读取上限；原文件已保留。");
            using var json=JsonDocument.Parse(File.ReadAllBytes(path),new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,MaxDepth=32});
            if(json.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("配置文件必须是 JSON 对象；原文件已保留。");
            foreach(var property in json.RootElement.EnumerateObject())if(!explicitKeys.Add(property.Name))throw new InvalidDataException("配置含有重复字段；原文件已保留。");
        }
        var options=new LauncherAccountStore(path).Load();Program.RegisterDiagnosticSecrets(options);
        Core.InstalledReceipt? receipt=null;
        try { receipt=new Core.InstallationStore(request.Directory).ReadInstalled(); }
        catch(InvalidDataException) { /* Trusted online metadata can repair a corrupt receipt; never delete it here. */ }
        var source=receipt?.Release.Source??V4UpdateSession.SourceFor(request.Directory,options);
        source=source with {
            Provider=request.Provider??(explicitKeys.Contains("UpdateProvider")?options.UpdateProvider:source.Provider),
            Repository=explicitKeys.Contains("UpdateRepository")?options.UpdateRepository:source.Repository,
            Channel=request.Channel??(explicitKeys.Contains("UpdateChannel")?options.UpdateChannel:source.Channel),
            Flavor=request.Flavor??(explicitKeys.Contains("UpdateFlavor")&&!string.IsNullOrWhiteSpace(options.UpdateFlavor)?options.UpdateFlavor:source.Flavor)
        };
        source.Validate();
        var arch=RuntimeInformation.ProcessArchitecture==Architecture.X86?"x86":"x64";
        if(source.Architecture!=arch)throw new InvalidDataException($"此安装需要 {source.Architecture} 修复工具；当前程序为 {arch}。请使用对应架构的独立修复包。");
        if(!string.IsNullOrWhiteSpace(options.UpdateServiceUrl)&&source.Provider=="github"&&request.Provider is null)
            throw new InvalidDataException("当前配置使用自托管 V3 更新源。此独立修复入口需要 V4 源；请使用该服务器提供的完整桥接包，或明确指定已授权的 --update-provider 和渠道。");
        return new(source,options.DisableGitHubUpdates&&request.Provider is null);
    }

    internal static async Task<RepairOutcome> RunAsync(RepairRequest request,Core.IReleaseProvider? provider=null,
        IProgress<Core.UpdateProgress>? progress=null,Func<IReadOnlyList<FileLockOwner>,CancellationToken,Task<bool>>? confirmClose=null,CancellationToken token=default,
        Action<Core.ClientManifest>? validateManifest=null,bool bootstrapSupervisor=false,bool forceVerification=true)
    {
        var root=Path.GetFullPath(request.Directory);
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("客户端目录不存在："+root);
        EnsureExternal(Environment.ProcessPath??throw new InvalidOperationException("无法确定修复工具自身路径。"),root);
        var legacyPending=Path.Combine(root,".dflegacy","pending.json");Core.PathPolicy.NoLinks(legacyPending);
        if(File.Exists(legacyPending)||Directory.Exists(legacyPending))
            throw new InvalidDataException("此客户端存在未完成的 V3 更新。请先使用原桥接更新包恢复该事务；独立 V4 修复不会覆盖旧恢复资料或提交混合版本。");
        Program.LogClientPath=Path.Combine(root,"DNF.exe");
        using var report=Program.Incidents.BeginUpdate("independent-repair");
        IEnumerable<string> WithoutSupervisor(IEnumerable<string> files)=>bootstrapSupervisor
            ?files.Where(f=>!string.Equals(Path.GetFullPath(f),Path.Combine(root,BootstrapLayout.EntryName),StringComparison.OrdinalIgnoreCase)):files;
        try
        {
            token.ThrowIfCancellationRequested();
            if(Core.InstallTransaction.HasPending(root))
            {
                await ReleaseLocksAsync(WithoutSupervisor(RecoveryFiles(root)),confirmClose,token).ConfigureAwait(false);
                progress?.Report(new("recover","",0,0,Detail:"请勿退出。"));
                await Task.Run(()=>Core.InstallTransaction.Recover(root,progress),CancellationToken.None).ConfigureAwait(false);
            }
            var settings=ReadSettings(request with {Directory=root});
            using var http=provider is null?Core.ReleaseProvider.CreateHttpClient():null;
            provider??=new Core.ReleaseProvider(http!,settings.Source);
            if(provider.Source!=settings.Source)throw new InvalidDataException("修复提供方与配置/安装记录的来源不一致。");
            var engine=new Core.UpdateEngine(root,provider);
            if(settings.NetworkDisabled)
            {
                var local=await engine.ValidateInstalledAsync(progress,token,forceVerification:forceVerification).ConfigureAwait(false);
                if(!local.CanLaunch)throw new InvalidDataException("已停用联网更新，无法修复不完整文件。请明确选择 V4 更新源后重试。"+local.Message);
                validateManifest?.Invoke(new Core.InstallationStore(root).ReadInstalled()!.Release.Manifest);
                report.Complete("offline-verified-network-disabled");
                return new(true,true,"已停用联网更新；"+local.Message);
            }
            var found=await engine.CheckAsync(progress,token,forceVerification:forceVerification).ConfigureAwait(false);
            if(found.Target is {} target)validateManifest?.Invoke(target.Manifest);
            else if(found.Installed is {} installed)validateManifest?.Invoke(installed.Release.Manifest);
            if(found.Verification is {} verified)
            {
                var problems=verified.Problems.ToDictionary(p=>p.Path,StringComparer.OrdinalIgnoreCase);
                foreach(var file in (found.Target?.Manifest??found.Installed?.Release.Manifest)?.Files??[])
                    report.AddResult(problems.TryGetValue(file.Path,out var failure)?$"{file.Path}: {failure.Reason}; expected={file.Sha256}; actual={failure.ActualSha256??"missing"}":$"{file.Path}: verified; sha256={file.Sha256}");
            }
            if(!found.NeedsInstall)
            {
                if(!found.CanLaunch)throw new InvalidDataException(found.Message+" 请提供相应架构/类型的 V4 桥接更新。");
                report.Complete(found.IsOffline?"offline-verified":"verified");return new(true,found.IsOffline,found.Message);
            }
            var staged=await engine.DownloadAsync(found,progress,token).ConfigureAwait(false);
            var files=staged.Release.Manifest.Files.Select(f=>Core.PathPolicy.Resolve(root,f.Path))
                .Concat(staged.Release.Manifest.Retired.Select(f=>Core.PathPolicy.Resolve(root,f.Path)))
                .Append(Path.Combine(root,"launcher.json"));
            await ReleaseLocksAsync(WithoutSupervisor(files),confirmClose,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            progress?.Report(new("install","",0,staged.Release.Manifest.Files.Length,Detail:"请勿退出。"));
            await Task.Run(()=>Core.InstallTransaction.Apply(root,staged,progress),CancellationToken.None).ConfigureAwait(false);
            var final=await engine.ValidateInstalledAsync(progress,CancellationToken.None).ConfigureAwait(false);
            if(!final.CanLaunch)throw new InvalidDataException(final.Message);
            validateManifest?.Invoke(new Core.InstallationStore(root).ReadInstalled()!.Release.Manifest);
            report.Complete("repaired-and-verified");
            return new(true,false,$"已修复并完整校验版本 {final.Version}（{settings.Source.Provider} / {settings.Source.Channel} / {settings.Source.Architecture}）。请手动启动客户端目录中的 DFLegacy.Launcher.exe。");
        }
        catch(OperationCanceledException) { report.Complete("cancelled");throw; }
        catch(Exception ex) { report.Complete("failed",ex);throw; }
    }

    private static IEnumerable<string> RecoveryFiles(string root)
    {
        Core.InstalledReceipt? receipt=null;
        try { receipt=new Core.InstallationStore(root).ReadInstalled(); }catch(InvalidDataException) { }
        return (receipt?.Release.Manifest.Files.Select(f=>Core.PathPolicy.Resolve(root,f.Path))??[])
            .Concat(new[]{"DFLegacy.Launcher.exe","DFLegacy.Launcher.App.exe","DFLegacy.Updater.exe","DFLegacy.Client.dll","DNF.exe","Script.pvf","launcher.json"}.Select(f=>Path.Combine(root,f)));
    }
    private static async Task ReleaseLocksAsync(IEnumerable<string> files,Func<IReadOnlyList<FileLockOwner>,CancellationToken,Task<bool>>? confirm,CancellationToken token)
    {
        var paths=files.ToArray();var owners=await Task.Run(()=>FileLockOwners.Find(paths),token).ConfigureAwait(false);
        if(owners.Count==0)return;
        var names=string.Join("、",owners.Select(o=>$"{o.Name} (PID {o.ProcessId})"));
        Program.WriteLauncherLog("Repair file locks: "+names);
        if(confirm is null||!await confirm(owners,token).ConfigureAwait(false))throw new OperationCanceledException("未确认关闭文件占用进程；修复未提交："+names,token);
        await FileLockOwners.CloseAsync(owners,true,token).ConfigureAwait(false);
        if(FileLockOwners.Find(paths).Count!=0)throw new IOException("仍有进程占用客户端文件，请手动关闭后重新修复。");
    }

    internal static async Task<int> RunEntryAsync(string[] args)
    {
        Views.UpdateInstallWindow? window=null;using var cancel=new CancellationTokenSource();
        var quiet=args.Any(a=>a.Equals("--no-dialog",StringComparison.OrdinalIgnoreCase)||a.Equals("--headless",StringComparison.OrdinalIgnoreCase));
        try
        {
            var request=ParseArguments(args);
            if(string.IsNullOrEmpty(request.Directory))
            {
                if(quiet)throw new ArgumentException("请指定 --repair-client <客户端目录>。");
                var picker=new Microsoft.Win32.OpenFolderDialog{Title="选择需要修复的客户端目录"};
                if(picker.ShowDialog()!=true)return 2;request=request with {Directory=picker.FolderName};
            }
            if(!quiet){window=new Views.UpdateInstallWindow{Title="修复客户端"};window.CancelRequested+=()=>cancel.Cancel();window.Show();}
            var progress=new Progress<Core.UpdateProgress>(p=>
            {
                var message=UpdateProgressText.Format(p);
                if(quiet)Console.WriteLine(message);
                else if(p.Phase is "recover" or "install" or "prepared" or "commit")window!.Render(new UpdateInstallProgress(.85,message));
                else window!.Render(new UpdateViewState(p.Phase=="download"?UpdatePhase.Downloading:UpdatePhase.Checking,message,
                    Progress:new UpdateProgress(p.BytesReceived,p.TotalBytes,p.BytesPerSecond,p.Phase,p.Path,p.CompletedFiles,p.TotalFiles,message)));
            });
            async Task<bool> Confirm(IReadOnlyList<FileLockOwner> owners,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();if(quiet)return false;
                return await Application.Current.Dispatcher.InvokeAsync(()=>MessageBox.Show(window,
                    "以下进程占用客户端文件：\n"+string.Join('\n',owners.Select(o=>$"{o.Name} (PID {o.ProcessId})"))+
                    "\n确认关闭可结束的进程并继续修复？请先保存其他工作。", "修复需要释放文件",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes);
            }
            var result=await RunAsync(request,progress:progress,confirmClose:Confirm,token:cancel.Token);
            if(quiet)Console.WriteLine(result.Message);else MessageBox.Show(window,result.Message,"客户端修复",MessageBoxButton.OK,MessageBoxImage.Information);
            return result.Success?0:1;
        }
        catch(OperationCanceledException ex){if(quiet)Console.WriteLine("修复已取消；下次将重新完整校验。"+ex.Message);return 2;}
        catch(Exception ex)
        {
            Program.ReportError("repair-entry",ex);
            if(quiet)Console.Error.WriteLine(ex.Message);
            else if(window is null)MessageBox.Show(ex.Message,"客户端修复失败",MessageBoxButton.OK,MessageBoxImage.Error);
            else MessageBox.Show(window,ex.Message,"客户端修复失败",MessageBoxButton.OK,MessageBoxImage.Error);
            return 1;
        }
        finally {window?.Finish();}
    }
}
