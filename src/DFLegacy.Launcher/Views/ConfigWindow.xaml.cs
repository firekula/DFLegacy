using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;

namespace DFLegacy.Launcher.Views;

// 构造函数用 internal：签名里的 LauncherAccountStore / LauncherOptions 是 internal（CS0051）。
public partial class ConfigWindow : System.Windows.Window
{
    private readonly LauncherAccountStore _store;
    private LauncherOptions _options;
    private ClientDisplaySnapshot? _display;
    private bool _loadingDisplay = true;
    private readonly bool _trackMonitor;
    private bool _fitQueued;
    private double? _preferredHeight;
    private sealed record ResolutionChoice(int Width, int Height, string Label)
    {
        public override string ToString() => Label;
    }

    internal ConfigWindow(LauncherAccountStore store, bool trackMonitor = true)
    {
        InitializeComponent();
        _store = store;
        _trackMonitor = trackMonitor;
        _options = store.Load();
        HostBox.Text = _options.EntranceHost;
        PortBox.Text = _options.EntrancePort.ToString();
        AccountPortBox.Text = _options.AccountServicePort.ToString();
        AccountUrlBox.Text = _options.AccountServiceUrl;
        ClientPathBox.Text = _options.ClientPath;
        StartServerBox.IsChecked = _options.StartServer;
        RelayEnabledBox.IsChecked = _options.Relay.Enabled;
        TcpPortsBox.Text = _options.Relay.TcpPorts ?? string.Empty;
        // 主题 / Preference 用 SelectedValue（XAML 已配 SelectedValuePath="Content"）：
        // 非可编辑 ComboBox 的 .Text 在条目为 ComboBoxItem 包装时可能为空。
        PreferenceBox.SelectedValue = _options.Relay.Preference;
        ThemeBox.SelectedValue = _options.Theme;
        LoadDiagnostics();

        // 三处任一改动都刷新"账号服务实际地址"提示。
        HostBox.TextChanged += (_, _) => UpdateAccountUrlHint();
        AccountPortBox.TextChanged += (_, _) => UpdateAccountUrlHint();
        AccountUrlBox.TextChanged += (_, _) => UpdateAccountUrlHint();
        ClientPathBox.TextChanged += (_, _) => UpdateDisplayAvailability();
        UpdateAccountUrlHint();
        if (_trackMonitor)
        {
            SourceInitialized += (_, _) => FitToMonitor(useOwner: true);
            ContentRendered += (_, _) => FitToMonitor(useOwner: false);
            LocationChanged += (_, _) => QueueMonitorFit();
        }
        Loaded += (_, _) => UpdateDisplayAvailability();
        LoadDisplay();
    }

    internal void FitToWorkArea(Size logicalArea)
    {
        _preferredHeight ??= Height;
        var availableWidth = Math.Max(1, logicalArea.Width - 16);
        var availableHeight = Math.Max(1, logicalArea.Height - 16);
        var scale = Math.Min(1, Math.Min(availableWidth / 480, availableHeight / 360));
        SettingsSurface.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        MinWidth = MinHeight = 0;
        MaxWidth = availableWidth; MaxHeight = availableHeight;
        MinWidth = 480 * scale; MinHeight = 360 * scale;
        Width = MinWidth; Height = Math.Min(_preferredHeight.Value * scale, availableHeight);
    }

    private void FitToMonitor(bool useOwner)
    {
        var area = WindowWorkArea.Read(useOwner ? Owner ?? this : this);
        var dpi = VisualTreeHelper.GetDpi(this);
        FitToWorkArea(area is { } physical ? new Size(physical.Width / dpi.DpiScaleX, physical.Height / dpi.DpiScaleY) : SystemParameters.WorkArea.Size);
        UpdateLayout();
        if (area is { } bounds) WindowWorkArea.Clamp(this, bounds);
    }

