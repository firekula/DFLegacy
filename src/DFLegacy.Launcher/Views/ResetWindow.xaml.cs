using System.Windows;
using DFLegacy.AccountClient;
using HandyControl.Controls;

namespace DFLegacy.Launcher.Views;

// Window 必须写全限定名：using HandyControl.Controls 后 Window 与 System.Windows.Window 歧义（CS0104）。
public partial class ResetWindow : System.Windows.Window
{
    private readonly AccountServiceClient _account;

    public ResetWindow(AccountServiceClient account)
    {
        InitializeComponent();
        _account = account;
    }

    private async void SubmitBtn_Click(object sender, RoutedEventArgs e)
    {
        var user = UserNameBox.Text.Trim();
        var oicq = OicqBox.Text.Trim();
        var pass = PasswordBox.Password;
        if (pass != ConfirmBox.Password) { Growl.Info("两次密码不一致。"); return; }
        if (user.Length is < 4 or > 32 || pass.Length is < 6 or > 128 || oicq.Length is 0 or > 32)
        { Growl.Info("账号 4-32、密码 6-128、QQ 不能为空且 ≤32。"); return; }

        SubmitBtn.IsEnabled = false;
        PasswordResult r;
        try
        {
            // 本窗口不涉及本地持久化，try/catch 只包网络请求。
            r = await _account.ResetPasswordAsync(user, oicq, pass);
        }
        catch (Exception ex)
        {
            Growl.Info($"请求失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }

        if (r.Outcome != PasswordOutcome.Success)
        {
            Growl.Warning(r.Error ?? "重置失败。");
            SubmitBtn.IsEnabled = true;
            return;
        }

        Growl.Success("密码已重置。");
        Close();
    }
}
