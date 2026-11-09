using System.Windows;
using DFLegacy.Launcher.Views;
namespace DFLegacy.Launcher.Updates;
// Window lifetime and dispatcher work stay separate from the update engine/controller.
internal sealed class UpdateCoordinator : IDisposable
{
    private readonly Window owner;
    private readonly IUpdateSession session;
    private readonly UpdateController controller;
    private readonly string installation;
    private readonly Action<string> notify;
    private readonly Func<Window,bool> confirmRecovery;
    private UpdateInstallWindow? window;
    private Task operation=Task.CompletedTask;
    private bool disposed;
    internal UpdateCoordinator(Window owner,LauncherOptions options,string installation)
        : this(owner,installation,new V4UpdateSession(installation,options),()=>owner.Dispatcher.BeginInvoke(new Action(()=>Application.Current.Shutdown()))) { }
    internal UpdateCoordinator(Window owner,string installation,IUpdateSession session,Action shutdown,Action<string>? notify=null,Func<Window,bool>? confirmRecovery=null)
    {
        this.owner=owner; this.installation=installation; this.session=session;
        this.notify=notify??(message=>ClassicMessage.Show(owner,message));
        this.confirmRecovery=confirmRecovery??(parent=>UpdateWindow.ConfirmRestart(parent,true));
        controller=new(session,installation,shutdown); controller.Changed+=Changed;
        owner.Closed+=OwnerClosed;
    }
    internal void Start()=>_=StartAsync();
    internal Task StartAsync()=>Run(controller.StartAsync);
    internal void Check()=>_=Run(controller.CheckAsync);
    private Task Run(Func<Task> check)
    {
        if(disposed || !operation.IsCompleted) return operation;
        owner.Hide(); operation=RunCore(check); return operation;
    }
    private async Task RunCore(Func<Task> check)
    {
        await check();
        if(disposed) return;
        if(controller.State.Phase==UpdatePhase.Ready)
        {
            ShowProgress(controller.State);
            // The user requested automatic regular updates. Interrupted transactions
            // still require explicit consent before restoring an earlier installation.
            var confirmed=!controller.State.IsRecovery || confirmRecovery(window!);
            if(confirmed)
            {
                try
                {
                    await Diagnostics.AutomaticDiagnostics.SuspendForUpdateAsync(CancellationToken.None);
                    var owners=await session.InspectLocksAsync(CancellationToken.None);
                    if(owners.Count>0)
                    {
                        var details=string.Join("\n",owners.Select(p=>$"{p.Name}（PID {p.ProcessId}）"+(p.CanTerminate?"":"，需手动关闭")));
                        var answer=MessageBox.Show(window!,"以下程序正在使用更新文件：\n"+details+"\n\n是否关闭可处理的程序后继续安装？未保存的内容可能丢失。选择“否”会保留下载结果。",
                            "文件被占用",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No);
                        confirmed=answer==MessageBoxResult.Yes;
                        if(confirmed) await FileLockOwners.CloseAsync(owners,true,CancellationToken.None);
                    }
                }
                catch(Exception ex) { Diagnostics.AutomaticDiagnostics.ResumeAfterUpdate(); Program.ReportError("update-lock-inspection",ex); notify("无法处理文件占用，请关闭相关程序后重试。\n"+ex.Message); confirmed=false; }
            }
            try { await controller.InstallAsync(confirmed); }
            finally { if(controller.State.Phase!=UpdatePhase.Installing)Diagnostics.AutomaticDiagnostics.ResumeAfterUpdate(); }
        }
        if(disposed || controller.State.Phase==UpdatePhase.Installing) return;
        ShowLogin();
        if(controller.State.Phase==UpdatePhase.Error) notify(controller.State.Message);
        else if(session.ShowStatusNotice) notify(controller.State.Message);
        await ShowPreviousWarning();
    }
    private void Changed(UpdateViewState state)
    {
        if(disposed || owner.Dispatcher.HasShutdownStarted) return;
        owner.Dispatcher.BeginInvoke(new Action(()=>
        {
            if(!disposed && ReferenceEquals(controller.State,state) && state.Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Ready or UpdatePhase.Installing)
                ShowProgress(state);
        }));
    }
    private void ShowProgress(UpdateViewState state)
    {
        if(disposed) return;
        owner.Hide();
        if(window==null)
        {
            window=new UpdateInstallWindow();
            window.CancelRequested+=()=>controller.Cancel();
            window.Show();
        }
        window.Render(state);
    }
    private void ShowLogin()
    {
        if(disposed) return;
        // Keep an interactive window alive while the compact one closes.
        owner.Show(); window?.Finish(); window=null;
    }
    private async Task ShowPreviousWarning()
    {
        var warning=await Task.Run(()=>
        {
            try
            {
                var result=session.ReadLastResult();
                if(result==null || result.Success && result.Restarted) return null;
                return UpdateArtifacts.PresentOnce(installation,result.Message)?result.Message:null;
            }
            catch(Exception ex)
            {
                var text="无法读取上次更新结果，可继续登录。\n"+ex.Message;
                return UpdateArtifacts.PresentOnce(installation,text)?text:null;
            }
        });
        if(!disposed && warning!=null && owner.IsVisible) notify(warning);
    }
    private void OwnerClosed(object? sender,EventArgs e)=>Dispose();
    public void Dispose()
    {
        if(disposed) return; disposed=true; owner.Closed-=OwnerClosed;
        controller.Changed-=Changed; controller.Dispose(); window?.Finish(); window=null; (session as IDisposable)?.Dispose();
    }
}
