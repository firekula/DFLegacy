using System.Windows;
using DFLegacy.AccountClient;
using HandyControl.Controls;

namespace DFLegacy.Launcher.Views;

// Window 必须写全限定名：using HandyControl.Controls 后 Window 与 System.Windows.Window 歧义（CS0104）。
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
        if (newP != ConfirmBox.Password) { Growl.Info("两次新密码不一致。"); return; }
        if (user.Length is < 4 or > 32 || newP.Length is < 6 or > 128) { Growl.Info("账号 4-32、新密码 6-128。"); return; }

        SubmitBtn.IsEnabled = false;
        PasswordResult r;
        try
        {
            // try/catch 只包网络请求：本地持久化失败不能伪装成"请求失败"。
            r = await _account.ChangePasswordAsync(user, oldP, newP);
        }
        catch (Exception ex)
        {
            Growl.Info($"请求失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }

        if (r.Outcome != PasswordOutcome.Success)
        {
            Growl.Warning(r.Error ?? "修改失败。");
            SubmitBtn.IsEnabled = true;
            return;
        }

        Growl.Success("密码已修改。");
        try
        {
            _onChanged(user, newP);
        }
        catch (Exception ex)
        {
            // 服务端密码已改，只是本地 launcher.json 保存失败：不关窗，可重试或稍后在账号管理处理。
            Growl.Warning($"密码已修改，但保存到本地账号列表失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }
        Close();
    }
}
