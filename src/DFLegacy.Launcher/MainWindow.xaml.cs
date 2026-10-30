using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DFLegacy.AccountClient;
using HandyControl.Controls;

namespace DFLegacy.Launcher;

public partial class MainWindow : System.Windows.Window
{
    /// <summary>原版 QQLogin.xml 的 CaptionHeight：顶部这块区域按下可拖动整个无边框窗口。</summary>
    private const int CaptionDragHeight = 100;

    private readonly LauncherAccountStore _store = new(Path.Combine(AppContext.BaseDirectory, "launcher.json"));
    private AccountServiceClient _account;
    private readonly DispatcherTimer _statusTimer;
    private LauncherOptions _options;

    public MainWindow()
    {
        InitializeComponent();
        _options = _store.Load();
        // 账号服务地址 = 入口域名 + 账号服务端口（可用 AccountServiceUrl 覆盖），发布前只改端口。
        _account = new AccountServiceClient(_options.ResolveAccountServiceUrl());

        // 原版三态图片按钮：位置/尺寸见 MainWindow.xaml（取自 QQLogin.xml），
        // 标签烙在图片里，点击动作用这里注入。
        _ = new SkinImage(CloseImage, "buton_close00.png", "buton_close01.png", "buton_close02.png",
            () => Close());
        _ = new SkinImage(RegisterImage, "buton_zczh00.png", "buton_zczh01.png", "buton_zczh02.png",
            () => RegisterBtn_Click(this, new RoutedEventArgs()));
        _ = new SkinImage(SiteImage, "buton_gfwz00.png", "buton_gfwz01.png", "buton_gfwz02.png",
            () => OpenUrl("http://dnf.qq.com/"));
        _ = new SkinImage(ServiceImage, "buton_kffw00.png", "buton_kffw01.png", "buton_kffw02.png",
            () => OpenUrl("http://gcs.qq.com/gcs/index.shtml"));
        _ = new SkinImage(PayImage, "buton_sccz00.png", "buton_sccz01.png", "buton_sccz02.png",
            () => OpenUrl("http://dnf.qq.com/web200805/pay.htm"));
        _ = new SkinImage(ForumImage, "buton_yxlt00.png", "buton_yxlt01.png", "buton_yxlt02.png",
            () => OpenUrl("http://gamebbs.qq.com/"));
        _ = new SkinImage(AccountsImage, "buton_bs03.png", "buton_bs03.png", "buton_bs03.png",
            () => AccountEditBtn_Click(this, new RoutedEventArgs()));
        _ = new SkinImage(SettingsImage, "choose_00.png", "choose_01.png", "choose_02.png",
            () => ConfigBtn_Click(this, new RoutedEventArgs()));
        _ = new SkinImage(EnterImage, "enter_00.png", "enter_01.png", "enter_02.png",
            StartGame);

        RefreshAccounts();
        AccountText.TextChanged += (_, _) => UpdateAccountHint();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();
        _ = RefreshStatusAsync();
    }

    private void RefreshAccounts()
    {
        AccountList.ItemsSource = _options.Accounts
            .Select(a => new AccountItem(a.DisplayName ?? a.Account, a.Account)).ToList();
        if (_options.Accounts.Count > 0)
        {
            AccountList.SelectedIndex = Math.Clamp(_options.AccountIndex, 0, _options.Accounts.Count - 1);
        }
        else
        {
            AccountText.Text = _options.Account;
            PasswordText.Password = _options.Password;
        }

        UpdateAccountHint();
    }

