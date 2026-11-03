namespace DFLegacy.Server;

public sealed class ServerOptions
{
    public AdminOptions Admin { get; set; } = new();
    public WebPanelsOptions WebPanels { get; set; } = new();
    public ProcessOptions Processes { get; set; } = new();
    public ListenerOptions Entrance { get; set; } = new() { Port = 8080 };
    public ListenerOptions CharacterDatagram { get; set; } = new() { Host = "127.0.0.1", Port = 2311 };
    public GameplayDatagramOptions GameplayDatagram { get; set; } = new();
    public ListenerOptions GameProbe { get; set; } = new() { Port = 7001, Enabled = false };
    public string DataPath { get; set; } = "data/state.json";
    public bool SeedTestAccount { get; set; } = true;
    public bool EnableTestCeraTopUp { get; set; } = false;
    public string ScriptPvfPath { get; set; } = "data/Script.pvf";
    public string SkillScriptPath { get; set; } = "";
    public string OriginalSkillScriptPath { get; set; } = "";
    public TownSyncOptions TownSync { get; set; } = new();
    public DebugOptions Debug { get; set; } = new();
    public ChannelOptions Channel { get; set; } = new();
    public PvpOptions Pvp { get; set; } = new();
    public FallbackRelayOptions FallbackRelay { get; set; } = new();
    public DropOptions Drop { get; set; } = new();
    public ExperienceOptions Experience { get; set; } = new();
    public DungeonOptions Dungeon { get; set; } = new();
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

/// <summary>
/// 副本（含组队副本）行为开关。默认值就是生产口径，保留可配置是为了真机调参不必重新发版。
/// </summary>
/// <remarks>
/// **2026-10-03（S4）**：这里原来有"门口集结宽限窗口"两个开关（`DoorGraceSeconds` / `DoorGrace` /
/// `ResolveDoorMemberCount`）——它们服务的 `DungeonDoorAssembly` 是**自研机制**，在官服里没有对应物：
/// `CParty::move_map` @0x085a8f4e(987)（伪码 `official/ida_targeted_out.txt:362-454`）的判定只看
/// `CBattle_Field::MoveMap(field, msg[13], msg[14], &out)` 对**请求者自己**目标的判定，
/// **一次合法请求就推进整队**，函数里不存在任何"全员到门口/等人"的条件。
/// 因此整套开关随该机制一起删除；换房的唯一入口改为 `DungeonInstance.TryAdvanceRoom`。
/// </remarks>
public sealed class DungeonOptions
{
}

public sealed class ChannelOptions
{
    public int ServerNumber { get; set; } = 1;
    public string Name { get; set; } = "Local Channel";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7001;
    public int ChannelNumber { get; set; } = 1;
    public bool RegionalChannelsEnabled { get; set; }
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
    /// <summary>
    /// 发一条 USER_AREA，且 (town, area) 填 0xFF/0xFF 哨兵（默认）。客户端只有在包里的
    /// (town, area) 与自己当前区域不一致时才 mapMgr->RemoveCreature，递归销毁角色及其
    /// 显示/特效子对象；同区 + active=0 只是隐藏标记，不会移除。
    /// </summary>
    UserAreaInactive = 0,

    /// <summary>
    /// 向同区其他人重发不含该成员的 AREA_USERS 名单。注意它**不能移除角色**：客户端的
    /// AREA_USERS 逐条只做加/更新，对名单里缺席的 uid 不做任何处理（残留 actor 与特效
    /// 依旧、重登可能显示旧位置），仅作兼容备选。
    /// </summary>
    RosterResend = 1,
}

public sealed class TownSyncOptions
{
    /// <summary>城镇同屏与附近聊天总开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// true（默认）= 给新人先发同区已有成员的外观（USERINFO mode 0），再发 AREA_USERS 名单。
    /// 客户端处理 AREA_USERS/USER_AREA 时会先查该 uid 的 USERINFO 记录，查不到就静默跳过
    /// 该条目，所以名单在前时新人建不出任何同镇角色（真机症状：同区两人互不可见）；
    /// 必须先有记录、后给区域条目。保留开关是为了能切回旧顺序做对照实验，不要再当作默认。
    /// </summary>
    public bool ForeignAppearanceBeforeRoster { get; set; } = true;

