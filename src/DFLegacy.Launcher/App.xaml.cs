using System.Windows;
using HandyControl.Data;

namespace DFLegacy.Launcher;

public partial class App : Application
{
    private FileStream? bootstrapAppLock;
    private bool restartBootstrap;
    internal static bool BootstrapVerified { get; private set; }
    private static readonly string[] HeadlessArgs =
        ["--headless", "--account", "--account-index", "--self-test", "--install-ijl15", "--check-d3d9", "--no-dialog", "--third-party-notices"];
    internal static bool IsHeadless(IEnumerable<string> args)=>args.Any(a=>HeadlessArgs.Contains(a,StringComparer.OrdinalIgnoreCase));

    protected override async void OnStartup(StartupEventArgs e)
    {
        if(Updates.BootstrapUpdateClient.IsRequested(e.Args))
        {
            if(ChapterTwoClient.IsBundled && (e.Args.Length<2 || !CanMaintainChapter(e.Args[1]))) { Shutdown(1); return; }
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await Updates.BootstrapUpdateClient.RunEntryAsync(e.Args));
            return;
        }
        if(Updates.RepairClient.IsRequested(e.Args,Environment.ProcessPath))
        {
            if(ChapterTwoClient.IsBundled)
            {
                var repairIndex=Array.FindIndex(e.Args,argument=>argument.Equals("--repair-client",StringComparison.OrdinalIgnoreCase));
                if(repairIndex<0 || repairIndex+1>=e.Args.Length || !CanMaintainChapter(e.Args[repairIndex+1])) { Shutdown(1); return; }
            }
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await Updates.RepairClient.RunEntryAsync(e.Args));
            return;
        }
        if (e.Args.Contains("--dflegacy-apply-update", StringComparer.Ordinal))
        {
            if(ChapterTwoClient.IsBundled && !Updates.UpdateInstaller.IsHelperInvocation(e.Args))
            {
                Shutdown(1);
                return;
            }
            // Keep the engine result authoritative when the only helper window closes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Helper UI has only embedded native artwork; it never reads launcher.json/theme.
            using var progress = new Views.UpdateOperationWindow("安装客户端更新", "正在等待登录器退出…");
            var outcome = await Updates.UpdateInstaller.RunHelperWithOutcomeAsync(e.Args,new Progress<Updates.UpdateInstallProgress>(progress.Report));
            progress.Finish(outcome.Result,outcome.Target);
            Shutdown(outcome.ExitCode);
            return;
        }
        base.OnStartup(e);
        Program.DetailedCaptureEnabled=e.Args.Contains("--detailed-crash-capture",StringComparer.OrdinalIgnoreCase);
        AppDomain.CurrentDomain.UnhandledException += (_,args) => { if(args.ExceptionObject is Exception error) Program.Incidents.Report(Diagnostics.IncidentKind.Crash,"launcher-unhandled",error,details:"evidence=managed-unhandled-exception"); };
        DispatcherUnhandledException += (_,args) => Program.Incidents.Report(Diagnostics.IncidentKind.Crash,"launcher-dispatcher",args.Exception,details:"evidence=dispatcher-unhandled-exception");
        var args = e.Args;
        var chapterProbe=Environment.GetEnvironmentVariable(Updates.UpdateInstaller.ChapterReadyProbeVariable);
        Environment.SetEnvironmentVariable(Updates.UpdateInstaller.ChapterReadyProbeVariable,null);
        try
        {
            var root=AppContext.BaseDirectory;
            var managed=Updates.ChapterUpdatePolicy.IsManaged(root);
            var entry=string.Equals(Path.GetFileName(Environment.ProcessPath),Updates.BootstrapLayout.EntryName,StringComparison.OrdinalIgnoreCase);
            var chapterRollback=DFLegacy.UpdateCore.ChapterTransition.HasPending(root);
            var v4Pending=DFLegacy.UpdateCore.InstallTransaction.HasPending(root);
            if(chapterProbe!=null)
            {
                if(!ChapterTwoClient.IsBundled || !managed || !v4Pending || chapterRollback
                    || Environment.GetEnvironmentVariable("DFLEGACY_BOOTSTRAP_READY_EVENT")==null)
                    throw new InvalidDataException("无效的跨章启动就绪检查。");
                DFLegacy.UpdateCore.InstallTransaction.ValidateChapterReadyProbe(root,chapterProbe);
            }
            if(chapterProbe==null && entry && (chapterRollback || managed && v4Pending))
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                var installer=new Updates.UpdateInstaller(root);
                var journal=chapterRollback?Updates.UpdateInstaller.ChapterRollbackJournal(root):
                    Path.Combine(root,".dflegacy","v4","pending.json");
                DFLegacy.UpdateCore.PathPolicy.NoLinks(journal);
                var hash=Updates.UpdateSafety.Hash(journal);
                var handoff=chapterRollback?await installer.PrepareChapterRecoveryAsync(hash,CancellationToken.None):
                    await installer.PrepareV4Async(null,hash,CancellationToken.None);
                await installer.StartHelperAsync(handoff);
                Shutdown(0);
                return;
            }
            if ((!ChapterTwoClient.IsBundled||managed) && string.Equals(Path.GetFileName(Environment.ProcessPath),
                Updates.BootstrapLayout.AppName, StringComparison.OrdinalIgnoreCase))
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                if(args.Length<2||args[0]!="--bootstrap-verified"||args[1]!="--")
                {
                    StartFixedEntry(args,false);Shutdown(0);return;
                }
                args=args[2..];
                try{bootstrapAppLock=Updates.BootstrapLayout.AcquireAppLock(AppContext.BaseDirectory);}
                catch(IOException ex) when((ex.HResult&0xffff) is 32 or 33){Shutdown(0);return;}
                if(chapterProbe!=null)
                {
                    var receipt=new DFLegacy.UpdateCore.InstallationStore(root).ReadInstalled()
                        ??throw new InvalidDataException("跨章检查缺少已提交的安装记录。");
                    Updates.BootstrapLayout.RequireReceiptLayout(root,Updates.BootstrapLayout.Architecture,receipt);
                }
                else
                {
                    // 原版 CProgressDlg 载入窗：记录校验快时延迟 400ms 再显示，避免闪窗。
                    var loading = new Views.LoadingWindow();
                    try
                    {
                        var progress = new Progress<DFLegacy.UpdateCore.UpdateProgress>(loading.Report);
                        var validation = Updates.BootstrapLayout.ValidateInstalledAsync(
                            AppContext.BaseDirectory, Updates.BootstrapLayout.Architecture, progress: progress);
                        if (await Task.WhenAny(validation, Task.Delay(400)) != validation) loading.Show();
                        await validation;
                    }
                    finally { loading.Close(); }
                }
                BootstrapVerified=true;
                if(managed && ChapterTwoClient.IsBundled!=Updates.BootstrapLayout.IsSecondChapter(root))
                    throw new InvalidDataException("启动器与已安装游戏的章节不匹配。");
            }
            else if(managed)
            {
                if(chapterProbe==null)await Updates.BootstrapLayout.ValidateChapterEntryAsync(root);
                if(ChapterTwoClient.IsBundled!=Updates.BootstrapLayout.IsSecondChapter(root))
                    throw new InvalidDataException("启动器与已安装游戏的章节不匹配。");
            }
            else if(chapterRollback)
                throw new InvalidDataException("存在未完成的跨章回退。请使用原启动入口或独立修复工具恢复。");
        }
        catch(Exception ex)
        {
            Program.ReportError("bootstrap-ui-validation",ex);
            // The native supervisor retries recovery when receipt validation fails.
            // An early modal would block that recovery until the user dismissed it.
            if(Environment.GetEnvironmentVariable("DFLEGACY_BOOTSTRAP_READY_EVENT") is null)
                ClassicMessage.Show(null,"启动校验失败\n"+ex.Message);
            Shutdown(1);return;
        }
        if (IsHeadless(args))
        {
            if(chapterProbe!=null) { Shutdown(1); return; }
            if(BootstrapVerified)Updates.BootstrapUpdateClient.SignalReady();
            var code = await Program.RunHeadlessAsync(args);
            Shutdown(code);
            return;
        }
        try
        {
            if (chapterProbe is null && BootstrapVerified && ChapterTwoClient.IsBundled
                && await TryStartFullStarterAsync(args))
            {
                Shutdown(0);
                return;
            }
            if(chapterProbe!=null && await Program.RunHeadlessAsync(["--self-test"])!=0)
                throw new InvalidDataException("第二章客户端及启动组件未通过本地自检。");
            ApplyTheme(Program.LoadOptions().Theme);
            var window = new MainWindow();
            MainWindow = window;
            ShutdownMode=ShutdownMode.OnMainWindowClose;
            var managed=Updates.ChapterUpdatePolicy.IsManaged(AppContext.BaseDirectory);
            if (chapterProbe==null && (ChapterTwoClient.IsBundled || Environment.GetEnvironmentVariable("DFLEGACY_BOOTSTRAP_READY_EVENT")!=null))
                window.Show();
            if(BootstrapVerified||managed)Updates.BootstrapUpdateClient.SignalReady();
            if(chapterProbe!=null)
            {
                var timeout=System.Diagnostics.Stopwatch.StartNew();
                while(DFLegacy.UpdateCore.InstallTransaction.HasPending(AppContext.BaseDirectory))
                {
                    if(timeout.Elapsed>TimeSpan.FromSeconds(35)) { Shutdown(1); return; }
                    await Task.Delay(100);
                }
                var installed=new DFLegacy.UpdateCore.InstallationStore(AppContext.BaseDirectory).ReadInstalled();
                if(installed?.Release.ManifestSha256!=chapterProbe) { Shutdown(1); return; }
                window.Show();
            }
            if(!ChapterTwoClient.IsBundled||managed)window.StartUpdates();
        }
        catch (Exception exception)
        {
            Program.ReportError("gui-startup",exception);
            // launcher.json 损坏等配置错误会从 LoadOptions 或 MainWindow 构造内的
            // _store.Load 抛出（InvalidDataException / JsonException）。async void
            // OnStartup 的未处理异常会以 WPF 崩溃对话框收场，这里与 headless 的
            // catch 保持同一交互方式：友好弹窗 + 以退出码 1 结束。
            if(Environment.GetEnvironmentVariable("DFLEGACY_BOOTSTRAP_READY_EVENT") is null)
                ClassicMessage.Show(null,"启动失败\n"+exception.Message);
            Shutdown(1);
        }
    }

    private static async Task<bool> TryStartFullStarterAsync(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var package = Updates.FullStarterMigration.FindPackage(root);
        if (package is null) return false;
        var options = new LauncherAccountStore(Program.LauncherConfigPath).LoadCurrent();
        using var cancel = new CancellationTokenSource();
        // 校验阶段走小载入窗；只有开始下载/替换时才打开大窗口。
        var loading = new Views.LoadingWindow(); loading.Show();
        Views.UpdateOperationWindow? window = null;
        // Close any window this process still owns before the new client starts;
        // otherwise the old and new login windows can both be visible.
        foreach (Window open in Application.Current.Windows)
        {
            if (open is not Views.LoadingWindow) open.Close();
        }
        // The fixed entry may exit after the progress window opens. The full
        // download and the new login window have independent readiness checks.
        Updates.BootstrapUpdateClient.SignalReady();
        var progress = new Progress<DFLegacy.UpdateCore.UpdateProgress>(p =>
        {
            if (p is { Phase: "stage" })
            {
                if (window is null) loading.Report(p);
                return;
            }
            if (window is null)
            {
                loading.Close();
                window = new Views.UpdateOperationWindow("升级客户端", "正在下载完整启动包…", cancel.Cancel);
                window.VisibleWindow.Show();
            }
            window.Report(p);
        });
        try
        {
            return await Updates.FullStarterMigration.RunAsync(root, package, options.DisableGitHubUpdates, args,
                progress, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error) when (Updates.FullStarterMigration.CanRestoreOldLauncher(error))
        {
            Program.ReportError("chapter-two-full-starter-migration", error);
            ClassicMessage.Show(window?.VisibleWindow, "完整启动包升级未完成，将继续打开原来的客户端。\n" + error.Message);
            return false;
        }
        finally { loading.Close(); window?.VisibleWindow.Close(); }
    }

    private static bool CanMaintainChapter(string path)
    {
        try { return Path.IsPathFullyQualified(path) && Updates.ChapterUpdatePolicy.IsManaged(path); }
        catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }

    internal static void RecheckBootstrap()
    {
        if(Current is not App app||!BootstrapVerified)return;
        app.restartBootstrap=true;app.Shutdown();
    }
    private static void StartFixedEntry(string[] args,bool waitForThisProcess)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var entry=Path.Combine(root,Updates.BootstrapLayout.EntryName);
        DFLegacy.UpdateCore.PathPolicy.NoLinks(entry);
        var start=new System.Diagnostics.ProcessStartInfo(entry){WorkingDirectory=root,UseShellExecute=false};
        if(waitForThisProcess)
        {
            using var process=System.Diagnostics.Process.GetCurrentProcess();
            start.ArgumentList.Add("--bootstrap-wait-parent");start.ArgumentList.Add(process.Id.ToString());start.ArgumentList.Add(process.StartTime.ToUniversalTime().Ticks.ToString());
        }
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var child=System.Diagnostics.Process.Start(start)??throw new InvalidOperationException("无法启动自动修复入口。");
    }
    protected override void OnExit(ExitEventArgs e)
    {
        bootstrapAppLock?.Dispose();bootstrapAppLock=null;
        if(restartBootstrap)
        {
            try{StartFixedEntry([],true);}
            catch(Exception ex){Program.ReportError("bootstrap-recheck",ex);ClassicMessage.Show(Current.MainWindow,"重新检查更新失败\n"+ex.Message);}
        }
        base.OnExit(e);
    }

    internal static void ApplyTheme(string theme)
    {
        var skin = theme.ToLowerInvariant() switch
        {
            "dark" => SkinType.Dark,
            "light" => SkinType.Default,
            _ => DetectSystemLight() ? SkinType.Default : SkinType.Dark
        };
        // HandyControl 3.5.1 没有 ConfigManager.SetSkin（那是旧版/不存在的 API）。
        // 官方方式是操作 App 资源里的 Theme 字典：改它的 Skin 属性会把内部
        // MergedDictionaries[0] 换成对应皮肤的色板。
        if (Current.Resources.MergedDictionaries.OfType<HandyControl.Themes.Theme>().FirstOrDefault() is { } current)
        {
            current.Skin = skin;
        }
        else
        {
            Current.Resources.MergedDictionaries.Add(new HandyControl.Themes.Theme { Skin = skin });
        }
    }

    private static bool DetectSystemLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value != 0;
        }
        catch { /* 读取失败按浅色处理 */ }
        return true;
    }
}
