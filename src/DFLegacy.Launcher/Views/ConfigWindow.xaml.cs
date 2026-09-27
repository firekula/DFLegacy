using System.Windows;
using HandyControl.Controls;
using Microsoft.Win32;

namespace DFLegacy.Launcher.Views;

// Window 必须写全限定名：using HandyControl.Controls 后 Window 与 System.Windows.Window 歧义（CS0104）。
// 构造函数用 internal：签名里的 LauncherAccountStore / LauncherOptions 是 internal（CS0051）。
public partial class ConfigWindow : System.Windows.Window
{
    private readonly LauncherAccountStore _store;
    private LauncherOptions _options;

    internal ConfigWindow(LauncherAccountStore store)
    {
        InitializeComponent();
        _store = store;
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

        // 三处任一改动都刷新"账号服务实际地址"提示。
        HostBox.TextChanged += (_, _) => UpdateAccountUrlHint();
        AccountPortBox.TextChanged += (_, _) => UpdateAccountUrlHint();
        AccountUrlBox.TextChanged += (_, _) => UpdateAccountUrlHint();
        UpdateAccountUrlHint();
    }

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
        { Growl.Info("入口端口须为 1-65535。"); return; }
        if (!int.TryParse(AccountPortBox.Text, out var accountPort) || accountPort is < 1 or > 65535)
        { Growl.Info("账号服务端口须为 1-65535。"); return; }
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        { Growl.Info("入口 Host 不能为空。"); return; }

        _options.EntranceHost = HostBox.Text.Trim();
        _options.EntrancePort = port;
        _options.AccountServicePort = accountPort;
        // 覆盖地址可以为空（为空即按 域名:端口 自动拼）。
        _options.AccountServiceUrl = AccountUrlBox.Text.Trim();
        _options.ClientPath = ClientPathBox.Text.Trim();
        _options.StartServer = StartServerBox.IsChecked == true;
        _options.Relay.Enabled = RelayEnabledBox.IsChecked == true;
        _options.Relay.TcpPorts = string.IsNullOrWhiteSpace(TcpPortsBox.Text) ? null : TcpPortsBox.Text.Trim();
        _options.Relay.Preference = PreferenceBox.SelectedValue as string ?? "ipv6";
        _options.Theme = ThemeBox.SelectedValue as string ?? "auto";
        try
        {
            _store.Save(_options);
        }
        catch (Exception ex)
        {
            // 落盘失败：不能应用主题也不能提示成功（内存不能看起来"已保存"）。
            // 按磁盘重同步内存（磁盘为权威，Task 6 同一策略）；表单字段保留用户输入，
            // 排除占用/权限问题后可直接再点保存，无需重新输入。
            Growl.Warning($"保存配置失败：{ex.Message}");
            try
            {
                _options = _store.Load();
            }
            catch (Exception loadEx)
            {
                // 磁盘也读不回来时只能提示；内存保留当前态，等下一次操作再试。
                Growl.Warning($"重新读取配置失败：{loadEx.Message}");
            }
            return;
        }
        App.ApplyTheme(_options.Theme);
        Growl.Success("已保存。");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
