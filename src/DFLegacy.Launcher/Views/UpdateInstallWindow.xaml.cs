using System.ComponentModel;
using System.Windows;
using DFLegacy.Launcher.Updates;
namespace DFLegacy.Launcher.Views;
public partial class UpdateInstallWindow : Window
{
    private bool finished;
    private bool canCancel;
    internal event Action? CancelRequested;
    internal UpdateInstallWindow() { InitializeComponent(); InstallProgress.Set(.85,false); }
    internal void Render(UpdateInstallProgress progress)
    {
        if(finished) return;
        canCancel=false; InstallStatus.Text=progress.Message; InstallProgress.Set(progress.Fraction,false);
    }
    internal void Render(UpdateViewState state)
    {
        if(finished) return;
        canCancel=state.Phase is UpdatePhase.Downloading or UpdatePhase.Ready;
        var fraction=state.Progress is {TotalBytes:>0} p?Math.Clamp((double)p.BytesReceived/p.TotalBytes,0,1):0;
        var value=state.Phase switch { UpdatePhase.Downloading=>.8*fraction,UpdatePhase.Ready=>.82,UpdatePhase.Installing=>.85,_=>0 };
        InstallProgress.Set(value,false);
        InstallStatus.Text=state.Phase switch
        {
            UpdatePhase.Downloading=>$"正在下载更新… {fraction:P0}",
            UpdatePhase.Ready or UpdatePhase.Installing when state.ReplacesScriptPvf=>"将替换 Script.pvf，并备份已有文件…",
            UpdatePhase.Ready=>state.IsRecovery?"发现未完成的更新，等待确认恢复…":"下载校验完成，正在准备安装…",
            UpdatePhase.Installing=>state.IsRecovery?"正在准备安全恢复助手…":"正在准备更新助手…",
            _=>state.Message
        };
    }
    internal void Finish(UpdateApplyResult? result,string? target)
    {
        if(result!=null && (!result.Success || !result.Restarted))
        {
            InstallProgress.Set(0,false); InstallStatus.Text="更新结果需要确认";
            var message=result.Message+"\n请手动启动此安装目录中的登录器 DFLegacy.Launcher.exe；如未完成更新，关闭占用文件的游戏后重新检查更新。\n安装目录："+target;
            message+=result.BackupDirectory is {} backup?"\n恢复备份："+backup:"\n如仍无法启动，请检查此安装目录下的 .dflegacy-backups 恢复资料。";
            new ClassicMessageWindow(message) { Owner=this }.ShowDialog();
        }
        Finish();
    }
    internal void Finish() { finished=true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if(!finished) { e.Cancel=true; if(canCancel) CancelRequested?.Invoke(); }
        base.OnClosing(e);
    }
}
