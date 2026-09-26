using System.Windows;
using DFLegacy.AccountClient;
using HandyControl.Controls;

namespace DFLegacy.Launcher.Views;

// Window 必须写全限定名：using HandyControl.Controls 后 Window 与 System.Windows.Window 歧义（CS0104）。
// 构造函数用 internal：回调签名里的 LauncherAccountProfile 是 internal（CS0051）。
public partial class RegisterWindow : System.Windows.Window
{
    private readonly AccountServiceClient _account;
    private readonly Action<LauncherAccountProfile, bool> _onRegistered;

    internal RegisterWindow(AccountServiceClient account, Action<LauncherAccountProfile, bool> onRegistered)
    {
        InitializeComponent();
        _account = account;
        _onRegistered = onRegistered;
    }

    private async void SubmitBtn_Click(object sender, RoutedEventArgs e)
    {
        var user = UserNameBox.Text.Trim();
        var pass = PasswordBox.Password;
        var confirm = ConfirmBox.Password;
        var oicq = string.IsNullOrWhiteSpace(OicqBox.Text) ? null : OicqBox.Text.Trim();
        var test = TestAccountBox.IsChecked == true;
        if (pass != confirm) { Growl.Info("两次密码不一致。"); return; }
        if (user.Length is < 4 or > 32 || pass.Length is < 6 or > 128 || oicq is null || oicq.Length > 32)
        { Growl.Info("账号 4-32、密码 6-128、QQ 不能为空且 ≤32。"); return; }

        SubmitBtn.IsEnabled = false;
        try
        {
            var r = await _account.RegisterAsync(user, pass, oicq, test);
            if (r.Outcome == RegistrationOutcome.Success)
            {
                Growl.Success($"注册成功：{r.Account?.UserName}");
                _onRegistered(new LauncherAccountProfile
                {
                    Account = user, Password = pass, DisplayName = user,
                    CreateTime = DateTimeOffset.Now, OicqCode = oicq, RememberPassword = true
                }, true);
                Close();
            }
            else Growl.Warning(r.Outcome == RegistrationOutcome.AlreadyExists ? "账号已存在。" : (r.Error ?? "注册失败。"));
        }
        catch (Exception ex) { Growl.Info($"请求失败：{ex.Message}"); }
        finally { SubmitBtn.IsEnabled = true; }
    }
}
