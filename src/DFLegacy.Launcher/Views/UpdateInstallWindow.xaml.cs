using System.ComponentModel;
using System.Windows;
using DFLegacy.Launcher.Updates;
namespace DFLegacy.Launcher.Views;
public partial class UpdateInstallWindow : Window
{
    private bool finished;
    internal UpdateInstallWindow() { InitializeComponent(); InstallProgress.Set(0,true); }
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
    protected override void OnClosing(CancelEventArgs e) { if(!finished) e.Cancel=true; base.OnClosing(e); }
}
