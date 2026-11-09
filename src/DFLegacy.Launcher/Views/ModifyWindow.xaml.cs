using System.Windows;
using DFLegacy.AccountClient;

namespace DFLegacy.Launcher.Views;

// 用户名框 x:Name="UserName"：主窗口打开前用 win.UserName.Text 预填当前选中账号。
public partial class ModifyWindow : System.Windows.Window
{
    private readonly AccountServiceClient _account;
    private readonly Action<string, string> _onChanged;

    public ModifyWindow(AccountServiceClient account, Action<string, string> onChanged)
    {
        InitializeComponent();
        _account = account;
        _onChanged = onChanged;
    }

    private async void SubmitBtn_Click(object sender, RoutedEventArgs e)
    {
        var user = UserName.Text.Trim();
        var oldP = OldBox.Password;
        var newP = NewBox.Password;
        if (newP != ConfirmBox.Password) { ClassicMessage.Show(this, "两次新密码不一致。"); return; }
        if (user.Length is < 4 or > 32 || newP.Length is < 6 or > 128) { ClassicMessage.Show(this, "账号 4-32、新密码 6-128。"); return; }

        Program.Incidents.RegisterSecret(user);
        Program.Incidents.RegisterSecret(oldP);
        Program.Incidents.RegisterSecret(newP);
        SubmitBtn.IsEnabled = false;
        // 8081 对外后改密接口要求身份：先用旧密码登录换 token（失败限流集中在登录一处，
        // 旧密码错误也在这一步暴露），再带 token 改密。
        LoginResult login;
        try
        {
            // try/catch 只包网络请求：本地持久化失败不能伪装成"请求失败"。
            login = await _account.LoginAsync(user, oldP);
        }
        catch (Exception ex)
        {
            Program.ReportError("account-modify",ex);
            ClassicMessage.Show(this, $"请求失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }

        if (login.Outcome == LoginOutcome.Locked)
        {
            ClassicMessage.Show(this, login.Error ?? "失败次数过多，请稍后再试。");
            SubmitBtn.IsEnabled = true;
            return;
        }

        if (login.Outcome != LoginOutcome.Success)
        {
            ClassicMessage.Show(this, login.Error ?? "账号或密码错误（旧密码不正确）。");
            SubmitBtn.IsEnabled = true;
            return;
        }

        Program.Incidents.RegisterSecret(login.Token);
        PasswordResult r;
        try
        {
            r = await _account.ChangePasswordAsync(user, oldP, newP, login.Token);
        }
        catch (Exception ex)
        {
            Program.ReportError("account-modify",ex);
            ClassicMessage.Show(this, $"请求失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }

        if (r.Outcome != PasswordOutcome.Success)
        {
            ClassicMessage.Show(this, r.Error ?? "修改失败。");
            SubmitBtn.IsEnabled = true;
            return;
        }

        ClassicMessage.Show(this, "密码已修改。");
        try
        {
            _onChanged(user, newP);
        }
        catch (Exception ex)
        {
            Program.ReportError("account-modify",ex);
            // 服务端密码已改，只是本地 launcher.json 保存失败：不关窗，可重试或稍后在账号管理处理。
            ClassicMessage.Show(this, $"密码已修改，但保存到本地账号列表失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }

        // 服务端改密后已吊销该账号的全部 token；这里再登出一次只是清理，失败无妨。
        await _account.LogoutAsync(login.Token);
        Close();
    }
}
