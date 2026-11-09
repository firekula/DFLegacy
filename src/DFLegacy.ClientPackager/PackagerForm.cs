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
        Row("V4 更新资源", Buttons(("资源选择、差异与迁移…", () => { using var form = new V4ResourcesForm(output.Text); form.ShowDialog(this); })));
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

internal sealed class V4ResourcesForm : Form
{
    private readonly TableLayoutPanel fields = new() { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
    private readonly TextBox source = new(), output = new(), version = new(), commit = new(), repository = new() { Text = "firekula/DFLegacy" },
        channel = new() { Text = "gitea-test" }, previous = Multi(42), reuse = Multi(42), includes = Multi(65), excludes = Multi(42),
        migrations = Multi(75), retired = Multi(65), targetCommit = new();
    private readonly ComboBox provider = Choice("Gitea 测试源", "Gitee 测试源", "GitHub 正式源"), architecture = Choice("x86", "x64"), flavor = Choice("test", "normal");
    private readonly NumericUpDown sequence = new() { Minimum = 1, Maximum = 999999999, Value = 1 },
        minimum = new() { Minimum = 0, Maximum = 999999999, Value = 0 };
    private readonly TextBox preview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Height = 260 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(780, 0) };
    private string? previewRequest, builtDirectory;
    private bool busy;

    internal V4ResourcesForm(string destination)
    {
        Text = "V4 更新资源：预览、生成、发布与验证";
        ClientSize = new Size(900, 860); MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Microsoft YaHei UI", 9); AutoScaleMode = AutoScaleMode.Dpi;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(fields); Controls.Add(scroll);
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        output.Text = destination;
        Row("客户端文件目录", Browse(source, true)); Row("新构建输出父目录", Browse(output, true));
        Row("提供方", provider); Row("公开仓库 owner/repo", repository); Row("渠道", channel);
        Row("不可变版本 / Tag", version); Row("源构建 Commit (40 位)", commit);
        Row("架构", architecture); Row("类型", flavor); Row("发布顺序", sequence); Row("最小支持顺序", minimum);
        Row("旧清单（每行一个）", Browse(previous, false)); Row("复用清单（每行一个）", Browse(reuse, false));
        includes.Text = "DFLegacy.Launcher.exe\r\nDFLegacy.Client.dll\r\nDFLegacy.Diagnostics.exe\r\nDFLegacy.CrashCapture.exe\r\nScript.pvf\r\nImagePacks2/**";
        excludes.Text = "*.pdb\r\n*.tmp"; migrations.Text = "[]"; retired.Text = "[]";
        Row("包含规则（每行一个）", includes); Row("排除规则（优先）", excludes);
        Row("配置迁移 JSON 数组", migrations); Row("退役 JSON 数组", retired);
        Row("公开仓库 Commit (40 位)", targetCommit);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        AddAction(actions, "扫描与预览", () => Execute("preview")); AddAction(actions, "生成 V4 资源", () => Execute("build"));
        AddAction(actions, "验证本地产物", () => Execute("verify")); AddAction(actions, "发布已生成产物…", () => Execute("publish"));
        Row("操作", actions); Row("文件差异 / 迁移预览", preview); Row("结果", status);
        status.Text = "默认仅选择列出的资源。账号、配置、日志、存档和私有目录始终排除。V3 桥接包仍由原发行工具生成。";
        provider.SelectedIndexChanged += (_, _) => channel.Text = provider.SelectedIndex switch { 0 => "gitea-test", 1 => "gitee-test", _ => "stable" };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
    }

