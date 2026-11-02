using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DFLegacy.Launcher.Controls;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Views;
using System.Net;
using System.Net.Sockets;
using System.Windows.Threading;

internal static class ClassicUiTests
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    internal static void Run(Action<bool, string> check, bool loginOnly = false, bool launchOnly = false, bool registrationOnly = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                if(launchOnly) { LaunchUiTests.Run(check); return; }
                if(registrationOnly) { RegistrationUiTests.Run(check); return; }
                LoginInputTests.Run(check);
                if (loginOnly) return;
                RegistrationUiTests.Run(check);
                UpdateUiTests.Run(check);
                ClassicMessageTests.Run(check);
                LaunchUiTests.Run(check);
                var messageCloser = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
                    (_, _) =>
                    {
                        foreach (var message in application.Windows.OfType<ClassicMessageWindow>().ToArray()) message.Close();
                    }, Dispatcher.CurrentDispatcher);
                messageCloser.Start();
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
                    var settings = new ConfigWindow(store, trackMonitor: false) { Height = 360, WindowStartupLocation = WindowStartupLocation.Manual,
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
                        var result = (PixelText)settings.FindName("DisplaySaveResultText");
                        result.Text = string.Concat(Enumerable.Repeat("保存失败，配置路径过长或文件被占用，原文件保持不变。", 16)); settings.UpdateLayout();
                        check(reload.TranslatePoint(new Point(0, reload.ActualHeight), settings).Y <= settings.ActualHeight,
                            "long SAVE failure cannot push the fixed footer outside a 360px window");
                        parent = result;
                        while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
                        check(parent is ScrollViewer { ScrollableHeight: > 0 }, "detailed save failures remain inside a scrollable viewport");
                        var tabs = (TabControl)settings.FindName("SettingsTabs");
                        foreach (TabItem tab in tabs.Items)
                        {
                            var hit = settings.InputHitTest(tab.TranslatePoint(new Point(4, 14), settings)) as DependencyObject;
                            while (hit is not null && !ReferenceEquals(hit, tab)) hit = VisualTreeHelper.GetParent(hit);
                            check(ReferenceEquals(hit, tab), "whole tab artwork receives clicks outside its header glyphs");
                        }
                        settings.FitToWorkArea(new Size(320, 240)); settings.UpdateLayout();
                        check(settings.ActualWidth <= 320 && settings.ActualHeight <= 240,
                            "settings fit a smaller monitor work area below nominal minimum dimensions");
                        var bottom = reload.TranslatePoint(new Point(reload.ActualWidth, reload.ActualHeight), settings);
                        check(bottom.X <= settings.ActualWidth && bottom.Y <= settings.ActualHeight,
                            "compact monitor fallback keeps footer actions inside actual bounds");
                    }
                    finally { settings.Close(); }
                    using var closedPort = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    closedPort.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    var oldPort = ((IPEndPoint)closedPort.LocalEndPoint!).Port;
                    using var liveEntrance = new TcpListener(IPAddress.Loopback, 0); liveEntrance.Start();
                    var livePort = ((IPEndPoint)liveEntrance.LocalEndpoint).Port;
                    var localOptions = store.Load(); localOptions.EntranceHost = "127.0.0.1"; localOptions.EntrancePort = oldPort;
                    localOptions.AccountServiceUrl = $"http://127.0.0.1:{oldPort}"; store.Save(localOptions);
                    var main = new MainWindow(store, refreshStatus: false) { WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        main.Show(); main.UpdateLayout();
                        IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
                        {
                            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                            {
                                var child = VisualTreeHelper.GetChild(root, i);
                                if (child is T found) yield return found;
                                foreach (var nested in Descendants<T>(child)) yield return nested;
                            }
                        }
                        void CheckInkCenter(PixelText label, FrameworkElement surface, string name)
                        {
                            var w = Math.Max(1, (int)Math.Ceiling(label.ActualWidth)); var h = Math.Max(1, (int)Math.Ceiling(label.ActualHeight));
                            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bitmap.Render(label);
                            var raw = Pixels(bitmap); var ink = Enumerable.Range(0, w * h).Where(i => raw[i * 4 + 3] > 0).ToArray();
                            check(ink.Length > 0, name + " contains visible glyphs");
                            if (ink.Length == 0) return;
                            var origin = label.TranslatePoint(new Point(), surface);
                            var cx = origin.X + (ink.Min(i => i % w) + ink.Max(i => i % w) + 1) / 2d;
                            var cy = origin.Y + (ink.Min(i => i / w) + ink.Max(i => i / w) + 1) / 2d;
                            if (Math.Abs(cx - surface.ActualWidth / 2) > 1 || Math.Abs(cy - surface.ActualHeight / 2) > 1)
                                Console.WriteLine($"Ink layout: {name}, surface={surface.ActualWidth}x{surface.ActualHeight}, label={w}x{h}, origin={origin}, inkCenter={cx},{cy}");
                            check(Math.Abs(cx - surface.ActualWidth / 2) <= 1 && Math.Abs(cy - surface.ActualHeight / 2) <= 1,
                                name + " actual glyph ink is centered on both axes");
                        }
                        foreach (var name in new[] { "Slide0", "Slide1", "Slide2", "Slide3", "Slide4" })
                        {
                            var slide = (SkinButton)main.FindName(name);
                            CheckInkCenter(Descendants<PixelText>(slide).Single(), slide, name);
                        }
                        foreach (var tag in Descendants<Border>(main).Where(b => b.Child is PixelText t && t.Text is "公告" or "指南" or "账号" or "设置" or "帮助"))
                            CheckInkCenter((PixelText)tag.Child, tag, "news tag " + ((PixelText)tag.Child).Text);
                        var shade = (Border)main.FindName("ModalShade"); shade.Visibility = Visibility.Visible; main.UpdateLayout();
                        var shadeImage = new RenderTargetBitmap(567, 512, 96, 96, PixelFormats.Pbgra32); shadeImage.Render(shade);
                        var shadePixels = Pixels(shadeImage);
                        check(shadePixels[3] == 0 && shadePixels[(200 * 567 + 100) * 4 + 3] > 0,
                            "modal shade preserves transparent launcher corners while dimming opaque artwork");
                        shade.Visibility = Visibility.Collapsed;
                        ((FrameworkElement)main.FindName("LoginPage")).Visibility = Visibility.Collapsed;
                        ((FrameworkElement)main.FindName("ServerPage")).Visibility = Visibility.Visible;
                        var endpoint = (PixelText)main.FindName("ServerEndpointText");
                        var details = (PixelText)main.FindName("ServerSelectionError");
                        endpoint.Text = new string('x', 500); details.Text = string.Concat(Enumerable.Repeat("无法读取服务器配置，路径内容很长。", 16));
                        main.UpdateLayout();
                        foreach (var text in new[] { endpoint, details })
                        {
                            DependencyObject? parent = text;
                            while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
                            check(parent is ScrollViewer { ScrollableHeight: > 0 }, "long server endpoint and diagnostics are readable by scrolling");
                        }
                        localOptions.EntrancePort = livePort; store.Save(localOptions); main.ReloadServerSelection(); main.UpdateLayout();
                        var confirm = (SkinButton)main.FindName("ConfirmServerButton");
                        check(!confirm.IsEnabled && ((PixelText)main.FindName("SelectedServerText")).Text.Contains("请选择"), "server page starts unselected with confirmation disabled");
                        var choiceButton = Descendants<SkinButton>(main).Single(b => b.Tag is LocalServerChoice);
                        check(((BitmapImage)choiceButton.NormalSource!).UriSource.ToString().EndsWith("classic-row0.png"), "unselected row uses original default265 art");
                        var hit = main.InputHitTest(choiceButton.TranslatePoint(new Point(3, 3), main)) as DependencyObject;
                        while (hit is not null && !ReferenceEquals(hit, choiceButton)) hit = VisualTreeHelper.GetParent(hit);
                        check(ReferenceEquals(hit, choiceButton), "whole server row artwork is a real clickable target");
                        choiceButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        void Pump(Task task)
                        {
                            var timeout = Stopwatch.StartNew();
                            while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(6))
                            { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })); Thread.Sleep(1); }
                            check(task.IsCompleted, "owned loopback status request completes");
                            if (task.IsCompleted) task.GetAwaiter().GetResult();
                        }
                        Pump(main.RefreshStatusAsync()); main.UpdateLayout();
                        check(confirm.IsEnabled && endpoint.Text.Contains(":" + livePort)
                            && ((PixelText)main.FindName("ServerText")).Text.Contains("可连接")
                            && ((PixelText)main.FindName("SelectedServerText")).Text.Contains("ms"), "selected entrance reports real TCP latency and current endpoint");
                        check(choiceButton.NormalSource == choiceButton.HoverSource && choiceButton.NormalSource == choiceButton.PressedSource,
                            "selected green art persists during hover and press after health update");
                        choiceButton.Focus(); main.UpdateLayout();
                        var renderedArt = (Image)choiceButton.Template.FindName("Artwork", choiceButton);
                        check(ReferenceEquals(renderedArt.Source, choiceButton.NormalSource)
                            && ((BitmapImage)renderedArt.Source).UriSource.ToString().EndsWith("classic-row2.png"),
                            "actual focused WPF template keeps selected264 image");
                        using var slowApi = new TcpListener(IPAddress.Loopback, 0); slowApi.Start();
                        using var fastApi = new TcpListener(IPAddress.Loopback, 0); fastApi.Start();
                        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var slowReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        async Task Serve(TcpListener listener, bool slow)
                        {
                            using var client = await listener.AcceptTcpClientAsync();
                            var stream = client.GetStream(); var buffer = new byte[1]; await stream.ReadExactlyAsync(buffer);
                            if (slow) { slowReceived.SetResult(); await releaseSlow.Task; }
                            try { await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK")); }
                            catch (IOException) { }
                        }
                        var slowServe = Serve(slowApi, true); var fastServe = Serve(fastApi, false);
                        localOptions.Accounts = [new() { Account = "first", Password = "saved-first" }, new() { Account = "second", Password = "saved-second" }];
                        localOptions.AccountIndex = 1;
                        localOptions.Servers = Enumerable.Range(0, 36).Select(i => new LauncherServerProfile {
                            Id = "region-" + i, Name = i == 0 ? new string('长', 120) + "🙂服务器" : "服务器 " + i,
                            Region = i < 32 ? "telecom" : "netcom", Host = "127.0.0.1", Port = i == 0 ? oldPort : livePort,
                            AccountServicePort = ((IPEndPoint)(i == 0 ? slowApi : fastApi).LocalEndpoint).Port }).ToList();
                        store.Save(localOptions); var unchanged = File.ReadAllBytes(Path.Combine(scratch, "launcher.json"));
                        main.ReloadServerSelection(); main.UpdateLayout();
                        var buttons = Descendants<SkinButton>(main).Where(b => b.Tag is LocalServerChoice).ToArray();
                        check(buttons.Length == 36, "both regions render every configured choice");
                        var telecom = (ScrollViewer)main.FindName("TelecomScroll"); var netcom = (ScrollViewer)main.FindName("NetcomScroll");
                        check(telecom.ScrollableHeight > 0 && netcom.ExtentWidth <= netcom.ViewportWidth + 1, "four-column overflow scrolls within its region");
                        check(buttons[0].ToolTip?.ToString()?.Contains(localOptions.Servers[0].Name) == true,
                            "long Unicode server name has complete tooltip");
                        check(buttons.Take(4).Select(b => b.TranslatePoint(new Point(), telecom).Y).Distinct().Count() == 1
                            && buttons[4].TranslatePoint(new Point(), telecom).Y > buttons[0].TranslatePoint(new Point(), telecom).Y,
                            "server entries use four columns before wrapping");
                        buttons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        var staleTask = main.RefreshStatusAsync(); Pump(slowReceived.Task);
                        check(buttons[0].NormalSource == buttons[0].HoverSource
                            && Descendants<System.Windows.Shapes.Ellipse>((DependencyObject)buttons[0].Parent).Single().Fill == Brushes.IndianRed,
                            "unreachable selected server keeps green selection with an independent red health lamp");
                        buttons[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        Pump(main.RefreshStatusAsync()); releaseSlow.SetResult(); Pump(staleTask); Pump(slowServe); Pump(fastServe);
                        check(((PixelText)main.FindName("SelectedServerText")).Text.Contains("服务器 1")
                            && ((PixelText)main.FindName("ServerText")).Text.Contains("可连接")
                            && ((PixelText)main.FindName("ApiText")).ToolTip?.ToString()?.Contains(":" + ((IPEndPoint)fastApi.LocalEndpoint).Port) == true,
                            "stale probe and account results cannot overwrite newer selection");
                        main.UpdateLayout();
                        var footer = confirm.TranslatePoint(new Point(confirm.ActualWidth, confirm.ActualHeight), main);
                        check(footer.Y <= main.ActualHeight && footer.X <= main.ActualWidth && confirm.IsEnabled,
                            "overflow and long names keep confirmation reachable");
                        var actual = new RenderTargetBitmap(567, 512, 96, 96, PixelFormats.Pbgra32); actual.Render(main);
                        var evidence = Environment.GetEnvironmentVariable("DFLEGACY_UI_EVIDENCE");
                        if (!string.IsNullOrEmpty(evidence)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(actual)); using var file = File.Create(evidence); encoder.Save(file); }
                        var accountInput = (TextBox)main.FindName("AccountText"); var passwordInput = (PasswordBox)main.FindName("PasswordText");
                        accountInput.Text = "typed-account"; passwordInput.Password = "typed-password";
                        confirm.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        check(accountInput.Text == "typed-account" && passwordInput.Password == "typed-password", "confirmation preserves typed credentials");
                        check(File.ReadAllBytes(Path.Combine(scratch, "launcher.json")).SequenceEqual(unchanged), "UI confirm never writes config bytes");
                        void InvokeEvent(object target, string method) => target.GetType().GetMethod(method,
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, [target, new RoutedEventArgs()]);
                        void DialogRoundtrip(string method, Action<Window> action)
                        {
                            Exception? error = null;
                            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            {
                                var dialog = main.OwnedWindows.Cast<Window>().Single();
                                try { action(dialog); }
                                catch (Exception ex) { error = ex; }
                                finally { dialog.Close(); }
                            }));
                            InvokeEvent(main, method);
                            if (error is not null) throw error;
                        }
                        var accountList = (ListBox)main.FindName("AccountList");
                        accountList.SelectedIndex = 0;
                        accountInput.Text = "typed-account"; passwordInput.Password = "typed-password";
                        void AssertRuntimeB(string operation)
                        {
                            var runtime = (LauncherOptions)typeof(MainWindow).GetField("_options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
                            var api = typeof(MainWindow).GetField("_account", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
                            var http = (System.Net.Http.HttpClient)api.GetType().GetField("_http", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(api)!;
                            check(runtime.EntrancePort == livePort && runtime.SelectedServerId == "region-1"
                                && runtime.ResolveAccountServiceUrl().TrimEnd('/') == http.BaseAddress!.ToString().TrimEnd('/')
                                && http.BaseAddress.Port == ((IPEndPoint)fastApi.LocalEndpoint).Port,
                                operation + " preserves confirmed B entrance and matching B account API");
                            check(accountInput.Text == "typed-account" && passwordInput.Password == "typed-password"
                                && accountList.SelectedItem is MainWindow.AccountItem { Account: "first" },
                                operation + " preserves typed credentials and selected account identity");
                        }
                        var updateOptions = (LauncherOptions)typeof(MainWindow).GetField("_options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
                        check(main.FindName("CheckUpdatesMenu") is MenuItem, "More Services exposes manual update check");
                        // Invalid repository stops before network but exercises the actual menu/error path.
                        updateOptions.UpdateRepository = "invalid-fixture";
                        InvokeEvent(main,"CheckUpdates_Click");
                        updateOptions.UpdateRepository = "firekula/DFLegacy";
                        AssertRuntimeB("manual update failure");
                        DialogRoundtrip("AccountEditBtn_Click", _ => { }); AssertRuntimeB("cancel account management");
                        DialogRoundtrip("AccountEditBtn_Click", dialog =>
                        {
                            var grid = (DataGrid)dialog.FindName("Grid"); grid.SelectedIndex = 0;
                            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            {
                                var fields = dialog.OwnedWindows.Cast<Window>().Single();
                                ((TextBox)fields.FindName("DisplayNameBox")).Text = "edited display";
                                ((PasswordBox)fields.FindName("PasswordBox")).Password = "edited-password";
                                InvokeEvent(fields, "Ok_Click");
                            }));
                            InvokeEvent(dialog, "Edit_Click");
                        });
                        check(store.Load().Accounts[0].Password == "edited-password", "real account edit dialog persists edited account");
                        AssertRuntimeB("edit account management");
                        DialogRoundtrip("ConfigBtn_Click", _ => { }); AssertRuntimeB("cancel display settings");
                        DialogRoundtrip("ConfigBtn_Click", dialog =>
                        {
                            ((ComboBox)dialog.FindName("ThemeBox")).SelectedValue = "dark";
                            InvokeEvent(dialog, "Save_Click");
                        }); AssertRuntimeB("save unrelated settings");
                        check(store.Load().Theme == "dark", "real settings save roundtrip persists unrelated setting");
                        DialogRoundtrip("AccountEditBtn_Click", _ => store.Update(o => o.Servers[1].Port = oldPort));
                        check(((FrameworkElement)main.FindName("ServerPage")).Visibility == Visibility.Visible && !confirm.IsEnabled
                            && endpoint.Text.Contains("重新选择"), "changed confirmed profile explicitly requires reselection after account reload");
                        check(accountInput.Text == "typed-account" && passwordInput.Password == "typed-password", "profile invalidation preserves typed credentials");
                        void ConfirmB()
                        {
                            store.Update(o => o.Servers = localOptions.Servers);
                            main.ReloadServerSelection(); main.UpdateLayout();
                            Descendants<SkinButton>(main).Single(b => b.Tag is LocalServerChoice { Id: "region-1" }).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            confirm.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        }
                        ConfirmB(); AssertRuntimeB("reconfirmed profile");
                        DialogRoundtrip("ConfigBtn_Click", _ => store.Update(o => o.Servers.RemoveAt(1)));
                        check(((FrameworkElement)main.FindName("ServerPage")).Visibility == Visibility.Visible && !confirm.IsEnabled
                            && endpoint.Text.Contains("重新选择"), "deleted confirmed profile explicitly requires reselection after settings reload");
                        InvokeEvent(main, "BackToLogin_Click");
                        InvokeEvent(main, "EnterGame_Click");
                        check(((FrameworkElement)main.FindName("ServerPage")).Visibility == Visibility.Visible,
                            "invalid runtime target routes attempted launch back to explicit selection");
                        Descendants<SkinButton>(main).First(b => b.Tag is LocalServerChoice).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        check(confirm.IsEnabled, "reselection page remains usable after returning to login with an invalid target");
                        ConfirmB(); AssertRuntimeB("restored and reconfirmed profile");
                        File.WriteAllText(Path.Combine(scratch, "launcher.json"), "{broken");
                        main.ReloadServerSelection(); main.UpdateLayout();
                        check(endpoint.Text.Contains("无法") && !confirm.IsEnabled,
                            "corrupt config shows failure on the visible first footer line without requiring a scroll");
                        check(File.ReadAllText(Path.Combine(scratch, "launcher.json")) == "{broken", "UI reload preserves corrupt bytes");
                        store.Save(localOptions);
                        var closeReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var closeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        async Task CloseServer()
                        {
                            using var client = await slowApi.AcceptTcpClientAsync();
                            var buffer = new byte[1]; await client.GetStream().ReadExactlyAsync(buffer);
                            closeReceived.SetResult(); await closeRelease.Task;
                        }
                        var closeServer = CloseServer();
                        ((FrameworkElement)main.FindName("LoginPage")).Visibility = Visibility.Collapsed;
                        ((FrameworkElement)main.FindName("ServerPage")).Visibility = Visibility.Visible;
                        main.ReloadServerSelection(); main.UpdateLayout();
                        check(!confirm.IsEnabled, "re-entering the server page clears prior highlight and requires a new choice");
                        Descendants<SkinButton>(main).First(b => b.Tag is LocalServerChoice).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        var closingProbe = main.RefreshStatusAsync(); Pump(closeReceived.Task);
                        var closeTimer = Stopwatch.StartNew(); main.Close(); Pump(closingProbe); closeRelease.SetResult(); Pump(closeServer);
                        check(closeTimer.Elapsed < TimeSpan.FromSeconds(1), "closing the owned window cancels outstanding account health immediately");
                    }
                    finally { main.Close(); }
                }
                finally { Directory.Delete(scratch, true); }
            }
            catch (Exception ex) { failure = ex; }
            finally { application.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Classic UI raster tests hung.");
        if (failure is not null) throw new InvalidOperationException("Classic UI raster tests failed", failure);
        Console.WriteLine("Classic UI native raster and WPF layout checks executed");
    }
}
