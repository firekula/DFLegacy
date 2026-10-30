using System.Net;

namespace DFLegacy.Manager;

internal sealed record ManagerHostOptions(string Root, string? DataDirectory, IPAddress Listen, int Port, bool NoBrowser, HashSet<string> AllowedHosts)
{
    internal static ManagerHostOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var noBrowser = Environment.GetEnvironmentVariable("DFLEGACY_MANAGER_NO_BROWSER") is "true" or "1";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--no-browser") { noBrowser = true; continue; }
            if (args[index] is not ("--root" or "--data-dir" or "--listen" or "--port" or "--allowed-hosts") || index == args.Length - 1) throw new ArgumentException("参数：--root 路径 --data-dir 路径 --listen IP --port 端口 --no-browser --allowed-hosts 域名列表");
            values[args[index]] = args[++index];
        }
        string? Value(string key, string environment) => values.GetValueOrDefault(key) ?? Environment.GetEnvironmentVariable(environment);
        var root = Value("--root", "DFLEGACY_MANAGER_ROOT") ?? AppContext.BaseDirectory;
        var data = Value("--data-dir", "DFLEGACY_MANAGER_DATA");
        if (!IPAddress.TryParse(Value("--listen", "DFLEGACY_MANAGER_LISTEN") ?? "127.0.0.1", out var listen)) throw new ArgumentException("管理器监听地址必须为 IP。");
        if (!int.TryParse(Value("--port", "DFLEGACY_MANAGER_PORT") ?? "18080", out var port) || port is < 1 or > 65535) throw new ArgumentException("管理器端口必须为 1–65535。");
        var hosts = new HashSet<string>((Value("--allowed-hosts", "DFLEGACY_MANAGER_ALLOWED_HOSTS") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        return new(root, data, listen, port, noBrowser, hosts);
    }
    internal bool AllowsHost(string host)
    {
        host = host.Trim('[', ']');
        if (AllowedHosts.Contains(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        return IPAddress.IsLoopback(address) || address.Equals(Listen) || Listen.Equals(IPAddress.Any) || Listen.Equals(IPAddress.IPv6Any);
    }
}
