using System.Diagnostics;
using System.Windows;
using Core=DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal sealed record BootstrapRequest(string Directory,string[] LauncherArguments,int? WaitPid=null,long? WaitStartTicks=null);
internal sealed class BootstrapCommandException(int exitCode):InvalidOperationException($"登录器命令已结束，退出码 {exitCode}；不会重复执行命令。");
internal sealed class BootstrapChildStillRunningException(Exception error):InvalidOperationException("新登录器尚未确认退出；为避免重复启动，已停止自动重试。",error);

internal static class BootstrapUpdateClient
{
    internal static bool IsRequested(string[] args)=>args.Length>0&&args[0]=="--bootstrap-client";
    internal static BootstrapRequest ParseArguments(string[] args)
    {
        if(args.Length<3||args[0]!="--bootstrap-client"||!Path.IsPathFullyQualified(args[1])||args[2]!="--")
            throw new ArgumentException("自动修复入口参数无效。");
        var rest=args[3..];int? pid=null;long? ticks=null;
        if(rest.Length>0&&rest[0]=="--bootstrap-wait-parent")
        {
            if(rest.Length<3||!int.TryParse(rest[1],out var p)||p<=0||!long.TryParse(rest[2],out var t)||t<=0)throw new ArgumentException("重新检查更新的父进程参数无效。");
            pid=p;ticks=t;rest=rest[3..];
        }
        if(rest.Any(a=>a.StartsWith("--bootstrap-",StringComparison.Ordinal)))throw new ArgumentException("不能从普通启动参数传递内部自动修复标记。");
        return new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1])),rest,pid,ticks);
    }
    private static async Task WaitForParentAsync(BootstrapRequest request,CancellationToken token)
    {
        if(request.WaitPid is not {} pid)return;
        Process parent;
        try{parent=Process.GetProcessById(pid);}catch(ArgumentException){return;}
        using(parent)
        {
            try
            {
                if(parent.HasExited||parent.StartTime.ToUniversalTime().Ticks!=request.WaitStartTicks)return;
                if(!string.Equals(parent.MainModule?.FileName,Path.Combine(request.Directory,BootstrapLayout.AppName),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("重新检查更新的父进程不属于此客户端。");
            }
            catch(InvalidOperationException){return;}
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(45));
            await FileLockOwners.WaitForExitConfirmedAsync(parent,deadline.Token).ConfigureAwait(false);
        }
    }
    internal static async Task<RepairOutcome> RunAsync(BootstrapRequest request,Core.IReleaseProvider? provider=null,
        IProgress<Core.UpdateProgress>? progress=null,Func<IReadOnlyList<FileLockOwner>,CancellationToken,Task<bool>>? confirmClose=null,
        Func<ProcessStartInfo,CancellationToken,Task>? launch=null,CancellationToken token=default)
    {
        await WaitForParentAsync(request,token).ConfigureAwait(false);
        // Both published and newer native entries forward the existing wait-
        // parent arguments for an explicit UI recheck. Ordinary startup may
        // reuse a completed receipt; external repair keeps its full-scan default.
        var outcome=await RepairClient.RunAsync(new(request.Directory),provider,progress,confirmClose,token,
            manifest=>BootstrapLayout.RequireStableEntry(request.Directory,manifest),bootstrapSupervisor:true,
            forceVerification:request.WaitPid is not null).ConfigureAwait(false);
        await BootstrapLayout.ValidateInstalledAsync(request.Directory,BootstrapLayout.Architecture,token).ConfigureAwait(false);
        var start=new ProcessStartInfo(Path.Combine(request.Directory,BootstrapLayout.AppName)){WorkingDirectory=request.Directory,UseShellExecute=false};
        start.ArgumentList.Add("--bootstrap-verified");start.ArgumentList.Add("--");
        foreach(var arg in request.LauncherArguments)start.ArgumentList.Add(arg);
        await (launch??LaunchAndConfirmAsync)(start,token).ConfigureAwait(false);
        Program.WriteLauncherLog($"Bootstrap verified UI handoff; offline={outcome.Offline}.");
        return outcome;
    }
    internal static async Task LaunchAndConfirmAsync(ProcessStartInfo start,CancellationToken token)
    {
        var eventName="Local\\DFLegacy.Startup."+Guid.NewGuid().ToString("N");
        using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,eventName);
        start.Environment["DFLEGACY_BOOTSTRAP_READY_EVENT"]=eventName;
        using var process=Process.Start(start)??throw new InvalidOperationException("无法打开已修复的登录器。");
        _ = process.SafeHandle;
        try
        {
            if(App.IsHeadless(start.ArgumentList))
            {
                await FileLockOwners.WaitForExitConfirmedAsync(process,token).ConfigureAwait(false);
                if(process.ExitCode!=0)throw new BootstrapCommandException(process.ExitCode);
                return;
            }
            var deadline=Stopwatch.StartNew();
            while(deadline.Elapsed<TimeSpan.FromSeconds(30))
            {
                token.ThrowIfCancellationRequested();
                // Signal is sent only after validated UI creation. An error dialog
                // must never bless a failed updater as the recovery engine.
                if(ready.WaitOne(0)&&(!process.HasExited||process.ExitCode==0))return;
                if(process.HasExited)throw new InvalidOperationException($"登录器启动后提前退出（{process.ExitCode}）；保留恢复组件供下次重试。");
                await Task.Delay(100,token).ConfigureAwait(false);
            }
            throw new TimeoutException("已修复的登录器未在限定时间内完成启动，请查看错误日志。");
        }
        catch
        {
            // Only the exact child created for this failed handoff is owned here.
            // Never allow the native fallback to run beside an unconfirmed child.
            try
            {
                if(!process.HasExited)process.Kill();
                using var exit=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await FileLockOwners.WaitForExitConfirmedAsync(process,exit.Token).ConfigureAwait(false);
            }
            catch(Exception cleanup){throw new BootstrapChildStillRunningException(cleanup);}
            throw;
        }
    }
    internal static void SignalReady()
    {
        var name=Environment.GetEnvironmentVariable("DFLEGACY_BOOTSTRAP_READY_EVENT");
        Environment.SetEnvironmentVariable("DFLEGACY_BOOTSTRAP_READY_EVENT",null);
        if(name is null)return;
        const string prefix="Local\\DFLegacy.Startup.";
        if(!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name[prefix.Length..],"N",out _))throw new InvalidDataException("启动握手事件格式无效。");
        using var ready=EventWaitHandle.OpenExisting(name);ready.Set();
    }
    internal static async Task<int> RunEntryAsync(string[] args)
    {
        Views.UpdateInstallWindow? window=null;using var cancel=new CancellationTokenSource();
        try
        {
            var request=ParseArguments(args);
            var quiet=App.IsHeadless(request.LauncherArguments);
            Program.LogClientPath=Path.Combine(request.Directory,"DNF.exe");
            if(!quiet){window=new Views.UpdateInstallWindow{Title="验证与修复客户端"};window.CancelRequested+=()=>cancel.Cancel();window.Show();}
            var progress=new Progress<Core.UpdateProgress>(p=>
            {
                var message=$"{p.Phase} {p.CompletedFiles}/{p.TotalFiles}：{p.Path} {p.Detail}";
                if(quiet){Console.WriteLine(message);return;}
                if(p.Phase is "recover" or "install" or "prepared" or "commit")window!.Render(new UpdateInstallProgress(.85,message));
                else window!.Render(new UpdateViewState(p.Phase=="download"?UpdatePhase.Downloading:UpdatePhase.Checking,message,
                    Progress:new UpdateProgress(p.BytesReceived,p.TotalBytes,p.BytesPerSecond,p.Phase,p.Path,p.CompletedFiles,p.TotalFiles,message)));
            });
            async Task<bool> Confirm(IReadOnlyList<FileLockOwner> owners,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if(quiet)return false;
                return await Application.Current.Dispatcher.InvokeAsync(()=>MessageBox.Show(window,
                    "以下进程占用客户端文件：\n"+string.Join('\n',owners.Select(o=>$"{o.Name} (PID {o.ProcessId})"))+"\n是否关闭这些进程并继续修复？请先保存其他工作。",
                    "修复需要释放文件",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes);
            }
            var outcome=await RunAsync(request,progress:progress,confirmClose:Confirm,token:cancel.Token);
            if(outcome.Offline&&quiet)Console.WriteLine(outcome.Message);
            return 0;
        }
        catch(OperationCanceledException){return 2;}
        catch(Exception ex) when(ex is BootstrapCommandException or BootstrapChildStillRunningException)
        {
            Program.ReportError("bootstrap-terminal-child",ex);return 3;
        }
        catch(Exception ex)
        {
            Program.ReportError("bootstrap-auto-repair",ex);
            // Native entry owns the failure dialog after trying a different
            // known-good updater. Do not block fallback with another modal here.
            return 1;
        }
        finally{window?.Finish();}
    }
}
