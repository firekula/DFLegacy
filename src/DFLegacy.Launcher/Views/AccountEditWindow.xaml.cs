using System.Windows;

namespace DFLegacy.Launcher.Views;

// 构造函数用 internal：签名里的 LauncherAccountStore / LauncherOptions 是 internal（CS0051）。
// 本窗口纯本地：只读写 launcher.json，不触碰服务端。
public partial class AccountEditWindow : System.Windows.Window
{
    private readonly LauncherAccountStore _store;
    private LauncherOptions _options;

    internal AccountEditWindow(LauncherAccountStore store)
    {
        InitializeComponent();
        _store = store;
        _options = store.Load();
        Grid.ItemsSource = _options.Accounts;
    }

    private void Reload() { _options = _store.Load(); Grid.ItemsSource = null; Grid.ItemsSource = _options.Accounts; }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var p = new LauncherAccountProfile { Account = "new", Password = string.Empty, DisplayName = "新账号", CreateTime = DateTimeOffset.Now, RememberPassword = true };
        if (!EditProfile(p, isNew: true)) return;
        _options.Accounts.Add(p);
        if (!SaveOptions()) return;
        Reload();
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();
    private void Grid_MouseDoubleClick(object sender, RoutedEventArgs e) => EditSelected();

    private void EditSelected()
    {
        if (Grid.SelectedItem is not LauncherAccountProfile p) { ClassicMessage.Show(this, "请先选择账号。"); return; }
        if (!EditProfile(p, isNew: false)) return;
        if (!SaveOptions()) return;
        Reload();
    }

    // isNew 决定对话框标题（新增账号 / 编辑账号）：brief 里的占位实现没用到这个参数，
    // 这里让它生效，用户能分辨自己开的是新增还是编辑。
    private bool EditProfile(LauncherAccountProfile p, bool isNew)
    {
        var dlg = new AccountFieldDialog(p) { Owner = this, Title = isNew ? "新增账号" : "编辑账号" };
        return dlg.ShowDialog() == true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not LauncherAccountProfile p) return;
        _options.Accounts.Remove(p);
        if (_options.AccountIndex >= _options.Accounts.Count)
            _options.AccountIndex = Math.Max(0, _options.Accounts.Count - 1);
        if (!SaveOptions()) return;
        Reload();
    }

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedIndex is int i and >= 0)
        {
            _options.AccountIndex = i;
            if (SaveOptions()) ClassicMessage.Show(this, "已设为默认。");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Save 的所有调用点（新增/编辑/删除/设为默认）都经这里：WPF 事件处理器里未处理
    // 异常会沿分发器冒泡成进程崩溃。落盘失败时提示 + 按磁盘重同步（磁盘为权威，
    // 内存里未落盘的改动随之丢弃），并返回 false 让调用方不再继续后续变更。
    private bool SaveOptions()
    {
        try
        {
            _store.Save(_options);
            return true;
        }
        catch (Exception ex)
        {
            ClassicMessage.Show(this, $"保存本地账号列表失败：{ex.Message}");
            try
            {
                _options = _store.Load();
                Grid.ItemsSource = _options.Accounts;
            }
            catch (Exception loadEx)
            {
                // 磁盘也读不回来时只能提示；内存保留当前态，等下一次操作再试。
                ClassicMessage.Show(this, $"重新读取本地账号列表失败：{loadEx.Message}");
            }
            return false;
        }
    }
}
