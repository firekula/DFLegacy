namespace DFLegacy.Server;

public sealed class ServerOptions
{
    public AdminOptions Admin { get; set; } = new();
    public ListenerOptions Entrance { get; set; } = new() { Port = 8080 };
    public ListenerOptions CharacterDatagram { get; set; } = new() { Host = "127.0.0.1", Port = 2311 };
    public GameplayDatagramOptions GameplayDatagram { get; set; } = new();
    public ListenerOptions GameProbe { get; set; } = new() { Port = 7001, Enabled = false };
    public string DataPath { get; set; } = "data/state.json";
    public string ScriptPvfPath { get; set; } = "data/Script.pvf";
    public string SkillScriptPath { get; set; } = "";
    public string OriginalSkillScriptPath { get; set; } = "";
    public TownSyncOptions TownSync { get; set; } = new();
    public DebugOptions Debug { get; set; } = new();
    public ChannelOptions Channel { get; set; } = new();
    public DropOptions Drop { get; set; } = new();
    public ExperienceOptions Experience { get; set; } = new();
    public int MaximumPacketLength { get; set; } = 4 * 1024 * 1024;
    public bool RejectBadCrc32 { get; set; } = false;
    public bool EnablePacketTracing { get; set; } = false;
    public int HexDumpLimit { get; set; } = 64;
}

public sealed class GameplayDatagramOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1";

    // Address handed to clients so they can reach the gameplay UDP ports. Empty
    // means "same as Host". Set it when the bind address is not reachable from
    // clients, e.g. 0.0.0.0 inside a container or a LAN bind behind NAT.
    public string AdvertisedHost { get; set; } = "";

    public int PrimaryPort { get; set; } = 7002;
    public int SecondaryPort { get; set; } = 7003;
    public ushort MonsterObjectType { get; set; } = 0x0211;
    public byte SenderPartyIndex { get; set; } = 0;

    public string ResolveAdvertisedHost() =>
        string.IsNullOrWhiteSpace(AdvertisedHost) ? Host : AdvertisedHost;
}

public sealed class DropOptions
{
    public bool Enabled { get; set; } = true;
    public double RatePercent { get; set; } = 100;
    public double EconomicRate { get; set; } = 1;
    public bool ForceDrops { get; set; }
}

public sealed class ExperienceOptions
{
    public double MonsterMultiplier { get; set; } = 1;
    public double ClearMultiplier { get; set; } = 1;
    public double BlackDiamondClearBonusRate { get; set; } = 0.05;
    public double ClearEventBonusRate { get; set; }
    public DateTimeOffset? ClearEventStartsAt { get; set; }
    public DateTimeOffset? ClearEventEndsAt { get; set; }
}

public sealed class ChannelOptions
{
    public int ServerNumber { get; set; } = 1;
    public string Name { get; set; } = "Local Channel";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7001;
    public int ChannelNumber { get; set; } = 1;
    public int MaximumUsers { get; set; } = 100;
    public int CurrentUsers { get; set; } = 1;
}

public sealed class AdminOptions
{
    // 双栈绑定：:: 同时接受 IPv4 与 IPv6。服务端玩法面是 IPv6（客户端经中继拨 IPv6），
    // 绑 0.0.0.0 会让 IPv6 侧访问不到管理接口；只需 IPv4 就填 0.0.0.0，只本机访问填 127.0.0.1。
    public string Host { get; set; } = "::";

    public int Port { get; set; } = 8081;

    /// <summary>引导管理员名单：名单里的账号在启动时与注册时被提升为管理员。</summary>
    public string[] Users { get; set; } = [];

    /// <summary>登录 token 有效期（小时）；每次使用滑动续期。</summary>
    public int TokenHours { get; set; } = 12;

    /// <summary>
    /// 触发账号级锁定的「不同失败来源数」阈值：来自这么多不同来源的失败才锁定该账号。
    /// 取 1 会让单个来源就能锁死别人的账号（拒绝服务），默认 3。
    /// </summary>
    public int LoginAccountLockSources { get; set; } = 3;

    /// <summary>同一来源（IP）的总尝试额度（跨账号），挡单机爆破/扫号/枚举。</summary>
    public int LoginSourceMaxAttempts { get; set; } = 30;

    /// <summary>触发限流后的锁定时长（分钟）。</summary>
    public int LoginLockoutMinutes { get; set; } = 5;

    /// <summary>token 绝对有效期上限（小时）：滑动续期也不能让 token 永生。</summary>
    public int MaxTokenLifetimeHours { get; set; } = 24 * 7;
}

public sealed class ListenerOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public int[] AdditionalPorts { get; set; } = [];
}

/// <summary>成员离开区域时用哪种信号告诉同区其他人。</summary>
public enum TownDepartureSignal
{
    /// <summary>发一条同 uid 的 USER_AREA、尾字节 active=0（默认，待真机验证）。</summary>
    UserAreaInactive = 0,

    /// <summary>向同区其他人重发不含该成员的 AREA_USERS 名单（已知副作用：可能重建区域并扯回本地角色）。</summary>
    RosterResend = 1,
}

public sealed class TownSyncOptions
{
    /// <summary>城镇同屏与附近聊天总开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// true = 给新人先发同区已有成员的外观、再发 AREA_USERS 名单；
    /// 默认 false（名单在前，与本地角色"先名单后外观"的既有顺序一致）。
    /// </summary>
    public bool ForeignAppearanceBeforeRoster { get; set; } = false;

    /// <summary>成员离开区域时使用的信号。</summary>
    public TownDepartureSignal DepartureSignal { get; set; } = TownDepartureSignal.UserAreaInactive;

    /// <summary>每个成员每秒最多向同区转发的 USER_POSITION 条数；&lt;=0 表示不限制。</summary>
    public int MaxPositionForwardsPerSecond { get; set; } = 10;
}

/// <summary>调试用：往指定区域投一个合成角色，用于单人复现"第二个角色出现/走动/消失"。</summary>
public sealed class TownGhostOptions
{
    public bool Enabled { get; set; } = false;
    public byte TownId { get; set; } = 1;
    public byte AreaId { get; set; } = 1;
    public string Name { get; set; } = "TEST";
    public int StepIntervalMs { get; set; } = 1000;

    /// <summary>出现时长（秒）；与 HiddenSeconds 同时 &gt;0 时按「出现/消失」循环，用于验证进出与移除。</summary>
    public int VisibleSeconds { get; set; } = 0;

    /// <summary>消失时长（秒）；0 表示一直留在区域里。</summary>
    public int HiddenSeconds { get; set; } = 0;
}

public sealed class DebugOptions
{
    public TownGhostOptions TownGhost { get; set; } = new();
}
