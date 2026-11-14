using System.ComponentModel;
using System.Windows;
using DFLegacy.Launcher.Updates;
namespace DFLegacy.Launcher.Views;
public partial class UpdateInstallWindow : Window
{
    private bool finished;
    internal UpdateInstallWindow() { InitializeComponent(); InstallProgress.Set(.85,false); }
    internal void Render(UpdateInstallProgress progress)
    {
        if(finished) return;
        SetStatus(progress.Message); InstallProgress.Set(progress.Fraction,false);
    }
    private void SetStatus(string message)
    {
        // The compact panel has one line. Preserve full details in the tooltip,
        // including explicit line breaks, while ellipsizing the visible text.
        InstallStatus.ToolTip=message;
        InstallStatus.Text=string.Join(" ",message.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
    }
    internal void Finish() { finished=true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if(!finished) e.Cancel=true;
        base.OnClosing(e);
    }
}
