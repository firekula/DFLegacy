using System.Windows;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Views;

internal sealed class ChapterTransitionWindow : UpdateWindow
{
    private readonly string root;
    private readonly ChapterTransitionOffer offer;
    private CancellationTokenSource? operation;

    internal ChapterTransitionWindow(string root, ChapterTransitionOffer offer)
        : base(offer.Enrollment ? "准备章节更新" : offer.Rollback ? "回退第一章" : "升级第二章", offer.Version, "返回")
    {
        this.root = Path.GetFullPath(root);
        this.offer = offer;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetPrimaryAction(offer.Enrollment ? "核验并准备" : offer.Rollback ? "回退并重启" : "下载并升级", async () => await RunAsync());
        var description = offer.Enrollment
            ? "核验并登记第一章。完成后可继续玩第一章，再选择升级。"
            : offer.Rollback
                ? "恢复第一章资源，保留现有账号。重开后停留第一章。"
                : "在原目录安装第二章游戏及资源，并保留第一章回退点和账号。";
        Render(new UpdateViewState(UpdatePhase.Idle, description + "\n安装目录：" + this.root));
    }

    private void Cancel() => operation?.Cancel();

    private async Task RunAsync()
    {
        if (operation is not null) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        var token = cancellation.Token;
        var handedOff = false;
        CancelRequested += Cancel;
        SetBackText("取消");
        Render(new UpdateViewState(UpdatePhase.Checking, "正在检查安装目录及文件占用…"));
        try
        {
            await Diagnostics.AutomaticDiagnostics.SuspendForUpdateAsync(token);
            var receipt = new Core.InstallationStore(root).ReadInstalled();
            if (receipt is null && !offer.Enrollment) throw new InvalidDataException("缺少受管理安装记录。");
            var lockPaths = receipt is null ? new[] { "DNF.exe", "DFLegacy.Client.dll", "Script.pvf" }
                : receipt.Release.Manifest.Files.Select(file => file.Path);
            await Task.Run(() => RequireFilesAvailable(lockPaths, token), token);
            var installer = new UpdateInstaller(root);
            UpdateHandoff handoff;
            if (offer.Rollback)
            {
                Render(new UpdateViewState(UpdatePhase.Checking, "正在核验第一章回退点…"));
                handoff = await installer.PrepareChapterRollbackAsync(offer.Source, token);
            }
            else
            {
                using var http = offer.Source.Provider == "local" ? null : Core.ReleaseProvider.CreateHttpClient();
                using var loopback = http is null ? new Core.LoopbackReleaseProvider(offer.Source) : null;
                Core.IReleaseProvider provider = loopback is not null ? loopback : new Core.ReleaseProvider(http!, offer.Source);
                var engine = new Core.UpdateEngine(root, provider);
                var report = new Progress<Core.UpdateProgress>(p => { if (operation == cancellation && !handedOff) RenderTransfer(p); });
                var check = offer.Enrollment ? await engine.CheckAsync(report, token)
                    : await engine.CheckChapterUpgradeAsync(true, report, token);
                if (check.Target is null) throw new IOException(check.Message);
                var staged = await engine.DownloadAsync(check, report, token);
                handoff = offer.Enrollment ? await installer.PrepareV4Async(staged, null, token)
                    : await installer.PrepareChapterUpgradeAsync(staged, offer.Source, token);
            }
            token.ThrowIfCancellationRequested();
            Render(new UpdateViewState(UpdatePhase.Installing, "正在交接安装，启动器将自动重开…"));
            await installer.StartHelperAsync(handoff, token);
            handedOff = true;
            Finish();
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            Render(new UpdateViewState(UpdatePhase.Cancelled, "已取消，现有客户端保持原状。可重新开始更新。"));
        }
        catch (Exception ex)
        {
            Program.ReportError("chapter-transition", ex);
            Render(new UpdateViewState(UpdatePhase.Error, ex.Message));
        }
        finally
        {
            CancelRequested -= Cancel;
            operation = null;
            if (!handedOff) SetBackText("返回");
            Diagnostics.AutomaticDiagnostics.ResumeAfterUpdate(handedOff);
        }
    }

    private void RequireFilesAvailable(IEnumerable<string> paths, CancellationToken token)
    {
        using var maintenance = Core.ClientGameLease.EnterMaintenance(root);
        var candidates = paths.Where(path => !path.Equals(BootstrapLayout.EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(path => Core.PathPolicy.Resolve(root, path))
            .Where(path => !string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase));
        // The initial scan reads files. The transaction checks write access only
        // for destinations it actually has to replace or retire.
        var blocked = FileLockOwners.FindUnreadableFiles(candidates, token);
        if (blocked.Count == 0) return;
        var owners = FileLockOwners.Find(blocked);
        var files = string.Join("、", blocked.Take(4).Select(path => Path.GetRelativePath(root, path)));
        var names = string.Join("、", owners.Select(owner => owner.Name).Distinct());
        throw new IOException("无法读取客户端文件，请释放占用后重试。\n文件：" + files
            + (names.Length == 0 ? "" : "\n检测到的进程：" + names));
    }
}
