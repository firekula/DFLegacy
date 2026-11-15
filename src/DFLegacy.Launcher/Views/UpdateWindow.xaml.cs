using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DFLegacy.Launcher.Controls;
using DFLegacy.Launcher.Updates;
namespace DFLegacy.Launcher.Views;
public partial class UpdateWindow : Window
{
    private readonly UpdateController? controller;
    private readonly string context;
    private Action? primaryAction;
    private bool finished;
    private string? previousStatus;
    private volatile bool closed;
    internal event Action? CancelRequested;

    internal UpdateWindow(string title, string context = "", string backText = "取消")
    {
        InitializeComponent();
        Title = PageTitle.Text = title;
        this.context = context;
        BackButton.Content = backText;
        RetryButton.Visibility = InstallButton.Visibility = Visibility.Collapsed;
        Closed += (_, _) => closed = true;
    }

    internal UpdateWindow(UpdateController controller) : this("客户端更新", backText: "返回登录")
    {
        this.controller=controller; controller.Changed+=Changed;
        RetryButton.Visibility = InstallButton.Visibility = Visibility.Visible;
        Closed+=(_,_)=> { controller.Changed-=Changed; controller.Cancel(); };
        Render(controller.State);
    }

    internal void SetPrimaryAction(string text, Action action)
    {
        primaryAction = action;
        RetryButton.Content = text;
        RetryButton.Visibility = Visibility.Visible;
    }

    internal void SetBackText(string text) => BackButton.Content = text;

    internal void RenderTransfer(DFLegacy.UpdateCore.UpdateProgress progress) => Render(
        new UpdateViewState(progress.Phase is "download" or "retry" ? UpdatePhase.Downloading : UpdatePhase.Checking,
            UpdateProgressText.Format(progress), Progress: new UpdateProgress(progress.BytesReceived, progress.TotalBytes,
                progress.BytesPerSecond, progress.Phase, progress.Path, progress.CompletedFiles, progress.TotalFiles)));

    internal void RenderInstallation(UpdateInstallProgress progress)
    {
        Render(new UpdateViewState(UpdatePhase.Installing, progress.Message));
        DownloadProgress.Set(progress.Fraction, false);
        AmountText.Text = "正在安装或恢复，请保持窗口打开。";
    }
    private void Changed(UpdateViewState state)
    {
        if(closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(()=> { if(!closed) Render(state); }));
    }
    internal void Render(UpdateViewState state)
    {
        var busy=state.Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;
        RetryButton.IsEnabled=!busy; InstallButton.IsEnabled=state.Phase==UpdatePhase.Ready;
        InstallButton.Content=state.IsRecovery?"重启恢复":"重启安装";
        BackButton.IsEnabled=state.Phase!=UpdatePhase.Installing;
        var text=state.Message;
        if (controller is null && state.Phase==UpdatePhase.Ready && !state.IsRecovery)
            text=state.ReplacesScriptPvf ? "下载校验完成，准备备份并替换 Script.pvf…" : "下载校验完成，正在准备安装…";
        var installed=controller?.InstalledVersion ?? context;
        if(installed.Length!=0 && (state.Phase is UpdatePhase.Idle or UpdatePhase.Ready || controller is not null))
            text="当前："+installed+"\n"+text;
        if(state.Version.Length!=0) text+="    可用："+state.Version;
        if(state.Phase==UpdatePhase.Downloading && state.AssetName.Length!=0) text+="\n文件："+state.AssetName;
        // Bound each native raster input, while keeping long diagnostic text scrollable.
        if(previousStatus!=text)
        {
            previousStatus=text;
            StatusBlocks.Children.Clear();
            for(int offset=0;offset<Math.Min(text.Length,16384);)
            {
                var count=Math.Min(4096,Math.Min(text.Length,16384)-offset);
                if(offset+count<text.Length && char.IsHighSurrogate(text[offset+count-1])) count--;
                var block=new PixelText { Text=text.Substring(offset,count),VerticalContentAlignment=VerticalAlignment.Top };
                StatusBlocks.Children.Add(block); offset+=count;
            }
        }
        if(state.Progress is { TotalBytes: > 0 } p)
        {
            var fraction=Math.Clamp((double)p.BytesReceived/p.TotalBytes,0,1);
            DownloadProgress.Set(p.Phase == "download" ? Fraction(p, fraction) : fraction,false);
            var amount=$"{Bytes(p.BytesReceived)} / {Bytes(p.TotalBytes)}    {fraction:P0}";
            AmountText.Text=p.Phase == "verify" ? amount
                : $"{(p.Phase == "download" && p.TotalFiles > 1 ? "当前文件 " : "")}{amount}    {Bytes((long)p.BytesPerSecond)}/s";
        }
        else if(state.Progress is { TotalFiles: > 0 } files)
        {
            DownloadProgress.Set(Fraction(files, 0),false);
            AmountText.Text=$"已处理 {files.CompletedFiles} / {files.TotalFiles} 个文件";
        }
        else
        {
            DownloadProgress.Set(state.Phase==UpdatePhase.Ready?1:0,state.Phase is UpdatePhase.Checking or UpdatePhase.Installing);
            AmountText.Text=state.Phase==UpdatePhase.Ready?(state.IsRecovery?"恢复资料已校验，等待确认重启恢复":"校验完成，正在准备安装"):state.Phase==UpdatePhase.Installing?"请勿退出…":"";
        }
    }
    private static double Fraction(UpdateProgress progress, double fileFraction) => progress.TotalFiles <= 0 ? fileFraction :
        Math.Clamp((progress.CompletedFiles + fileFraction) / progress.TotalFiles, 0, 1);
    private static string Bytes(long value)=>value<1024?$"{value} B":value<1024*1024?$"{value/1024d:F1} KB":$"{value/(1024d*1024):F1} MB";
    private async void OnRetry(object sender,RoutedEventArgs e)
    {
        if(primaryAction is not null) primaryAction();
        else if(controller is not null) await controller.CheckAsync();
    }
    private async void OnInstall(object sender,RoutedEventArgs e)
    {
        if(controller is null) return;
        var state=controller.State;
        if(state.Phase!=UpdatePhase.Ready) return;
        await controller.InstallAsync(ConfirmRestart(this,state.IsRecovery,state.ReplacesScriptPvf));
    }
    internal static bool ConfirmRestart(Window owner,bool recovery=false,bool replacesScriptPvf=false)
    {
        var message=recovery?"恢复上次未完成的更新。\n确认退出登录器、恢复原版本并重新启动？":"更新已下载并校验完成。\n确认退出登录器、安装并重新启动？";
        if(replacesScriptPvf) message="将替换 Script.pvf，并备份已有文件。\n"+message;
        var dialog=new ClassicMessageWindow(message,confirmRestart:true) { Owner=owner };
        dialog.ShowDialog(); return dialog.Accepted;
    }
    private void OnBack(object sender,RoutedEventArgs e)=>Close();
    internal void Finish() { finished=true; if(!closed) Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if(!finished)
        {
            if(!BackButton.IsEnabled) e.Cancel=true;
            else if(CancelRequested is { } cancel) { e.Cancel=true; cancel(); }
        }
        base.OnClosing(e);
    }
    private void OnKey(object sender,KeyEventArgs e) { if(e.Key==Key.Escape && BackButton.IsEnabled) { e.Handled=true; Close(); } }
    private void OnDrag(object sender,MouseButtonEventArgs e) { if(e.GetPosition(this).Y<80) DragMove(); }
}
