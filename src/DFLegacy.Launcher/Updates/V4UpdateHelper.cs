using System.Diagnostics;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal sealed record V4InstallPlan(Core.StagedUpdate? Staged,string? RecoveryJournalSha256,
    Core.ReleaseSource? ChapterUpgradeSource=null,Core.ReleaseSource? ChapterRollbackSource=null,bool ChapterRecovery=false);

public sealed partial class UpdateInstaller
{
    internal Task<UpdateHandoff> PrepareV4Async(Core.StagedUpdate? update,string? recoveryHash,CancellationToken token)
        =>PrepareV4PlanAsync(new(update,recoveryHash),token);

    internal Task<UpdateHandoff> PrepareChapterUpgradeAsync(Core.StagedUpdate staged,Core.ReleaseSource authorizedSource,CancellationToken token)
        =>PrepareV4PlanAsync(new(staged,null,ChapterUpgradeSource:authorizedSource),token);

    internal Task<UpdateHandoff> PrepareChapterRollbackAsync(Core.ReleaseSource source,CancellationToken token)
        =>PrepareV4PlanAsync(new(null,null,ChapterRollbackSource:source),token);

    internal Task<UpdateHandoff> PrepareChapterRecoveryAsync(string recoveryHash,CancellationToken token)
        =>PrepareV4PlanAsync(new(null,recoveryHash,ChapterRecovery:true),token);

    internal static string ChapterRollbackJournal(string root) => Path.Combine(root,".dflegacy","chapter-transition","rollback.json");

    private Task<UpdateHandoff> PrepareV4PlanAsync(V4InstallPlan install,CancellationToken token)
        =>Task.Run(()=>
        {
            token.ThrowIfCancellationRequested();
            using var pin=UpdateSafety.PinDirectory(target);
            using var parent=Process.GetCurrentProcess();
            var executable=Path.GetFullPath(parent.MainModule!.FileName);
            var hash=UpdateSafety.Hash(executable);
            var plan=new UpdatePlan(target,null,parent.Id,parent.StartTime.ToUniversalTime().Ticks,executable,"",hash,V4:install);
            ValidateParent(plan,parent.Id);
            ValidateV4Inputs(plan);
            var helper=CreateHelperCopy(executable,hash);
            return new UpdateHandoff(plan with {HelperPath=helper});
        },token);

