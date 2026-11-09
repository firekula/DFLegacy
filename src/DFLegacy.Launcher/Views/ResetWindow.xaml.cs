using System.Windows;
using DFLegacy.AccountClient;

namespace DFLegacy.Launcher.Views;

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
        if (pass != ConfirmBox.Password) { ClassicMessage.Show(this, "两次密码不一致。"); return; }
        if (user.Length is < 4 or > 32 || pass.Length is < 6 or > 128 || oicq.Length is 0 or > 32)
        { ClassicMessage.Show(this, "账号 4-32、密码 6-128、QQ 不能为空且 ≤32。"); return; }

        Program.Incidents.RegisterSecret(user);
        Program.Incidents.RegisterSecret(pass);
        Program.Incidents.RegisterSecret(oicq);
        SubmitBtn.IsEnabled = false;
        PasswordResult r;
        try
        {
            // 本窗口不涉及本地持久化，try/catch 只包网络请求。
            r = await _account.ResetPasswordAsync(user, oicq, pass);
        }
        catch (Exception ex)
        {
            Program.ReportError("account-reset",ex);
            ClassicMessage.Show(this, $"请求失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }

        if (r.Outcome != PasswordOutcome.Success)
        {
            ClassicMessage.Show(this, r.Error ?? "重置失败。");
            SubmitBtn.IsEnabled = true;
            return;
        }

        ClassicMessage.Show(this, "密码已重置。");
        Close();
    }
}
