using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Views;

internal sealed class ChapterTransitionWindow : Window
{
    private readonly string root;
    private readonly ChapterTransitionOffer offer;
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,16,0,12)};
    private readonly ProgressBar progress=new(){Height=12,Minimum=0,Maximum=1,Margin=new(0,0,0,20)};
    private readonly Button action=new(){MinWidth=160,Padding=new(16,8,16,8)};
    private readonly Button cancel=new(){Content="返回",MinWidth=90,Padding=new(12,8,12,8),Margin=new(10,0,0,0)};
    private CancellationTokenSource? operation;
    private bool installing;
    private bool handedOff;

    internal ChapterTransitionWindow(string root, ChapterTransitionOffer offer)
    {
        this.root=Path.GetFullPath(root);this.offer=offer;
        Title=offer.Enrollment?"准备章节更新":offer.Rollback?"回退第一章":"升级第二章";
        Width=610;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(28,34,42));Foreground=Brushes.White;FontSize=15;
        var body=new StackPanel{Margin=new(24)};Content=body;
        body.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,14)});
        body.Children.Add(new TextBlock{Text=offer.Enrollment
            ?"先核验并登记完整第一章游戏资源，建立可恢复的章节更新入口。完成后仍可继续玩第一章，再主动选择升级第二章。"
            :offer.Rollback
            ?"恢复此目录中备份的第一章。保留最新账号记录和测试日志；回退后停留第一章，直到你再次选择升级。"
            :"在原目录安装第二章游戏和资源。先自动保留完整第一章回退点，保留账号记录；请先退出此目录中的游戏。",
            TextWrapping=TextWrapping.Wrap});
        body.Children.Add(new TextBlock{Text="当前版本："+offer.Version+"\n安装目录："+this.root,
            TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0),Foreground=Brushes.LightSteelBlue});
        body.Children.Add(status);body.Children.Add(progress);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        action.Content=offer.Enrollment?"核验并准备更新":offer.Rollback?"恢复第一章并重启":"下载并升级第二章";
        action.Click+=async(_,_)=>await RunAsync();cancel.Click+=(_,_)=>{if(operation!=null)operation.Cancel();else Close();};
        buttons.Children.Add(action);buttons.Children.Add(cancel);body.Children.Add(buttons);
        status.Text=offer.Rollback?"将先核验回退点和当前文件。":"这是本机隔离验收渠道，不会发布更新。";
    }

    private async Task RunAsync()
    {
        if(operation!=null)return;
        operation=new();var token=operation.Token;action.IsEnabled=false;cancel.Content="取消";
        try
        {
            await Diagnostics.AutomaticDiagnostics.SuspendForUpdateAsync(token);
            var receipt=new Core.InstallationStore(root).ReadInstalled();
            if(receipt==null&&!offer.Enrollment)throw new InvalidDataException("缺少受管理安装记录。");
            var lockPaths=receipt==null?new[]{"DNF.exe","DFLegacy.Client.dll","Script.pvf"}
                :receipt.Release.Manifest.Files.Select(file=>file.Path).Where(path=>!path.Equals(BootstrapLayout.EntryName,StringComparison.OrdinalIgnoreCase));
            var owners=await Task.Run(()=>FileLockOwners.Find(lockPaths.Select(path=>Core.PathPolicy.Resolve(root,path))),token);
            if(owners.Count>0)throw new IOException("请先退出此目录的游戏及占用文件的程序，再重试。\n"+string.Join("、",owners.Select(p=>p.Name)));
            var installer=new UpdateInstaller(root);
            UpdateHandoff handoff;
            if(offer.Rollback)
            {
                status.Text="正在核验回退点…";progress.IsIndeterminate=true;
                handoff=await installer.PrepareChapterRollbackAsync(offer.Source,token);
            }
            else
            {
                using var provider=new Core.LoopbackReleaseProvider(offer.Source);
                var engine=new Core.UpdateEngine(root,provider);
                var report=new Progress<Core.UpdateProgress>(p=>
                {
                    status.Text=UpdateProgressText.Format(p);
                    progress.IsIndeterminate=p.TotalFiles<=0;
                    if(p.TotalFiles>0)progress.Value=Math.Clamp((p.CompletedFiles+(p.TotalBytes>0?(double)p.BytesReceived/p.TotalBytes:0))/p.TotalFiles,0,1);
                });
                status.Text="核验第一章并检查第二章资源…";
                var check=offer.Enrollment?await engine.CheckAsync(report,token):await engine.CheckChapterUpgradeAsync(true,report,token);
                if(check.Target==null)throw new IOException(check.Message);
                var staged=await engine.DownloadAsync(check,report,token);
                handoff=offer.Enrollment?await installer.PrepareV4Async(staged,null,token)
                    :await installer.PrepareChapterUpgradeAsync(staged,offer.Source,token);
            }
            token.ThrowIfCancellationRequested();
            installing=true;cancel.IsEnabled=false;status.Text="正在交接安装，启动器将自动重开…";
            await installer.StartHelperAsync(handoff,token);
            handedOff=true;
            Application.Current.Shutdown();
        }
        catch(OperationCanceledException){status.Text="已取消。现有客户端保持原状，已下载内容可继续使用。";}
        catch(Exception ex){Program.ReportError("chapter-transition",ex);status.Text=ex.Message;}
        finally
        {
            installing=false;operation.Dispose();operation=null;
            action.IsEnabled=true;cancel.IsEnabled=true;cancel.Content="返回";progress.IsIndeterminate=false;
            Diagnostics.AutomaticDiagnostics.ResumeAfterUpdate(handedOff);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if(operation!=null&&!handedOff)
        {e.Cancel=true;if(!installing)operation.Cancel();}
        base.OnClosing(e);
    }
}