    private static void ValidateV4Inputs(UpdatePlan plan)
    {
        var install=plan.V4;
        if(plan.Update!=null || plan.Recovery!=null || install==null
            || new[]{install.Staged!=null,install.RecoveryJournalSha256!=null,install.ChapterRollbackSource!=null}.Count(value=>value)!=1
            || install.ChapterUpgradeSource!=null && install.Staged==null
            || install.ChapterRecovery && install.RecoveryJournalSha256==null)
            throw new InvalidDataException("V4 安装交接模式无效。");
        Core.PathPolicy.NoLinks(plan.Target);
        var pending=Core.InstallTransaction.HasPending(plan.Target);
        var chapterPending=Core.ChapterTransition.HasPending(plan.Target);
        if(install.Staged is {} staged)
        {
            staged.Release.Validate(install.ChapterUpgradeSource??staged.Release.Source);
            var expected=Path.Combine(Path.GetFullPath(plan.Target),".dflegacy","v4","cache");
            if(!Path.GetFullPath(staged.CacheDirectory).Equals(expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新缓存不属于安装目录。");
            Core.PathPolicy.NoLinks(staged.CacheDirectory);
            if(pending||chapterPending) throw new InvalidDataException("存在未完成安装，必须先恢复。");
            if(install.ChapterUpgradeSource!=null)
            {
                var installed=new Core.InstallationStore(plan.Target).ReadInstalled()??throw new InvalidDataException("缺少第一章桥接安装记录。");
                Core.ChapterTransitionSource.RequireUpgrade(installed.Release.Source,install.ChapterUpgradeSource);
                if(ChapterUpdatePolicy.Validate(plan.Target,installed.Release)?.Chapter!=Core.ClientChapterIdentity.ChapterOne)
                    throw new InvalidDataException("跨章更新需要已验证的第一章桥接安装。");
                if(IsFixedLayout(installed.Release.Manifest)) BootstrapLayout.RequireStableEntry(plan.Target,staged.Release.Manifest);
            }
            else if(Path.GetFileName(plan.ParentPath).Equals(BootstrapLayout.AppName,StringComparison.OrdinalIgnoreCase))
                BootstrapLayout.RequireStableEntry(plan.Target,staged.Release.Manifest);
        }
        else if(install.ChapterRollbackSource is {} source)
        {
            if(pending||chapterPending) throw new InvalidDataException("存在未完成安装，必须先恢复。");
            var installed=new Core.InstallationStore(plan.Target).ReadInstalled()??throw new InvalidDataException("缺少第二章安装记录。");
            installed.Release.Validate(source);
            if(source.Provider!="local"&&!Core.ChapterTransitionSource.IsOfficialChapterTwo(source))
                throw new InvalidDataException("第二章安装来源不支持跨章回退。");
            if(V4UpdateSession.ConfigurationName(installed)!="launcher.chapter2.json")
                throw new InvalidDataException("当前安装不是可回退的第二章。");
        }
        else
        {
            var journal=install.ChapterRecovery?ChapterRollbackJournal(plan.Target):Path.Combine(plan.Target,".dflegacy","v4","pending.json");
            Core.PathPolicy.NoLinks(journal);
            if(!UpdateSafety.IsHash(install.RecoveryJournalSha256) || (install.ChapterRecovery?!chapterPending||pending:!pending||chapterPending)
                || UpdateSafety.Hash(journal)!=install.RecoveryJournalSha256)
                throw new InvalidDataException("恢复记录已改变，请重新检查更新。");
        }
    }

    private static bool IsFixedLayout(Core.ClientManifest manifest)=>manifest.Files.Any(file=>file.Path==BootstrapLayout.AppName);

    private static UpdateApplyResult ApplyV4(UpdatePlan plan,IProgress<UpdateInstallProgress>? progress)
    {
        Program.LogClientPath=Path.Combine(plan.Target,"DNF.exe");
        var install=plan.V4!;
        using var report=Program.Incidents.BeginUpdate(install.Staged==null?"v4-recovery":"v4-install");
        var bridge=new V4InstallProgress(progress);
        try
        {
            using var maintenance=Core.ClientGameLease.EnterMaintenance(plan.Target);
            ValidateV4Inputs(plan);
            if(install.ChapterUpgradeSource is {} upgrade)
                Core.InstallTransaction.ApplyChapterUpgrade(plan.Target,install.Staged!,upgrade,true,bridge,
                    ChapterDisplayMigration.Read(plan.Target),deferReadiness:true);
            else if(install.ChapterRollbackSource is {} rollback)
                Core.ChapterTransition.Rollback(plan.Target,rollback,bridge);
            else if(install.ChapterRecovery)
                Core.ChapterTransition.Recover(plan.Target,bridge);
            else if(install.Staged is {} staged)
                Core.InstallTransaction.Apply(plan.Target,staged,bridge);
            else Core.InstallTransaction.Recover(plan.Target,bridge);

            string version;
            bool restarted;
            if(install.ChapterUpgradeSource!=null)
            {
                try
                {
                    var release=install.Staged!.Release;
                    UpdateInstallProgress.Report(progress,1,"正在验证第二章启动器，请保持窗口打开…");
                    ConfirmChapterReady(plan.Target,release);
                    version=release.Manifest.Version;
                    restarted=true;
                }
                catch(Exception ex) when((ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
                    && ex is not BootstrapChildStillRunningException)
                {
                    Core.InstallTransaction.Recover(plan.Target,bridge);
                    ClientPackageManifest.LoadAndValidate(plan.Target);
                    restarted=Restart(Path.Combine(plan.Target,BootstrapLayout.EntryName));
                    report.Complete("chapter-ready-failed-rolled-back",ex);
                    return new(false,restarted,"第二章未通过本地启动检查，已恢复第一章。\n"+ex.Message);
                }
            }
            else
            {
                version=ClientPackageManifest.LoadAndValidate(plan.Target).Version;
                restarted=Restart(Path.Combine(plan.Target,BootstrapLayout.EntryName));
            }
            report.AddResult($"verified={version}; restarted={restarted}"); report.Complete("installed-verified");
            return new(true,restarted,restarted?"全部受管理文件已校验，登录器已重启。":"安装已完整校验，请手动启动登录器。");
        }
        catch(Exception ex)
        {
            report.Complete("failed",ex);
            return new(false,false,"更新或恢复未完成。请运行独立修复工具或重新启动登录器检查。\n"+ex.Message,
                Path.Combine(plan.Target,".dflegacy","v4","transactions"));
        }
    }

    internal const string ChapterReadyProbeVariable="DFLEGACY_CHAPTER_READY_MANIFEST";

    private static void ConfirmChapterReady(string root,Core.VerifiedRelease release)
    {
        var receipt=new Core.InstallationStore(root).ReadInstalled()??throw new InvalidDataException("缺少第二章安装记录。");
        var identity=ChapterUpdatePolicy.Validate(root,receipt.Release)??throw new InvalidDataException("缺少第二章身份。");
        var fixedLayout=IsFixedLayout(receipt.Release.Manifest);
        var start=new ProcessStartInfo(Path.Combine(root,fixedLayout?BootstrapLayout.AppName:identity.Entry))
        { UseShellExecute=false,WorkingDirectory=root };
        start.Environment[ChapterReadyProbeVariable]=release.ManifestSha256;
        if(fixedLayout) { start.ArgumentList.Add("--bootstrap-verified"); start.ArgumentList.Add("--"); }
        var elapsed=Stopwatch.StartNew();
        Program.WriteLauncherLog("Chapter Two startup check began.");
        BootstrapUpdateClient.LaunchAndConfirmAsync(start,CancellationToken.None,
            ()=>Core.InstallTransaction.ConfirmChapterReady(root,release.ManifestSha256),
            readinessTimeout:TimeSpan.FromSeconds(90)).GetAwaiter().GetResult();
        Program.WriteLauncherLog($"Chapter Two startup check passed in {elapsed.Elapsed.TotalSeconds:F1}s.");
    }

    private sealed class V4InstallProgress(IProgress<UpdateInstallProgress>? progress):IProgress<Core.UpdateProgress>
    {
        public void Report(Core.UpdateProgress p)
        {
            try { progress?.Report(UpdateInstallProgress.FromCore(p)); }
            catch { /* Presentation cannot change a transaction's outcome. */ }
        }
    }
}
