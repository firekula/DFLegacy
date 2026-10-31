using System.Windows;
using HandyControl.Data;

namespace DFLegacy.Launcher;

public partial class App : Application
{
    private static readonly string[] HeadlessArgs =
        ["--headless", "--account", "--account-index", "--self-test", "--install-ijl15", "--check-d3d9", "--no-dialog"];

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--dflegacy-apply-update", StringComparer.Ordinal))
        {
            // Keep the engine result authoritative when the only helper window closes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Helper UI has only embedded native artwork; it never reads launcher.json/theme.
            var progress = new Views.UpdateInstallWindow();
            progress.Show();
            var outcome = await Updates.UpdateInstaller.RunHelperWithOutcomeAsync(e.Args);
            progress.Finish(outcome.Result,outcome.Target);
            Shutdown(outcome.ExitCode);
            return;
        }
        base.OnStartup(e);
        var args = e.Args;
        if (args.Any(a => HeadlessArgs.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            var code = await Program.RunHeadlessAsync(args);
            Shutdown(code);
            return;
        }
        try
        {
            ApplyTheme(Program.LoadOptions().Theme);
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            // launcher.json 损坏等配置错误会从 LoadOptions 或 MainWindow 构造内的
            // _store.Load 抛出（InvalidDataException / JsonException）。async void
            // OnStartup 的未处理异常会以 WPF 崩溃对话框收场，这里与 headless 的
            // catch 保持同一交互方式：友好弹窗 + 以退出码 1 结束。
            MessageBox.Show(exception.Message, "DFLegacy 启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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
