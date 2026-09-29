using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 队员入场的报文规划器（**纯函数**，不碰 IO，方便冒烟测试钉住顺序与槽位）。
/// 硬顺序（设计稿 §3.2 + 探针真机证据 3d5bb45）：名册 → NOTI 26（自己的槽位）→ NOTI 11 → NOTI 28 → NOTI 29。
/// 名册必须早于副本报文：客户端在**收到名册那一刻**按 uid 查本地 user 表建"谁属于哪支队伍"的链接。
/// </summary>
internal static class DungeonEntryPackets
{
    public static List<GameServerPacket> PlanMemberEntry(
        DungeonInstance instance,
        DungeonInstanceRoomSnapshot room,
        GameServerPacket? roster,
        GameServerPacket? peerTable,
        byte slotIndex)
    {
        var packets = new List<GameServerPacket>(5);
        if (roster is not null)
        {
            packets.Add(roster);
        }

        packets.Add(GameProtocolEngine.CreateUdpHost(slotIndex));
        if (peerTable is not null)
        {
            packets.Add(peerTable);
        }

        packets.Add(GameProtocolEngine.CreateDungeonInfo(
            instance.Run.DungeonId,
            instance.Run.Difficulty,
            instance.Layout?.MazeIndex ?? 0,
            instance.Layout?.BossX ?? byte.MaxValue,
            instance.Layout?.BossY ?? byte.MaxValue,
            flagA: room.HellRoomX,
            flagB: room.HellRoomY));
        packets.Add(room.ReuseClientRoomState
            ? GameProtocolEngine.CreateCachedStartMap(room.RoomX, room.RoomY, room.RandomSeed)
            : GameProtocolEngine.CreateStartMap(
                room.RoomX,
                room.RoomY,
                room.MapId,
                room.Monsters,
                room.PassiveObjectDrops,
                room.RandomSeed,
                room.RoomStateFlag,
                room.HellPartyMode));
        return packets;
    }
}

/// <summary>
/// 组队副本的编排（设计稿 §3.2）：同区域校验、选图/入场请求投递、跨会话广播、
/// 门口集结倒计时扫描。**不发自己的包**——每个队员的包由他自己的会话发（只有他本人建得出自己的外观）。
/// </summary>
public sealed class DungeonPartyService : BackgroundService
{
    /// <summary>门口集结倒计时的扫描间隔（与交易/组队超时同一套 TimeProvider 惯例）。</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    private readonly PartyService _partyService;
    private readonly TownSessionDirectory _directory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DungeonPartyService> _logger;

