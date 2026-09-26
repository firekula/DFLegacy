using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DFLegacy.AccountClient;
using HandyControl.Controls;

namespace DFLegacy.Launcher;

public partial class MainWindow : System.Windows.Window
{
    private readonly LauncherAccountStore _store = new(Path.Combine(AppContext.BaseDirectory, "launcher.json"));
    private readonly AccountServiceClient _account;
    private readonly DispatcherTimer _statusTimer;
    private LauncherOptions _options;

    public MainWindow()
    {
        InitializeComponent();
        _options = _store.Load();
        _account = new AccountServiceClient(_options.AccountServiceUrl);
        RefreshAccounts();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();
        _ = RefreshStatusAsync();
    }

    private void RefreshAccounts()
    {
        AccountBox.ItemsSource = _options.Accounts
            .Select(a => new AccountItem(a.DisplayName ?? a.Account, a.Account)).ToList();
        if (_options.Accounts.Count > 0)
            AccountBox.SelectedIndex = Math.Clamp(_options.AccountIndex, 0, _options.Accounts.Count - 1);
    }

    private LauncherAccountProfile SelectedProfile() =>
        _options.Accounts.Count == 0
            ? new LauncherAccountProfile { Account = _options.Account, Password = _options.Password }
            : _options.Accounts[AccountBox.SelectedIndex >= 0 ? AccountBox.SelectedIndex : 0];

    private void AccountBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AccountBox.SelectedIndex < 0 || _options.Accounts.Count == 0) return;
        var p = _options.Accounts[AccountBox.SelectedIndex];
        PasswordBox.Password = p.RememberPassword ? p.Password : string.Empty;
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var ok = await _account.CheckHealthAsync();
            StatusDot.Fill = ok ? Brushes.Green : Brushes.IndianRed;
            StatusText.Text = ok ? "服务端在线" : "服务端不可达";
        }
        catch (Exception)
        {
            // CheckHealthAsync 只捕获 HttpRequestException；超时会以 TaskCanceledException
            // 冒出。DispatcherTimer 的 Tick 是 async void，未处理异常会拖垮整个应用，
            // 所以任何失败都按离线处理。
            StatusDot.Fill = Brushes.IndianRed;
            StatusText.Text = "服务端不可达";
        }
    }

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile();
        profile.Password = PasswordBox.Password;
        try
        {
            Hide();
            await LaunchFlow.RunAsync(_options, profile);
            Close();
        }
        catch (Exception ex)
        {
            Show();
            Growl.Info($"启动失败：{ex.Message}");
        }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();
    private void RegisterBtn_Click(object sender, RoutedEventArgs e) { }      // Task 4 接入
    private void ModifyBtn_Click(object sender, RoutedEventArgs e) { }        // Task 5 接入
    private void ResetBtn_Click(object sender, RoutedEventArgs e) { }        // Task 5 接入
    private void AccountEditBtn_Click(object sender, RoutedEventArgs e) { }   // Task 6 接入
    private void ConfigBtn_Click(object sender, RoutedEventArgs e) { }        // Task 7 接入

    protected override void OnClosing(CancelEventArgs e)
    {
        _statusTimer.Stop();
        base.OnClosing(e);
    }

    public sealed record AccountItem(string Display, string Account);
}
