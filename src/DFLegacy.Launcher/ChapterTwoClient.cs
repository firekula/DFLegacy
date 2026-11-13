using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

namespace DFLegacy.Launcher;

internal static class ChapterTwoClient
{
    internal const string Host = "127.0.0.2";
    private const int EntrancePort = 42312;
    private const int AccountPort = 48091;
    private const string ScriptName = "Start-Client.ps1";
    private const string ImageName = "DNF-rebuilt-v3-probe.exe";
    private const string HostName = "ChapterTwoHost.exe";
    private const string PreviewName = "ChapterTwoPresentation.exe";
    private const string BrokerName = "dnf_startup_trace.exe";
    private const string QuietBrokerName = "QuietStartupBroker.exe";
    private const string ImageHash = "9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB";
    private const string HostHash = "B72C2C7DFF350BCE0F8E5A178740CB5BE703BC9559956951BDBBF8E573A71139";
    private const string BrokerHash = "751AA409EC440FE987AB4462EEA057BD9BB3F650BA93FC6C1498D541F55F5079";
    private const string ResourcePrefix = "ChapterTwo.";
    private static readonly (string Name, string Hash)[] Binaries =
    [
        (ImageName, ImageHash), (HostName, HostHash), (BrokerName, BrokerHash)
    ];

    internal static bool IsBundled { get; } = typeof(ChapterTwoClient).Assembly
        .GetManifestResourceInfo(ResourcePrefix + ScriptName) is not null;
    internal static bool HasPresentationPreview { get; } = typeof(ChapterTwoClient).Assembly
        .GetManifestResourceInfo(ResourcePrefix + PreviewName) is not null;
    private static bool HasQuietBroker { get; } = typeof(ChapterTwoClient).Assembly
        .GetManifestResourceInfo(ResourcePrefix + QuietBrokerName) is not null;

    internal static ChapterTwoDisplayOptions DisplayOptions(LauncherOptions options) =>
        HasPresentationPreview && options.ChapterTwoDisplay is { Filter: "sharp" or "point" or "linear" } display
        && ClientDisplayConfiguration.Presets.Any(p => p.Width == display.Width && p.Height == display.Height)
            ? display : new(640, 480, "legacy");

    internal static void ApplyLocalProfile(LauncherOptions options)
    {
        if (!IsBundled) return;
        options.ClientPath = "DNF.exe";
        options.EntranceHost = Host;
        options.EntrancePort = EntrancePort;
        options.AccountServicePort = AccountPort;
        options.AccountServiceUrl = $"http://{Host}:{AccountPort}";
        options.Servers = [new() { Id = "chapter-two-local", Name = "第二章本机", Host = Host,
            Port = EntrancePort, AccountServicePort = AccountPort }];
        options.SelectedServerId = "chapter-two-local";
        options.StartServer = false;
        options.Relay = new();
        if (!Updates.ChapterUpdatePolicy.IsManaged(AppContext.BaseDirectory))
        {
            options.DisableGitHubUpdates = true;
            options.UpdateServiceUrl = "";
            options.UpdateRepository = "";
        }
        options.Diagnostics.Enabled = false;
        options.Diagnostics.ServiceUrl = "";
    }

    internal const string CredentialHint = "第二章账号需要 4–16 位英文字母或数字，密码需要 6–16 位英文字母或数字。";
    private const string LoginCredentialHint = "第二章账号需要 4–16 位英文字母或数字，登录密码需要 1–16 位英文字母或数字。";

    internal static bool IsValidCredential(string value, int minimum) =>
        value.Length >= minimum && value.Length <= 16 && value.All(char.IsAsciiLetterOrDigit);

    internal static void ValidateSource(string clientPath)
    {
        var root = Path.GetDirectoryName(clientPath)!;
        ClientFileIdentity.RequireSameDirectory(AppContext.BaseDirectory, root);
        VerifyFile(clientPath, "4E59EE7346D92DC3E3374E171269CBC4EE4E97807013B18306EAD160829AB7E6");
        VerifyFile(Path.Combine(root, "Script.pvf"),
            "0CB607B9502084ECB9E8CD45A67087E1E96F58F4AC5420D3FC673ADC73788AE0",
            "FB0EEF35483852E0689D037FA6B5018B750940B0E35FD8A73FD35395C0396144",
            "4FA23E0270CCA7BD3D97F333C9F8F406193DB77B37FF3CACFCD3DCB34C2BCA00",
            "C20741D48A641A525F7C214FBEDE555783FB737B344D14EEADA47AECA8337669",
            "639489430820E62F50D5D239B1C16AB8B65AFCE97C528BEBB03E3B723CE55D85",
            "06734F9C5B23CF3BCE364890403AE1661311447CA8ADEBD1B79FCFD8E00FAB9B");
        var images = Path.Combine(root, "ImagePacks2");
        if (!Directory.Exists(images) || ClientLogPaths.HasLink(images))
            throw new InvalidDataException("请将第二章启动器放在原版 DNFS1C2 客户端目录，与 DNF.exe 和 ImagePacks2 同层。");
    }

