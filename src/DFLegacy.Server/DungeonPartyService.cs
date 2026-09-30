using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 队员入场的报文规划器（**纯函数**，不碰 IO，方便冒烟测试钉住顺序与槽位）。
/// 硬顺序（设计稿 §3.2 + 探针真机证据 3d5bb45）：名册 → NOTI 26（**主机槽位**）→ NOTI 11 → NOTI 28 → NOTI 29。
/// 名册必须早于副本报文：客户端在**收到名册那一刻**按 uid 查本地 user 表建"谁属于哪支队伍"的链接。
/// NOTI 26 载荷口径见 <see cref="PartyService.TryGetLeaderSlot"/>：官服 `CParty::send_host_info` 发的是
/// **主机（队长）的槽位、全队同一个值**（2026-09-30 静态实证），不是收件人自己的槽位。
/// </summary>
internal static class DungeonEntryPackets
{
    public static List<GameServerPacket> PlanMemberEntry(
        DungeonInstance instance,
        DungeonInstanceRoomSnapshot room,
        GameServerPacket? roster,
        GameServerPacket? peerTable,
        byte hostSlot)
    {
        var packets = new List<GameServerPacket>(5);
        if (roster is not null)
        {
            packets.Add(roster);
        }

        packets.Add(GameProtocolEngine.CreateUdpHost(hostSlot));
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
        packets.Add(PlanStartMap(room));
        return packets;
    }

    /// <summary>换房间（门口集结推进）只发一份 START_MAP；缓存房间用 `CreateCachedStartMap`。</summary>
    public static GameServerPacket PlanStartMap(DungeonInstanceRoomSnapshot room) =>
        room.ReuseClientRoomState
            ? GameProtocolEngine.CreateCachedStartMap(room.RoomX, room.RoomY, room.RandomSeed)
            : GameProtocolEngine.CreateStartMap(
                room.RoomX,
                room.RoomY,
                room.MapId,
                room.Monsters,
                room.PassiveObjectDrops,
                room.RandomSeed,
                room.RoomStateFlag,
                room.HellPartyMode);
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
        if (TryGetGatheringParty(leaderCharacterId, townId, areaId, out var selectionOutsiders) is not { } slots)
        {
            // 真机诊断（选图推送失败时唯一的线索）：没队伍 / 队伍只有自己 / 有人在别的区域，
            // 这三种都投不出去，以前是静默 return 0。
            _logger.LogInformation(
                "Dungeon selection push skipped for leader {Leader} at area {TownId}/{AreaId}: no gathering party (outsiders={Outsiders}).",
                leaderCharacterId, townId, areaId, selectionOutsiders.Length);
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

        if (posted == 0)
        {
            _logger.LogInformation(
                "Dungeon selection push for leader {Leader}: the gathering party has no other member (slots={Slots}).",
                leaderCharacterId, slots.Length);
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

    /// <summary>
    /// 给同实例所有成员（可跳过自己）投一份包；坏通道（含**被踢/掉线成员自己的** OCE）只告警、
    /// 不影响其他人，也不把异常顺着发送者的会话冒泡（终审 I1：护栏见 <see cref="CrossSessionDelivery"/>）。
    /// </summary>
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

            await CrossSessionDelivery.SendSafelyAsync(
                _logger,
                packet,
                CrossSessionDelivery.Describe(
                    member.CharacterId, member.CharacterName, member.AreaUserId),
                member.Send);
        }
    }

