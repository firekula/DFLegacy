using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using DFLegacy.Launcher.Controls;
using DFLegacy.AccountClient;

namespace DFLegacy.Launcher;

public partial class MainWindow : System.Windows.Window
{
    /// <summary>原版 QQLogin.xml 的 CaptionHeight：顶部这块区域按下可拖动整个无边框窗口。</summary>
    private const int CaptionDragHeight = 100;

    private readonly LauncherAccountStore _store;
    private AccountServiceClient _account;
    private readonly DispatcherTimer _statusTimer;
    private LauncherOptions _options;
    private LocalServerSelection? _serverSelection;
    private readonly bool _refreshStatus;
    private readonly Dictionary<string, ImageSource> _artwork = [];
    private static readonly string[] BannerTitles = ["勇士，欢迎重返阿拉德", "经典职业，再次集结", "携手伙伴，开启冒险", "熟悉的世界，新的旅程", "准备出发，探索阿拉德"];

    public MainWindow() : this(new LauncherAccountStore(Path.Combine(AppContext.BaseDirectory, "launcher.json"))) { }

    internal MainWindow(LauncherAccountStore store, bool refreshStatus = true)
    {
        InitializeComponent();
        _store = store;
        _refreshStatus = refreshStatus;
        _options = _store.Load();
        // 账号服务地址 = 入口域名 + 账号服务端口（可用 AccountServiceUrl 覆盖），发布前只改端口。
        _account = new AccountServiceClient(_options.ResolveAccountServiceUrl());

        ShowBanner(0);
        RefreshAccounts();
        AccountText.TextChanged += (_, _) => UpdateAccountHint();
        PasswordText.PasswordChanged += (_, _) => UpdateAccountHint();
        AccountText.IsKeyboardFocusWithinChanged += (_, _) => UpdateAccountHint();
        PasswordText.IsKeyboardFocusWithinChanged += (_, _) => UpdateAccountHint();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        if (refreshStatus) { _statusTimer.Start(); _ = RefreshStatusAsync(); Loaded += (_, _) => StartUpdates(); }
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

    private void UpdateAccountHint()
    {
        AccountHint.Visibility = string.IsNullOrEmpty(AccountText.Text) && !AccountText.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
        PasswordHint.Visibility = string.IsNullOrEmpty(PasswordText.Password) && !PasswordText.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartGame()
    {
        if (!EnsureRuntimeServerSelected()) return;
        if (AgreeBox.IsChecked != true)
        {
            ClassicMessage.Show(this, "请先勾选同意服务条款。");
            return;
        }

        var account = AccountText.Text.Trim();
        var password = PasswordText.Password;
        if (string.IsNullOrEmpty(account))
        {
            ClassicMessage.Show(this, "请输入账号。");
            AccountText.Focus();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ClassicMessage.Show(this, "请输入密码。");
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
            ClassicMessage.Show(this, $"启动失败：{ex.Message}");
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
        if (e.Handled) return;
        if (ServerPage.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter) { ConfirmServer_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.Escape) { BackToLogin_Click(this, new RoutedEventArgs()); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Enter)
        {
            if (Keyboard.FocusedElement is Button) return;
            StartGame();
            e.Handled = true;
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"打开链接失败：{ex.Message}");
        }
    }

    private void OpenTerms()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Skin", "fwtk.html");
            if (!File.Exists(path))
            {
                ClassicMessage.Show(this, "未找到服务条款页（Assets/Skin/fwtk.html）。");
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"打开服务条款失败：{ex.Message}");
        }
    }

    private void TermsLink_Click(object sender, MouseButtonEventArgs e) => OpenTerms();

    private void ModifyLink_Click(object sender, MouseButtonEventArgs e) => ModifyBtn_Click(this, new RoutedEventArgs());

    private void ResetLink_Click(object sender, MouseButtonEventArgs e) => ResetBtn_Click(this, new RoutedEventArgs());

    private void ManageLink_Click(object sender, MouseButtonEventArgs e) => AccountEditBtn_Click(this, new RoutedEventArgs());