    /// <summary>成员离开区域时使用的信号。</summary>
    public TownDepartureSignal DepartureSignal { get; set; } = TownDepartureSignal.UserAreaInactive;

    /// <summary>
    /// true = 成员变化时向同区成员下发 UDP 对端表（NOTI 11）。客户端只为表内的 uid
    /// 建立同镇角色，缺了它同区其他人的头像不会出现；关掉可退回只发名单/外观的旧行为。
    /// </summary>
    public bool SendAreaPeerTable { get; set; } = true;

    /// <summary>
    /// true = 新人进区时，在名单之后逐个已有成员补发 USER_AREA(active=1)：
    /// 客户端可能只有在「名单里有该 uid」且「收到该 uid 的 USER_AREA」时才建立同镇角色，
    /// 新人此前缺的正是后者。默认 false，保持既有报文序列不变（待真机验证的对称性开关）。
    /// </summary>
    public bool SendUserAreaForExistingMembers { get; set; } = false;

    /// <summary>
    /// true = 成员进出/迁移导致成员变化时，向同区其他人重发完整 AREA_USERS 名单。
    /// 既有实现里老人的名单永远停在进入那一刻；刷新它老人才能在名单里看到新 uid。
    /// 默认 false 保持旧行为（老人只收增量 USER_AREA，名单不刷新）。
    /// </summary>
    public bool RefreshRosterOnMembershipChange { get; set; } = false;

    /// <summary>每个成员每秒最多向同区转发的 USER_POSITION 条数；&lt;=0 表示不限制。</summary>
    public int MaxPositionForwardsPerSecond { get; set; } = 10;

    /// <summary>
    /// 额外的单人实例区域，条目为 <c>"城镇:区域"</c>（两侧均为 1..255）。默认 <c>[]</c>。
    /// 五处赛丽亚房间（1:1、2:5、3:2、4:1、5:1）由目录固定隔离，无需配置。
    /// **不要改回非空默认**：ConfigurationBinder.Get&lt;T&gt;
    /// 对已初始化的数组属性是「追加」而不是「替换」，默认值带项会让配置无法清空列表
    /// （默认非空会让配置无法清空额外列表）。非法条目（如 "0:1"、"1:abc"）
    /// 在启动时告警并跳过，不阻止服务启动。
    /// </summary>
    public string[] PrivateAreas { get; set; } = [];
}

/// <summary>调试用：往指定区域投一个合成角色，用于单人复现"第二个角色出现/走动/消失"。</summary>
public sealed class TownGhostOptions
{
    public bool Enabled { get; set; } = false;
    public byte TownId { get; set; } = 1;

    /// <summary>
    /// 默认 0（城镇 1 的广场，共享区域）。不能是 1：1/1 是赛利亚房间，默认已是私密区域，
    /// 幽灵投在那里会与玩家各自成区、单人复现同屏时看不到任何角色。
    /// </summary>
    public byte AreaId { get; set; } = 0;

    public string Name { get; set; } = "TEST";
    public int StepIntervalMs { get; set; } = 1000;

    /// <summary>出现时长（秒）；与 HiddenSeconds 同时 &gt;0 时按「出现/消失」循环，用于验证进出与移除。</summary>
    public int VisibleSeconds { get; set; } = 0;

    /// <summary>消失时长（秒）；0 表示一直留在区域里。</summary>
    public int HiddenSeconds { get; set; } = 0;
}

public sealed class TestControlOptions
{
    public bool Enabled { get; set; }
}

public sealed class DebugOptions
{
    public TestControlOptions TestControl { get; set; } = new();
    public TownGhostOptions TownGhost { get; set; } = new();
    public ClientOverlayOptions ClientOverlay { get; set; } = new();
}

public sealed class ClientOverlayOptions
{
    public bool Enabled { get; set; }
    public string SnapshotDirectory { get; set; } = "debug-overlay";
}

public sealed class WebPanelsOptions
{
    public ListenerOptions Gm { get; set; } = new() { Host = "0.0.0.0", Port = 18081 };
    public ListenerOptions Player { get; set; } = new() { Host = "0.0.0.0", Port = 18082 };
}