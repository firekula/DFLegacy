using System.Windows;
using DFLegacy.Launcher.Views;

namespace DFLegacy.Launcher;

public static class ClassicMessage
{
    public static void Show(Window? owner, string message)
    {
        ShowDialog(owner, new ClassicMessageWindow(message));
    }

    internal static bool Confirm(Window? owner, string message)
    {
        var window = new ClassicMessageWindow(message, confirmRestart: true, cancelText: "取消");
        window.Loaded += (_, _) => window.FocusCancel();
        return ShowDialog(owner, window);
    }

    private static bool ShowDialog(Window? owner, ClassicMessageWindow window)
    {
        if (owner is { IsVisible: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
        return window.Accepted;
    }
}
