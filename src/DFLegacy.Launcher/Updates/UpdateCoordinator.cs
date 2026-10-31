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
    private UpdateWindow? window;
    private bool disposed;
    internal UpdateCoordinator(Window owner,LauncherOptions options,string installation)
    {
        this.owner=owner; this.installation=installation; session=new UpdateSession(installation,options);
        controller=new(session,installation,()=>owner.Dispatcher.BeginInvoke(new Action(()=>Application.Current.Shutdown())));
    }
    internal void Start()
    {
        Show(); _=controller.StartAsync(); _=ShowPreviousWarning();
    }
    internal void Check() { Show(); _=controller.CheckAsync(); }
    private void Show()
    {
        if(disposed) return;
        if(window==null)
        {
            window=new UpdateWindow(controller) { Owner=owner };
            window.Closed+=(_,_)=>window=null; window.Show();
        }
        else { window.WindowState=WindowState.Normal; window.Activate(); }
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
        if(!disposed && warning!=null && owner.IsVisible) ClassicMessage.Show(window is {IsVisible:true}?window:owner,warning);
    }
    public void Dispose()
    {
        if(disposed) return; disposed=true; controller.Dispose(); window?.Close(); (session as IDisposable)?.Dispose();
    }
}