    private LauncherAccountProfile SelectedProfile() =>
        _options.Accounts.Count == 0
            ? new LauncherAccountProfile { Account = _options.Account, Password = _options.Password }
            : _options.Accounts[Math.Clamp(
                AccountList.SelectedIndex >= 0 ? AccountList.SelectedIndex : 0,
                0,
                _options.Accounts.Count - 1)];

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountList.SelectedIndex < 0 || _options.Accounts.Count == 0) return;
        var profile = _options.Accounts[Math.Clamp(AccountList.SelectedIndex, 0, _options.Accounts.Count - 1)];
        AccountText.Text = profile.Account;
        PasswordText.Password = profile.RememberPassword ? profile.Password : string.Empty;
        UpdateAccountHint();
    }

    private void UpdateAccountHint() =>
        AccountHint.Visibility = string.IsNullOrEmpty(AccountText.Text) ? Visibility.Visible : Visibility.Collapsed;

    // 状态灯语义按原版：探的是游戏入口（EntranceHost:EntrancePort），不是本机账号 API。
    // 账号 API（注册/改密/找回用）单独一行显示，避免"远端服务端明明在跑却提示不可达"的误解。
    private async Task RefreshStatusAsync()
    {
        try
        {
            var entranceUp = await ProbeTcpAsync(
                _options.EntranceHost, _options.EntrancePort, TimeSpan.FromSeconds(2));
            ServerDot.Fill = entranceUp ? Brushes.LimeGreen : Brushes.IndianRed;
            ServerText.Text = entranceUp ? "服务端已上线" : "服务端未上线";
            ServerAddr.Text = $"{_options.EntranceHost}:{_options.EntrancePort}";
        }
        catch (Exception)
        {
            ServerDot.Fill = Brushes.IndianRed;
            ServerText.Text = "服务端未上线";
        }

        try
        {
            var apiUp = await _account.CheckHealthAsync();
            ApiDot.Fill = apiUp ? Brushes.LimeGreen : Brushes.Gray;
            ApiText.Text = apiUp
                ? $"账号服务：可达（{_options.ResolveAccountServiceUrl()}）"
                : $"账号服务：不可达（{_options.ResolveAccountServiceUrl()}）";
        }
        catch (Exception)
        {
            // CheckHealthAsync 只捕获 HttpRequestException，超时会以 TaskCanceledException 冒出；
            // DispatcherTimer 的 Tick 是 async void，任何失败都按不可达处理，避免拖垮应用。
            ApiDot.Fill = Brushes.Gray;
            ApiText.Text = "账号服务：不可达";
        }
    }

    private static async Task<bool> ProbeTcpAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutSource = new CancellationTokenSource(timeout);
            await client.ConnectAsync(host, port, timeoutSource.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void StartGame()
    {
        if (AgreeBox.IsChecked != true)
        {
            Growl.Info("请先勾选同意服务条款。");
            return;
        }

        var account = AccountText.Text.Trim();
        var password = PasswordText.Password;
        if (string.IsNullOrEmpty(account))
        {
            Growl.Info("请输入账号。");
            AccountText.Focus();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            Growl.Info("请输入密码。");
            PasswordText.Focus();
            return;
        }

        // 用界面当前输入启动（不回写存储，避免把界面里临时输入的密码持久化到 launcher.json）。
        var stored = SelectedProfile();
        var launchProfile = new LauncherAccountProfile
        {
            Account = account,
            Password = password,
            DisplayName = stored.DisplayName,
            CreateTime = stored.CreateTime,
            OicqCode = stored.OicqCode,
            RememberPassword = stored.RememberPassword
        };
        _ = StartGameAsync(launchProfile);
    }

    private async Task StartGameAsync(LauncherAccountProfile profile)
    {
        try
        {
            Hide();
            await LaunchFlow.RunAsync(_options, profile);
            Close();
        }
        catch (Exception ex)
        {
            Program.WriteLauncherLog($"Launcher failed: {ex}");
            Show();
            Growl.Info($"启动失败：{ex.Message}");
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.GetPosition(this).Y <= CaptionDragHeight)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StartGame();
            e.Handled = true;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Growl.Warning($"打开链接失败：{ex.Message}");
        }
    }

    private static void OpenTerms()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Skin", "fwtk.html");
            if (!File.Exists(path))
            {
                Growl.Warning("未找到服务条款页（Assets/Skin/fwtk.html）。");
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Growl.Warning($"打开服务条款失败：{ex.Message}");
        }
    }

    private void TermsLink_Click(object sender, MouseButtonEventArgs e) => OpenTerms();

    private void ModifyLink_Click(object sender, MouseButtonEventArgs e) => ModifyBtn_Click(this, new RoutedEventArgs());

    private void ResetLink_Click(object sender, MouseButtonEventArgs e) => ResetBtn_Click(this, new RoutedEventArgs());

    private void ManageLink_Click(object sender, MouseButtonEventArgs e) => AccountEditBtn_Click(this, new RoutedEventArgs());

    private void RegisterBtn_Click(object sender, RoutedEventArgs e)
    {
        Views.RegisterWindow win;
        try
        {
            win = new Views.RegisterWindow(_account, OnRegistered) { Owner = this };
        }
        catch (Exception ex)
        {
            Growl.Warning($"打开注册窗口失败：{ex.Message}");
            return;
        }
        win.ShowDialog();
    }

    private void OnRegistered(LauncherAccountProfile profile, bool remember)
    {
        profile.RememberPassword = remember;
        var previousIndex = _options.AccountIndex;
        _options.Accounts.Add(profile);
        _options.AccountIndex = _options.Accounts.Count - 1;
        try
        {
            _store.Save(_options);
        }
        catch
        {
            // 落盘失败：回滚内存态再抛给窗口（窗口会提示"注册成功但保存失败"且不关窗），
            // 避免用户重试时内存里出现重复账号。
            _options.Accounts.Remove(profile);
            _options.AccountIndex = previousIndex;
            throw;
        }

        RefreshAccounts();
        if (_options.Accounts.Count > 0)
        {
            AccountList.SelectedIndex = _options.Accounts.Count - 1;
        }
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        Views.ResetWindow win;
        try
        {
            win = new Views.ResetWindow(_account) { Owner = this };
        }
        catch (Exception ex)
        {
            Growl.Warning($"打开找回密码窗口失败：{ex.Message}");
            return;
        }
        win.ShowDialog();
    }

    private void ModifyBtn_Click(object sender, RoutedEventArgs e)
    {
        Views.ModifyWindow win;
        try
        {
            win = new Views.ModifyWindow(_account, OnPasswordChanged) { Owner = this };
        }
        catch (Exception ex)
        {
            Growl.Warning($"打开修改密码窗口失败：{ex.Message}");
            return;
        }

        // 预填界面上当前可见的账号
        var account = AccountText.Text.Trim();
        if (!string.IsNullOrEmpty(account))
        {
            win.UserName.Text = account;
        }

        win.ShowDialog();
    }

    private void OnPasswordChanged(string userName, string newPassword)
    {
        var idx = _options.Accounts.FindIndex(a =>
            string.Equals(a.Account, userName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || !_options.Accounts[idx].RememberPassword) return;
        var previousPassword = _options.Accounts[idx].Password;
        _options.Accounts[idx].Password = newPassword;
        try
        {
            _store.Save(_options);
        }
        catch
        {
            // 落盘失败：回滚内存态再抛给窗口（窗口会提示"密码已修改，但保存失败"且不关窗），
            // 避免内存里的新密码与磁盘不一致被下次启动覆盖。
            _options.Accounts[idx].Password = previousPassword;
            throw;
        }
    }

    private void AccountEditBtn_Click(object sender, RoutedEventArgs e)
    {
        Views.AccountEditWindow win;
        try
        {
            // 账号管理窗口在构造里会 Load 一份独立的 launcher.json 快照，文件损坏时
            // 这里就抛；不能让异常沿事件处理器冒泡成进程崩溃。
            win = new Views.AccountEditWindow(_store) { Owner = this };
        }
        catch (Exception ex)
        {
            Growl.Warning($"加载本地账号列表失败：{ex.Message}");
            return;
        }

        win.ShowDialog();
        try
        {
            _options = _store.Load();      // 编辑后重新加载
            RefreshAccounts();
        }
        catch (Exception ex)
        {
            // 重载失败时内存保留编辑前快照：提示用户但不崩溃，下次启动以磁盘为准。
            Growl.Warning($"重新加载本地账号列表失败：{ex.Message}");
        }
    }

    private void ConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        Views.ConfigWindow win;
        try
        {
            // 配置窗口在构造里会 Load 一份 launcher.json 快照，文件损坏时这里就抛；
            // 不能让异常沿事件处理器冒泡成进程崩溃。
            win = new Views.ConfigWindow(_store) { Owner = this };
        }
        catch (Exception ex)
        {
            Growl.Warning($"加载配置失败：{ex.Message}");
            return;
        }

        win.ShowDialog();
        try
        {
            _options = _store.Load();      // 设置变更后重新加载（host/url/relay 等）
            RefreshAccounts();
            // 设置里改了账号 API URL 后必须重建客户端（_account 按构造时的 URL 创建），
            // 否则状态点与注册/改密/找回窗口会一直打到旧地址；重建后立即重探一次状态。
            _account = new AccountServiceClient(_options.ResolveAccountServiceUrl());
            _ = RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            // 重载失败时内存保留打开设置前的快照：提示用户但不崩溃，下次启动以磁盘为准。
            Growl.Warning($"重新加载配置失败：{ex.Message}");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _statusTimer.Stop();
        base.OnClosing(e);
    }

    public sealed record AccountItem(string Display, string Account);
}
