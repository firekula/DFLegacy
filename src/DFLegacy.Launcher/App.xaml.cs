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
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await Updates.BootstrapUpdateClient.RunEntryAsync(e.Args));
            return;
        }
        if(Updates.RepairClient.IsRequested(e.Args,Environment.ProcessPath))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await Updates.RepairClient.RunEntryAsync(e.Args));
            return;
        }
        if (e.Args.Contains("--dflegacy-apply-update", StringComparer.Ordinal))
        {
            // Keep the engine result authoritative when the only helper window closes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Helper UI has only embedded native artwork; it never reads launcher.json/theme.
            var progress = new Views.UpdateInstallWindow();
            progress.Show();
            var outcome = await Updates.UpdateInstaller.RunHelperWithOutcomeAsync(e.Args,new Progress<Updates.UpdateInstallProgress>(progress.Render));
            progress.Finish(outcome.Result,outcome.Target);
            Shutdown(outcome.ExitCode);
            return;
        }
        base.OnStartup(e);
        Program.DetailedCaptureEnabled=e.Args.Contains("--detailed-crash-capture",StringComparer.OrdinalIgnoreCase);
        AppDomain.CurrentDomain.UnhandledException += (_,args) => { if(args.ExceptionObject is Exception error) Program.Incidents.Report(Diagnostics.IncidentKind.Crash,"launcher-unhandled",error,details:"evidence=managed-unhandled-exception"); };
        DispatcherUnhandledException += (_,args) => Program.Incidents.Report(Diagnostics.IncidentKind.Crash,"launcher-dispatcher",args.Exception,details:"evidence=dispatcher-unhandled-exception");
        var args = e.Args;
        try
        {
            if(string.Equals(Path.GetFileName(Environment.ProcessPath),Updates.BootstrapLayout.AppName,StringComparison.OrdinalIgnoreCase))
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                if(args.Length<2||args[0]!="--bootstrap-verified"||args[1]!="--")
                {
                    StartFixedEntry(args,false);Shutdown(0);return;
                }
                args=args[2..];
                try{bootstrapAppLock=Updates.BootstrapLayout.AcquireAppLock(AppContext.BaseDirectory);}
                catch(IOException ex) when((ex.HResult&0xffff) is 32 or 33){Shutdown(0);return;}
                await Updates.BootstrapLayout.ValidateInstalledAsync(AppContext.BaseDirectory,Updates.BootstrapLayout.Architecture);
                BootstrapVerified=true;
            }
        }
        catch(Exception ex)
        {
            Program.ReportError("bootstrap-ui-validation",ex);
            MessageBox.Show(ex.Message,"DFLegacy 启动校验失败",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);return;
        }
        if (IsHeadless(args))
        {
            if(BootstrapVerified)Updates.BootstrapUpdateClient.SignalReady();
            var code = await Program.RunHeadlessAsync(args);
            Shutdown(code);
            return;
        }
        try
        {
            ApplyTheme(Program.LoadOptions().Theme);
            var window = new MainWindow();
            MainWindow = window;
            ShutdownMode=ShutdownMode.OnMainWindowClose;
            window.StartUpdates();
            if(BootstrapVerified)Updates.BootstrapUpdateClient.SignalReady();
        }
        catch (Exception exception)
        {
            Program.ReportError("gui-startup",exception);
            // launcher.json 损坏等配置错误会从 LoadOptions 或 MainWindow 构造内的
            // _store.Load 抛出（InvalidDataException / JsonException）。async void
            // OnStartup 的未处理异常会以 WPF 崩溃对话框收场，这里与 headless 的
            // catch 保持同一交互方式：友好弹窗 + 以退出码 1 结束。
            MessageBox.Show(exception.Message, "DFLegacy 启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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
            catch(Exception ex){Program.ReportError("bootstrap-recheck",ex);MessageBox.Show(ex.Message,"重新检查更新失败");}
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
