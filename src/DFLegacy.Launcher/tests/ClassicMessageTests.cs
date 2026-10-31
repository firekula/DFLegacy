using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Controls;

internal static class ClassicMessageTests
{
    internal static void Run(Action<bool, string> check)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "DFLegacy.Launcher"))) root = root.Parent;
        if (root is not null)
            foreach (var source in Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "DFLegacy.Launcher"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar)))
                check(!System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(source), @"\bGrowl\s*\."),
                    "normal launcher notifications avoid generic Growl: " + Path.GetFileName(source));
        var assembly = typeof(MainWindow).Assembly;
        var type = assembly.GetType("DFLegacy.Launcher.Views.ClassicMessageWindow");
        var facade = assembly.GetType("DFLegacy.Launcher.ClassicMessage");
        check(type is not null && facade is not null, "original message window and shared Show API exist");
        if (type is null || facade is null) return;
        Window Create(string message) => (Window)Activator.CreateInstance(type, message)!;
        void Key(Window window, UIElement target, Key key)
        {
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        var owner = new Window { Width = 567, Height = 512, Left = 100, Top = 100,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        owner.Show();
        try
        {
            foreach(var accept in new[]{false,true})
            {
                var migration=new DFLegacy.Launcher.Views.ClassicMessageWindow("已校验原版备份；确认后备份旧补丁并恢复。取消阻止游戏启动。",true,"取消")
                    {Owner=owner,ShowActivated=false,ShowInTaskbar=false};
                migration.Show(); migration.UpdateLayout();
                check(!migration.Accepted,"migration requires explicit acceptance");
                var grid=(Grid)migration.Content;
                check(grid.Children.OfType<SkinButton>().Any(b=>Equals(b.Content,"取消")),"migration has readable explicit cancel action");
                if(accept) ((SkinButton)migration.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                else Key(migration,migration,System.Windows.Input.Key.Escape);
                check(migration.Accepted==accept,"classic migration acceptance/cancellation is explicit");
                migration.Close();
            }
            foreach (var key in new[] { System.Windows.Input.Key.Enter, System.Windows.Input.Key.Escape })
            {
                var window = Create("欢迎来到阿拉德大陆。\n选择服务器，输入账号和密码，\n开始冒险。");
                window.Owner = owner; window.ShowActivated = false; window.ShowInTaskbar = false;
                try
                {
                    window.Show(); window.UpdateLayout();
                    check(window.ActualWidth == 286 && window.ActualHeight == 170 && window.ResizeMode == ResizeMode.NoResize,
                        "message retains native 286x170 layout");
                    check(window.AllowsTransparency && window.Background is SolidColorBrush { Color.A: 0 },
                        "message window has no rectangular backdrop outside artwork");
                    check(Math.Abs(window.Left - (owner.Left + (owner.ActualWidth - 286) / 2)) <= 1
                        && Math.Abs(window.Top - (owner.Top + (owner.ActualHeight - 170) / 2)) <= 1,
                        "message is centered on its owning window");
                    var scroll = (ScrollViewer)window.FindName("MessageScroll");
                    var label = (PixelText)window.FindName("MessageText");
                    var confirm = (SkinButton)window.FindName("ConfirmButton");
                    check(label.Text.Contains("欢迎") && label.OutlineThickness == 1 && label.FontSize == 13,
                        "message uses readable original monochrome outlined pixel text");
                    check(AutomationProperties.GetName(confirm) == "确认" && AutomationProperties.GetName(window) == "提示信息",
                        "message and confirmation expose accessible labels");
                    check(confirm.ActualWidth == 80 && confirm.ActualHeight == 29, "original confirmation retains its full 80x29 target");
                    foreach (var point in new[] { new Point(1,1), new Point(78,1), new Point(1,27), new Point(78,27), new Point(40,14) })
                    {
                        DependencyObject? hit = window.InputHitTest(confirm.TranslatePoint(point, window)) as DependencyObject;
                        while (hit is not null && !ReferenceEquals(hit, confirm)) hit = VisualTreeHelper.GetParent(hit);
                        check(ReferenceEquals(hit, confirm), "confirmation receives actual WPF hits across its entire artwork");
                    }
                    var render = new RenderTargetBitmap(286,170,96,96,PixelFormats.Pbgra32); render.Render(window);
                    var pixels = new byte[286 * 170 * 4]; render.CopyPixels(pixels,286 * 4,0);
                    check(pixels[3] == 0 && pixels[(90 * 286 + 143) * 4 + 3] == 255,
                        "actual message rendering preserves transparent corners and opaque stone interior");
                    var evidence = Environment.GetEnvironmentVariable("DFLEGACY_MESSAGE_EVIDENCE");
                    if (!string.IsNullOrEmpty(evidence))
                    {
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                        using var file = File.Create(evidence); encoder.Save(file);
                    }
                    Key(window, scroll, key);
                    check(!window.IsVisible, key + " closes the real message from its scroll content");
                }
                finally { window.Close(); }
            }
            var longMessage = new string('界', 4095) + "🙂\n" + string.Join("\n", Enumerable.Range(1,320).Select(i => $"第{i}行：连接失败，路径含中文🙂和很长的说明。")) + "\n最后一行 END";
            var longWindow = Create(longMessage); longWindow.Owner = owner; longWindow.ShowActivated = false; longWindow.ShowInTaskbar = false;
            try
            {
                longWindow.Show(); longWindow.UpdateLayout();
                var scroll = (ScrollViewer)longWindow.FindName("MessageScroll");
                var label = (PixelText)longWindow.FindName("MessageText");
                var confirm = (SkinButton)longWindow.FindName("ConfirmButton");
                IEnumerable<PixelText> MessageBlocks(DependencyObject root)
                {
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                    {
                        var child = VisualTreeHelper.GetChild(root, i);
                        if (child is PixelText text) yield return text;
                        foreach (var nested in MessageBlocks(child)) yield return nested;
                    }
                }
                var blocks = MessageBlocks(scroll).ToArray();
                check(scroll.ScrollableHeight > 100 && scroll.ViewportHeight > 0
                    && string.Concat(blocks.Select(b => b.Text)) == longMessage,
                    "long Unicode multiline messages retain every line in a real scroll viewport");
                check(scroll.ExtentHeight >= 320 * 18 && blocks.All(b => b.Text.Length <= 4096)
                    && blocks.All(b => b.Text.Length == 0 || (!char.IsHighSurrogate(b.Text[^1]) && !char.IsLowSurrogate(b.Text[0]))),
                    "oversized diagnostics are fully laid out without truncation or split Unicode pairs");
                scroll.ScrollToEnd(); longWindow.UpdateLayout();
                var tail = blocks[^1];
                var tailBottom = tail.TranslatePoint(new Point(0, tail.ActualHeight), scroll).Y;
                check(tail.Text.EndsWith("最后一行 END") && tailBottom > 0 && tailBottom <= scroll.ActualHeight + 1,
                    "final diagnostic line is inside the viewport after scrolling to the end");
                check(scroll.VerticalOffset > 0 && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1,
                    "long message can scroll to its final line");
                check(confirm.TranslatePoint(new Point(0,confirm.ActualHeight), longWindow).Y <= 170,
                    "scrolling long content keeps confirmation inside the fixed panel");
                confirm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                check(!longWindow.IsVisible, "confirmation actually closes the long message window");
            }
            finally { longWindow.Close(); }
            bool observed = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var message = owner.OwnedWindows.Cast<Window>().Single();
                observed = type.IsInstanceOfType(message) && ((PixelText)message.FindName("MessageText")).Text == "共用入口";
                message.Close();
            }));
            facade.GetMethod("Show", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null,[owner,"共用入口"]);
            check(observed && owner.IsEnabled, "shared Show API uses an owned modal and restores its owner");
        }
        finally { owner.Close(); }
    }
}