    public DungeonPartyService(
        PartyService partyService,
        TownSessionDirectory directory,
        DungeonInstanceRegistry registry,
        TimeProvider timeProvider,
        ILogger<DungeonPartyService> logger)
    {
        _partyService = partyService;
        _directory = directory;
        Registry = registry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public DungeonInstanceRegistry Registry { get; }

    /// <summary>
    /// 同区域校验（用户硬性要求）：返回队长所在队伍的槽位快照；**任一同队队员不在 (<paramref name="townId"/>, <paramref name="areaId"/>)**
    /// → 返回 null 并把不满足的人放进 <paramref name="outsiders"/>（调用方只记日志、整队放弃）。
    /// </summary>
    internal PartySlotHandle[]? TryGetGatheringParty(
        Guid leaderCharacterId,
        byte townId,
        byte areaId,
        out Guid[] outsiders)
    {
        outsiders = [];
        var slots = _partyService.GetPartySlots(leaderCharacterId);
        if (slots is null)
        {
            return null;
        }

        List<Guid> missing = [];
        foreach (var slot in slots)
        {
            if (slot.CharacterId == leaderCharacterId)
            {
                continue;
            }

            if (!_directory.TryGetSnapshot(slot.CharacterId, out var snapshot)
                || snapshot.TownId != townId
                || snapshot.AreaId != areaId)
            {
                missing.Add(slot.CharacterId);
            }
        }

        outsiders = [.. missing];
        return missing.Count == 0 ? slots : null;
    }

    /// <summary>队长开选图界面：给每个同区队员投一条"待选图"请求（他的会话自己把选图状态发给自己客户端）。</summary>
    internal int PostSelectionRequests(Guid leaderCharacterId, byte townId, byte areaId)
    {
        if (TryGetGatheringParty(leaderCharacterId, townId, areaId, out _) is not { } slots)
        {
            return 0;
        }

        var posted = 0;
        foreach (var slot in slots)
        {
            if (slot.CharacterId == leaderCharacterId)
            {
                continue;
            }

            Registry.PostSelectionRequest(slot.CharacterId, leaderCharacterId);
            posted++;
        }

        return posted;
    }

    /// <summary>队长入场成功：给每个队员投一条"待入队"请求（队员会话自己执行入场，设计稿 §3.2）。</summary>
    internal int PostEntryRequests(
        DungeonInstance instance,
        ushort dungeonId,
        byte difficulty,
        byte dungeonOption,
        Guid leaderCharacterId,
        byte townId,
        byte areaId)
    {
        if (TryGetGatheringParty(leaderCharacterId, townId, areaId, out var outsiders) is not { } slots)
        {
            _logger.LogWarning(
                "Dungeon {DungeonId} party entry aborted: {Count} member(s) are outside the leader's area {TownId}/{AreaId}.",
                dungeonId, outsiders.Length, townId, areaId);
            return 0;
        }

        var request = new DungeonEntryRequest(
            instance.PartyId, dungeonId, difficulty, dungeonOption, townId, areaId, leaderCharacterId);
        var posted = 0;
        foreach (var slot in slots)
        {
            if (slot.CharacterId == leaderCharacterId)
            {
                continue;
            }

            Registry.PostEntryRequest(slot.CharacterId, request);
            posted++;
        }

        _logger.LogInformation(
            "Dungeon {DungeonId} entry request posted to {Count} party member(s) for instance {InstanceId}.",
            dungeonId, posted, instance.PartyId);
        return posted;
    }

    /// <summary>给同实例所有成员（可跳过自己）投一份包；坏通道只告警不影响其他人。</summary>
    internal async Task BroadcastToMembersAsync(
        DungeonInstance instance,
        GameServerPacket packet,
        Guid? omitCharacterId = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var member in instance.Members)
        {
            if (member.CharacterId == omitCharacterId)
            {
                continue;
            }

            try
            {
                await member.Send(packet);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not deliver dungeon packet {ProtocolId} to {CharacterId}.",
                    packet.ProtocolId,
                    member.CharacterId);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(SweepInterval, _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await SweepDoorAssembliesAsync(stoppingToken);
            }
        }
        finally
        {
            // 只在取消/退出时清空：放在循环之后的话，扫描里任何一个异常都会跳过它，
            // 让托管服务带着一注册表的活实例挂掉（Task 5 的扫描逻辑正在长出来）。
            Registry.Clear();
        }
    }

    /// <summary>
    /// 门口集结倒计时（设计稿 §3.3 第 4 条）：到点的实例 → 锁内推进房间取快照 → 出锁给全员发 NOTI 29。
    /// 被强制推进的成员自己不操作，所以这里**不发**命令回包（只给到过门口的人补回包）。
    /// 单个实例的异常只告警、不影响其它实例（扫描是每实例独立的一步）。
    /// </summary>
    private async Task SweepDoorAssembliesAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in Registry.Instances)
        {
            try
            {
                await SweepDoorAssemblyAsync(instance, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Door assembly sweep failed for instance {PartyId}.",
                    instance.PartyId);
            }
        }
    }

    /// <summary>单个实例的集结扫描（Task 5 填：倒计时到点 → instance.TryMoveRoom + 广播 NOTI 29）。</summary>
    private async Task SweepDoorAssemblyAsync(DungeonInstance instance, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}
