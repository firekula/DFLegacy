using System.Windows;
using System.Windows.Input;
using DFLegacy.Launcher.Controls;
using DFLegacy.Launcher.Updates;
namespace DFLegacy.Launcher.Views;
public partial class UpdateWindow : Window
{
    private readonly UpdateController controller;
    private volatile bool closed;
    internal UpdateWindow(UpdateController controller)
    {
        InitializeComponent(); this.controller=controller; controller.Changed+=Changed;
        Closed+=(_,_)=> { closed=true; controller.Changed-=Changed; controller.Cancel(); };
        Render(controller.State);
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
        BackButton.IsEnabled=state.Phase!=UpdatePhase.Installing;
        var text=state.Message+"\n当前："+controller.InstalledVersion+(state.Version.Length==0?"":"    可用："+state.Version);
        if(state.Phase==UpdatePhase.Downloading) text+="\n文件："+GitHubUpdateClient.AssetName;
        // Bound each native raster input, while keeping long diagnostic text scrollable.
        StatusBlocks.Children.Clear();
        for(int offset=0;offset<Math.Min(text.Length,16384);)
        {
            var count=Math.Min(4096,Math.Min(text.Length,16384)-offset);
            if(offset+count<text.Length && char.IsHighSurrogate(text[offset+count-1])) count--;
            var block=new PixelText { Text=text.Substring(offset,count),VerticalContentAlignment=VerticalAlignment.Top };
            StatusBlocks.Children.Add(block); offset+=count;
        }
        if(state.Progress is {} p)
        {
            var fraction=p.TotalBytes>0?(double)p.BytesReceived/p.TotalBytes:0;
            DownloadProgress.Set(fraction,false);
            AmountText.Text=$"{Bytes(p.BytesReceived)} / {Bytes(p.TotalBytes)}    {Bytes((long)p.BytesPerSecond)}/s    {fraction:P0}";
        }
        else
        {
            DownloadProgress.Set(state.Phase==UpdatePhase.Ready?1:0,state.Phase is UpdatePhase.Checking or UpdatePhase.Installing);
            AmountText.Text=state.Phase==UpdatePhase.Ready?"校验完成，等待确认重启安装":state.Phase==UpdatePhase.Installing?"准备交接中，请稍候…":"可以返回登录；稍后从更多服务重新检查。";
        }
    }
    private static string Bytes(long value)=>value<1024?$"{value} B":value<1024*1024?$"{value/1024d:F1} KB":$"{value/(1024d*1024):F1} MB";
    private async void OnRetry(object sender,RoutedEventArgs e)=>await controller.CheckAsync();
    private async void OnInstall(object sender,RoutedEventArgs e)
    {
        if(controller.State.Phase!=UpdatePhase.Ready) return;
        await controller.InstallAsync(ConfirmRestart(this));
    }
    internal static bool ConfirmRestart(Window owner)
    {
        var dialog=new ClassicMessageWindow("更新已下载并校验完成。\n确认退出登录器、安装并重新启动？",confirmRestart:true) { Owner=owner };
        dialog.ShowDialog(); return dialog.Accepted;
    }
    private void OnBack(object sender,RoutedEventArgs e)=>Close();
    private void OnKey(object sender,KeyEventArgs e) { if(e.Key==Key.Escape && BackButton.IsEnabled) { e.Handled=true; Close(); } }
    private void OnDrag(object sender,MouseButtonEventArgs e) { if(e.GetPosition(this).Y<80) DragMove(); }
}
