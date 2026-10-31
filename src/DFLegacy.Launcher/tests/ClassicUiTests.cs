using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DFLegacy.Launcher.Controls;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Views;

internal static class ClassicUiTests
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var plain = PixelRasterizer.Render("窗口分辨率", 13, Colors.White, 0);
                var edged = PixelRasterizer.Render("窗口分辨率", 13, Colors.Gold, 1);
                byte[] Pixels(BitmapSource image)
                {
                    var result = new byte[image.PixelWidth * image.PixelHeight * 4];
                    image.CopyPixels(result, image.PixelWidth * 4, 0); return result;
                }
                var p = Pixels(plain.Image); var e = Pixels(edged.Image);
                check(plain.Image.IsFrozen && edged.Image.IsFrozen, "pixel rasters freeze for safe reuse");
                check(edged.Image.PixelWidth == plain.Image.PixelWidth + 2 && edged.Image.PixelHeight == plain.Image.PixelHeight + 2,
                    "outline adds exactly one pixel around real glyph bounds");
                for (var y = 0; y < edged.Image.PixelHeight; y++)
                for (var x = 0; x < edged.Image.PixelWidth; x++)
                {
                    bool expected = false;
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                    {
                        var sx = x - 1 + dx; var sy = y - 1 + dy;
                        expected |= sx >= 0 && sy >= 0 && sx < plain.Image.PixelWidth && sy < plain.Image.PixelHeight
                            && p[(sy * plain.Image.PixelWidth + sx) * 4 + 3] == 255;
                    }
                    var offset = (y * edged.Image.PixelWidth + x) * 4;
                    check(e[offset + 3] == (expected ? 255 : 0), "outlined raster follows actual monochrome glyph mask");
                }
                check(p.Where((_, index) => index % 4 == 3).All(a => a is 0 or 255), "glyphs have no blurred gray alpha pixels");
                var hint = new PixelText { Text = "请输入账号", FontSize = 13, OutlineThickness = 0,
                    TextWrapping = TextWrapping.NoWrap, Foreground = Brushes.White, Width = 150, Height = 17 };
                hint.Measure(new Size(150, 17)); hint.Arrange(new Rect(0, 0, 150, 17));
                var rendered = new RenderTargetBitmap(150, 17, 96, 96, PixelFormats.Pbgra32); rendered.Render(hint);
                var pixels = Pixels(rendered);
                var rows = Enumerable.Range(0, 17).Where(y => Enumerable.Range(0, 150).Any(x => pixels[(y * 150 + x) * 4 + 3] > 0)).ToArray();
                check(rows.Length > 0 && Math.Abs(rows[0] - (16 - rows[^1])) <= 1, "actual WPF placeholder ink is vertically centered in original 17px field");
                hint.Foreground = Brushes.Lime;
                hint.UpdateLayout();
                var recolored = new RenderTargetBitmap(150, 17, 96, 96, PixelFormats.Pbgra32); recolored.Render(hint);
                var changed = Pixels(recolored);
                check(Enumerable.Range(0, changed.Length / 4).Any(i => changed[i * 4 + 1] == 255 && changed[i * 4 + 2] == 0),
                    "changing a live pixel label foreground redraws its actual WPF drawing");
                var path = new PixelText { Text = @"G:\very-long-client-installation\中文路径🙂\ImagePacks2\DNF.exe", FontSize = 13,
                    TextWrapping = TextWrapping.Wrap };
                path.Measure(new Size(130, double.PositiveInfinity));
                check(path.DesiredSize.Height > 30 && path.DesiredSize.Width <= 130, "long Unicode path wraps within available WPF width");
                var baseline = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                for (var i = 0; i < 600; i++) PixelRasterizer.Render("原版像素文字 " + i, 13, Colors.White, 1);
                var after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                check(after <= baseline + 2, "repeated uncached text releases every GDI object");
                check(PixelRasterizer.CachedLineCount <= 256 && PixelRasterizer.CachedBytes <= 4 * 1024 * 1024,
                    "text cache stays within entry and byte budgets");
                check(PixelRasterizer.Render("", 13, Colors.White, 1).Advance == 0, "empty labels do not invent layout advance");
                var panel = new BitmapImage(new Uri("pack://application:,,,/DFLegacy.Launcher;component/Assets/Skin/classic-settings-panel.png"));
                check(panel.PixelWidth == 480 && panel.PixelHeight == 428, "approved full-size settings panel is embedded and readable");
                var bgra = new FormatConvertedBitmap(panel, PixelFormats.Bgra32, null, 0);
                var panelPixels = Pixels(bgra); var red = 0;
                for (var y = 8; y < 420; y++) for (var x = 0; x < 8; x++)
                {
                    var offset = (y * panel.PixelWidth + x) * 4;
                    if (panelPixels[offset + 2] > 70 && panelPixels[offset + 2] > panelPixels[offset + 1] * 1.7
                        && panelPixels[offset + 2] > panelPixels[offset] * 1.7) red++;
                }
                check(red == 0, "embedded panel cannot recreate the old stretched red title strip");
                var styles = (ResourceDictionary)Application.LoadComponent(new Uri("/DFLegacy.Launcher;component/Views/ClassicStyles.xaml", UriKind.Relative));
                var button = new SkinButton { Content = "游戏设置", Style = (Style)styles["ClassicButton"], Width = 70, Height = 21 };
                var ownedWindow = new Window { Title = "DFLegacy owned control test", Content = button, Width = 70, Height = 21,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                try
                {
                    ownedWindow.Show(); ownedWindow.UpdateLayout();
                    var hit = ownedWindow.InputHitTest(new Point(35, 10)) as DependencyObject;
                    while (hit is not null && !ReferenceEquals(hit, button)) hit = VisualTreeHelper.GetParent(hit);
                    check(ReferenceEquals(hit, button), "real styled toolbar button receives mouse hit testing over its decorative children");
                }
                finally { ownedWindow.Close(); }
                var scratch = Path.Combine(Path.GetTempPath(), "dflegacy-classic-ui-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(scratch);
                try
                {
                    var store = new LauncherAccountStore(Path.Combine(scratch, "launcher.json"));
                    store.Save(new LauncherOptions { ClientPath = Path.Combine(scratch, "missing-client", "DNF.exe") });
                    var settings = new ConfigWindow(store) { Height = 360, WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        settings.Show(); settings.UpdateLayout();
                        var error = (PixelText)settings.FindName("DisplayErrorText");
                        var save = (SkinButton)settings.FindName("DisplaySaveButton");
                        var reload = (SkinButton)settings.FindName("DisplayReloadButton");
                        check(!string.IsNullOrWhiteSpace(error.Text) && !save.IsEnabled && reload.IsEnabled,
                            "missing client remains visible as an error and cannot be saved in classic settings");
                        error.Text = string.Concat(Enumerable.Repeat("无法读取客户端配置，原文件保持不变。", 16)); settings.UpdateLayout();
                        check(reload.TranslatePoint(new Point(0, reload.ActualHeight), settings).Y <= settings.ActualHeight
                            && save.TranslatePoint(new Point(0, save.ActualHeight), settings).Y <= settings.ActualHeight,
                            "long errors at 360px height cannot push reload/save below the window");
                        DependencyObject? parent = error;
                        while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
                        check(parent is ScrollViewer { ScrollableHeight: > 0 }, "long error text has a real scrollable viewport at minimum height");
                    }
                    finally { settings.Close(); }
                }
                finally { Directory.Delete(scratch, true); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Classic UI raster tests hung.");
        if (failure is not null) throw new InvalidOperationException("Classic UI raster tests failed", failure);
        Console.WriteLine("Classic UI native raster and WPF layout checks executed");
    }
}
