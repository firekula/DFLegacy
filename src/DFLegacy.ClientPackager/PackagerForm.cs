using System.Diagnostics;
using System.Text.Json;
using DFLegacy.Manager;

namespace DFLegacy.ClientPackager;

internal sealed class PackagerForm : Form
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly TableLayoutPanel fields = new() { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
    private readonly TextBox root = new(), output = new(), host = new(), api = new(), fallbackHost = new();
    private readonly NumericUpDown entrance = Port(), channel = Port(), additional = Port(), apiPort = Port(),
        character = Port(), primary = Port(), secondary = Port(), fallbackTcp = Port(), fallbackUdp = Port(), pvpPort = Port();
    private readonly CheckBox relay = new(), regional = new(), fallback = new(), pvp = new(), test = new();
    private readonly ComboBox preference = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly TextBox status = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private bool busy;

    public PackagerForm()
    {
        Text = "DFLegacy 客户端离线打包器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(740, 820);
        MinimumSize = new Size(590, 480);
        Font = new Font("Microsoft YaHei UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(fields);
        Controls.Add(scroll);

        root.Text = Directory.Exists(Path.Combine(Environment.CurrentDirectory, "client-assets")) ? Environment.CurrentDirectory : "";
        output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DFLegacy-客户端包");
        Row("发行资源目录", Browse(root, "选择包含 client-assets 的目录"));
        Row("输出目录", Browse(output, "选择保存客户端包的目录"));
        Row("玩家连接地址", host);
        Row("玩家账号服务 URL", api);
        Row("登录 TCP", entrance);
        Row("频道 TCP", channel);
        Row("附加入口 TCP", additional);
        Row("账号 API HTTP 端口", apiPort);
        Row("角色 UDP", character);
        Row("玩法主 UDP", primary);
        Row("玩法次 UDP", secondary);
        Row("地区频道 7100–7105 TCP", regional);
        Row("启用本机中继", relay);
        preference.Items.AddRange(["优先 IPv4", "优先 IPv6"]);
        Row("中继地址族", preference);
        Row("启用 ACT1 回退中继", fallback);
        Row("回退中继通告 IPv4", fallbackHost);
        Row("回退 TCP", fallbackTcp);
        Row("回退 UDP", fallbackUdp);
        Row("启用 PvP 频道", pvp);
        Row("PvP TCP", pvpPort);
        Row("生成测试版安装包", test);
        Row("配置方案", Buttons(("导入后台配置", Import), ("打开方案", LoadProfile), ("保存方案", SaveProfile)));
        Row("安装包与更新", Buttons(("仅生成", () => Generate(false)), ("生成并发布到 GitHub", () => Generate(true)), ("打开输出目录", OpenOutput)));
        Row("进度", progress);
        status.Height = 110;
        Row("结果", status);
        LoadFields(new DeploymentConfig());
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
    }

    private static NumericUpDown Port() => new() { Minimum = 1, Maximum = 65535 };

    private void Row(string label, Control input)
    {
        var row = fields.RowCount++;
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        fields.Controls.Add(input, 1, row);
    }

    private static Control Browse(TextBox box, string description)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        box.Dock = DockStyle.Fill;
        var button = new Button { Text = "浏览…", AutoSize = true };
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = description, SelectedPath = box.Text };
            if (dialog.ShowDialog() == DialogResult.OK) box.Text = dialog.SelectedPath;
        };
        row.Controls.Add(box, 0, 0);
        row.Controls.Add(button, 1, 0);
        return row;
    }

    private Control Buttons(params (string Label, Action Click)[] choices)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        foreach (var (label, action) in choices)
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (_, _) => { try { action(); } catch (Exception error) { status.Text = error.Message; } };
            row.Controls.Add(button);
        }
        return row;
    }

    private void LoadFields(DeploymentConfig config)
    {
        host.Text = config.AdvertisedHost;
        api.Text = config.AccountServiceUrl;
        fallbackHost.Text = config.FallbackRelayAdvertisedHost;
        var ports = new[] { (entrance, config.EntrancePort), (channel, config.ChannelPort),
            (additional, config.AdditionalEntrancePort), (apiPort, config.AdminPort), (character, config.CharacterPort),
            (primary, config.GameplayPrimaryPort), (secondary, config.GameplaySecondaryPort),
            (fallbackTcp, config.FallbackRelayTcpPort), (fallbackUdp, config.FallbackRelayUdpPort), (pvpPort, config.PvpPort) };
        foreach (var (field, port) in ports)
            if (port is < 1 or > 65535) throw new InvalidDataException("导入配置中存在无效端口。");
        foreach (var (field, port) in ports) field.Value = port;
        relay.Checked = config.RelayEnabled;
        regional.Checked = config.RegionalChannelsEnabled;
        fallback.Checked = config.FallbackRelayEnabled;
        pvp.Checked = config.PvpEnabled;
        preference.SelectedIndex = config.RelayPreference == "ipv4" ? 0 : 1;
    }

    private DeploymentConfig ReadFields() => new()
    {
        ScriptPvfPath = "Script.pvf",
        BindHost = "0.0.0.0",
        GmPanelEnabled = false,
        PlayerPanelEnabled = false,
        AdvertisedHost = host.Text.Trim(),
        AccountServiceUrl = api.Text.Trim().TrimEnd('/'),
        FallbackRelayAdvertisedHost = fallbackHost.Text.Trim(),
        EntrancePort = (int)entrance.Value,
        ChannelPort = (int)channel.Value,
        AdditionalEntrancePort = (int)additional.Value,
        AdminPort = (int)apiPort.Value,
        CharacterPort = (int)character.Value,
        GameplayPrimaryPort = (int)primary.Value,
        GameplaySecondaryPort = (int)secondary.Value,
        FallbackRelayTcpPort = (int)fallbackTcp.Value,
        FallbackRelayUdpPort = (int)fallbackUdp.Value,
        PvpPort = (int)pvpPort.Value,
        RelayEnabled = relay.Checked,
        RegionalChannelsEnabled = regional.Checked,
        FallbackRelayEnabled = fallback.Checked,
        PvpEnabled = pvp.Checked,
        RelayPreference = preference.SelectedIndex == 0 ? "ipv4" : "ipv6"
    };

    private void Import()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择同时包含 server.json 和 manager.json 的 config 目录" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadFields(ConfigurationStore.ReadSavedClientConfiguration(dialog.SelectedPath));
        status.Text = "已导入后台保存值。由 Compose 环境变量覆盖的选项，请按线上生效值核对。";
    }

    private void LoadProfile()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON 配置|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(dialog.FileName), Json)
            ?? throw new InvalidDataException("配置方案为空。");
        LoadFields(profile.Config ?? throw new InvalidDataException("配置方案缺少客户端连接配置。"));
        root.Text = profile.ResourceRoot;
        output.Text = profile.OutputDirectory;
        test.Checked = profile.TestBuild;
    }

    private void SaveProfile()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON 配置|*.json", FileName = "dflegacy-client-profile.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new Profile(ReadFields(), root.Text, output.Text, test.Checked), Json));
        status.Text = "配置方案已保存。";
    }

    private void OpenOutput()
    {
        if (!Directory.Exists(output.Text)) throw new DirectoryNotFoundException("输出目录还没有创建。");
        Process.Start(new ProcessStartInfo(output.Text) { UseShellExecute = true });
    }

    private async void Generate(bool publish)
    {
        if (busy) return;
        try
        {
            var source = root.Text.Trim();
            var destination = output.Text.Trim();
            if (source.Length == 0 || destination.Length == 0) throw new ArgumentException("请选择发行资源目录和输出目录。");
            var config = ReadFields();
            ConfigurationStore.Validate(config);
            var testBuild = test.Checked;
            busy = true;
            foreach (Control field in fields.Controls)
                if (field != progress && field != status) field.Enabled = false;
            status.Text = "正在读取本地构建资源…";
            progress.Style = ProgressBarStyle.Marquee;
            var reporter = new Progress<ClientExportProgress>(value =>
            {
                progress.Style = value.TotalBytes > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (value.TotalBytes > 0) progress.Value = (int)Math.Clamp(100d * value.CompletedBytes / value.TotalBytes, 0, 100);
                status.Text = value.Message;
            });
            var package = await Task.Run(() => ClientBuild.RunAsync(source, destination, config, testBuild, publish, reporter));
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 100;
            status.Text = publish ? $"GitHub 发布已验证。\r\n安装包：{package}" : $"安装包：{package}";
        }
        catch (Exception error)
        {
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 0;
            status.Text = error.Message.ReplaceLineEndings("\r\n");
        }
        finally
        {
            foreach (Control field in fields.Controls) field.Enabled = true;
            busy = false;
        }
    }

    private sealed record Profile(DeploymentConfig Config, string ResourceRoot, string OutputDirectory, bool TestBuild);
}
