using System.Windows;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Views;

// Only progress for work on a launcher executable can show the compact panel.
internal sealed class UpdateOperationWindow : IDisposable
{
    private readonly UpdateWindow large;
    private UpdateInstallWindow? compact;
    private bool installing;
    private bool disposed;

    internal UpdateOperationWindow(string title, string initialStatus, Action? cancel = null)
    {
        large = new UpdateWindow(title);
        if (cancel is not null) large.CancelRequested += cancel;
        installing = cancel is null;
        if (installing) large.RenderInstallation(new UpdateInstallProgress(0, initialStatus));
        else large.Render(new UpdateViewState(UpdatePhase.Checking, initialStatus));
        large.Show();
    }

    internal Window VisibleWindow => compact is { IsVisible: true } ? compact : large;

    internal void Report(Core.UpdateProgress progress)
    {
        if (disposed) return;
        if (progress.Phase is "backup" or "stage" or "replace" or "restore" or "prepared" or "install" or "recover" or "commit" or "chapter-rollback")
            installing = true;
        if (installing) Report(UpdateInstallProgress.FromCore(progress));
        else large.RenderTransfer(progress);
    }

    internal void Report(UpdateInstallProgress progress)
    {
        if (disposed) return;
        if (progress.ReplacingLauncher)
        {
            compact ??= new UpdateInstallWindow();
            compact.Render(progress);
            if (!compact.IsVisible) compact.Show();
            if (large.IsVisible) large.Hide();
        }
        else
        {
            large.RenderInstallation(progress);
            if (!large.IsVisible) large.Show();
            if (compact is { IsVisible: true }) compact.Hide();
        }
    }

    internal void Finish(UpdateApplyResult? result, string? target)
    {
        if (result is not null && (!result.Success || !result.Restarted))
        {
            Report(new UpdateInstallProgress(0, "更新结果需要确认"));
            var message = result.Message + "\n安装目录：" + target;
            message += result.BackupDirectory is { } backup ? "\n恢复备份：" + backup
                : "\n如仍无法启动，请检查此目录下的 .dflegacy-backups 恢复资料。";
            ClassicMessage.Show(large, message + "\n可手动启动此目录中的登录器 DFLegacy.Launcher.exe。");
        }
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        compact?.Finish();
        large.Finish();
    }
}
