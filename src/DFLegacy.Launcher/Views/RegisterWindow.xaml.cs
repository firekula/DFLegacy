using System.Windows;
using DFLegacy.AccountClient;

namespace DFLegacy.Launcher.Views;

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
        if (pass != confirm) { ClassicMessage.Show(this, "两次密码不一致。"); return; }
        if (user.Length is < 4 or > 32 || pass.Length is < 6 or > 128
            || oicq is null || oicq.Length is < 5 or > 32)
        {
            ClassicMessage.Show(this, "账号 4-32、密码 6-128、QQ 不能为空且至少 5 位。");
            return;
        }

        SubmitBtn.IsEnabled = false;
        RegistrationResult r;
        try
        {
            // try/catch 只包网络请求：本地持久化失败不能伪装成"请求失败"。
            r = await _account.RegisterAsync(user, pass, oicq, testAccount: false);
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"请求失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }

        if (r.Outcome != RegistrationOutcome.Success)
        {
            ClassicMessage.Show(this, r.Outcome == RegistrationOutcome.AlreadyExists ? "账号已存在。" : (r.Error ?? "注册失败。"));
            SubmitBtn.IsEnabled = true;
            return;
        }

        ClassicMessage.Show(this, $"注册成功：{r.Account?.UserName}");
        var profile = new LauncherAccountProfile
        {
            Account = user, Password = pass, DisplayName = user,
            CreateTime = DateTimeOffset.Now, OicqCode = oicq, RememberPassword = true
        };
        try
        {
            _onRegistered(profile, true);
        }
        catch (Exception ex)
        {
            // 服务端账号已创建，只是本地 launcher.json 保存失败：不关窗，可重试或稍后在账号管理处理。
            ClassicMessage.Show(this, $"注册成功，但保存到本地账号列表失败：{ex.Message}");
            SubmitBtn.IsEnabled = true;
            return;
        }
        Close();
    }
}
