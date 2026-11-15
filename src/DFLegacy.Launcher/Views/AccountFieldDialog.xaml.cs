using System.Windows;

namespace DFLegacy.Launcher.Views;

// 构造函数用 internal：签名里的 LauncherAccountProfile 是 internal（CS0051）。
// 基类同样写全限定名，与 RegisterWindow / AccountEditWindow 保持同一约定。
public partial class AccountFieldDialog : System.Windows.Window
{
    private readonly LauncherAccountProfile _p;

    internal AccountFieldDialog(LauncherAccountProfile p)
    {
        InitializeComponent();
        _p = p;
        DisplayNameBox.Text = p.DisplayName ?? string.Empty;   // DisplayName 可空，直接赋给 string 会告警（CS8601）
        AccountBox.Text = p.Account;
        PasswordBox.Password = p.Password;
        RememberBox.IsChecked = p.RememberPassword;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _p.DisplayName = DisplayNameBox.Text;
        _p.Account = AccountBox.Text;
        _p.Password = PasswordBox.Password;
        _p.RememberPassword = RememberBox.IsChecked == true;
        // 不记住密码时不把明文落盘：清空已存密码。UI 启动前在界面重新输入即可；
        // headless 会因「Accounts[i].Password 不能为空…」校验失败（见 STATUS 已知限制）。
        if (RememberBox.IsChecked != true)
        {
            _p.Password = string.Empty;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
