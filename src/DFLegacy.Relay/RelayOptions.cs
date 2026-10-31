using System.Globalization;
using System.Net;

namespace DFLegacy.Relay;

internal enum AddressPreference
{
    IPv6,
    IPv4
}

internal sealed record RelayOptions(
    string ServerHost,
    IPAddress BindAddress,
    IReadOnlyList<int> TcpPorts,
    IReadOnlyList<int> UdpPorts,
    AddressPreference Preference,
    int? ExitWithProcessId,
    string? ExitWithImage,
    bool ShowHelp)
{
    private static readonly int[] DefaultTcpPorts = [2311, 7001, 8080];
    private static readonly int[] DefaultUdpPorts = [2311, 7002, 7003];

    public const string Usage = """
        DFLegacy 本地中继：让只支持 IPv4 的原版客户端经本机中继连接 IPv6 服务器。

        用法：
          DFLegacy.Relay --server <主机> [选项]

        选项：
          --server <主机>        目标服务器地址，域名或 IP（IPv6 字面量可加方括号）。必填。
          --bind <地址>          本地监听地址，默认 127.0.0.1。
          --tcp <端口列表>       转发的 TCP 端口，默认 2311,7001,8080。
          --udp <端口列表>       转发的 UDP 端口，默认 2311,7002,7003。
          --prefer <ipv6|ipv4>   域名解析优先使用的地址族，默认 ipv6。
          --exit-with-pid <pid>  该进程退出时中继同时退出。
          --exit-with-image <名> 先等待该进程名出现，之后其全部退出时中继退出；
                                 适合随客户端自动收尾（如 --exit-with-image DNF.exe）。
          -h, --help             显示本帮助。

        说明：
          - 端口按 1:1 映射，客户端仍按原端口访问本机中继。
          - 服务端需要把 Channel.Host 与 GameplayDatagram.AdvertisedHost 设为 127.0.0.1，
            客户端则由启动器指向 127.0.0.1，详见 DEPLOYING.md。
        """;

    public static bool TryParse(
        string[] args,
        out RelayOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;

        string? serverHost = null;
        var bindAddress = IPAddress.Loopback;
        IReadOnlyList<int> tcpPorts = DefaultTcpPorts;
        IReadOnlyList<int> udpPorts = DefaultUdpPorts;
        var preference = AddressPreference.IPv6;
        int? exitWithPid = null;
        string? exitWithImage = null;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "-h" or "--help":
                    showHelp = true;
                    break;
                case "--server":
                    if (!TryTakeValue(args, ref index, out serverHost, out error))
                    {
                        return false;
                    }

                    break;
                case "--bind":
                    if (!TryTakeValue(args, ref index, out var bindText, out error)
                        || !IPAddress.TryParse(bindText, out bindAddress))
                    {
                        error = $"--bind 需要一个 IP 字面量：{bindText}";
                        return false;
                    }

                    break;
                case "--tcp":
                    if (!TryTakeValue(args, ref index, out var tcpText, out error)
                        || !TryParsePorts(tcpText, out tcpPorts, out error))
                    {
                        return false;
                    }

                    break;
                case "--udp":
                    if (!TryTakeValue(args, ref index, out var udpText, out error)
                        || !TryParsePorts(udpText, out udpPorts, out error))
                    {
                        return false;
                    }

                    break;
                case "--prefer":
                    if (!TryTakeValue(args, ref index, out var preferText, out error))
                    {
                        return false;
                    }

                    preference = preferText.ToLowerInvariant() switch
                    {
                        "ipv6" => AddressPreference.IPv6,
                        "ipv4" => AddressPreference.IPv4,
                        _ => (AddressPreference)(-1)
                    };
                    if ((int)preference < 0)
                    {
                        error = "--prefer 只支持 ipv6 或 ipv4。";
                        return false;
                    }

                    break;
                case "--exit-with-pid":
                    if (!TryTakeValue(args, ref index, out var pidText, out error)
                        || !int.TryParse(
                            pidText,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var pid))
                    {
                        error = $"参数 --exit-with-pid 需要一个整数：{pidText}";
                        return false;
                    }

                    exitWithPid = pid;
                    break;
                case "--exit-with-image":
                    if (!TryTakeValue(args, ref index, out exitWithImage, out error)
                        || string.IsNullOrWhiteSpace(exitWithImage))
                    {
                        error = "--exit-with-image 需要一个进程名，例如 DNF.exe。";
                        return false;
                    }

                    exitWithImage = Path.GetFileNameWithoutExtension(exitWithImage.Trim());
                    break;
                default:
                    error = $"无法识别的参数：{argument}";
                    return false;
            }
        }

        if (showHelp)
        {
            options = new RelayOptions(
                string.Empty,
                bindAddress,
                tcpPorts,
                udpPorts,
                preference,
                null,
                null,
                true);
            return true;
        }

        if (string.IsNullOrWhiteSpace(serverHost))
        {
            error = "缺少必填参数 --server。";
            return false;
        }

        if (tcpPorts.Count == 0 && udpPorts.Count == 0)
        {
            error = "--tcp 与 --udp 不能同时为空。";
            return false;
        }

        options = new RelayOptions(
            serverHost.Trim(),
            bindAddress,
            tcpPorts,
            udpPorts,
            preference,
            exitWithPid,
            exitWithImage,
            false);
        return true;
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        out string value,
        out string error)
    {
        error = string.Empty;
        if (index + 1 >= args.Length)
        {
            error = $"参数 {args[index]} 缺少取值。";
            value = string.Empty;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool TryParsePorts(
        string text,
        out IReadOnlyList<int> ports,
        out string error)
    {
        error = string.Empty;
        var parsed = new List<int>();
        foreach (var token in text.Split(
                     [',', ';', ' '],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(
                    token,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var port)
                || port is < 1 or > 65535)
            {
                error = $"端口无效：{token}";
                ports = [];
                return false;
            }

            parsed.Add(port);
        }

        ports = [.. parsed.Distinct().Order()];
        return true;
    }
}