    private void RegisterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureRuntimeServerSelected()) return;
        Views.RegisterWindow win;
        try
        {
            win = new Views.RegisterWindow(_account, OnRegistered) { Owner = this };
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"打开注册窗口失败：{ex.Message}");
            return;
        }
        ShowOwnedDialog(win);
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
        if (!EnsureRuntimeServerSelected()) return;
        Views.ResetWindow win;
        try
        {
            win = new Views.ResetWindow(_account) { Owner = this };
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"打开找回密码窗口失败：{ex.Message}");
            return;
        }
        ShowOwnedDialog(win);
    }

    private void ModifyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureRuntimeServerSelected()) return;
        Views.ModifyWindow win;
        try
        {
            win = new Views.ModifyWindow(_account, OnPasswordChanged) { Owner = this };
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"打开修改密码窗口失败：{ex.Message}");
            return;
        }

        // 预填界面上当前可见的账号
        var account = AccountText.Text.Trim();
        if (!string.IsNullOrEmpty(account))
        {
            win.UserName.Text = account;
        }

        ShowOwnedDialog(win);
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
            ClassicMessage.Show(this, $"加载本地账号列表失败：{ex.Message}");
            return;
        }

        ShowOwnedDialog(win);
        try
        {
            ReloadRuntimeOptions();
        }
        catch (Exception ex)
        {
            // 重载失败时内存保留编辑前快照：提示用户但不崩溃，下次启动以磁盘为准。
            ClassicMessage.Show(this, $"重新加载本地账号列表失败：{ex.Message}");
        }
    }

    private void ConfigBtn_Click(object sender, RoutedEventArgs e)
        => OpenConfig(false);

    private void OpenConfig(bool connection)
    {
        Views.ConfigWindow win;
        try
        {
            // 配置窗口在构造里会 Load 一份 launcher.json 快照，文件损坏时这里就抛；
            // 不能让异常沿事件处理器冒泡成进程崩溃。
            win = new Views.ConfigWindow(_store) { Owner = this };
            if (connection) win.ShowConnectionPage();
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"加载配置失败：{ex.Message}");
            return;
        }

        ShowOwnedDialog(win);
        try
        {
            ReloadRuntimeOptions();
            if (_refreshStatus) _ = RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            // 重载失败时内存保留打开设置前的快照：提示用户但不崩溃，下次启动以磁盘为准。
            ClassicMessage.Show(this, $"重新加载配置失败：{ex.Message}");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        updateCoordinator?.Dispose();
        _statusTimer.Stop();
        _windowClosed = true;
        CancelServerRequests();
        base.OnClosing(e);
    }

    public sealed record AccountItem(string Display, string Account);

    private ImageSource Artwork(string name)
    {
        if (_artwork.TryGetValue(name, out var image)) return image;
        var bitmap = new BitmapImage(new Uri($"pack://application:,,,/DFLegacy.Launcher;component/Assets/Skin/{name}", UriKind.Absolute));
        bitmap.Freeze(); _artwork[name] = bitmap; return bitmap;
    }
    private void ShowBanner(int index)
    {
        index = Math.Clamp(index, 0, 4); BannerImage.Source = Artwork($"classic-banner{index}.png");
        BannerCaption.Text = BannerTitles[index];
        var buttons = new[] { Slide0, Slide1, Slide2, Slide3, Slide4 };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].NormalSource = Artwork(i == index ? "classic-small1.png" : "classic-small0.png");
            System.Windows.Automation.AutomationProperties.SetHelpText(buttons[i], i == index ? "当前宣传图" : "切换宣传图");
        }
    }
    private void Slide_Click(object sender, RoutedEventArgs e)
    { if (sender is SkinButton button && int.TryParse(button.Tag?.ToString(), out var index)) ShowBanner(index); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void EnterGame_Click(object sender, RoutedEventArgs e) => StartGame();
    private void MoreServices_Click(object sender, RoutedEventArgs e)
    {
        MoreServicesButton.ContextMenu.PlacementTarget = MoreServicesButton;
        MoreServicesButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MoreServicesButton.ContextMenu.IsOpen = true;
    }
    private void ServiceUrl_Click(object sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: string url }) OpenUrl(url); }
    private void News_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as SkinButton)?.Tag?.ToString())
        {
            case "display": ConfigBtn_Click(this, e); break;
            case "connection": OpenConfig(true); break;
            case "account": RegisterBtn_Click(this, e); break;
            case "install": ClassicMessage.Show(this, "将玩家 ZIP 解压到 DNF.exe 所在目录，选择服务器并输入账号、密码后进入游戏。"); break;
            default: ClassicMessage.Show(this, "欢迎来到阿拉德大陆。选择服务器，输入账号和密码，开始冒险。"); break;
        }
    }
    private void ShowOwnedDialog(System.Windows.Window window)
    {
        ModalShade.Visibility = Visibility.Visible;
        try { window.ShowDialog(); }
        finally { ModalShade.Visibility = Visibility.Collapsed; }
    }
}
