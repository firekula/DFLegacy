using System.Diagnostics;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal sealed record V4InstallPlan(Core.StagedUpdate? Staged,string? RecoveryJournalSha256);

public sealed partial class UpdateInstaller
{
    internal Task<UpdateHandoff> PrepareV4Async(Core.StagedUpdate? update,string? recoveryHash,CancellationToken token)
        =>Task.Run(()=>
        {
            token.ThrowIfCancellationRequested();
            using var pin=UpdateSafety.PinDirectory(target);
            using var parent=Process.GetCurrentProcess();
            var executable=Path.GetFullPath(parent.MainModule!.FileName);
            if(!executable.Equals(Path.Combine(target,"DFLegacy.Launcher.exe"),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("自动更新只能修改当前登录器所在目录；损坏安装请用独立修复工具。");
            var hash=UpdateSafety.Hash(executable);
            var plan=new UpdatePlan(target,null,parent.Id,parent.StartTime.ToUniversalTime().Ticks,executable,"",hash,V4:new(update,recoveryHash));
            ValidateV4Inputs(plan);
            var helper=CreateHelperCopy(executable,hash);
            return new UpdateHandoff(plan with {HelperPath=helper});
        },token);

    private static void ValidateV4Inputs(UpdatePlan plan)
    {
        if(plan.Update!=null || plan.Recovery!=null || plan.V4==null || (plan.V4.Staged==null)==(plan.V4.RecoveryJournalSha256==null))
            throw new InvalidDataException("V4 安装交接模式无效。");
        Core.PathPolicy.NoLinks(plan.Target);
        if(plan.V4.Staged is {} staged)
        {
            staged.Release.Validate(staged.Release.Source);
            var expected=Path.Combine(Path.GetFullPath(plan.Target),".dflegacy","v4","cache");
            if(!Path.GetFullPath(staged.CacheDirectory).Equals(expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新缓存不属于安装目录。");
            Core.PathPolicy.NoLinks(staged.CacheDirectory);
            if(Core.InstallTransaction.HasPending(plan.Target)) throw new InvalidDataException("存在未完成安装，必须先恢复。");
        }
        else
        {
            var journal=Path.Combine(plan.Target,".dflegacy","v4","pending.json");
            Core.PathPolicy.NoLinks(journal);
            if(!UpdateSafety.IsHash(plan.V4.RecoveryJournalSha256) || !Core.InstallTransaction.HasPending(plan.Target)
                || UpdateSafety.Hash(journal)!=plan.V4.RecoveryJournalSha256)
                throw new InvalidDataException("恢复记录已改变，请重新检查更新。");
        }
    }
    private static UpdateApplyResult ApplyV4(UpdatePlan plan,IProgress<UpdateInstallProgress>? progress)
    {
        Program.LogClientPath=Path.Combine(plan.Target,"DNF.exe");
        using var report=Program.Incidents.BeginUpdate(plan.V4!.Staged==null?"v4-recovery":"v4-install");
        var bridge=new V4InstallProgress(progress);
        try
        {
            ValidateV4Inputs(plan);
            if(plan.V4.Staged is {} staged) Core.InstallTransaction.Apply(plan.Target,staged,bridge);
            else Core.InstallTransaction.Recover(plan.Target,bridge);
            // Recovery is not permission to run a corrupt old baseline.
            var manifest=ClientPackageManifest.LoadAndValidate(plan.Target);
            var restarted=Restart(Path.Combine(plan.Target,"DFLegacy.Launcher.exe"));
            report.AddResult($"verified={manifest.Version}; restarted={restarted}"); report.Complete("installed-verified");
            return new(true,restarted,restarted?"全部受管理文件已校验，登录器已重启。":"安装已完整校验，请手动启动登录器。");
        }
        catch(Exception ex)
        {
            report.Complete("failed",ex);
            return new(false,false,"更新或恢复未完成。恢复记录已保留；请运行独立修复工具或重新启动登录器检查。\n"+ex.Message,
                Path.Combine(plan.Target,".dflegacy","v4","transactions"));
        }
    }
    private sealed class V4InstallProgress(IProgress<UpdateInstallProgress>? progress):IProgress<Core.UpdateProgress>
    {
        public void Report(Core.UpdateProgress p)=>UpdateInstallProgress.Report(progress,
            p.TotalFiles>0?.85+.14*Math.Clamp((double)p.CompletedFiles/p.TotalFiles,0,1):.85,
            UpdateProgressText.Format(p));
    }
}
