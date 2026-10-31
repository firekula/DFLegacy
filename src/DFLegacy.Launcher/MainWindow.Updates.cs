using System.Windows;
using DFLegacy.Launcher.Updates;
namespace DFLegacy.Launcher;
public partial class MainWindow
{
    private UpdateCoordinator? updateCoordinator;
    private void StartUpdates()
    {
        if(_windowClosed || updateCoordinator!=null) return;
        try { updateCoordinator=new(this,_options,AppContext.BaseDirectory); updateCoordinator.Start(); }
        catch(Exception ex) { ClassicMessage.Show(this,"自动更新暂不可用，可继续登录。\n"+ex.Message); }
    }
    private void CheckUpdates_Click(object sender,RoutedEventArgs e)
    {
        // Reuse confirmed runtime options. Opening updates must not reload the legacy endpoint.
        if(updateCoordinator==null) StartUpdates(); else updateCoordinator.Check();
    }
}