    private static TextBox Multi(int height) => new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = height };
    private static ComboBox Choice(params string[] values)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; box.Items.AddRange(values); box.SelectedIndex = 0; return box;
    }
    private void Row(string label, Control control)
    {
        var row = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right; fields.Controls.Add(control, 1, row);
    }
    private static Control Browse(TextBox box, bool directory)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true }; panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        box.Dock = DockStyle.Fill; panel.Controls.Add(box, 0, 0);
        var button = new Button { Text = "浏览…", AutoSize = true }; panel.Controls.Add(button, 1, 0);
        button.Click += (_, _) =>
        {
            if (directory) { using var dialog = new FolderBrowserDialog { SelectedPath = box.Text }; if (dialog.ShowDialog() == DialogResult.OK) box.Text = dialog.SelectedPath; }
            else { using var dialog = new OpenFileDialog { Filter = "V4 清单|*.json", Multiselect = true }; if (dialog.ShowDialog() == DialogResult.OK) box.Text = string.Join(Environment.NewLine, dialog.FileNames); }
        };
        return panel;
    }
    private static void AddAction(FlowLayoutPanel panel, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true }; button.Click += (_, _) => action(); panel.Controls.Add(button);
    }
    private static string[] Lines(TextBox box) => box.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    private string Request()
    {
        using var migrationJson = JsonDocument.Parse(migrations.Text); using var retiredJson = JsonDocument.Parse(retired.Text);
        if (migrationJson.RootElement.ValueKind != JsonValueKind.Array || retiredJson.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("迁移与退役必须为 JSON 数组。");
        return JsonSerializer.Serialize(new { root = source.Text.Trim(), version = version.Text.Trim(), commit = commit.Text.Trim(),
            architecture = architecture.Text, flavor = flavor.Text, channel = channel.Text.Trim(), sequence = (long)sequence.Value,
            minimum_sequence = (long)minimum.Value, includes = Lines(includes), excludes = Lines(excludes), previous = Lines(previous), reuse = Lines(reuse),
            migrations = migrationJson.RootElement, retired = retiredJson.RootElement,
            provider = provider.SelectedIndex switch { 0 => "gitea", 1 => "gitee", _ => "github" }, repository = repository.Text.Trim() });
    }
    private void ShowPreview(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!root.TryGetProperty("diff", out var differences)) { preview.Text = json; return; }
        var lines = differences.EnumerateArray().Select(x => $"{x.GetProperty("status").GetString(),-10} {x.GetProperty("size").GetInt64(),12:N0}  {x.GetProperty("path").GetString()}").ToList();
        lines.Add("\r\n配置迁移："); lines.Add(root.GetProperty("migrations").ToString()); lines.Add("\r\n退役："); lines.Add(root.GetProperty("retired").ToString());
        lines.Add("\r\n排除："); lines.AddRange(root.GetProperty("excluded").EnumerateArray().Select(x => x.GetProperty("path").GetString() + " (" + x.GetProperty("reason").GetString() + ")"));
        preview.Text = string.Join(Environment.NewLine, lines);
        status.Text = $"完整文件 {root.GetProperty("totalBytes").GetInt64():N0} 字节；新增上传 {root.GetProperty("uploadBytes").GetInt64():N0} 字节；复用 {root.GetProperty("reusedBytes").GetInt64():N0} 字节。";
    }
    private async void Execute(string command)
    {
        if (busy) return;
        string? requestFile = null;
        try
        {
            var request = command is "preview" or "build" ? Request() : null;
            if (command == "build" && request != previewRequest) { status.Text = "选择或元数据已变化，请先重新扫描与预览，再确认生成。"; return; }
            if (command is "verify" or "publish" && builtDirectory == null) throw new InvalidOperationException("请先生成本次 V4 资源。");
            if (command == "publish")
            {
                using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(builtDirectory!, "receipt.json")));
                var r = receipt.RootElement;
                var identity = $"{r.GetProperty("provider").GetString()} / {r.GetProperty("repository").GetString()} / {r.GetProperty("manifest").GetProperty("version").GetString()}";
                if (MessageBox.Show(this, $"将发布已生成并验证的产物：\n{identity}\n\n发布凭据仅从专用环境变量读取；不会推送源代码或 Git 历史。继续？", "发布 V4 资源", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            busy = true; foreach (Control c in fields.Controls) if (c != status && c != preview) c.Enabled = false;
            status.Text = "正在执行 " + command + "…";
            if (request != null)
            {
                requestFile = Path.Combine(Path.GetTempPath(), "dflegacy-v4-request-" + Guid.NewGuid().ToString("N") + ".json");
                await File.WriteAllTextAsync(requestFile, request);
            }
            var destination = command == "build" ? Path.Combine(Path.GetFullPath(output.Text), "v4-" + version.Text.Trim() + "-" + architecture.Text + "-" + flavor.Text + "-" + Guid.NewGuid().ToString("N")[..8]) : null;
            var result = await ClientBuild.RunV4Async(command, requestFile ?? builtDirectory!, destination, targetCommit.Text.Trim());
            ShowPreview(result);
            if (command == "preview") previewRequest = request;
            if (command == "build") { builtDirectory = destination; status.Text += "\r\n产物：" + destination; }
            if (command == "verify") status.Text = "本地产物完整校验通过：" + builtDirectory;
            if (command == "publish") status.Text = "发布完成，清单及资源已通过匿名下载校验。";
        }
        catch (Exception error) { status.Text = error.Message; }
        finally
        {
            if (requestFile != null) File.Delete(requestFile);
            foreach (Control c in fields.Controls) c.Enabled = true; busy = false;
        }
    }
}
