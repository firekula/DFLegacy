using System.Windows;
using HandyControl.Controls;
using HandyControl.Data;

namespace DFLegacy.Launcher;

public partial class App : Application
{
    private static readonly string[] HeadlessArgs =
        ["--headless", "--account", "--account-index", "--self-test", "--install-ijl15", "--check-d3d9", "--no-dialog"];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        if (args.Any(a => HeadlessArgs.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            var code = await Program.RunHeadlessAsync(args);
            Shutdown(code);
            return;
        }
        ApplyTheme(Program.LoadOptions().Theme);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
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