    /// <summary>
    /// 有成员离开副本（回城/掉线/换角色）：给其余成员的客户端发既有的"角色离场"信号
    /// （USER_AREA 带 0xFF/0xFF 哨兵 → 客户端按区域不匹配销毁该 actor，见 TownSyncSmokeTests 的离场断言）。
    /// **必须在 <see cref="DungeonInstanceRegistry.UnbindCharacter"/> 之前捕获 (uid, 坐标)**——
    /// 解绑之后实例里已经没有这个人了。单人实例没有"其余成员"，调用方跳过（自己也要走了）。
    /// 令牌一律用 <c>CancellationToken.None</c>：这是给别人的收尾包，离开者的会话令牌在掉线路径上
    /// 已经取消、在回城/回选人路径上也不代表收件人。
    /// 真机检查点：Task 7 第 6 项（其余成员是否看到对方角色消失）。
    /// internal（不是任务书写的 public）：<see cref="DungeonInstance"/> 是 internal，public 方法收不了它
    /// （CS0051）；与 <see cref="BroadcastToMembersAsync"/> 同一可见性，也是冒烟测试的接缝。
    /// </summary>
    internal async Task NotifyMemberLeftAsync(
        DungeonInstance instance,
        Guid characterId,
        string characterName,
        ushort areaUserId,
        short x,
        short y,
        byte direction,
        CancellationToken cancellationToken = default)
    {
        var departure = GameProtocolEngine.CreateUserAreaDeparture(
            new GameTownUser(areaUserId, x, y, direction));
        await BroadcastToMembersAsync(instance, departure, characterId, cancellationToken);
        _logger.LogInformation(
            "Party member {CharacterName} (uid {AreaUserId}, {CharacterId}) left dungeon instance {PartyId}; departure sent to {Count} remaining member(s).",
            characterName, areaUserId, characterId, instance.PartyId, instance.MemberCount);

        // 全员加载门槛的安全网：走后人少了，"剩下的都加载完了"可能刚好成立——
        // 官方 `check_allmember_loading` 每收到一份 CMD 40 都会重评，成员离开也会改变分母；
        // 不在这里补一次广播，剩下的人就会一直等到超时（官方有 `map_load_forced_finish` 兜底）。
        if (instance.IsParty && instance.AllMembersLoaded)
        {
            await BroadcastToMembersAsync(
                instance, GameProtocolEngine.CreateFinishLoading(), cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Dungeon instance {PartyId}: remaining {Count} member(s) are all loaded after the departure; NOTI 30 broadcast.",
                instance.PartyId, instance.MemberCount);
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
    /// internal 是冒烟测试的接缝（同 <see cref="BroadcastToMembersAsync"/>）：测试要钉住
    /// "到点推进时，先补回包（只给到门口者、各自一份）、再发 NOTI 29（全员各一份）"。
    /// </summary>
    internal async Task SweepDoorAssembliesAsync(CancellationToken cancellationToken)
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
        // 单人实例没有集结（`Register` 单人直接 Advance，`PendingTarget` 恒 null）：
        // 早退不碰 `Doors`，单人的换房间仍然只走会话里那条立即推进的分支。
        if (!instance.IsParty)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        DungeonDoorOutcome? outcome;
        DungeonInstanceMember[] members;
        DungeonInstanceRoomSnapshot? room = null;
        ushort dungeonId;
        lock (instance.Gate)
        {
            // 目标房间必须在判定**之前**取：`TryAdvanceOnCountdown` 一返回 Advance 就已经 `Reset()`，
            // 之后再读 `Doors.PendingTarget` 只会是 null（任务书此处按"返回后读"写，是拿不到房间的）。
            var pendingTarget = instance.Doors.PendingTarget;
            // 人数缩水后重新评估（可能返回 StartedCountdown）：必须原样处理，不能当成推进。
            outcome = instance.Doors.TryAdvanceOnCountdown(now, instance.MemberCount);
            if (outcome is null)
            {
                return;
            }

            if (outcome.Decision == DungeonDoorDecision.StartedCountdown)
            {
                // 人数缩水后重新过半（例如 4 人时 2 人到门口、其中一人离开 → 2/3 过半）：
                // 只开倒计时，不改房间；到点由下一轮扫描推进。
                _logger.LogInformation(
                    "Door assembly for dungeon {DungeonId} re-evaluated to a majority after a member left: {Arrived}/{Total} arriving, countdown restarted.",
                    instance.Run.DungeonId, outcome.Arrivals.Count, instance.MemberCount);
                return;
            }

            dungeonId = instance.Run.DungeonId;
            if (pendingTarget is { } target
                && instance.TryMoveRoom(target.X, target.Y, out var transition))
            {
                // 物主填 0（无主）：被强制推进的成员没有"谁先到门口"的语义，掉落按先到先得。
                room = instance.CaptureRoomSnapshot(target.X, target.Y, transition, ownerUserId: 0);
                // 官方 `CParty::move_map` 的 `resetFinishLoadingVar`：换房即清零全员"已加载"标志，
                // 等每个人重新上报 CMD 40（同一把锁内做）。
                instance.ResetMemberLoadingForRoomChange();
            }

            members = instance.Members;
        }

        if (room is null)
        {
            // 到点了却推进不了（run 被收尾 / 房间定义没了）：Doors 已被 TryAdvanceOnCountdown 清空，
            // 下一次 CMD 48 会从零重新登记。
            _logger.LogWarning(
                "Door assembly countdown expired for dungeon {DungeonId} but the room could not be advanced.",
                dungeonId);
            return;
        }

        // 出锁发：先给"到过门口的人"补各自的 CMD 48 成功回包（他们的客户端还在等），
        // 再给全员（含被强制者）各发一份 NOTI 29。
        foreach (var arrival in outcome.Arrivals)
        {
            var member = members.FirstOrDefault(candidate => candidate.CharacterId == arrival.CharacterId);
            if (member is not null)
            {
                await SendSafelyAsync(member, arrival.Reply);
            }
        }

        var startMap = DungeonEntryPackets.PlanStartMap(room);
        foreach (var member in members)
        {
            await SendSafelyAsync(member, startMap);
        }

        _logger.LogInformation(
            "Door assembly countdown expired for dungeon {DungeonId}; forced the party into room ({RoomX},{RoomY}).",
            dungeonId, room.RoomX, room.RoomY);
        _ = cancellationToken;
    }

    /// <summary>
    /// 跨会话投递的坏通道护栏（与 <see cref="BroadcastToMembersAsync"/> 同一份
    /// <see cref="CrossSessionDelivery"/> 判定，含被踢成员自己的 OCE）：
    /// 扫描是每实例独立的一步，一个断线成员不该把整轮推进打断（其余成员仍要收到 NOTI 29）。
    /// </summary>
    private Task SendSafelyAsync(DungeonInstanceMember member, GameServerPacket packet) =>
        CrossSessionDelivery.SendSafelyAsync(
            _logger,
            packet,
            CrossSessionDelivery.Describe(
                member.CharacterId, member.CharacterName, member.AreaUserId),
            member.Send);
}
