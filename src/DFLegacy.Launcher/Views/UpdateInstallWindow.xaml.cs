using System.ComponentModel;
using System.Windows;
namespace DFLegacy.Launcher.Views;
public partial class UpdateInstallWindow : Window
{
    private bool finished;
    internal UpdateInstallWindow() { InitializeComponent(); InstallProgress.Set(0,true); }
    internal void Finish() { finished=true; Close(); }
    protected override void OnClosing(CancelEventArgs e) { if(!finished) e.Cancel=true; base.OnClosing(e); }
}
