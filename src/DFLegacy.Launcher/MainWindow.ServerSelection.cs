using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DFLegacy.AccountClient;
using DFLegacy.Launcher.Controls;

namespace DFLegacy.Launcher;

public partial class MainWindow
{
    private readonly Dictionary<string, (SkinButton Button, Ellipse Dot)> _serverButtons = [];
    private CancellationTokenSource? _serverRequest;
    private long _serverGeneration;
    private bool _windowClosed;
    // This validated snapshot survives closing the selection page; it is never persisted here.
    private LocalServerSelection? _confirmedServerSelection;
    private bool _requiresServerSelection;

    private void ApplyRuntimeOptions(LauncherOptions options, AccountServiceClient account)
    {
        var typedAccount = AccountText.Text; var typedPassword = PasswordText.Password;
        var selectedAccount = (AccountList.SelectedItem as AccountItem)?.Account;
        _options = options; _account = account;
        RefreshAccounts();
        if (selectedAccount is not null)
        {
            var index = _options.Accounts.FindIndex(p => p.Account == selectedAccount);
            if (index >= 0) AccountList.SelectedIndex = index;
        }
        AccountText.Text = typedAccount; PasswordText.Password = typedPassword;
        UpdateAccountHint();
    }

    private void ReloadRuntimeOptions()
    {
        if (_requiresServerSelection) { EnsureRuntimeServerSelected(); return; }
        LauncherOptions options;
        try { options = _confirmedServerSelection?.Confirm(_store) ?? _store.Load(); }
        catch (Exception ex) when (_confirmedServerSelection is not null)
        {
            _requiresServerSelection = true;
            LoginPage.Visibility = Visibility.Collapsed; ServerPage.Visibility = Visibility.Visible;
            ReloadServerSelection();
            ShowServerSelectionError("服务器配置已变化或无法读取，请重新选择", ex);
            return;
        }
        var account = new AccountServiceClient(options.ResolveAccountServiceUrl());
        CancelServerRequests(); _serverSelection = null;
        ApplyRuntimeOptions(options, account);
    }

    private bool EnsureRuntimeServerSelected()
    {
        if (!_requiresServerSelection) return true;
        LoginPage.Visibility = Visibility.Collapsed; ServerPage.Visibility = Visibility.Visible;
        if (_serverSelection is null)
        {
            ReloadServerSelection();
            ServerEndpointText.Text = "服务器配置已变化，请重新选择";
        }
        return false;
    }

    private void CancelServerRequests()
    {
        _serverGeneration++;
        _serverRequest?.Cancel();
        _serverRequest?.Dispose();
        _serverRequest = null;
    }

