using System.Windows;
using DFLegacy.Launcher.Views;

namespace DFLegacy.Launcher;

public static class ClassicMessage
{
    public static void Show(Window? owner, string message)
    {
        var window = new ClassicMessageWindow(message);
        if (owner is { IsVisible: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
    }
}
