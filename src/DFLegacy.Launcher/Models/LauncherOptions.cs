using System.Text.Json;
using System.Text.Json.Serialization;

namespace DFLegacy.Launcher;

internal sealed class LauncherOptions
{
    public string UpdateRepository { get; set; } = "firekula/DFLegacy";
    public bool IncludePrereleaseUpdates { get; set; } = true;
    public string ClientPath { get; set; } = Program.DefaultClientPath;
    public string EntranceHost { get; set; } = "127.0.0.1";
    public int EntrancePort { get; set; } = 2311;

    /// <summary>
    /// 账号服务（8081 后台）端口。默认与入口同域名、只用独立端口：
    /// 实际地址由 <see cref="ResolveAccountServiceUrl"/> 拼成 http://{EntranceHost}:{AccountServicePort}。
    /// 发布前改这一个端口即可（域名跟随 EntranceHost，不用改两处）。
    /// </summary>
    public int AccountServicePort { get; set; } = 8081;

    /// <summary>
    /// 可选：直接覆盖账号 API 地址（内网走别的域名、或本机把账号服务指向别处时才需要）。
    /// 留空即按「入口域名 + 账号服务端口」自动拼。
    /// </summary>
    public string AccountServiceUrl { get; set; } = string.Empty;

    public List<LauncherServerProfile> Servers { get; set; } = [];
    public string? SelectedServerId { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string Theme { get; set; } = "auto";
    public string Account { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<LauncherAccountProfile> Accounts { get; set; } = [];
    public int AccountIndex { get; set; }
    public bool StartServer { get; set; } = true;
    public int D3D9Adapter { get; set; }
    public double D3D9TimeoutSeconds { get; set; } = 30;
    public double D3D9RetryIntervalSeconds { get; set; } = 1;
    public LauncherRelayOptions Relay { get; set; } = new();

    public LauncherAccountProfile GetSelectedAccount() =>
        Accounts.Count == 0
            ? new LauncherAccountProfile { Account = Account, Password = Password }
            : Accounts[AccountIndex];

    /// <summary>
    /// 账号 HTTP API 的实际地址：优先用显式覆盖值，否则按「入口域名 + 账号服务端口」拼。
    /// 域名与入口共用，发布前只需要改端口。
    /// </summary>
    public string ResolveAccountServiceUrl() =>
        string.IsNullOrWhiteSpace(AccountServiceUrl)
            ? $"http://{(EntranceHost.Contains(':') && !EntranceHost.StartsWith('[') ? $"[{EntranceHost}]" : EntranceHost)}:{AccountServicePort}"
            : AccountServiceUrl.Trim();
}

internal sealed class LauncherAccountProfile
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public string Account { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTimeOffset? CreateTime { get; set; }
    public string? OicqCode { get; set; }
    public bool RememberPassword { get; set; } = true;
}

internal sealed class LauncherRelayOptions
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public bool Enabled { get; set; }
    public string? TcpPorts { get; set; }
    public string? UdpPorts { get; set; }
    public string Preference { get; set; } = "ipv6";
}

internal sealed class LauncherServerProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = "telecom";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 2311;
    public int AccountServicePort { get; set; } = 8081;
    public string AccountServiceUrl { get; set; } = string.Empty;
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