    internal async Task RefreshStatusAsync()
    {
        if (_windowClosed || (_requiresServerSelection && _serverSelection is null)) return;
        var selection = _serverSelection;
        var target = selection?.SelectedChoice;
        if (selection is not null && target is null) return;
        target ??= new LocalServerChoice("本地服务器", _options.EntranceHost, _options.EntrancePort)
            { AccountServicePort = _options.AccountServicePort, AccountServiceUrl = _options.AccountServiceUrl };
        CancelServerRequests();
        var generation = _serverGeneration;
        var source = _serverRequest = new CancellationTokenSource();
        var token = source.Token;
        bool Current() => !token.IsCancellationRequested && !_windowClosed && generation == _serverGeneration;
        if (selection is not null) SetSelectedFooter(target, "测速中…");
        try
        {
            var probe = await ServerConnectionProbe.ProbeAsync(target, TimeSpan.FromSeconds(2), token);
            if (!Current()) return;
            ServerDot.Fill = probe.Reachable ? Brushes.LimeGreen : Brushes.IndianRed;
            ServerText.Text = probe.Reachable ? "服务端：可连接" : "服务端：超时或不可达";
            ServerAddr.Text = target.Endpoint;
            ServerEndpointText.Text = target.Endpoint + (probe.Reachable ? "　入口可连接" : "　入口超时或不可达");
            if (_serverButtons.TryGetValue(target.Id, out var row)) row.Dot.Fill = ServerDot.Fill;
            if (selection is not null)
                SetSelectedFooter(target, probe.Elapsed is { } elapsed ? $"{elapsed.TotalMilliseconds:F0} ms" : "不可达");
            using var http = new HttpClient { BaseAddress = new Uri(target.ResolveAccountServiceUrl().TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(2) };
            var api = new AccountServiceClient(http);
            bool healthy;
            try { healthy = await api.CheckHealthAsync(token); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { healthy = false; }
            if (!Current()) return;
            ApiDot.Fill = healthy ? Brushes.LimeGreen : Brushes.Gray;
            ApiText.Text = healthy ? "账号服务：可达" : "账号服务：不可达";
            ApiText.ToolTip = target.ResolveAccountServiceUrl();
            ServerEndpointText.ToolTip = target.Name + "\n" + target.Endpoint + "\n" + ApiText.Text + "\n" + target.ResolveAccountServiceUrl();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!Current()) return;
            ServerText.Text = "服务端：不可达"; ServerText.ToolTip = ex.Message;
            if (selection is not null) SetSelectedFooter(target, "不可达");
        }
    }

    private void SelectServer_Click(object sender, RoutedEventArgs e)
    {
        LoginPage.Visibility = Visibility.Collapsed; ServerPage.Visibility = Visibility.Visible;
        ReloadServerSelection();
    }

    internal void ReloadServerSelection()
    {
        CancelServerRequests(); _serverSelection = null; _serverButtons.Clear();
        TelecomChoices.Children.Clear(); NetcomChoices.Children.Clear();
        TelecomChoices.RowDefinitions.Clear(); NetcomChoices.RowDefinitions.Clear();
        ServerDot.Fill = ApiDot.Fill = Brushes.Gray;
        ServerText.Text = "服务端：未选择"; ApiText.Text = "账号服务：未选择";
        ApiText.ToolTip = ServerText.ToolTip = null;
        SelectedServerText.Text = "请选择服务器"; SelectedServerText.ToolTip = null;
        ServerEndpointText.Text = "选择服务器后显示入口 TCP 建连耗时。";
        ServerEndpointText.ToolTip = null;
        ServerSelectionError.Text = ""; ConfirmServerButton.IsEnabled = false;
        try
        {
            _serverSelection = LocalServerSelection.Open(_store);
            RenderRegion(TelecomChoices, "telecom"); RenderRegion(NetcomChoices, "netcom");
        }
        catch (Exception ex) { ShowServerSelectionError("无法读取服务器配置", ex); }
    }

    private void ShowServerSelectionError(string summary, Exception error)
    {
        ServerEndpointText.Text = summary;
        ServerEndpointText.ToolTip = error.Message;
        ServerSelectionError.Text = error.Message;
    }

    private void RenderRegion(Grid grid, string region)
    {
        grid.ColumnDefinitions.Clear();
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var choices = _serverSelection!.Choices.Where(c => c.Region == region).ToArray();
        if (choices.Length == 0)
        {
            var empty = new PixelText { Text = "暂无已配置服务器", FontSize = 12, Foreground = Brushes.Tan,
                Height = 42, TextAlignment = TextAlignment.Center };
            Grid.SetColumnSpan(empty, 4); grid.Children.Add(empty); return;
        }
        for (var i = 0; i < choices.Length; i++)
        {
            if (i % 4 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            var choice = choices[i];
            var button = new SkinButton { Width = 101, Height = 25, FontSize = 12, Padding = new Thickness(14, 0, 2, 0),
                Style = (Style)FindResource("ClassicImageButton"), Content = FitServerName(choice.Name, 80), Tag = choice,
                ToolTip = choice.Name + "\n" + choice.Endpoint, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(button, choice.Name);
            button.Click += ServerChoice_Click;
            var row = new Grid { Height = 32, ClipToBounds = true };
            row.Children.Add(button);
            var dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.Gray, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 3, 0, 0) };
            row.Children.Add(dot); _serverButtons.Add(choice.Id, (button, dot));
            SetServerArtwork(button, false);
            Grid.SetRow(row, i / 4); Grid.SetColumn(row, i % 4); grid.Children.Add(row);
        }
    }

    private static string FitServerName(string name, int width)
    {
        var elements = StringInfo.GetTextElementEnumerator(name); var result = "";
        while (elements.MoveNext())
        {
            var next = result + elements.GetTextElement();
            if (PixelRasterizer.Render(next, 12, Colors.White, 1).Advance > width - 12) return result + "…";
            result = next;
        }
        return result;
    }

    private void SetServerArtwork(SkinButton button, bool selected)
    {
        button.NormalSource = Artwork(selected ? "classic-row2.png" : "classic-row0.png");
        button.HoverSource = Artwork(selected ? "classic-row2.png" : "classic-row1.png");
        button.PressedSource = Artwork("classic-row2.png");
    }

    private void SetSelectedFooter(LocalServerChoice choice, string status)
    {
        SelectedServerText.Text = FitServerName(choice.Name, 82) + "  " + status;
        SelectedServerText.ToolTip = choice.Name + "\n" + choice.Endpoint + "\n入口 TCP 建连耗时：" + status;
    }

    private void ServerChoice_Click(object sender, RoutedEventArgs e)
    {
        if (_serverSelection is null || sender is not SkinButton { Tag: LocalServerChoice choice }) return;
        CancelServerRequests(); _serverSelection.Select(choice.Id);
        foreach (var pair in _serverButtons) SetServerArtwork(pair.Value.Button, pair.Key == choice.Id);
        ServerEndpointText.Text = choice.Endpoint;
        ServerEndpointText.ToolTip = choice.Name + "\n" + choice.Endpoint;
        ServerSelectionError.Text = ""; ConfirmServerButton.IsEnabled = true;
        ServerDot.Fill = ApiDot.Fill = Brushes.Gray;
        ServerText.Text = "服务端：测速中…"; ApiText.Text = "账号服务：检测中…"; ApiText.ToolTip = null;
        SetSelectedFooter(choice, "测速中…");
        if (_refreshStatus) _ = RefreshStatusAsync();
    }

    private void BackToLogin_Click(object sender, RoutedEventArgs e)
    {
        CancelServerRequests(); _serverSelection = null;
        ServerPage.Visibility = Visibility.Collapsed; LoginPage.Visibility = Visibility.Visible;
    }

    private void ConfirmServer_Click(object sender, RoutedEventArgs e)
    {
        if (_serverSelection?.SelectedChoice is null) return;
        try
        {
            var options = _serverSelection.Confirm(_store);
            var accountClient = new AccountServiceClient(options.ResolveAccountServiceUrl());
            ApplyRuntimeOptions(options, accountClient);
            _confirmedServerSelection = _serverSelection;
            _requiresServerSelection = false;
            BackToLogin_Click(this, e);
            if (_refreshStatus) _ = RefreshStatusAsync();
        }
        catch (InvalidOperationException ex) { ReloadServerSelection(); ShowServerSelectionError("配置已变化，请重新选择", ex); }
        catch (Exception ex) { ConfirmServerButton.IsEnabled = false; ShowServerSelectionError("无法确认服务器", ex); }
    }
}