    private void QueueMonitorFit()
    {
        if (!_trackMonitor || _fitQueued || !IsLoaded) return;
        _fitQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { _fitQueued = false; if (IsLoaded) FitToMonitor(useOwner: false); }));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    { base.OnDpiChanged(oldDpi, newDpi); QueueMonitorFit(); }

    /// <summary>实时展示账号服务「实际会用」的地址：域名跟随入口，端口可改，覆盖值优先。</summary>
    private void UpdateAccountUrlHint()
    {
        var host = string.IsNullOrWhiteSpace(HostBox.Text) ? "(未填域名)" : HostBox.Text.Trim();
        var port = string.IsNullOrWhiteSpace(AccountPortBox.Text) ? "?" : AccountPortBox.Text.Trim();
        var url = string.IsNullOrWhiteSpace(AccountUrlBox.Text)
            ? $"http://{host}:{port}"
            : AccountUrlBox.Text.Trim();
        AccountUrlHint.Text = $"账号服务地址：{url}";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "DNF.exe|DNF.exe|可执行文件|*.exe" };
        if (dlg.ShowDialog() == true) ClientPathBox.Text = dlg.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        { ClassicMessage.Show(this, "入口端口须为 1-65535。"); return; }
        if (!int.TryParse(AccountPortBox.Text, out var accountPort) || accountPort is < 1 or > 65535)
        { ClassicMessage.Show(this, "账号服务端口须为 1-65535。"); return; }
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        { ClassicMessage.Show(this, "入口 Host 不能为空。"); return; }

        try
        {
            var candidate = _store.Load();
            candidate.EntranceHost = HostBox.Text.Trim();
            candidate.EntrancePort = port;
            candidate.AccountServicePort = accountPort;
            // 覆盖地址可以为空（为空即按 域名:端口 自动拼）。
            candidate.AccountServiceUrl = AccountUrlBox.Text.Trim();
            candidate.ClientPath = ClientPathBox.Text.Trim();
            candidate.StartServer = StartServerBox.IsChecked == true;
            candidate.Relay.Enabled = RelayEnabledBox.IsChecked == true;
            candidate.Relay.TcpPorts = string.IsNullOrWhiteSpace(TcpPortsBox.Text) ? null : TcpPortsBox.Text.Trim();
            candidate.Relay.Preference = PreferenceBox.SelectedValue as string ?? "ipv6";
            candidate.Theme = ThemeBox.SelectedValue as string ?? "auto";
            _store.Save(candidate);
            _options = candidate;
        }
        catch (Exception ex)
        {
            _display = null;
            // 落盘失败：不能应用主题也不能提示成功（内存不能看起来"已保存"）。
            // 按磁盘重同步内存（磁盘为权威，Task 6 同一策略）；表单字段保留用户输入，
            // 排除占用/权限问题后可直接再点保存，无需重新输入。
            ClassicMessage.Show(this, $"保存配置失败：{ex.Message}");
            try
            {
                _options = _store.Load();
            }
            catch (Exception loadEx)
            {
                // 磁盘也读不回来时只能提示；内存保留当前态，等下一次操作再试。
                ClassicMessage.Show(this, $"重新读取配置失败：{loadEx.Message}");
            }
            UpdateDisplayAvailability();
            return;
        }
        App.ApplyTheme(_options.Theme);
        LoadDisplay();
        ClassicMessage.Show(this, "已保存。");
    }

    private void LoadDisplay()
    {
        _loadingDisplay = true;
        _display = null;
        DisplaySaveResultText.Text = "";
        DisplayFooterText.Text = "下次启动后生效";
        DisplayErrorText.Text = "";
        try
        {
            _options = _store.Load();
            _display = ClientDisplayConfiguration.Load(_options.ClientPath);
            DisplayTargetText.Text = _display.ClientPath;
            var choices = ClientDisplayConfiguration.Presets.Select(p => new ResolutionChoice(p.Width, p.Height, p.ToString())).ToList();
            if (!choices.Any(p => p.Width == _display.Width && p.Height == _display.Height))
                choices.Add(new(_display.Width, _display.Height, $"当前自定义：{_display.Width} × {_display.Height}（旧配置）"));
            ResolutionBox.ItemsSource = choices;
            ResolutionBox.SelectedItem = choices.First(p => p.Width == _display.Width && p.Height == _display.Height);
            PresentationBox.SelectedValue = _display.PresentationFilter;
            DisplayRuntimeText.Text = ClientRenderDiagnostic.DescribeLatest(_display.ClientPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            DisplayTargetText.Text = _options.ClientPath;
            DisplayErrorText.Text = "无法读取显示配置：" + ex.Message;
        }
        finally { _loadingDisplay = false; UpdateDisplayAvailability(); }
    }

    private void DisplaySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingDisplay && DisplayEditorPanel is not null) UpdateDisplayAvailability();
    }

    private void UpdateDisplayAvailability()
    {
        if (DisplayEditorPanel is null || _loadingDisplay) return;
        var samePath = ClientDisplayConfiguration.SameClientPath(_options.ClientPath, ClientPathBox.Text);
        if (_display is not null && samePath)
        {
            try
            {
                using var authority = _store.AcquireReadLease();
                ClientDisplayConfiguration.RequireCurrentTarget(authority.Options.ClientPath, _options.ClientPath, ClientPathBox.Text, _display);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
                _display = null;
                DisplayErrorText.Text = "无法确认已保存的客户端路径，请重新打开设置或先保存连接配置：" + ex.Message;
                DisplaySaveResultText.Text = "";
            }
        }
        var ready = _display is not null && samePath;
        DisplayEditorPanel.IsEnabled = ready;
        DisplayReloadButton.IsEnabled = samePath;
        var choice = ResolutionBox.SelectedItem as ResolutionChoice;
        var mode = PresentationBox.SelectedValue as string;
        var supported = choice is not null && (mode == "legacy" || ClientDisplayConfiguration.Presets.Any(p => p.Width == choice.Width && p.Height == choice.Height));
        DisplaySaveButton.IsEnabled = ready && supported && mode is not null;
        DisplayWarningText.Text = !samePath ? "客户端路径已修改，请先在连接页保存路径，再修改显示设置。"
            : ready && !supported ? "此自定义尺寸只保留旧版兼容行为。使用增强画质前，请选择列表中的 4:3 分辨率。" : "";
        DisplayDescriptionText.Text = mode switch
        {
            "sharp" => "保留像素边缘，减轻非整数放大的生硬感。",
            "point" => "保留像素边缘；非整数倍尺寸下，笔画可能粗细不均。",
            "linear" => "边缘更平滑，但文字和像素图可能偏软。",
            _ => "保留原呈现方式，并沿用已有的纹理过滤设置。"
        };
        if (ready && supported && choice is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var work = WindowWorkArea.Read(this);
            if (choice.Width + 32 > (work?.Width ?? SystemParameters.WorkArea.Width * dpi.DpiScaleX)
                || choice.Height + 48 > (work?.Height ?? SystemParameters.WorkArea.Height * dpi.DpiScaleY))
                DisplayWarningText.Text = "此窗口可能超出当前屏幕可用区域；保存时仍会保留你的选择。";
        }
    }

    private void DisplayReload_Click(object sender, RoutedEventArgs e) => LoadDisplay();

    private void DisplaySave_Click(object sender, RoutedEventArgs e)
    {
        if (_display is null || !ClientDisplayConfiguration.SameClientPath(_options.ClientPath, ClientPathBox.Text)
            || ResolutionBox.SelectedItem is not ResolutionChoice choice || PresentationBox.SelectedValue is not string mode)
        { UpdateDisplayAvailability(); return; }
        try
        {
            _display = ClientDisplayConfiguration.SaveWithAuthority(_store, _display, _options.ClientPath, ClientPathBox.Text, choice.Width, choice.Height, mode);
            DisplaySaveResultText.Text = "";
            DisplayFooterText.Text = "已保存 · 下次启动后生效";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            DisplaySaveResultText.Text = "保存失败，原配置未被覆盖：" + ex.Message;
            DisplayFooterText.Text = "保存失败，请查看提示";
            DisplayScroll.ScrollToTop();
            _display = null;
            UpdateDisplayAvailability();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void LoadDiagnostics()
    {
        var settings=Program.UserDiagnosticSettings;
        try { var preference=settings.Read();DiagnosticEnabledBox.IsChecked=preference.Enabled;DiagnosticSmallDumpsBox.IsChecked=preference.LocalSmallDumps; }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { DiagnosticEnabledBox.IsEnabled=false; }
        string? clientRoot=null;
        try {clientRoot=Path.GetDirectoryName(Program.ResolveClientPath(_options.ClientPath));}
        catch(Exception error) when(error is IOException or ArgumentException or NotSupportedException) { }
        DiagnosticStatusText.Text=settings.Describe(string.IsNullOrWhiteSpace(_options.Diagnostics.ServiceUrl)?_options.ResolveAccountServiceUrl():_options.Diagnostics.ServiceUrl,clientRoot);
    }
    private void DiagnosticSave_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            Program.UserDiagnosticSettings.SetEnabled(DiagnosticEnabledBox.IsChecked==true);
            Program.UserDiagnosticSettings.SetLocalSmallDumps(DiagnosticSmallDumpsBox.IsChecked==true);
            Diagnostics.AutomaticDiagnostics.Configure(_options);LoadDiagnostics();
        }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { ClassicMessage.Show(this,"保存诊断设置失败："+error.Message); }
    }
    internal void ShowConnectionPage() => SettingsTabs.SelectedIndex = 0;

    private void Chrome_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.GetPosition(this).Y < 93) DragMove();
    }
}
