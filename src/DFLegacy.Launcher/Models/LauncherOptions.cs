namespace DFLegacy.Launcher;

internal sealed class LauncherOptions
{
    public string ClientPath { get; set; } = Program.DefaultClientPath;
    public string EntranceHost { get; set; } = "127.0.0.1";
    public int EntrancePort { get; set; } = 2311;
    public string AccountServiceUrl { get; set; } = "http://127.0.0.1:8081";
    public string Theme { get; set; } = "auto";
    public string Account { get; set; } = "test";
    public string Password { get; set; } = "test";
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
}

internal sealed class LauncherAccountProfile
{
    public string Account { get; set; } = "test";
    public string Password { get; set; } = "test";
    public string? DisplayName { get; set; }
    public DateTimeOffset? CreateTime { get; set; }
    public string? OicqCode { get; set; }
    public bool RememberPassword { get; set; } = true;
}

internal sealed class LauncherRelayOptions
{
    public bool Enabled { get; set; }
    public string? TcpPorts { get; set; }
    public string? UdpPorts { get; set; }
    public string Preference { get; set; } = "ipv6";
}
