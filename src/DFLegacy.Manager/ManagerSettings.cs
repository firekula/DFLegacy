namespace DFLegacy.Manager;

public sealed record ManagerSettings
{
    public string AdvertisedHost { get; init; } = "127.0.0.1";
    public string AccountServiceUrl { get; init; } = "http://127.0.0.1:8081";
    public bool RelayEnabled { get; init; }
    public string RelayPreference { get; init; } = "ipv6";
    public bool Autostart { get; init; }
    public bool Configured { get; init; }
}

public sealed record DeploymentConfig
{
    public string ScriptPvfPath { get; init; } = "";
    public string BindHost { get; init; } = "127.0.0.1";
    public string AdminHost { get; init; } = "127.0.0.1";
    public string AdvertisedHost { get; init; } = "127.0.0.1";
    public string GameplayAdvertisedHost { get; init; } = "127.0.0.1";
    public int EntrancePort { get; init; } = 2311;
    public int ChannelPort { get; init; } = 7001;
    public int AdditionalEntrancePort { get; init; } = 8080;
    public int AdminPort { get; init; } = 8081;
    public int CharacterPort { get; init; } = 2311;
    public int GameplayPrimaryPort { get; init; } = 7002;
    public int GameplaySecondaryPort { get; init; } = 7003;
    public string ChannelName { get; init; } = "DFLegacy";
    public int MaximumUsers { get; init; } = 100;
    public double MonsterExperienceMultiplier { get; init; } = 1;
    public double ClearExperienceMultiplier { get; init; } = 1;
    public double DropRatePercent { get; init; } = 100;
    public double DropEconomicRate { get; init; } = 1;
    public string AccountServiceUrl { get; init; } = "http://127.0.0.1:8081";
    public bool RelayEnabled { get; init; }
    public string RelayPreference { get; init; } = "ipv6";
    public bool Autostart { get; init; }
}

public sealed record ConfigurationSnapshot(DeploymentConfig Saved, DeploymentConfig Effective, string[] EnvironmentOverrides, bool Configured);