    internal static void VerifyEmbeddedPayload()
    {
        RequireOrdinaryProcessComponents();
        foreach (var (name, hash) in Binaries)
            VerifyResource(name, hash);
        VerifyMachine(PreviewName, Machine.I386);
        VerifyMachine(QuietBrokerName, Machine.Amd64);
    }

    private static void RequireOrdinaryProcessComponents()
    {
        if (!HasPresentationPreview || !HasQuietBroker)
            throw new InvalidDataException("第二章普通启动组件不完整，请使用完整的第二章启动器。");
    }

    internal static async Task RunAsync(string clientPath, LauncherOptions options, LauncherAccountProfile account,
        CancellationToken cancellationToken)
    {
        // Existing C1 accounts may have short passwords. Creation/reset policy
        // must not prevent the server from authenticating those unchanged accounts.
        if (!IsValidCredential(account.Account, 4) || !IsValidCredential(account.Password, 1))
            throw new InvalidDataException(LoginCredentialHint);
        RequireOrdinaryProcessComponents();
        ValidateSource(clientPath);
        Program.LogClientPath = clientPath;
        await Program.EnsureServerReadyAsync(options);
        await Program.EnsureD3D9AvailableAsync(options);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetDirectoryName(clientPath)!;
        var display = DisplayOptions(options);
        var dialogue = ChapterTwoDialogueOptions.Normalize(options.ChapterTwoDialogue);
        var hostName = PreviewName;
        var brokerName = QuietBrokerName;
        var hostHash = HashResource(hostName);
        var brokerHash = HashResource(brokerName);
        var scriptHash = HashResource(ScriptName);
        var version = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(hostHash + brokerHash + scriptHash)));
        var runtime = Path.Combine(root, ".dflegacy", "chapter-two", version[..16]);
        ClientLogPaths.EnsureDirectory(runtime);
        ClientLogPaths.EnsureDirectory(ClientLogPaths.GetDirectory(clientPath));
        EnsureExtracted(ImageName, Path.Combine(root, ImageName), ImageHash);
        EnsureExtracted(brokerName, Path.Combine(runtime, BrokerName), brokerHash);
        EnsureExtracted(ScriptName, Path.Combine(runtime, ScriptName), scriptHash);
        var host = Path.Combine(runtime, hostName);
        EnsureExtracted(hostName, host, hostHash);
        await using var payment = await ChapterTwoPaymentRelay.StartAsync(runtime, options.ResolveAccountServiceUrl(), cancellationToken);

        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(runtime, ScriptName), "-Game", root, "-HostFile", host, "-Filter", display.Filter,
            "-Width", display.Width.ToString(CultureInfo.InvariantCulture),
            "-Height", display.Height.ToString(CultureInfo.InvariantCulture),
            "-DialogueFontSize", dialogue.FontSize.ToString(CultureInfo.InvariantCulture),
            "-DialogueLineGap", dialogue.LineGap.ToString(CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(argument);
        if (payment is not null)
        {
            info.Environment["DFLEGACY_CH2_PAYMENT_URL"] = payment.BaseUrl;
            info.Environment["DFLEGACY_CH2_PAYMENT_REPLIES"] = payment.ReplyDirectory;
        }
        if (Program.DetailedCaptureEnabled) info.ArgumentList.Add("-DetailedDiagnostics");
        info.Environment["DFLEGACY_CH2_SETTINGS_PATH"] = ChapterTwoGameSettings.Prepare(root);
        info.Environment.Remove("DFLEGACY_CH2_DEBUG_ITEMS");
        info.Environment.Remove("DFLEGACY_CH2_DEBUG_PVF");
        ChapterTwoDebugSession? debug = null;
        if (options.ChapterTwoDebugEnabled)
        {
            var view = await Task.Run(() => new ChapterTwoDebugResources(Path.Combine(root, "Script.pvf"))
                .BuildDebugView(Path.Combine(root, ".dflegacy", "chapter-two", "debug-items")), cancellationToken);
            info.Environment["DFLEGACY_CH2_DEBUG_ITEMS"] = "1";
            info.Environment["DFLEGACY_CH2_DEBUG_PVF"] = view.Path;
            Program.WriteLauncherLog($"Chapter Two debug labels enabled: {view.Count} item scripts; private view={Path.GetFileName(view.Path)}.");
            debug = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => new ChapterTwoDebugSession(root, account.Account));
        }

        Program.WriteLauncherLog("Starting Chapter Two ordinary desktop process with the isolated local profile.");
        try
        {
        await ClientProcessLifetime.RunAsync(
            () => Process.Start(info) ?? throw new InvalidOperationException("第二章启动进程未创建。"),
            async (process, token) =>
            {
                var stdout = ObserveClientOutputAsync(process, root, payment, debug, token);
                var stderr = process.StandardError.ReadToEndAsync(token);
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { account.Account, account.Password }));
                process.StandardInput.Close();
                await process.WaitForExitAsync(token);
                await stdout;
                var error = (await stderr).Trim();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(error.Length > 0 ? error : "请检查 Logs/DFLegacy 下的第二章启动日志。");
                Program.WriteLauncherLog("Chapter Two client exited.");
            }, handoffOnSuccess: false, cancellationToken);
        }
        finally
        {
            if (debug is not null) await System.Windows.Application.Current.Dispatcher.InvokeAsync(debug.Dispose);
        }
    }

    private static async Task ObserveClientOutputAsync(Process bootstrap, string gameDirectory,
        ChapterTwoPaymentRelay? payment, ChapterTwoDebugSession? debug, CancellationToken token)
    {
        using var owned = new Diagnostics.ChapterTwoLaunchDiagnostics(bootstrap.StartTime, gameDirectory);
        try
        {
        while (await bootstrap.StandardOutput.ReadLineAsync(token) is { } line)
        {
            var fields = line.Split(' ');
            if (fields.Length == 4 && fields[0] == "CHAPTER_TWO_ATTEMPT")
            {
                owned.Begin(fields[1], fields[2], fields[3]);
            }
            else if (fields.Length == 2 && fields[0] == "CHAPTER_TWO_FINISH" && fields[1] is "retry" or "final")
            {
                owned.Finish(fields[1] == "retry", token.IsCancellationRequested);
            }
            else if (fields.Length == 2 && fields[0] == "OWNED_PID" && ReadProcessId(fields[1], out var childId))
            {
                owned.SetGame(childId);
                if (debug is not null && owned.IsRunning(childId))
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => debug.Bind(childId));
            }
            else if (payment is not null && fields.Length == 5 && fields[0] == "LOCAL_PAY_REQUEST" &&
                ReadProcessId(fields[1], out var gameId))
            {
                if (owned.IsRunning(gameId))
                    await payment.RespondAsync(gameId, fields[2], fields[3], fields[4], token);
                else
                    Program.WriteLauncherLog("Chapter Two local payment request did not match the active game.");
            }
            else if (line.StartsWith("Chapter Two instance ", StringComparison.Ordinal))
            {
                Program.WriteLauncherLog(line);
            }
        }
        }
        finally { owned.Finish(false, token.IsCancellationRequested); }
    }

    private static bool ReadProcessId(string value, out int id) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private static Stream OpenResource(string name) => typeof(ChapterTwoClient).Assembly
        .GetManifestResourceStream(ResourcePrefix + name)
        ?? throw new InvalidDataException($"第二章启动器缺少内嵌组件。{name}");

    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    private static string HashResource(string name)
    {
        using var source = OpenResource(name);
        return Hash(source);
    }

    private static void VerifyMachine(string name, Machine machine)
    {
        using var source = OpenResource(name);
        using var image = new PEReader(source);
        if (image.PEHeaders.CoffHeader.Machine != machine)
            throw new InvalidDataException($"第二章组件架构不匹配。{name}");
    }

    private static void VerifyResource(string name, string hash)
    {
        using var source = OpenResource(name);
        if (Hash(source) != hash) throw new InvalidDataException($"第二章启动组件校验失败。{name}");
    }

    private static void VerifyFile(string path, params string[] hashes)
    {
        if (ClientLogPaths.HasLink(path) || !File.Exists(path))
            throw new InvalidDataException($"第二章客户端文件不存在或路径是链接。{path}");
        using var file = File.OpenRead(path);
        if (!hashes.Contains(Hash(file), StringComparer.Ordinal))
            throw new InvalidDataException($"第二章客户端文件与构建版本不匹配。{path}");
    }

    private static void EnsureExtracted(string name, string path, string hash)
    {
        if (ClientLogPaths.HasLink(path)) throw new InvalidDataException($"第二章启动组件路径不能是链接。{path}");
        if (!File.Exists(path))
        {
            var temporary = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var source = OpenResource(name))
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(target);
                VerifyFile(temporary, hash);
                try { File.Move(temporary, path, overwrite: false); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally { File.Delete(temporary); }
        }
        VerifyFile(path, hash);
    }
}
