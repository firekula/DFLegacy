using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using DFLegacy.Relay;

namespace DFLegacy.Launcher;

internal static class Program
{
    internal const string DefaultClientPath = @"D:\DFLegacy\DF2008\DNF.exe";
    private const int ServerStartupAttempts = 40;
    private static readonly TimeSpan PortProbeTimeout = TimeSpan.FromMilliseconds(250);
    internal const string RelayListenAddress = "127.0.0.1";
    private static readonly int[] RelayDefaultTcpPorts = [2311, 7001, 8080];
    private static readonly int[] RelayDefaultUdpPorts = [2311, 7002, 7003];

    // WPF 的入口由 App.xaml.cs 的 App 间接承载（SDK 自动生成 Main 调用 new App().Run()）。
    // headless 分支由 App.OnStartup 调用本方法，保留原 Main 的全部行为。
    internal static async Task<int> RunHeadlessAsync(string[] args)
    {
        var noDialog = args.Contains("--no-dialog", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--self-test", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--install-ijl15", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--check-d3d9", StringComparer.OrdinalIgnoreCase);
        try
        {
            var options = LoadOptions();
            ApplyCommandLineAccountSelection(options, args);
            ValidateOptions(options);

            if (args.Contains("--check-d3d9", StringComparer.OrdinalIgnoreCase))
            {
                await EnsureD3D9AvailableAsync(options);
                return 0;
            }

            var clientPath = ResolveClientPath(options.ClientPath);
            ValidateClient(clientPath);
            ValidateScriptPvf(clientPath);
            var bundledIjl15 = Ijl15ReplacementInstaller.ValidateBundledReplacement();
            if (args.Contains("--install-ijl15", StringComparer.OrdinalIgnoreCase))
            {
                var install = Ijl15ReplacementInstaller.Install(clientPath, options.ResolveAccountServiceUrl());
                WriteLauncherLog(install.Installed
                    ? $"Installed x86 ijl15 replacement at {install.TargetPath}; sha256={install.Sha256}; backup={install.BackupPath ?? "(none)"}."
                    : $"x86 ijl15 replacement is already installed at {install.TargetPath}; sha256={install.Sha256}.");
                return 0;
            }

            _ = BuildLauncherData(options); // Actual login requires explicit credentials.

            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                ClientBootstrapInspector.ValidateForLaunch(clientPath);
                WriteLauncherLog($"Self-test passed for DFLegacy client {clientPath} (bootstrap layout checked; file version is metadata only); ijl15={bundledIjl15.Sha256}.");
                return 0;
            }

            await LaunchFlow.RunAsync(options, options.GetSelectedAccount());
            return 0;
        }
        catch (Exception exception)
        {
            WriteLauncherLog($"Launcher failed: {exception}");
            if (!noDialog)
                MessageBox.Show(exception.Message, "DFLegacy 启动失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    internal static string BuildLauncherData(LauncherOptions options)
    {
        ValidateOptions(options);
        var account = options.GetSelectedAccount();
        ValidateLauncherField(account.Account, "Account");
        ValidateLauncherField(account.Password, "Password");
        // With the relay enabled the client talks to the local relay instead of
        // the real server, which is what lets an IPv4-only client reach an IPv6
        // server.
        var entranceHost = options.Relay.Enabled
            ? RelayListenAddress
            : options.EntranceHost;
        return string.Join('?',
        [
            "99",
            entranceHost,
            options.EntrancePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            account.Account,
            account.Password,
            "0", "0", "0", "0", "0", "0", "0", "0"
        ]);
    }

    internal static LauncherOptions LoadOptions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "launcher.json");
        return new LauncherAccountStore(path).Load();
    }

    internal static string ResolveClientPath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultClientPath
            : Environment.ExpandEnvironmentVariables(configuredPath);
        return Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, path));
    }

    internal static void ValidateOptions(LauncherOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EntranceHost))
        {
            throw new InvalidDataException("EntranceHost 不能为空。");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.EntrancePort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.EntrancePort, 65535);
        ValidateLauncherField(options.EntranceHost, nameof(options.EntranceHost));
        if (options.Accounts.Count > 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(options.AccountIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
                options.AccountIndex,
                options.Accounts.Count);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.D3D9Adapter);
        ArgumentOutOfRangeException.ThrowIfNegative(options.D3D9TimeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            options.D3D9RetryIntervalSeconds,
            0.1);

        if (options.Relay.Enabled)
        {
            if (IsIPv4LoopbackLiteral(options.EntranceHost))
            {
                throw new InvalidDataException(
                    "启用 Relay 时 EntranceHost 必须是中继要拨号的服务器地址，" +
                    "不能是 127.0.0.1（否则中继只会转发给自己）。");
            }

            if (!string.Equals(options.Relay.Preference, "ipv6", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(options.Relay.Preference, "ipv4", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Relay.Preference 只支持 ipv6 或 ipv4。");
            }
        }
    }

    private static void ApplyCommandLineAccountSelection(
        LauncherOptions options,
        IReadOnlyList<string> args)
    {
        var indexArgument = Array.FindIndex(
            args.ToArray(),
            argument => string.Equals(argument, "--account-index", StringComparison.OrdinalIgnoreCase));
        if (indexArgument >= 0)
        {
            if (indexArgument + 1 >= args.Count
                || !int.TryParse(args[indexArgument + 1], out var accountIndex))
            {
                throw new InvalidDataException("--account-index 需要一个非负整数。");
            }

            options.AccountIndex = accountIndex;
        }

        var accountArgument = Array.FindIndex(
            args.ToArray(),
            argument => string.Equals(argument, "--account", StringComparison.OrdinalIgnoreCase));
        if (accountArgument < 0)
        {
            return;
        }

        if (accountArgument + 1 >= args.Count
            || string.IsNullOrWhiteSpace(args[accountArgument + 1]))
        {
            throw new InvalidDataException("--account 需要一个账号名。");
        }

        if (options.Accounts.Count == 0)
        {
            options.Account = args[accountArgument + 1];
            return;
        }

        var selectedIndex = options.Accounts.FindIndex(profile =>
            string.Equals(
                profile.Account,
                args[accountArgument + 1],
                StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
        {
            throw new InvalidDataException(
                $"Accounts 中不存在账号 {args[accountArgument + 1]}。");
        }

        options.AccountIndex = selectedIndex;
    }

    internal static void ValidateLauncherField(string value, string name)
    {
        if (string.IsNullOrEmpty(value)
            || value.Contains('?')
            || value.Contains('"')
            || value.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException(
                $"{name} 不能为空，也不能包含空白、问号或双引号。");
        }
    }

    internal static void ValidateClient(string clientPath)
    {
        if (!File.Exists(clientPath))
        {
            throw new FileNotFoundException(
                $"找不到 DFLegacy 客户端：{clientPath}",
                clientPath);
        }

        // The PE file version is metadata only. The IJL15/native patches and
        // protocol implementation target the known 2008DF layout, but the
        // launcher must not reject a locally rebuilt or relabeled executable.
    }

    internal static void ValidateScriptPvf(string clientPath)
    {
        var scriptPvfPath = Path.Combine(Path.GetDirectoryName(clientPath)!, "Script.pvf");
        if (!File.Exists(scriptPvfPath))
        {
            throw new FileNotFoundException(
                "目标客户端目录缺少 Script.pvf。",
                scriptPvfPath);
        }
    }

    // Hosts the relay inside this process when launcher.json asks for it, so the
    // user never has to start a second program. Returns null when disabled.
    internal static async Task<RelayHost?> StartRelayIfConfiguredAsync(LauncherOptions options)
    {
        if (!options.Relay.Enabled)
        {
            return null;
        }

        var preferIPv6 = !string.Equals(
            options.Relay.Preference,
            "ipv4",
            StringComparison.OrdinalIgnoreCase);
        var target = await RelayHost.ResolveTargetAsync(
            options.EntranceHost,
            preferIPv6,
            LogRelay,
            CancellationToken.None);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"无法解析中继目标 {options.EntranceHost}，请检查 Relay 与 EntranceHost 配置。");
        }

        var relay = new RelayHost(
            IPAddress.Loopback,
            target,
            BuildRelayPorts(options.Relay.TcpPorts, RelayDefaultTcpPorts, options.EntrancePort),
            BuildRelayPorts(options.Relay.UdpPorts, RelayDefaultUdpPorts, requiredPort: null),
            LogRelay);
        try
        {
            relay.Start();
        }
        catch (SocketException exception)
        {
            relay.Stop();
            throw new InvalidOperationException(
                $"本地中继无法绑定 {RelayListenAddress} 的转发端口：{exception.Message}" +
                "（端口可能被上一个未退出的实例占用）",
                exception);
        }

        WriteLauncherLog($"Local relay listening on {RelayListenAddress}; target={target}.");
        return relay;
    }

    private static void LogRelay(RelayLogLevel level, string message) =>
        WriteLauncherLog(level == RelayLogLevel.Info
            ? $"[relay] {message}"
            : $"[relay] {level}: {message}");

    // Configured ports, or the defaults, plus the entrance port the client has to
    // reach through the relay.
    private static int[] BuildRelayPorts(
        string? configured,
        int[] defaults,
        int? requiredPort)
    {
        var ports = new SortedSet<int>();
        var tokens = string.IsNullOrWhiteSpace(configured)
            ? defaults.Select(port =>
                port.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : configured.Split(
                [',', ';', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var port) || port is < 1 or > 65535)
            {
                throw new InvalidDataException($"Relay 端口列表包含无效项：{token}");
            }

            ports.Add(port);
        }

        if (requiredPort is { } required)
        {
            ports.Add(required);
        }

        return [.. ports];
    }

    private static bool IsIPv4LoopbackLiteral(string host) =>
        host.StartsWith("127.", StringComparison.Ordinal);

    internal static async Task EnsureServerReadyAsync(LauncherOptions options)
    {
        if (await IsPortOpenAsync(options.EntranceHost, options.EntrancePort))
        {
            return;
        }

        if (!options.StartServer)
        {
            throw new InvalidOperationException(
                $"入口服务 {options.EntranceHost}:{options.EntrancePort} 未监听，且 StartServer=false。");
        }

        if (!IsLoopbackHost(options.EntranceHost))
        {
            throw new InvalidOperationException(
                "StartServer 只能自动启动本机入口服务；远程地址请先手动启动服务端。");
        }

        var serverPath = Path.Combine(AppContext.BaseDirectory, "DFLegacy.Server.exe");
        if (!File.Exists(serverPath))
        {
            throw new FileNotFoundException(
                "找不到模拟服务端，请保留发布目录内的全部文件。",
                serverPath);
        }

        Process.Start(new ProcessStartInfo(serverPath)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        for (var attempt = 0; attempt < ServerStartupAttempts; attempt++)
        {
            await Task.Delay(250);
            if (await IsPortOpenAsync(options.EntranceHost, options.EntrancePort))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"模拟服务端启动失败，{options.EntranceHost}:{options.EntrancePort} 没有开始监听。");
    }

    internal static async Task EnsureD3D9AvailableAsync(LauncherOptions options)
    {
        var result = await D3D9DeviceProbe.WaitForDeviceAsync(
            options.D3D9Adapter,
            TimeSpan.FromSeconds(options.D3D9TimeoutSeconds),
            TimeSpan.FromSeconds(options.D3D9RetryIntervalSeconds));
        if (!result.Available)
        {
            WriteLauncherLog(
                $"D3D9 preflight failed after {result.Attempt} attempt(s): {result.Summary}");
            throw new InvalidOperationException(
                "当前桌面没有可用的 Direct3D 9 显示设备，已拒绝启动 DNF.exe。" +
                $"{Environment.NewLine}{result.Summary}" +
                $"{Environment.NewLine}请恢复显示输出或图形设备后再试。");
        }

        WriteLauncherLog(
            $"D3D9 preflight passed after {result.Attempt} attempt(s): {result.Summary}.");
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(PortProbeTimeout);
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsLoopbackHost(string host) =>
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

    internal static void TryRaisePriority(Process client)
    {
        try
        {
            client.PriorityClass = ProcessPriorityClass.AboveNormal;
            client.PriorityBoostEnabled = true;
        }
        catch (Exception exception)
        {
            WriteLauncherLog(
                $"Could not raise client process {client.Id} priority: {exception.Message}");
        }
    }

    internal static void WriteLauncherLog(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "launcher.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never prevent the target process from starting.
        }
    }
}
