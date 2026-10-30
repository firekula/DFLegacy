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

    /// <summary>
    /// 结算期 EPLP 超时：结算包（NOTI 34/35）发出后这么久仍没收到该成员的 EPLP（CMD 75），
    /// 就由扫掠给他投"强制回城"信箱。官服同款机制，参考实现日志逐字 `town return: eplp timer 20`。
    /// 真机 2026-09-30：队员结算页卡死（客户端未响应、一个包都不发），只认 EPLP 就永远留在副本里。
    /// 只对组队实例生效（扫掠对非组队实例整体早退），单人路径不受影响。
    /// </summary>
    public static readonly TimeSpan SettlementReturnTimeout = TimeSpan.FromSeconds(120);

    /// <summary>结算期自动返回前，用聊天框系统消息逐秒播报的秒数（用户 2026-09-30 反馈的原版行为）。</summary>
    public const int SettlementCountdownSeconds = 10;

    internal const string PartyWipeReturnReason = "party-death-countdown-expired";

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
    /// 官服 set_charac_live（0x085B29BC）：生死状态发给全队。有人仍活着时死者留本；
    /// 全灭才由实例开 10 秒回城期限，复活立即取消。单人返回 false，沿用会话的单人路径。
    /// </summary>
    internal async Task<bool> SetMemberAliveAsync(
        DungeonInstance instance,
        Guid characterId,
        bool isAlive,
        CancellationToken cancellationToken = default)
    {
        var member = instance.FindMember(characterId);
        if (member is null
            || !instance.TrySetPartyMemberAlive(characterId, isAlive, _timeProvider.GetUtcNow()))
        {
            return false;
        }

        await BroadcastToMembersAsync(
            instance,
            GameProtocolEngine.CreateDieState(member.AreaUserId, isAlive),
            cancellationToken: cancellationToken);
        _logger.LogInformation(
            "Party member {CharacterName} (uid {AreaUserId}) is {LifeState} in dungeon instance {PartyId}; all-dead return deadline={Deadline}.",
            member.CharacterName, member.AreaUserId, isAlive ? "alive" : "dead",
            instance.PartyId, instance.PartyWipeDeadline);
        return true;
    }

    /// <summary>
    /// 官服：**篝火页上任何一个成员选"返回城镇"，全队一起回城**（不是各自等各自的超时）。
    /// `[静态]` 两处依据：① `CParty::ReturnToVillage` @0x085aca60 的组队分支**按槽位循环**——
    /// `for i in 0..3: CParty::_checkValidUser(i) → CUser::make_basic_info(member[i]) →
    /// GameWorld::out_from_dungeon(member[i])`（成员指针 = `party+0x78+i*0x18`，循环增量在
    /// 0x085acf1d-0x085acf2a）；② `Dispatcher_EPLPCommand::isReTurnToVillage` @0x0825f7f4 只判
    /// "这个人在副本里"（`CUser::getMoveSpace() == 1` → 1，0x0825f828-0x0825f83c），**不判是不是队长**。
    /// 实现：给其余成员各投一次"强制回城"信箱，他们的会话取走后走同一条
    /// `ReturnToTownFromDungeonAsync`（各自的城镇收尾、结算、退队判定都照旧）。
    /// **不额外区分"是不是通关后"**（用户 2026-09-30 决定）：本机客户端 ESC 的「返回城镇」走 `CMD 45`、
    /// EPLP 只在结算页出现（24 次 `CMD 75` 全在 `boss map cleared` 之后），且客户端版本不再更换，
    /// 所以保持简单形式、不引"通关过没有"的判定。
    /// 单人实例 / 不在实例里 → 返回 0 且一个信箱都不投（单人零回归）。
    /// </summary>
    internal static int PostSettlementReturnsForOthers(
        DungeonInstanceRegistry registry,
        Guid characterId,
        string reason)
    {
        if (registry.TryGetForCharacter(characterId) is not { IsParty: true } instance)
        {
            return 0;
        }

        var posted = 0;
        foreach (var member in instance.Members)
        {
            if (member.CharacterId == characterId)
            {
                continue;
            }

            registry.PostSettlementReturn(member.CharacterId, reason);
            posted++;
        }

        return posted;
    }

    /// <summary>
    /// 第 13 条：**副本内擅自退出 = 退队**（官服语义）。触发点是客户端上行 CMD 45（GIVEUP_GAME，
    /// 副本内"返回城镇"按钮），官服 `CParty::giveup_game` 里就是 `CParty::leave_user(party, user, 2)`
    /// ——与 CMD 13（退出队伍）**同一个函数、同一个实参**，所以这里复用 CMD 13 的
    /// `PartyLeaveNotice.Silent`（只重发名册、不发 NOTI 10；NOTI 10 属于 `CParty::walkout_member`）。
    /// **前置条件**：只有"确实在**队伍**副本实例里"才动队伍——单人实例/不在任何实例（选图界面、
    /// 城镇里收到 CMD 45）一律返回 false 且一个包都不发（单人零回归；结算回城 `CParty::ReturnToVillage`
    /// 与副本内死亡 `die_user` 在官服里同样不调用 `leave_user`，所以它们不走这里）。
    /// 调用方必须在 `LeaveDungeonInstanceAsync` **之前**调用：解绑之后实例里就没有这个人了。
    /// </summary>
    internal async Task<bool> AbandonPartyForDungeonGiveUpAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        // 只有"确实在**队伍**副本实例里"才动队伍：单人实例（键 = 角色 id，IsParty == false）与
        // 根本不在实例里的会话（选图界面、城镇里收到 CMD 45）一律原样返回——单人路径一个包都不发。
        var instance = Registry.TryGetForCharacter(characterId);
        if (instance is not { IsParty: true })
        {
            return false;
        }

        // **只在队里还有别人时才算"退队"**（用户 2026-09-30 真机更正）：最后一人擅自退出时队伍保留，
        // 他仍是这支 1 人队的队长（官方 `CParty::leave_user` 只有 `IsSinglePlay` 那支才 `CParty::destroy`）。
        var left = await _partyService.TryLeavePartySilentlyIfOthersRemainAsync(characterId, cancellationToken);
        if (left)
        {
            _logger.LogInformation(
                "Party member {CharacterId} gave up inside party dungeon {PartyId} and left the party (official giveup semantics).",
                characterId,
                instance.PartyId);
        }
        else
        {
            _logger.LogInformation(
                "Party member {CharacterId} gave up inside party dungeon {PartyId} as the only member; party kept (he stays its leader).",
                characterId,
                instance.PartyId);
        }

        return left;
    }

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

    internal async Task BroadcastActiveDropAsync(
        DungeonInstance instance, DungeonGroundItem groundItem, GameServerPacket packet, long roomGeneration)
    {
        foreach (var member in instance.Members)
        {
            if (!instance.IsGroundItemInCurrentRoom(groundItem, roomGeneration)) break;
            // Never fall back to ordinary Send: it can wait for the write gate
            // after this check, letting a new room packet overtake the old actor.
            if (member.SendConditional is not { } sendConditional) continue;
            await CrossSessionDelivery.SendSafelyAsync(_logger, packet,
                CrossSessionDelivery.Describe(member.CharacterId, member.CharacterName, member.AreaUserId),
                value => sendConditional(value, () =>
                    instance.IsGroundItemInCurrentRoom(groundItem, roomGeneration)
                    && ReferenceEquals(instance.FindMember(member.CharacterId), member)));
        }
    }

    /// <summary>
    /// 原生 NOTI39 消费四槽骰子分，并仅在 winner uid 等于本地 uid 时入背包。
    /// 骰子结果必须先到全队，再由调用方广播地面移除；直取仍只通知得主会话。
    /// </summary>
    internal async Task BroadcastInventoryItemPickupAsync(
        DungeonInstance instance,
        ushort groundId,
        ushort pickerUserId,
        ushort winnerUserId,
        ushort destinationSlot,
        IReadOnlyList<byte>? routingScores,
        Func<GameServerPacket, ValueTask> sendToWinner)
    {
        var packet = GameProtocolEngine.CreateInventoryItemPickup(
            groundId, pickerUserId, destinationSlot, routingScores, winnerUserId);
        if (instance.IsParty && routingScores?.Any(score => score != 0) == true)
            await BroadcastToMembersAsync(instance, packet);
        else
            await sendToWinner(packet);
    }

    // This client ignores the official SERVER_BROADCAST subtype 5. The
    // compatible extension carries the exact ground id for the native patch;
    // it must reach every member, including the picker and a routed winner.
    internal async Task BroadcastPickupRemovalAsync(
        DungeonInstance instance,
        ushort groundId,
        ushort pickerUserId,
        uint itemId,
        Func<GameServerPacket, ValueTask> sendToSolo,
        CancellationToken cancellationToken = default)
    {
        var removed = GameProtocolEngine.CreatePickupRemovedBroadcast(pickerUserId, itemId, groundId);
        if (!instance.IsParty)
        {
            await sendToSolo(removed);
            return;
        }
        await BroadcastToMembersAsync(instance, removed, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 有成员离开副本但仍在队伍中时，给其余成员发 USER_AREA 的区域离场信号。
    /// 如果退队名册已经移除此 actor，则不再发送第二条移除通知：NOTI9 在副本状态
    /// 会调用客户端 4C6700；该调用即使对象不在地图向量中也仍执行虚函数回调，不能重复。
    /// 结算/全灭回城保留队伍，名册不会移除成员，因此仍需要 USER_AREA 的 FF/FF 离场信号。
    /// **必须在 <see cref="DungeonInstanceRegistry.UnbindCharacter"/> 之前捕获 (uid, 坐标)**——
    /// 解绑之后实例里已经没有这个人了。单人实例没有"其余成员"，调用方跳过（自己也要走了）。
    /// 令牌一律用 <c>CancellationToken.None</c>：这是给别人的收尾包，离开者的会话令牌在掉线路径上
    /// 已经取消、在回城/回选人路径上也不代表收件人。
    /// 真机检查点：Task 7 第 6 项（其余成员是否看到对方角色消失）。
    /// internal（不是任务书写的 public）：<see cref="DungeonInstance"/> 是 internal，public 方法收不了它
    /// （CS0051）；与 <see cref="BroadcastToMembersAsync"/> 同一可见性，也是冒烟测试的接缝。
    /// </summary>
    /// <param name="partyRosterAlreadyRemovesActor">
    /// 只有已完成退队名册发布时才为 true；单纯离开副本而保留队伍必须为 false。
    /// </param>
    internal async Task NotifyMemberLeftAsync(
        DungeonInstance instance,
        Guid characterId,
        string characterName,
        ushort areaUserId,
        short x,
        short y,
        byte direction,
        CancellationToken cancellationToken = default,
        bool partyRosterAlreadyRemovesActor = false)
    {
        if (partyRosterAlreadyRemovesActor)
        {
            _logger.LogInformation(
                "Party member {CharacterName} (uid {AreaUserId}, {CharacterId}) left dungeon instance {PartyId}; party roster already removes the actor, so no additional USER_AREA departure was sent.",
                characterName, areaUserId, characterId, instance.PartyId);
            return;
        }

        var departure = GameProtocolEngine.CreateUserAreaDeparture(
            new GameTownUser(areaUserId, x, y, direction));
        await BroadcastToMembersAsync(instance, departure, characterId, cancellationToken);
        _logger.LogInformation(
            "Party member {CharacterName} (uid {AreaUserId}, {CharacterId}) left dungeon instance {PartyId}; departure sent to {Count} remaining member(s).",
            characterName, areaUserId, characterId, instance.PartyId, instance.MemberCount);

        // 刻意**不**在这里补发 NOTI 30（2026-09-30 真机反证）：NOTI 30 的语义是"地图开始/全员加载完成"，
        // 而正在篝火结算页的客户端收到它可能会重新走"开始地图"的流程而崩溃退出（真机 09:10:26 发、
        // :10:30 客户端进程死亡；改动前同一时刻不发 NOTI 30 的日志里，留下的队员活得好好的）。
        // 门槛也不需要这个安全网：成员离开会把它从 _members 摘掉，剩下的人下次上报 CMD 40 时
        // "全员已加载"自然成立（`TryMarkMemberLoaded` 实时按当前成员表判定）。
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

                await SweepInstanceTimersAsync(stoppingToken);
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
    internal async Task SweepInstanceTimersAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in Registry.Instances)
        {
            try
            {
                await SweepSettlementTimersAsync(instance, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Instance timer sweep failed for instance {PartyId}.",
                    instance.PartyId);
            }
        }
    }

    /// <summary>单个实例的集结扫描（Task 5 填：倒计时到点 → instance.TryMoveRoom + 广播 NOTI 29）。</summary>
    private async Task SweepSettlementTimersAsync(DungeonInstance instance, CancellationToken cancellationToken)
    {
        // 单人实例不参与结算倒计时扫描（保持改造前行为）：单人的换房走会话里那条立即推进的分支。
        if (!instance.IsParty)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();

        // 死亡回城是整队全灭计时，不能套用每个会话的单人死亡倒计时。
        // 使用既有强制回城信箱唤醒空闲会话；会话取信时必须再调用
        // TryBeginPartyWipeReturn，拦住陈旧邮件并与花币前的复活预留原子仲裁。
        foreach (var deadCharacterId in instance.TakeExpiredPartyWipeReturns(now))
        {
            Registry.PostSettlementReturn(deadCharacterId, PartyWipeReturnReason);
            _logger.LogInformation(
                "Dungeon {DungeonId} party wipe countdown expired: forced town return queued for {CharacterId} in instance {PartyId}.",
                instance.Run.DungeonId, deadCharacterId, instance.PartyId);
        }

        if (instance.PartyWipeDeadline is not null)
        {
            // 全灭期间只走死亡回城；同一个信箱里的旧结算期限不能覆盖死亡原因，
            // 否则会绕过会话取信时对“全队仍死着”的复核。
            return;
        }

        // 结算期最后 10 秒的聊天框系统消息（用户 2026-09-30 报的原版行为）：逐秒播报"还剩几秒"，
        // 到点才由下面的 EPLP 超时真正送回城。**只发给本人**（别人的聊天框不该出现这行），
        // 走成员自己的发送句柄（与共享广播同一条路），坏通道由 SendSafely 吞掉。
        foreach (var (pendingCharacterId, remainingSeconds) in
                 instance.TakeSettlementCountdowns(now, SettlementCountdownSeconds))
        {
            var announced = instance.FindMember(pendingCharacterId);
            if (announced is null)
            {
                continue;
            }

            var countdownText = CrossSessionDelivery.ClientEncoding.GetBytes(
                $"{remainingSeconds} 秒后自动返回城镇。");
            await SendSafelyAsync(
                announced,
                GameProtocolEngine.CreateMessageNotification(
                    GameMessageType.Type16Brown,
                    // 目标 uid 0 = 不加发言人前缀（系统消息形态，与"负重超限"提示同一口径）。
                    0,
                    countdownText));
        }

        // 结算期 EPLP 超时（与门口集结无关，先判）：到点把"还没走"的成员投进强制回城信箱。
        foreach (var pendingCharacterId in instance.TakeExpiredSettlementReturns(now))
        {
            Registry.PostSettlementReturn(pendingCharacterId, "settlement-eplp-timeout");
            _logger.LogInformation(
                "Dungeon {DungeonId} settlement EPLP timeout: forced town return queued for {CharacterId}.",
                instance.Run.DungeonId,
                pendingCharacterId);
        }

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
