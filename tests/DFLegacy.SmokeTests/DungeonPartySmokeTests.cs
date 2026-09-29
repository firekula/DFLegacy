using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// 同队进副本（组队第二轮）的冒烟测试：实例模型、门口集结状态机、入场报文序列、共享状态广播。
/// 设计与证据见 docs/superpowers/specs/2026-09-29-dungeon-party-design.md 与
/// .superpowers/sdd/2026-09-29-dungeon-party-recon.md。
/// </summary>
internal static class DungeonPartySmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        CheckSoloInstanceKey(check, fixture);
        CheckInstanceMembershipLifecycle(check, fixture);
        CheckLastMemberDestroysInstance(check, fixture);
        CheckConcurrentKillSettlesOnce(check, fixture);
        CheckRoomSnapshotsAndPassiveDrops(check, fixture);
        CheckLiveJoinDoesNotReplenishPassiveDrops(check, fixture);
        CheckSharedCollectionAccessors(check, fixture);
        CheckRoomSettlementReset(check);
        CheckRoomFirstVisitFlag(check, fixture);
        CheckDoorAssemblyRules(check);
        CheckPlanStartMapSharedPlanner(check, fixture);
        await CheckDoorAssemblySweepAdvancesWholePartyAsync(check, fixture);
        await CheckDoorAssemblySweepSurvivesBrokenMemberAsync(check, fixture);
        await CheckDoorAssemblySweepIgnoresSoloInstancesAsync(check, fixture);
        CheckRegistryEntryRequestMailbox(check, fixture);
        CheckPendingPartyMailLookup(check, fixture);
        CheckRunStartArbitration(check, fixture);
        CheckLockedRunReadAccessors(check, fixture);
        CheckHellRoomPassiveDetachOnMove(check, fixture);
        CheckSoloRunBindsByCharacterId(check, fixture);
        CheckSoloRoomChangeKeepsImmediateBehaviour(check, fixture);
        CheckSoloLookupNeverReusesPartyBinding(check, fixture);
        CheckStoppedInstanceCannotBeJoined(check, fixture);
        await CheckGatheringValidationAsync(check, fixture);
        await CheckRequestTargetingAsync(check, fixture);
        CheckMemberEntryPacketOrder(check, fixture);
        await CheckBroadcastReachesEveryMemberOnceAsync(check, fixture);
        await CheckPickupBroadcastOmitsSenderAsync(check, fixture);
        CheckGroundItemClaimIsFirstComeFirstServed(check, fixture);
        await CheckMemberDeparturePacketAsync(check, fixture);
        await CheckCrossSessionDeliverySurvivesCanceledMembersAsync(check, fixture);
    }

    /// <summary>
    /// 掉落拾取的"先到先得"与落库回滚（评审 Important 1/2，设计稿 §7 决策 5）：
    /// ①准入不看归属：阿尔法（uid 1）掉的（OwnerUserId=1）布拉沃（uid 2）照样能赢下；
    /// ②无主掉落（OwnerUserId=0，Task 5 的强制推进会登记这种）同样能捡；
    /// ③同一个 groundId 的并发/重复 claim 只有一个 true（输的一方拿到 false，实体已消失）；
    /// ④落库失败的原样放回：`TryReAddGroundItem` 之后同一个 GroundId/归属还在，别人还能再捡。
    /// **变异验证**（三处模型层变异，实测变红后已回滚）：
    /// ①让 `OwnerUserId` 重新参与准入 → 第一/第二条断言变红；
    /// ②`TryClaimGroundItem` 不看 `GroundItems.Remove` 的返回值 → 第三条变红；
    /// ③`TryReAddGroundItem` 不放回（直接 false）→ 第四条变红。
    /// 会话侧那几行（claim 的返回值决定 continue、落库失败调放回）不可单测——与会话接线同一局限。
    /// </summary>
    private static void CheckGroundItemClaimIsFirstComeFirstServed(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        instance.AddMember(new DungeonInstanceMember(
            alpha, 1, 0, "阿尔法", _ => ValueTask.CompletedTask));
        instance.AddMember(new DungeonInstanceMember(
            bravo, 2, 1, "布拉沃", _ => ValueTask.CompletedTask));
        instance.EnterDetachedRoom(DungeonFixture.Room([DungeonFixture.Monster(uniqueId: 0)]));

        DungeonGroundItem Drop(ushort ownerUserId) => instance.AddGroundItem(
            new DungeonGeneratedDrop(IsGold: true, ItemId: 0, CountOrValue: 100),
            ownerUserId,
            sourceMonsterUniqueId: 0,
            roomX: 0,
            roomY: 0,
            x: 10,
            y: 20);

        // ① 怪物掉落的 OwnerUserId 恒是击杀者（uid 1）：归属只做显示，uid 2 的队友照样赢下。
        var ownedDrop = Drop(ownerUserId: 1);
        check(instance.TryGetGroundItem(ownedDrop.GroundId, 0, 0, out var ownedSeen)
                && ownedSeen.OwnerUserId == 1,
            "a monster drop keeps its owner id for the client-side ownership display");
        check(instance.TryClaimGroundItem(ownedDrop.GroundId, 0, 0, out var ownedClaim)
                && ownedClaim.GroundId == ownedDrop.GroundId
                && ownedClaim.OwnerUserId == 1
                && !instance.TryGetGroundItem(ownedDrop.GroundId, 0, 0, out _),
            "a teammate can claim another member's drop (ownership never gates the pickup)");

        // ② 无主掉落（Task 5 的倒计时强制推进按 ownerUserId=0 登记）也必须能捡。
        var ownerlessDrop = Drop(ownerUserId: 0);
        check(instance.TryClaimGroundItem(ownerlessDrop.GroundId, 0, 0, out var ownerlessClaim)
                && ownerlessClaim.OwnerUserId == 0,
            "an ownerless drop (OwnerUserId 0) can be claimed as well");

        // ③ 并发仲裁：只有一次 true（这就是会话里 continue 与否的唯一判定）。
        var contestedDrop = Drop(ownerUserId: 1);
        var winners = 0;
        Parallel.For(0, 8, iteration =>
        {
            _ = iteration;
            if (instance.TryClaimGroundItem(contestedDrop.GroundId, 0, 0, out _))
            {
                Interlocked.Increment(ref winners);
            }
        });
        check(winners == 1, "eight concurrent claims settle exactly one winner");
        check(!instance.TryClaimGroundItem(contestedDrop.GroundId, 0, 0, out _)
                && !instance.TryGetGroundItem(contestedDrop.GroundId, 0, 0, out _),
            "the loser of the race gets false and the entity is gone (nothing left to persist or broadcast)");

        // ④ 落库失败的原样放回：同一个 GroundId/归属，别人还能再捡。
        // 断言要对"claim 失败"保持可读（别在契约被破坏时抛 NRE）：先落成可空局部再判。
        var returnedDrop = Drop(ownerUserId: 1);
        var claimedForReturn = instance.TryClaimGroundItem(
            returnedDrop.GroundId, 0, 0, out var claimedEntity)
                ? claimedEntity
                : null;
        check(claimedForReturn is not null,
            "the entity is claimed before the pickup persistence runs");
        check(claimedForReturn is not null
                && instance.TryReAddGroundItem(claimedForReturn)
                && instance.TryGetGroundItem(returnedDrop.GroundId, 0, 0, out var restored)
                && restored.GroundId == claimedForReturn.GroundId
                && restored.OwnerUserId == claimedForReturn.OwnerUserId,
            "a failed pickup persistence returns the exact entity (same ground id and owner)");
        check(claimedForReturn is not null && !instance.TryReAddGroundItem(claimedForReturn),
            "returning the same entity twice cannot duplicate the row");
        check(instance.TryClaimGroundItem(returnedDrop.GroundId, 0, 0, out _),
            "the returned entity can be claimed again (first come, first served)");
    }

    /// <summary>
    /// 共享状态广播（设计稿 §3.3）：击杀/房间开放只判定一次（实例的原子方法），
    /// 但**每个成员各收一份**；判定不成立的陈旧上报一份都不发。
    /// 注意：本用例跑的是**模型层的口径复述**（自己在 `Members` 上循环），不经过生产的
    /// `DungeonPartyService.BroadcastToMembersAsync`——生产广播（含 omit 自己）的护栏是同文件的
    /// <see cref="CheckPickupBroadcastOmitsSenderAsync"/>，那里才有可复核的变异验证。
    /// </summary>
    private static async Task CheckBroadcastReachesEveryMemberOnceAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        List<GameServerPacket> alphaInbox = [];
        List<GameServerPacket> bravoInbox = [];
        instance.AddMember(new DungeonInstanceMember(
            alpha, 1, 0, "阿尔法", packet => { alphaInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            bravo, 2, 1, "布拉沃", packet => { bravoInbox.Add(packet); return ValueTask.CompletedTask; }));
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the fixture run starts for the broadcast case");
        var victim = instance.AliveMonsters.First();
        // 任务书原文这里传 `championRewardOrdinal: 0`：0 既不是"无奖励"哨兵（-1）也不在 1..11，
        // `CreateMonsterDie` 会抛 ArgumentOutOfRangeException（实测）。普通怪用引擎的哨兵常量。
        var monsterDie = GameProtocolEngine.CreateMonsterDie(
            uniqueId: victim, drops: [], championRewardOrdinal: GameProtocolEngine.NoChampionRewardOrdinal);
        // 模拟"两个会话都上报了同一只怪"：只有第一次判定成立，但成立的这一次要发给**每个人**。
        for (var round = 0; round < 2; round++)
        {
            if (!instance.TryTakeMonsterDeath(victim, out _))
            {
                continue;
            }

            foreach (var member in instance.Members)
            {
                await member.Send(monsterDie);
            }
        }

        check(alphaInbox.Count == 1 && bravoInbox.Count == 1
                && alphaInbox[0].ProtocolId == GameProtocolEngine.MonsterDieNotification
                && bravoInbox[0].ProtocolId == GameProtocolEngine.MonsterDieNotification,
            "a single kill is broadcast once to every member and stale reports broadcast nothing");
    }

    /// <summary>
    /// 拾取广播的 omit 语义（任务书 Step 4③的硬要求）：`BroadcastToMembersAsync` 只发给
    /// "除 omit 以外"的成员——拾取者那一份已经走本会话的 `stream` 发过，**绝不能再收第二份**。
    /// 会话接线本身没有单测（Task 2 已记），这里是能自动化的那一半证据。
    /// **变异验证**：把 `BroadcastToMembersAsync` 里的 omit 判断删掉 → 第一条断言变红。
    /// </summary>
    private static async Task CheckPickupBroadcastOmitsSenderAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            new DungeonInstanceRegistry(),
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(1000);
        var picker = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var pickerInbox = new List<GameServerPacket>();
        var peerInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(
            picker, 1, 0, "拾取者", packet => { pickerInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            peer, 2, 1, "队友", packet => { peerInbox.Add(packet); return ValueTask.CompletedTask; }));
        var pickup = GameProtocolEngine.CreateGoldItemPickup(
            groundId: 7, pickerUserId: 1, goldAmount: 100, pickerPartyIndex: 0);

        await service.BroadcastToMembersAsync(instance, pickup, omitCharacterId: picker);

        check(pickerInbox.Count == 0,
            "the picker's own broadcast copy is omitted (it already went out on the session stream)");
        check(peerInbox.Count == 1 && peerInbox[0].ProtocolId == GameProtocolEngine.GetItemNotification,
            "the other members receive exactly one pickup notification");
        // 不 omit 时两个成员各收一份：证明上面那次"0 份"确实来自 omit，而不是投递被吞了。
        await service.BroadcastToMembersAsync(instance, pickup);
        check(pickerInbox.Count == 1 && peerInbox.Count == 2,
            "without an omit target the same packet reaches every member (no delivery is swallowed)");
    }

    /// <summary>
    /// I2（评审 Important 2）：入场中止路径（`TryStartRun` 失败 / 地狱票被拒 / 票落库失败）会留下
    /// "有成员、run 已停"的实例；那种实例**不可加入**，且取当前房间快照必须返回 false 而不是抛
    /// （异常逃到会话外层 catch 会把会话打断）。
    /// **变异验证**：① 把 `CanAcceptJoin` 里的 `_run.IsActive` 去掉 → 第三条断言变红；
    /// ② 把 `TryCaptureCurrentRoomSnapshot` 改回"没有房间就抛" → 第一条/第四条断言变红（异常）。
    /// </summary>
    private static void CheckStoppedInstanceCannotBeJoined(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), slot: 0, "队长"));
        check(!instance.CanAcceptJoin
                && !instance.TryCaptureCurrentRoomSnapshot(ownerUserId: 1, out _),
            "an instance whose run never started cannot be joined and has no current room");
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the fixture instance starts a run");
        check(instance.CanAcceptJoin
                && instance.TryCaptureCurrentRoomSnapshot(ownerUserId: 1, out var liveRoom)
                && liveRoom.RoomX == instance.Run.RoomX
                && liveRoom.RoomY == instance.Run.RoomY,
            "a live instance accepts joins and hands out its current room");
        instance.StopRun();
        check(!instance.CanAcceptJoin,
            "stopping the run closes the instance to joins");
        check(!instance.TryCaptureCurrentRoomSnapshot(ownerUserId: 1, out _),
            "a stopped instance reports no current room instead of throwing");
    }

    /// <summary>
    /// 硬规则 2（Task 2 评审遗留项）：`_byCharacter` 同时被 `BindCharacter` 用于队员（指向**队伍**实例），
    /// `GetOrCreateForSolo` **只许复用单人实例**——命中队伍实例时必须另建单人实例，
    /// 否则单人入场会把自己塞进队伍实例，房间/掉落/成员广播被两队共用。
    /// **变异验证**：把 `GetOrCreateForSolo` 改回 `_byCharacter.GetOrAdd`，本用例第一条变红。
    /// </summary>
    private static void CheckSoloLookupNeverReusesPartyBinding(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        var party = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        registry.BindCharacter(characterId, party);
        var solo = registry.GetOrCreateForSolo(
            characterId, () => fixture.CreateSoloInstance(characterId));
        check(!solo.IsParty && !ReferenceEquals(solo, party) && solo.SoloCharacterId == characterId,
            "GetOrCreateForSolo never hands out the party instance a character is bound to");
        check(ReferenceEquals(
                solo,
                registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId))),
            "the solo instance is still reused for that character");
    }

    /// <summary>
    /// 同区域校验（用户硬性要求，设计稿 §3.2）：队长 `CMD 16` 时逐人核对 `(town, area)`，
    /// 任一队员不在同一区域 → 整队放弃（只记日志、谁的状态都不改）。
    /// </summary>
    private static async Task CheckGatheringValidationAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        // BackgroundService 不实现 IAsyncDisposable（.NET 10 实测 CS8417），用同步 Dispose。
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leader = harness.IdOf("阿尔法");
        var member = harness.IdOf("布拉沃");
        var gathering = service.TryGetGatheringParty(leader, townId: 1, areaId: 0, out var outsiders);
        check(gathering is { Length: 2 } && outsiders.Length == 0,
            "a party standing in the same area passes the gathering check");

        // 把布拉沃挪到别的区域（目录是权威），队长再校验 → 整队放弃。
        await harness.MoveToAreaAsync("布拉沃", townId: 1, areaId: 1);
        var blocked = service.TryGetGatheringParty(leader, townId: 1, areaId: 0, out var blockedOutsiders);
        check(blocked is null && blockedOutsiders.Length == 1 && blockedOutsiders[0] == member,
            "one member outside the leader's area aborts the whole party entry");
        _ = fixture;
    }

    /// <summary>
    /// 请求投递的成员过滤（Task 3 评审 Minor 4）：**队长自己不被投递**；不在同一区域的队员不被投递
    /// （异区时整队 abort，两条投递路径都返回 0）。全部走 `PartyHarness` 的真实组队/换区流程。
    /// **变异验证**：把投递循环里的"跳过队长"判断去掉 → 第一条/第二条断言变红；
    /// 把同区域校验换成"永远放行" → 第三条/第四条断言变红。
    /// </summary>
    private static async Task CheckRequestTargetingAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leader = harness.IdOf("阿尔法");
        var member = harness.IdOf("布拉沃");
        var instance = fixture.CreatePartyInstance(1000);

        check(service.PostEntryRequests(instance, 14, 0, 0, leader, townId: 1, areaId: 0) == 1
                && !registry.TryTakeEntryRequest(leader, out _)
                && registry.TryTakeEntryRequest(member, out var entry)
                && entry.LeaderCharacterId == leader,
            "an entry request goes to the other member only, never to the leader");
        check(service.PostSelectionRequests(leader, townId: 1, areaId: 0) == 1
                && !registry.TryTakeSelectionRequest(leader, out _)
                && registry.TryTakeSelectionRequest(member, out var selectionLeader)
                && selectionLeader == leader,
            "a selection request goes to the other member only, never to the leader");

        // 异区：整队放弃，两条投递路径都不投任何人（谁的状态都不改）。
        await harness.MoveToAreaAsync("布拉沃", townId: 1, areaId: 1);
        check(service.PostEntryRequests(instance, 14, 0, 0, leader, townId: 1, areaId: 0) == 0
                && !registry.TryTakeEntryRequest(leader, out _)
                && !registry.TryTakeEntryRequest(member, out _),
            "an outsider blocks the whole party: no entry request is posted");
        check(service.PostSelectionRequests(leader, townId: 1, areaId: 0) == 0
                && !registry.TryTakeSelectionRequest(leader, out _)
                && !registry.TryTakeSelectionRequest(member, out _),
            "an outsider blocks the whole party: no selection request is posted");
    }

    /// <summary>
    /// 队员入场报文序列（设计稿 §3.2，硬顺序）：**名册 → NOTI 26（自己的槽位）→ NOTI 11 → NOTI 28 → NOTI 29**；
    /// 没有名册时名册位跳过，其余顺序不变。`NOTI 26` 的 partyIndex 必须是**收件人自己的槽位**。
    /// **变异验证**：把 udpHost 的槽位改成 0、或把 dungeonInfo 放到 roster 之前，本用例变红。
    /// </summary>
    private static void CheckMemberEntryPacketOrder(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, difficulty: 0, dungeonOption: 0, out var transition),
            "the fixture run starts");
        var snapshot = instance.CaptureRoomSnapshot(instance.Run.RoomX, instance.Run.RoomY, transition, 7);
        var roster = GameProtocolEngine.CreatePartyInfoNotification(
            1000, nameSel: 1, maxMembers: 4, slotUids: [11, 22, 0xFFFF, 0xFFFF], leaderSlot: 0);
        var peerTable = GameProtocolEngine.CreateUdpPeerInfo([]);
        var packets = DungeonEntryPackets.PlanMemberEntry(instance, snapshot, roster, peerTable, slotIndex: 2);
        var ids = packets.Select(packet => (packet.Type, packet.ProtocolId)).ToArray();
        check(ids is
            [
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.PartyInfoNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.UdpHostNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.UdpPeerInfoNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.DungeonInfoNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.StartMapNotification)
            ],
            "member entry sends roster, UDP host, peer table, DUNGEON_INFO and START_MAP in that order");
        check(packets[1].Payload[0] == 2,
            "NOTI 26 carries the member's own party slot index");
        var startMap = packets[4];
        // START_MAP 布局：roomX(0) roomY(1) randomSeed(2..5) roomStateFlag(6) mapId(7..8) ...
        check(BinaryPrimitives.ReadUInt16LittleEndian(startMap.Payload.AsSpan(7, 2)) == snapshot.MapId,
            "START_MAP carries the instance room map id");
        var withoutRoster = DungeonEntryPackets.PlanMemberEntry(instance, snapshot, null, peerTable, 2);
        check(withoutRoster.Count == 4 && withoutRoster[0].ProtocolId == GameProtocolEngine.UdpHostNotification,
            "a missing roster is skipped without disturbing the rest of the order");
    }

    /// <summary>
    /// 单人零回归的结构性断言（设计稿 §4/§6 第 8 项）：单人进场绑定的是"以自己角色 id 为键"的实例、
    /// 实例里只有自己一个成员、`IsParty == false`（因此 `CMD 48` 走"立即换房间"、不走门口集结）。
    /// **变异验证**：把 `GetOrCreateForSolo` 换成 `GetOrCreateForParty`、或让 AddMember 加两个人，本用例变红。
    /// </summary>
    private static void CheckSoloRunBindsByCharacterId(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        var instance = registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId));
        registry.BindCharacter(characterId, instance);
        instance.AddMember(fixture.Member(characterId, slot: 0, "独行"));
        check(!instance.IsParty && instance.MemberCount == 1
                && ReferenceEquals(instance, registry.TryGetForCharacter(characterId)),
            "a solo dungeon runs in a single-member instance keyed by the character id");
        check(!instance.Doors.IsWaiting,
            "a solo instance never enters the door assembly state (CMD 48 keeps advancing immediately)");
    }

    /// <summary>单人换房间：`TryMoveRoom` 立即推进（没有"等其他人"这一步），掉落在实例里跟着走。</summary>
    private static void CheckSoloRoomChangeKeepsImmediateBehaviour(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), slot: 0, "独行"));
        var started = instance.TryStartRun(fixture.Layout, 0, 0, out _);
        // `out` 一定要单独一句话（放在 && 里编译器会报"未赋值"）：先无条件调一次再断言结论。
        var moved = instance.TryMoveRoom(1, 0, out var transition);
        check(started && moved && instance.Run.RoomX == 1 && !instance.Doors.IsWaiting,
            "a solo room change applies immediately and leaves no pending door assembly");
        check(instance.CaptureRoomSnapshot(1, 0, transition, ownerUserId: 1).RoomX == 1,
            "the room snapshot follows the solo move");
    }

    /// <summary>单人实例：键 = 角色 id、单成员、`IsParty == false`（设计稿 §3.1/§7 决策 1）。</summary>
    private static void CheckSoloInstanceKey(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        var instance = registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId));
        check(!instance.IsParty && instance.SoloCharacterId == characterId && instance.PartyId == 0,
            "a solo dungeon instance is keyed by the character id");
        check(ReferenceEquals(
                instance,
                registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId))),
            "asking for the same solo character twice returns the same instance");
        // 单人的键就是角色 id：`GetOrCreateForSolo` 用**单人专属**的键表（`_soloByCharacter`），
        // 与 <c>BindCharacter</c> 的会话绑定表（`_byCharacter`）分开——后者也会指向队伍实例。
        // 未登记过的角色才查不到。
        check(registry.TryGetForCharacter(Guid.NewGuid()) is null,
            "a character that never bound itself has no dungeon instance");
        registry.BindCharacter(characterId, instance);
        check(ReferenceEquals(instance, registry.TryGetForCharacter(characterId)),
            "binding a character maps it to its instance");
    }

    /// <summary>成员增删幂等 + 最后一个成员离开后实例销毁（设计稿 §3.1）。</summary>
    private static void CheckInstanceMembershipLifecycle(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var instance = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        check(instance.AddMember(fixture.Member(alpha, slot: 0, "阿尔法")),
            "a party instance accepts its first member");
        check(!instance.AddMember(fixture.Member(alpha, slot: 0, "阿尔法")),
            "adding the same character twice is idempotent");
        check(instance.AddMember(fixture.Member(bravo, slot: 2, "布拉沃")) && instance.MemberCount == 2,
            "a second member joins the same instance");
        check(instance.RemoveMember(bravo) && instance.MemberCount == 1 && !instance.ContainsMember(bravo),
            "leaving removes exactly that member");
        check(!instance.RemoveMember(bravo), "removing an absent member is a no-op");

        // 先起一局：解绑时的 StopRun 必须真的被断言到（不起 run 的话 !Run.IsActive 恒真）。
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _, random: fixture.Random),
            "the party instance starts a run before its last member leaves");

        // 设计稿 §3.4：最后一个成员离开 → 实例就地销毁（注册表两张表都要摘掉，运行态清空）。
        registry.BindCharacter(alpha, instance);
        check(ReferenceEquals(instance, registry.UnbindCharacter(alpha)) && !instance.ContainsMember(alpha),
            "unbinding the last member removes it from the instance");
        check(registry.TryGetForParty(1000) is null && !instance.Run.IsActive,
            "the last member leaving destroys the party instance and discards its run");
    }

    /// <summary>
    /// 收尾（设计稿 §3.4）：任一成员离开 → 只解绑自己，实例仍活（房间/掉落/run 留给其余成员）；
    /// 最后一个成员离开 → 实例销毁（房间/掉落全部丢弃）；队长离开不特殊——实例留给其余成员。
    /// 先起 run 再解绑：不起 run 的话 `Run.IsActive` 的两条断言恒真（Task 1 评审的"断言重言"教训）。
    /// **变异验证**（已实测变红后回滚）：把 `UnbindCharacter` 的"空了才 Remove"改成"每次解绑都
    /// Remove" → 上面三条（run 还活着 / 按队伍仍查得到 / 最后一人销毁并丢 run）变红；第一条
    /// （解绑返回值 MemberCount==1）**不会**变红——返回值来自实例本身，与注册表是否销毁它无关，
    /// 这正是必须补后两条断言的原因。
    /// </summary>
    private static void CheckLastMemberDestroysInstance(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var instance = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        instance.AddMember(fixture.Member(alpha, 0, "阿尔法"));
        instance.AddMember(fixture.Member(bravo, 2, "布拉沃"));
        registry.BindCharacter(alpha, instance);
        registry.BindCharacter(bravo, instance);
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _, random: fixture.Random),
            "the party instance runs before anyone leaves it");
        check(registry.UnbindCharacter(alpha) is { MemberCount: 1 },
            "leaving the party instance keeps it alive for the remaining member");
        // 关键：离开的那个人没有把"别人的一局"带走——其余成员还要接着打（真机第 6 项）。
        check(instance.Run.IsActive,
            "one member leaving does not stop the shared run (the survivors keep their rooms and drops)");
        check(ReferenceEquals(instance, registry.TryGetForParty(1000)),
            "the surviving member can still find the instance by party id");
        check(registry.UnbindCharacter(bravo) is { MemberCount: 0 }
                && registry.TryGetForParty(1000) is null
                && registry.TryGetForCharacter(bravo) is null
                && !instance.Run.IsActive,
            "the last member leaving destroys the instance and discards its run");
    }

    /// <summary>
    /// 离开提示（设计稿 §3.4）：离开者自己收不到（他已经在回城/断线路径上，客户端侧不留 actor），
    /// 其余成员各收一份既有的"角色离场"信号——USER_AREA 带 0xFF/0xFF 哨兵 + 离开者的 uid
    /// （客户端按区域不匹配销毁该 actor，见 TownSyncSmokeTests 的离场断言）。
    /// **变异验证**：把 `BroadcastToMembersAsync` 的 omit 去掉 → 第一条变红；
    /// 把 `CreateUserAreaDeparture` 换成同区域 `CreateUserArea(active: false)` → 第二条变红。
    /// 会话接线（三条离开路径何时调用它）不可单测：真机检查点是 Task 7 第 6 项。
    /// </summary>
    private static async Task CheckMemberDeparturePacketAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            new DungeonInstanceRegistry(),
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(1000);
        var leaver = Guid.NewGuid();
        var survivor = Guid.NewGuid();
        var leaverInbox = new List<GameServerPacket>();
        var survivorInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(
            leaver, 7, 0, "离开者", packet => { leaverInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            survivor, 8, 1, "留守者", packet => { survivorInbox.Add(packet); return ValueTask.CompletedTask; }));

        await service.NotifyMemberLeftAsync(
            instance, leaver, characterName: "离开者", areaUserId: 7, x: 120, y: 340, direction: 3);

        check(leaverInbox.Count == 0,
            "the member who left receives no departure notice of their own");
        check(survivorInbox.Count == 1
                && survivorInbox[0].ProtocolId == GameProtocolEngine.UserAreaNotification
                && survivorInbox[0].Payload.Length == 10
                && survivorInbox[0].Payload[0] == 7
                && survivorInbox[0].Payload[2] == 0xFF
                && survivorInbox[0].Payload[3] == 0xFF,
            "the remaining member receives the USER_AREA departure signal with the leaver's uid (0xFF/0xFF sentinel)");
    }

    /// <summary>
    /// 并发语义：同一只怪被两个会话同时上报只结算一次（`AliveMonsters.Remove` 的返回值就是唯一判定，
    /// 与改造前 `aliveDungeonMonsters.Remove` 同口径）；房间开放（`TryEnableClear`）同样只翻一次。
    /// **变异验证**：让 `TryTakeMonsterDeath` 无视 `AliveMonsters.Remove` 的返回值（每条上报都返回 true），
    /// 本用例的两条死亡断言变红（已实测）。注意：把 `MarkMonsterDefeated` 提到判断之前**不会**变红——
    /// 结算的唯一判定就是锁内的那一次 `Remove`，`MarkMonsterDefeated` 自身幂等。
    /// </summary>
    private static void CheckConcurrentKillSettlesOnce(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, difficulty: 0, dungeonOption: 0, out _),
            "the fixture layout starts a run");
        var victim = instance.AliveMonsters.First();
        var wins = 0;
        Parallel.For(0, 8, iteration =>
        {
            _ = iteration;
            if (instance.TryTakeMonsterDeath(victim, out _))
            {
                Interlocked.Increment(ref wins);
            }
        });
        check(wins == 1, "eight concurrent death reports settle exactly one kill");
        check(!instance.TryTakeMonsterDeath(victim, out _), "a second report is stale");

        var clearWins = 0;
        Parallel.For(0, 8, iteration =>
        {
            _ = iteration;
            if (instance.TryEnableClear())
            {
                Interlocked.Increment(ref clearWins);
            }
        });
        check(clearWins == 1, "enabling room settlement flips exactly once");
    }

    /// <summary>房间快照：换房间刷新 `AliveMonsters`；被动掉落只在首次访问登记一次，重访只读已登记的。</summary>
    private static void CheckRoomSnapshotsAndPassiveDrops(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, 0, 0, out var start),
            "the run starts in the fixture start room");
        var firstSnapshot = instance.CaptureRoomSnapshot(instance.Run.RoomX, instance.Run.RoomY, start, ownerUserId: 7);
        check(firstSnapshot.FirstVisit && !firstSnapshot.ReuseClientRoomState
                && firstSnapshot.Monsters.Length == start.Room.Monsters.Length,
            "the first visit snapshot carries the room monster table");
        check(instance.TryMoveRoom(1, 0, out var second),
            "moving to the neighbouring room succeeds");
        var secondSnapshot = instance.CaptureRoomSnapshot(1, 0, second, ownerUserId: 7);
        check(secondSnapshot.FirstVisit && instance.AliveMonsters.SetEquals(
                secondSnapshot.Monsters.Select(monster => monster.UniqueId)),
            "moving refreshes the alive monster set from the new room");
        check(!instance.TryMoveRoom(1, 0, out _), "moving into the current room is rejected");
        check(instance.TryMoveRoom(0, 0, out var revisit) && revisit.Revisit,
            "walking back marks the room as a revisit");
        var revisitSnapshot = instance.CaptureRoomSnapshot(0, 0, revisit, ownerUserId: 7);
        // 与 DungeonRunState 的既有契约一致：普通重访让客户端复用上次的 START_MAP 快照
        // （ReuseClientRoomState = true），只有"地狱房已清"的重访才必须重建为空地图。
        check(!revisitSnapshot.FirstVisit && revisitSnapshot.ReuseClientRoomState,
            "an ordinary revisit reuses the client's cached room state");
    }

    /// <summary>
    /// "加入一个已经在跑的实例"（`CaptureCurrentRoomSnapshot`）不许把被动掉落重新登记一遍：
    /// 判据必须是"这个房间登记过没有"，而不是"现在还剩几条掉落"——掉落被捡走（`Remove`）或 detach 后
    /// 后者会归零，而房间定义里的 `PassiveItemSpawns` 是静态的，重登 = 新 GroundId + 客户端幽灵物件 = 复制道具。
    /// 用 `EnterDetachedRoom`（教学副本路径）喂一个合成房间：不依赖 PVF 掉落脚本，跑的是同一条
    /// `CaptureCurrentRoomSnapshot → RegisterRoomPassiveDropsLocked` 代码。
    /// </summary>
    private static void CheckLiveJoinDoesNotReplenishPassiveDrops(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var room = DungeonFixture.Room(
            [DungeonFixture.Monster(uniqueId: 0), DungeonFixture.Monster(uniqueId: 1)],
            passiveDropCount: 2);
        instance.EnterDetachedRoom(room);

        var firstJoin = instance.CaptureCurrentRoomSnapshot(ownerUserId: 7);
        check(firstJoin.PassiveObjectDrops.Length == 2
                && firstJoin.PassiveObjectDrops.All(drop => drop.OwnerUserId == 7)
                && instance.GetRoomGroundItems(0, 0).Length == 2,
            "a live join registers the room passive drops exactly once");
        var lootedGroundIds = instance.GetRoomGroundItems(0, 0)
            .Select(item => item.GroundId)
            .ToArray();

        // 队友进来之前这批掉落已经被捡光（= DungeonGroundItemState.Remove）。
        foreach (var groundId in lootedGroundIds)
        {
            instance.RemoveGroundItem(groundId);
        }

        var secondJoin = instance.CaptureCurrentRoomSnapshot(ownerUserId: 8);
        check(secondJoin.PassiveObjectDrops.Length == 0
                && instance.GetRoomGroundItems(0, 0).Length == 0,
            "a live join must not re-register passive drops that were already looted");

        // 换局/重开必须忘掉登记表：同一个实例再进这个房间时要重新登记（新 GroundId）。
        instance.EnterDetachedRoom(room);
        var reopened = instance.CaptureCurrentRoomSnapshot(ownerUserId: 9);
        check(reopened.PassiveObjectDrops.Length == 2
                && reopened.PassiveObjectDrops.All(drop => !lootedGroundIds.Contains(drop.GroundId)),
            "re-entering the room after a reset registers a fresh passive drop set");
    }

    /// <summary>
    /// 实例共享集合的锁内访问口（评审 Important 3）：裸集合一律 internal、只许在 `lock (Gate)` 内碰；
    /// `TryReportBossDeath` 复刻改造前 BOSS_DIE_CHECK 的"上报 + 全 boss 到齐后级联"三句
    /// （EntranceService.cs:12333-12341），并保持"同一 boss 重复上报幂等、不重复级联"。
    /// </summary>
    private static void CheckSharedCollectionAccessors(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.EnterDetachedRoom(DungeonFixture.Room(
            [
                DungeonFixture.Monster(uniqueId: 0, type: GameDungeonMonsterTypes.Boss),
                DungeonFixture.Monster(uniqueId: 1, type: GameDungeonMonsterTypes.Boss),
                DungeonFixture.Monster(uniqueId: 2)
            ]));

        check(instance.AliveMonsterCount == 3,
            "the alive monster count is read behind the instance gate");
        var aliveCopy = instance.SnapshotAliveMonsters();
        aliveCopy.Remove(0);
        check(instance.AliveMonsterCount == 3 && instance.SnapshotAliveMonsters().SetEquals([0, 1, 2]),
            "the alive monster snapshot is a copy that cannot corrupt the instance");

        ushort[] bossIds = [0, 1];
        check(instance.TryReportBossDeath(0, bossIds, out var allBossesReported)
                && !allBossesReported && instance.ReportedBossDeathCount == 1,
            "the first boss report is recorded without completing the boss list");
        check(!instance.TryReportBossDeath(0, bossIds, out _) && instance.ReportedBossDeathCount == 1,
            "a repeated boss report is idempotent");
        check(instance.TryReportBossDeath(1, bossIds, out allBossesReported) && allBossesReported
                && instance.ReportedBossDeathCount == 2
                && instance.BossCascadeMonsterDeaths.SetEquals([2]),
            "completing the boss list cascades the remaining non-boss monsters");

        var droppedItem = new CharacterItemRecord(Slot: ushort.MaxValue, ItemId: 3, CountOrValue: 5);
        var groundItem = instance.AddPlayerDroppedGroundItem(
            droppedItem, ownerUserId: 7, roomX: 0, roomY: 0, x: 12, y: 34);
        check(groundItem.ItemId == 3 && groundItem.CountOrValue == 5
                && instance.TryGetGroundItem(groundItem.GroundId, 0, 0, out var found)
                && ReferenceEquals(groundItem, found)
                && instance.GetRoomGroundItems(0, 0).Length == 1,
            "a player-dropped item is registered behind the instance gate");
        instance.ResetGroundItems();
        check(instance.GetRoomGroundItems(0, 0).Length == 0,
            "resetting the dungeon drops every ground item");
    }

    /// <summary>
    /// 换房间必须重置会话级「每房结算」标志（Task 5 评审 Important）：结果屏/卡片布局/选卡权/选卡截止/
    /// 结算奖励/两张已选卡要整组回到默认，否则 `ClearEnabled &amp;&amp; !ResultSent` 的守卫
    /// （`EntranceService` 的 `SET_PLAY_RESULT` 分支）会把第二间及以后的结算整个吞掉。
    /// 单人 <c>CMD 48</c> 一直这么做；门口集结制让房间也能被**别人**推进，所以这套状态搬进可测对象，
    /// 三条换房路径（单人立即推进 / 组队自己推进 / 被强制推进后的镜像追赶）共用
    /// `ResetRoomSettlementState()` → 这里的 `Reset()`。
    /// **变异验证**：删掉 `Reset()` 里任意一条赋值（实测删 `ResultSent = false;`）→ 第一条断言变红。
    /// </summary>
    private static void CheckRoomSettlementReset(Action<bool, string> check)
    {
        check(new DungeonRoomSettlementState() is
                {
                    ResultSent: false,
                    CardLayoutSent: false,
                    CardRightsSent: false,
                    CardSelectionDeadline: null,
                    ClearReward: null,
                    FreeCardIndex: null,
                    GoldCardIndex: null
                },
            "a fresh room starts with no settlement state (no result screen, cards or reward)");

        var state = new DungeonRoomSettlementState
        {
            ResultSent = true,
            CardLayoutSent = true,
            CardRightsSent = true,
            CardSelectionDeadline = DateTimeOffset.UtcNow.AddSeconds(30),
            ClearReward = new DungeonClearRewardRoll(
                GoldCardCost: 1000,
                GoldCardEnabled: true,
                FreeGoldAmount: 100,
                FreeItemId: 1,
                PremiumItemId: 2,
                GoldItemId: 3),
            FreeCardIndex = 1,
            GoldCardIndex = 2
        };
        state.Reset();
        check(state is
                {
                    ResultSent: false,
                    CardLayoutSent: false,
                    CardRightsSent: false,
                    CardSelectionDeadline: null,
                    ClearReward: null,
                    FreeCardIndex: null,
                    GoldCardIndex: null
                },
            "a room change resets every per-room settlement flag (the next room settles again)");
    }

    /// <summary>
    /// 追赶式房间疲劳的"首访才扣"门控（Task 5 评审 Minor 2）：`DungeonInstance.LastRoomEntryWasFirstVisit`
    /// 跟着**最近一次进房**走——开局与新房间是首访（true），重访是 false（重访让客户端复用缓存快照，
    /// 单人不扣、组队的镜像追赶也不能扣），实例收尾后没有当前房间 → 回到 false。
    /// **变异验证**：把 `TryMoveRoom` 里那行 `LastRoomEntryWasFirstVisit = transition.FirstVisit;`
    /// 改成恒 `true` → 第三条断言变红（实测）。
    /// </summary>
    private static void CheckRoomFirstVisitFlag(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _) && instance.LastRoomEntryWasFirstVisit,
            "starting the run counts as a first visit (the catch-up path charges the start room)");
        var moved = instance.TryMoveRoom(1, 0, out var firstMove);
        check(moved && firstMove.FirstVisit && instance.LastRoomEntryWasFirstVisit,
            "moving into a new room counts as a first visit");
        var revisited = instance.TryMoveRoom(0, 0, out var revisit);
        check(revisited && !revisit.FirstVisit && !instance.LastRoomEntryWasFirstVisit,
            "walking back into a visited room clears the first-visit flag (a revisit charges no fatigue)");
        instance.StopRun();
        check(!instance.LastRoomEntryWasFirstVisit,
            "a stopped instance has no current room and no first-visit flag");
    }

    /// <summary>
    /// 门口集结（设计稿 §3.3）：
    /// ①第一个请求只登记、不推进；②不同目标的请求忽略；③全员到齐立即推进；
    /// ④过半开 10 秒倒计时、到点强制推进；⑤推进后状态清空（下一次从零开始）；
    /// ⑥同一成员重复登记（等待期间重发 CMD 48）仍只算一个到门口者，但回包换成**最新**那一份。
    /// </summary>
    private static void CheckDoorAssemblyRules(Action<bool, string> check)
    {
        var assembly = new DungeonDoorAssembly();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var charlie = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var target = new DungeonRoomPosition(1, 0);
        var other = new DungeonRoomPosition(0, 1);

        var first = assembly.Register(target, alpha, memberCount: 4, now, Reply(alpha));
        check(first.Decision == DungeonDoorDecision.Waiting && assembly.IsWaiting
                && assembly.PendingTarget == target,
            "the first door request only registers and waits");
        var wrongDoor = assembly.Register(other, bravo, memberCount: 4, now, Reply(bravo));
        check(wrongDoor.Decision == DungeonDoorDecision.Ignored && wrongDoor.Arrivals.Count == 1,
            "a request for a different door is ignored");
        var second = assembly.Register(target, bravo, memberCount: 4, now, Reply(bravo));
        check(second.Decision == DungeonDoorDecision.Waiting,
            "two of four members is not yet a majority");
        var third = assembly.Register(target, charlie, memberCount: 4, now, Reply(charlie));
        check(third.Decision == DungeonDoorDecision.StartedCountdown
                && third.CountdownDeadline == now + DungeonDoorAssembly.Countdown,
            "a majority starts the ten second countdown");
        check(assembly.TryAdvanceOnCountdown(now + TimeSpan.FromSeconds(9), memberCount: 4) is null,
            "the countdown does not fire early");
        var forced = assembly.TryAdvanceOnCountdown(now + TimeSpan.FromSeconds(10), memberCount: 4);
        check(forced is { Decision: DungeonDoorDecision.Advance, Forced: true }
                && forced.Arrivals.Count == 3
                && !assembly.IsWaiting && assembly.CountdownDeadline is null,
            "the countdown expiry advances the room for everyone and resets the assembly");

        var all = assembly.Register(target, alpha, memberCount: 3, now, Reply(alpha));
        check(all.Decision == DungeonDoorDecision.Waiting, "a new assembly starts from scratch");
        assembly.Register(target, bravo, memberCount: 3, now, Reply(bravo));
        var complete = assembly.Register(target, charlie, memberCount: 3, now, Reply(charlie));
        check(complete.Decision == DungeonDoorDecision.Advance && !complete.Forced
                && complete.Arrivals.Count == 3,
            "every member at the door advances immediately without waiting for the countdown");

        // 单人（memberCount <= 1）：门口请求直接推进，不进等待/倒计时。
        var solo = assembly.Register(target, alpha, memberCount: 1, now, Reply(alpha));
        check(solo.Decision == DungeonDoorDecision.Advance && !solo.Forced && !assembly.IsWaiting,
            "a single-member door request advances immediately");

        // 人数缩水：4 人时 2 人到门口只是 Waiting；只剩 3 人时 2/3 已过半，扫描循环必须能重新评估，
        // 否则这批人既拿不到 CMD 48 回包、也永远换不了房间。
        assembly.Register(target, alpha, memberCount: 4, now, Reply(alpha));
        assembly.Register(target, bravo, memberCount: 4, now, Reply(bravo));
        var shrunk = assembly.TryAdvanceOnCountdown(now, memberCount: 3);
        check(shrunk is { Decision: DungeonDoorDecision.StartedCountdown, Forced: false }
                && shrunk.CountdownDeadline == now + DungeonDoorAssembly.Countdown,
            "a shrinking roster re-evaluates the door into a countdown");
        check(assembly.TryAdvanceOnCountdown(now, memberCount: 3) is null,
            "re-evaluating again before the deadline changes nothing");
        var shrunkForced = assembly.TryAdvanceOnCountdown(
            now + DungeonDoorAssembly.Countdown, memberCount: 3);
        check(shrunkForced is { Decision: DungeonDoorDecision.Advance, Forced: true }
                && !assembly.IsWaiting && assembly.CountdownDeadline is null,
            "the re-evaluated countdown still expires into a forced advance");

        // 人数缩到与到门口人数相等 → 与 Register 的全员到齐走同一条路径（不必等倒计时）。
        assembly.Register(target, alpha, memberCount: 4, now, Reply(alpha));
        assembly.Register(target, bravo, memberCount: 4, now, Reply(bravo));
        var settled = assembly.TryAdvanceOnCountdown(now, memberCount: 2);
        check(settled is { Decision: DungeonDoorDecision.Advance, Forced: false }
                && settled.Arrivals.Count == 2 && !assembly.IsWaiting,
            "a roster shrinking to the arrived headcount advances immediately");

        // 重复登记：客户端在等回包期间重发 CMD 48（真机第一检查点就是"客户端能否接受延迟回包"）。
        // 同一角色仍只算一个到门口者，但推进时补发的必须是**最新**那份回包（旧序列号客户端对不上号）。
        var reRegisterAssembly = new DungeonDoorAssembly();
        var staleAlphaReply = Reply(alpha);
        var latestAlphaReply = Reply(alpha);
        var firstRegister = reRegisterAssembly.Register(target, alpha, memberCount: 2, now, staleAlphaReply);
        var secondRegister = reRegisterAssembly.Register(target, alpha, memberCount: 2, now, latestAlphaReply);
        check(firstRegister.Decision == DungeonDoorDecision.Waiting
                && secondRegister.Decision == DungeonDoorDecision.Waiting
                && secondRegister.Arrivals.Count == 1
                && ReferenceEquals(secondRegister.Arrivals[0].Reply, latestAlphaReply)
                && !ReferenceEquals(secondRegister.Arrivals[0].Reply, staleAlphaReply),
            "re-registering the same member keeps exactly one arrival but swaps in the newest CMD 48 reply");
        var afterReRegister = reRegisterAssembly.Register(target, bravo, memberCount: 2, now, Reply(bravo));
        check(afterReRegister.Decision == DungeonDoorDecision.Advance
                && afterReRegister.Arrivals.Count == 2
                && afterReRegister.Arrivals[0].CharacterId == alpha
                && ReferenceEquals(afterReRegister.Arrivals[0].Reply, latestAlphaReply),
            "the swapped-in reply is the one the advance later delivers to that member");
    }

    /// <summary>
    /// 门口集结里"到过门口的人"各自的那份 `CMD 48` 成功回包（生产代码在 `Register` 时用请求帧就地生成）。
    /// 用例传的是**逐人独立**的实例：推进入口要把它原样投给那个成员的收件箱（不是别的人那份）。
    /// </summary>
    private static GameServerPacket Reply(Guid _) =>
        GameProtocolEngine.CreateCommandReply(
            new PacketFrame(
                GameProtocolEngine.CommandPacketType,
                GameProtocolEngine.MoveMapCommand,
                DeclaredCrc32: 0,
                Body: [0, 0, 1, 0]),
            success: true);

    /// <summary>
    /// 换房间报文的共享规划器（Task 5 Step 1：`PlanStartMap` 从 `PlanMemberEntry` 抽出来复用）：
    /// 首次进房 → 完整 `START_MAP`（房间/地图 id/怪物表都在载荷里）；重访（客户端复用上次快照）
    /// → 四字节的 cached `START_MAP`（第四字节 = 0，客户端据此跳过地图/怪物/掉落段）。
    /// **变异验证**：把 `PlanStartMap` 的 `ReuseClientRoomState` 分支删掉（恒发完整 START_MAP）→ 第二条变红。
    /// </summary>
    private static void CheckPlanStartMapSharedPlanner(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, 0, 0, out var start),
            "the fixture run starts for the shared START_MAP planner case");
        var fresh = instance.CaptureRoomSnapshot(instance.Run.RoomX, instance.Run.RoomY, start, ownerUserId: 7);
        var startMap = DungeonEntryPackets.PlanStartMap(fresh);
        // START_MAP 布局：roomX(0) roomY(1) randomSeed(2..5) roomStateFlag(6) mapId(7..8) ...
        check(startMap.Type == GameProtocolEngine.NotificationPacketType
                && startMap.ProtocolId == GameProtocolEngine.StartMapNotification
                && startMap.Payload.Length > 9
                && startMap.Payload[0] == fresh.RoomX
                && startMap.Payload[1] == fresh.RoomY
                && BinaryPrimitives.ReadUInt16LittleEndian(startMap.Payload.AsSpan(7, 2)) == fresh.MapId,
            "a first visit plans a full START_MAP carrying the room, the map id and the monster table");

        // `out` 单独一句话（放在 && 里编译器会报"未赋值"）。
        check(instance.TryMoveRoom(1, 0, out _), "the planner case moves away from the start room");
        var revisited = instance.TryMoveRoom(0, 0, out var revisit);
        check(revisited, "walking back to the start room is a revisit");
        var revisitSnapshot = instance.CaptureRoomSnapshot(0, 0, revisit, ownerUserId: 7);
        var cached = DungeonEntryPackets.PlanStartMap(revisitSnapshot);
        // cached START_MAP 布局：roomX(0) roomY(1) randomSeed(2..5) state(6)=0，没有地图/怪物/掉落段。
        check(revisitSnapshot.ReuseClientRoomState
                && cached.ProtocolId == GameProtocolEngine.StartMapNotification
                && cached.Payload.Length == 7
                && cached.Payload[6] == 0,
            "a revisit plans the cached START_MAP without the map/monster sections");
    }

    /// <summary>
    /// 倒计时到点的强制推进（Task 5 Step 4，设计稿 §3.3 第 3/4 条）：扫描循环每实例一轮 ——
    /// 锁内换房间取快照 → 出锁**先**给"到过门口的人"补各自的 CMD 48 成功回包（只有到门口的人有、
    /// 各自一份）→ **再**给全员（含没到门口、被强制的人）各发一份 NOTI 29。
    /// 同时钉住：①没过半不推进；②人数缩水后重新过半 → 扫描里只重开倒计时（`StartedCountdown`：
    /// 不改房间、不发包）；③没到点不推进；④到点强制推进并清空集结。
    /// **变异验证**：①把 `SweepDoorAssemblyAsync` 里"判定前捕获 `PendingTarget`"改回任务书的
    /// "返回后再读"（到点那次 `PendingTarget` 已被 Reset）→ 推进断言变红（房间不换、只剩一条告警日志）；
    /// ②把 `StartedCountdown` 当成推进处理 → 第二条变红；③把回包改成发给全员 → 归属断言变红。
    /// </summary>
    private static async Task CheckDoorAssemblySweepAdvancesWholePartyAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, clock,
            NullLogger<DungeonPartyService>.Instance);
        var instance = registry.GetOrCreateForParty(4242, () => fixture.CreatePartyInstance(4242));
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var charlie = Guid.NewGuid();
        var delta = Guid.NewGuid();
        var inbox = new Dictionary<Guid, List<GameServerPacket>>();
        foreach (var characterId in new[] { alpha, bravo, charlie, delta })
        {
            var owner = characterId;
            inbox[owner] = [];
            instance.AddMember(new DungeonInstanceMember(
                owner, 1, 0, "成员", packet => { inbox[owner].Add(packet); return ValueTask.CompletedTask; }));
        }

        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the party instance starts a run for the sweep case");
        var target = new DungeonRoomPosition(1, 0);
        var alphaReply = Reply(alpha);
        var bravoReply = Reply(bravo);
        lock (instance.Gate)
        {
            var first = instance.Doors.Register(target, alpha, memberCount: 4, clock.GetUtcNow(), alphaReply);
            var second = instance.Doors.Register(target, bravo, memberCount: 4, clock.GetUtcNow(), bravoReply);
            check(first.Decision == DungeonDoorDecision.Waiting
                    && second.Decision == DungeonDoorDecision.Waiting,
                "two of four at the door is not yet a majority and starts no countdown");
        }

        // 人数缩水（delta 回城）→ 2/3 过半：扫描循环必须重新评估成 `StartedCountdown`，
        // 只重开倒计时，不改房间、不发包（到点由下一轮扫描推进）。
        instance.RemoveMember(delta);
        await service.SweepDoorAssembliesAsync(CancellationToken.None);
        check(instance.Run.RoomX == 0 && instance.Run.RoomY == 0
                && inbox.Values.All(packets => packets.Count == 0),
            "a roster shrink re-evaluates the door into a countdown without moving the room or sending anything");
        check(instance.Doors.CountdownDeadline == clock.GetUtcNow() + DungeonDoorAssembly.Countdown,
            "the re-evaluated countdown starts at the sweep tick");

        clock.Advance(TimeSpan.FromSeconds(9));
        await service.SweepDoorAssembliesAsync(CancellationToken.None);
        check(instance.Run.RoomX == 0
                && inbox.Values.All(packets => packets.Count == 0)
                && instance.Doors.IsWaiting,
            "the countdown does not fire early");

        clock.Advance(TimeSpan.FromSeconds(1));
        await service.SweepDoorAssembliesAsync(CancellationToken.None);
        check(instance.Run.RoomX == target.X && instance.Run.RoomY == target.Y
                && !instance.Doors.IsWaiting && instance.Doors.CountdownDeadline is null,
            "the expired countdown forces the whole party into the next room and clears the assembly");
        var alphaPackets = inbox[alpha];
        var bravoPackets = inbox[bravo];
        var charliePackets = inbox[charlie];
        var alphaReplySeen = PacketAt(alphaPackets, 0);
        var alphaStartMap = PacketAt(alphaPackets, 1);
        var bravoReplySeen = PacketAt(bravoPackets, 0);
        var bravoStartMap = PacketAt(bravoPackets, 1);
        var charlieStartMap = PacketAt(charliePackets, 0);
        check(alphaPackets.Count == 2
                && ReferenceEquals(alphaReplySeen, alphaReply)
                && alphaReplySeen is { } alphaReplyPacket
                && alphaReplyPacket.Payload[0] == 1,
            "an arrived member gets their own CMD 48 success reply first");
        check(bravoPackets.Count == 2 && ReferenceEquals(bravoReplySeen, bravoReply),
            "the second arrived member gets their own reply packet, not the other member's");
        check(alphaStartMap is { } alphaMap
                && alphaMap.ProtocolId == GameProtocolEngine.StartMapNotification
                && bravoStartMap is { } bravoMap
                && bravoMap.ProtocolId == GameProtocolEngine.StartMapNotification
                && charliePackets.Count == 1
                && charlieStartMap is { } charlieMap
                && charlieMap.ProtocolId == GameProtocolEngine.StartMapNotification,
            "every member (including the one forced along) receives exactly one NOTI 29 and nobody else a reply");
        check(alphaStartMap is { } moved
                && moved.Payload.Length > 3
                && moved.Payload[0] == target.X
                && charlieStartMap is { } forced
                && forced.Payload.Length > 3
                && forced.Payload[0] == target.X,
            "the forced NOTI 29 describes the room that was actually advanced to");
    }

    /// <summary>
    /// 扫描的**每实例独立**语义（Task 3 的壳 + Task 5 强化）：一个成员的发送通道坏了
    /// （IOException）只记告警——房间照推进，**排在它后面**的健康成员仍各收到一份 NOTI 29。
    /// 坏成员故意排在**第一个**：广播顺序就是成员顺序，去掉护栏时它会把后面的投递一起打断。
    /// **变异验证**：把 `SendSafelyAsync` 的 try/catch 拆掉（裸 `member.Send`）→ 后续成员少收包，本用例变红。
    /// </summary>
    private static async Task CheckDoorAssemblySweepSurvivesBrokenMemberAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, clock,
            NullLogger<DungeonPartyService>.Instance);
        var instance = registry.GetOrCreateForParty(4243, () => fixture.CreatePartyInstance(4243));
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var broken = Guid.NewGuid();
        var alphaInbox = new List<GameServerPacket>();
        var bravoInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(
            broken, 1, 0, "断线", _ => throw new IOException("broken pipe")));
        instance.AddMember(new DungeonInstanceMember(
            alpha, 2, 1, "甲", packet => { alphaInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            bravo, 3, 2, "乙", packet => { bravoInbox.Add(packet); return ValueTask.CompletedTask; }));
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the party instance starts a run for the broken-member case");
        var target = new DungeonRoomPosition(1, 0);
        lock (instance.Gate)
        {
            instance.Doors.Register(target, alpha, memberCount: 3, clock.GetUtcNow(), Reply(alpha));
            instance.Doors.Register(target, bravo, memberCount: 3, clock.GetUtcNow(), Reply(bravo));
        }

        clock.Advance(DungeonDoorAssembly.Countdown);
        await service.SweepDoorAssembliesAsync(CancellationToken.None);
        check(instance.Run.RoomX == target.X,
            "a member with a broken send channel cannot stop the forced room advance");
        check(alphaInbox.Count == 2 && bravoInbox.Count == 2
                && PacketAt(alphaInbox, 1)?.ProtocolId == GameProtocolEngine.StartMapNotification
                && PacketAt(bravoInbox, 1)?.ProtocolId == GameProtocolEngine.StartMapNotification,
            "the healthy members still receive their reply and the forced NOTI 29");
    }

    /// <summary>
    /// 收件箱取第 index 份包（越界返回 null）：变异让某个成员少收/收错包时，
    /// 断言给的是一条可读的失败，而不是 <see cref="IndexOutOfRangeException"/> 把整个测试进程打断。
    /// </summary>
    private static GameServerPacket? PacketAt(IReadOnlyList<GameServerPacket> packets, int index) =>
        index < packets.Count ? packets[index] : null;

    /// <summary>
    /// 扫描只处理**队伍实例**（单人零回归的结构性护栏）：单人的 `CMD 48` 走会话里那条立即推进的分支，
    /// 永远不会在实例的 `Doors` 里留下集结。这里在单人实例上人为摆一个"已到点的集结"（只有模型能造出
    /// 这个状态）——扫描必须原样无视它（不改房间、不发包），否则单人会在客户端没发 CMD 48 时被换房间。
    /// **变异验证**：把 `SweepDoorAssemblyAsync` 开头的 `if (!instance.IsParty) return;` 删掉 → 本用例变红。
    /// </summary>
    private static async Task CheckDoorAssemblySweepIgnoresSoloInstancesAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, clock,
            NullLogger<DungeonPartyService>.Instance);
        var soloCharacterId = Guid.NewGuid();
        var solo = registry.GetOrCreateForSolo(
            soloCharacterId, () => fixture.CreateSoloInstance(soloCharacterId));
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var charlie = Guid.NewGuid();
        var inbox = new List<GameServerPacket>();
        solo.AddMember(new DungeonInstanceMember(
            alpha, 1, 0, "独行", packet => { inbox.Add(packet); return ValueTask.CompletedTask; }));
        solo.AddMember(new DungeonInstanceMember(bravo, 2, 0, "陪衬", _ => ValueTask.CompletedTask));
        solo.AddMember(new DungeonInstanceMember(charlie, 3, 0, "陪衬", _ => ValueTask.CompletedTask));
        check(solo.TryStartRun(fixture.Layout, 0, 0, out _),
            "the solo instance starts a run for the sweep-ignore case");
        var target = new DungeonRoomPosition(1, 0);
        lock (solo.Gate)
        {
            // 过半 + 倒计时已过期：这一套在队伍实例上必推进（见上一个用例），单人必须纹丝不动。
            solo.Doors.Register(target, alpha, memberCount: 3,
                clock.GetUtcNow() - DungeonDoorAssembly.Countdown, Reply(alpha));
            solo.Doors.Register(target, bravo, memberCount: 3,
                clock.GetUtcNow() - DungeonDoorAssembly.Countdown, Reply(bravo));
        }

        await service.SweepDoorAssembliesAsync(CancellationToken.None);
        check(!solo.IsParty && solo.Run.RoomX == 0 && solo.Doors.IsWaiting,
            "the sweep never advances a solo instance (its MOVE_MAP path is the immediate one)");
        check(inbox.Count == 0,
            "a solo instance whose assembly state was somehow populated still receives no scan packets");
    }

    /// <summary>待入队信箱：同一角色只留最后一条（重复投递幂等）、取走即清、未投递时为 false。</summary>
    private static void CheckRegistryEntryRequestMailbox(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        check(!registry.TryTakeEntryRequest(characterId, out _),
            "an untouched character has no pending dungeon entry");
        var request = new DungeonEntryRequest(
            PartyId: 1000, DungeonId: 14, Difficulty: 0, DungeonOption: 0,
            LeaderTownId: 1, LeaderAreaId: 0, LeaderCharacterId: Guid.NewGuid());
        registry.PostEntryRequest(characterId, request);
        registry.PostEntryRequest(characterId, request with { DungeonId = 15 });
        check(registry.TryTakeEntryRequest(characterId, out var taken) && taken.DungeonId == 15,
            "re-posting overwrites the pending request (last one wins)");
        check(!registry.TryTakeEntryRequest(characterId, out _),
            "taking the request clears it so the entry runs once");
    }

    /// <summary>
    /// 待处理组队邮箱的"有没有货"查询（终审 P2）：会话主循环的读等待只有在这个查询为真时才
    /// 缩短到短 tick，否则完全按原样等待（单人零回归）。两个信箱**任一**有货即为 true；
    /// 取走/解绑后必须回到 false——否则主循环会永远 250ms 空转。
    /// **变异验证**：把 `HasPendingMail` 改成恒 false（或只看其中一个信箱）→ 第三/第四/第五
    /// 条断言之一变红。会话里"用它缩短等待 + 短 tick 到点取件"的接线不可单测（无会话接缝），
    /// 真机检查点是 Task 7 的第 2 项。
    /// </summary>
    private static void CheckPendingPartyMailLookup(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        check(!registry.HasPendingMail(characterId),
            "an untouched character has no pending party mail (the read wait stays untouched)");
        registry.PostSelectionRequest(characterId, Guid.NewGuid());
        check(registry.HasPendingMail(characterId),
            "a pending selection request counts as pending mail");
        registry.PostEntryRequest(
            characterId,
            new DungeonEntryRequest(
                PartyId: 1000, DungeonId: 14, Difficulty: 0, DungeonOption: 0,
                LeaderTownId: 1, LeaderAreaId: 0, LeaderCharacterId: Guid.NewGuid()));
        check(registry.TryTakeSelectionRequest(characterId, out _) && registry.HasPendingMail(characterId),
            "taking the selection request leaves the entry request pending");
        check(registry.TryTakeEntryRequest(characterId, out _) && !registry.HasPendingMail(characterId),
            "taking the last request clears the pending-mail signal");
        registry.PostEntryRequest(
            characterId,
            new DungeonEntryRequest(
                PartyId: 1000, DungeonId: 14, Difficulty: 0, DungeonOption: 0,
                LeaderTownId: 1, LeaderAreaId: 0, LeaderCharacterId: Guid.NewGuid()));
        registry.UnbindCharacter(characterId);
        check(!registry.HasPendingMail(characterId),
            "unbinding the character clears both mailboxes");
    }

    /// <summary>
    /// 并发 CMD 16 的开局仲裁（终审 P5）：两个成员会话可能都通过 CanAcceptJoin 门槛，
    /// 第二个人若照旧 `TryStartRun`，`DungeonRunState.TryStart` 会先 `Stop()` 把先到者的
    /// 迷宫/房间几何重掷成另一套（先到者的 NOTI 28/29 已经发出去）。
    /// `TryStartRunForEntry` 把"判定 + 开局"收进锁内：第二个人拿 `AlreadyActive`，房间一字不动。
    /// **变异验证**：把方法里的 `if (_run.IsActive) return AlreadyActive;` 删掉（或改回
    /// 无条件 `TryStartRun`）→ 第二/第三条断言变红（返回 Started、迷宫被重掷）。
    /// 会话说"拿到 AlreadyActive 就改走 live-join"的接线不可单测（会话无接缝）。
    /// </summary>
    private static void CheckRunStartArbitration(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        var first = instance.TryStartRunForEntry(fixture.Layout, 0, 0, out var firstTransition);
        check(first == DungeonRunEntryDecision.Started
                && instance.Run.IsActive
                && firstTransition.Room.MapId != 0,
            "the first entry claims the run start and enters the start room");
        // 让先到者真正离开起始房间并开放房间：一旦第二个人把 run 重掷（TryStart 会 Stop + 重进
        // 起始房间），下面两条断言都会看到 (0,0)/未开放而变红——这就是"重掷迷宫几何"的可观测面。
        check(instance.TryMoveRoom(1, 0, out _), "the claimed run advances to the next room");
        instance.TryEnableClear();
        var second = instance.TryStartRunForEntry(fixture.Layout, 0, 0, out _);
        check(second == DungeonRunEntryDecision.AlreadyActive,
            "the second concurrent entry is told the run is already active (it must live-join instead)");
        check(instance.Run.IsActive
                && instance.Run.RoomX == 1
                && instance.Run.RoomY == 0
                && instance.ClearEnabled,
            "the already-active run keeps its room and clear flags (a restart would re-enter the start room)");
        check(instance.TryStartRunForEntry(fixture.Layout, 0, 0, out _)
                == DungeonRunEntryDecision.AlreadyActive
                && instance.MemberCount == 1,
            "repeated arbitration is idempotent and never touches the roster");
    }

    /// <summary>
    /// 锁内的运行态只读（终审 P6）：会话不再裸读 `_run` 的 `HellMode`/`CurrentRoom`，
    /// 走实例的锁内访问器。访问器必须与 `DungeonRunState` 同口径（非地狱模式恒 false、
    /// run 未活时当前房间为 null）。
    /// **变异验证**：把 `HellMode` 改成恒 true（或 `RunCurrentRoom` 改成恒 null）→ 断言变红。
    /// 会话三处调用点的替换不可单测（同一接缝限制），但它们都只是取值来源的替换。
    /// </summary>
    private static void CheckLockedRunReadAccessors(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(!instance.HellMode && instance.RunCurrentRoom is null,
            "an idle instance reports neither hell mode nor a current room");
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _)
                && !instance.HellMode
                && instance.RunCurrentRoom is { MapId: not 0 },
            "a normal run reports its current room through the locked accessor");
        check(instance.TryMoveRoom(1, 0, out var transition)
                && instance.RunCurrentRoom is { } movedRoom
                && movedRoom.MapId == transition.Room.MapId
                && movedRoom.Monsters.Length == transition.Room.Monsters.Length,
            "the locked current-room accessor follows the room the run actually entered");

        var hell = fixture.CreateInstance();
        hell.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(hell.TryStartRun(fixture.HellLayout, 0, dungeonOption: 1, out _) && hell.HellMode,
            "a hell dungeon run reports hell mode through the locked accessor");
    }

    /// <summary>
    /// 地狱房的被动掉落 detach（终审 I3）：detach 从单人的 CMD 48 分支搬进
    /// `DungeonInstance.TryMoveRoom` 的锁内后，三条换房路径（单人立即推进 / 组队自己推进 /
    /// 倒计时强制推进）都拿到同一份行为。两个条件各一个用例：
    /// ①退出**未清**的地狱房（`CompletedHellRoomOnExit`）→ 离开的那间房的被动掉落失联；
    /// ②走进**已清**的地狱房（客户端重建空场景）→ 那间房留下的被动掉落失联。
    /// detach 只摘掉"被动物件关联"（掉落本身还在本房间的地面表里，仍是普通掉落）——
    /// 所以两条都同时断言 `GetRoomGroundItems` 仍是 2 条。
    /// **变异验证**：把 `TryMoveRoom` 里那两段 detach 删掉 → 第二/第四条断言变红（掉落没被摘）。
    /// 组队两条推进路径的调用点（AdvancePartyRoomAsync / 扫描）不额外断言——它们走
    /// `TryMoveRoom` 同一份实现，本用例覆盖的就是那份实现。
    /// </summary>
    private static void CheckHellRoomPassiveDetachOnMove(Action<bool, string> check, DungeonFixture fixture)
    {
        // ① 退出未清的地狱房：`CompletedHellRoomOnExit` → detach 离开的那间房。
        var exit = fixture.CreateInstance();
        exit.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(exit.TryStartRun(fixture.HellLayout, 0, dungeonOption: 1, out _)
                && exit.TryMoveRoom(1, 0, out _),
            "the hell-mode run enters the hell room");
        check(exit.RegisterRoomPassiveDrops(
                    DungeonFixture.Room([], passiveDropCount: 2), 1, 1, 0).Length == 2
                && exit.GetRoomPassiveDrops(1, 0).Length == 2,
            "two passive drops are registered in the hell room");
        check(exit.TryMoveRoom(0, 0, out var leavingHell) && leavingHell.CompletedHellRoomOnExit,
            "leaving the uncleared hell room reports CompletedHellRoomOnExit");
        check(exit.GetRoomPassiveDrops(1, 0).Length == 0
                && exit.GetRoomGroundItems(1, 0).Length == 2,
            "leaving an uncleared hell room detaches its passive drops (they stay as plain ground items)");

        // ② 走进已清的地狱房：目标房间那次的被动掉落要 detach（客户端重建的是空场景）。
        var cleared = fixture.CreateInstance();
        cleared.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(cleared.TryStartRun(fixture.HellLayout, 0, dungeonOption: 1, out _)
                && cleared.TryMoveRoom(1, 0, out _),
            "the second hell-mode run enters the hell room");
        check(cleared.RegisterRoomPassiveDrops(
                    DungeonFixture.Room([], passiveDropCount: 2), 1, 1, 0).Length == 2,
            "the second hell room also holds two passive drops");
        var hellRoomMonsters = cleared.RunCurrentRoom?.Monsters ?? [];
        check(hellRoomMonsters.Length != 0,
            "the hell room exposes its monsters through the locked current-room accessor");
        foreach (var monster in hellRoomMonsters)
        {
            cleared.TryTakeMonsterDeath(monster.UniqueId, out _);
        }

        check(cleared.IsCurrentHellRoomCleared(),
            "the hell encounter is cleared once every hell-room monster is defeated");
        check(cleared.TryMoveRoom(0, 0, out var leftCleared) && !leftCleared.CompletedHellRoomOnExit
                && cleared.GetRoomPassiveDrops(1, 0).Length == 2,
            "leaving a cleared hell room keeps its drops (the detach belongs to the return entry)");
        check(cleared.TryMoveRoom(1, 0, out _)
                && cleared.IsCurrentHellRoomCleared()
                && cleared.GetRoomPassiveDrops(1, 0).Length == 0
                && cleared.GetRoomGroundItems(1, 0).Length == 2,
            "re-entering the cleared hell room detaches the previous encounter's passive drops");
    }

    /// <summary>
    /// 跨会话投递的坏通道护栏（终审 I1）：每个成员句柄写的是**他自己会话**的流，
    /// 被踢/掉线的成员会抛**他自己 token** 的 `OperationCanceledException`；
    /// 现在的 filter 只捕 IOException/ObjectDisposed/Socket，OCE 会顺着**发送者**的会话冒泡
    /// （击杀/开放/拾取广播把健康玩家打断；扫描里让房间已经换了、后面的成员收不到 NOTI 29）。
    /// 两条生产路径：①`DungeonPartyService.BroadcastToMembersAsync`（生产广播实现）；
    /// ②`PartyService.SendToOtherMembersAsync`（队长外观推送走的真通道）。
    /// 同时钉住 P1 的单人护栏：不在队伍的角色一份包都不会多发。
    /// **变异验证**：把 `CrossSessionDelivery.IsBrokenChannel` 里的 `or OperationCanceledException`
    /// 删掉 → 第一次 await 就把 OCE 抛出去，本用例变红。
    /// 会话里的局部发送函数（AdvancePartyRoomAsync）与会话接线同一局限，但共用这份判定。
    /// </summary>
    private static async Task CheckCrossSessionDeliverySurvivesCanceledMembersAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            registry,
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);

        // ① 实例广播：第一个成员（被踢）的句柄抛 OCE，排在它后面的健康成员仍各收一份。
        var instance = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        var canceledMember = Guid.NewGuid();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var alphaInbox = new List<GameServerPacket>();
        var bravoInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(
            canceledMember, 1, 0, "被踢", _ => throw new OperationCanceledException()));
        instance.AddMember(new DungeonInstanceMember(
            alpha, 2, 1, "甲", packet => { alphaInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            bravo, 3, 2, "乙", packet => { bravoInbox.Add(packet); return ValueTask.CompletedTask; }));
        await service.BroadcastToMembersAsync(
            instance, GameProtocolEngine.CreateEnableClearDungeon());
        check(alphaInbox.Count == 1 && bravoInbox.Count == 1,
            "members queued after a canceled channel still receive the instance broadcast");

        // 扫全员的强制推进同一条护栏（先补 CMD 48 回包、再发 NOTI 29，两步都走 SendSafelyAsync）：
        // 被踢成员排在第一个，去掉 OCE 的话它的异常会把整轮推进打断、健康成员两份都收不到。
        var sweepInstance = registry.GetOrCreateForParty(
            1001, () => fixture.CreatePartyInstance(1001));
        var sweepInbox = new List<GameServerPacket>();
        var stayed = Guid.NewGuid();
        sweepInstance.AddMember(new DungeonInstanceMember(
            canceledMember, 1, 0, "被踢", _ => throw new OperationCanceledException()));
        sweepInstance.AddMember(new DungeonInstanceMember(
            bravo, 2, 1, "乙", packet => { sweepInbox.Add(packet); return ValueTask.CompletedTask; }));
        sweepInstance.AddMember(new DungeonInstanceMember(stayed, 3, 2, "没到门口", _ => ValueTask.CompletedTask));
        check(sweepInstance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the sweep instance starts a run for the canceled-member case");
        var target = new DungeonRoomPosition(1, 0);
        lock (sweepInstance.Gate)
        {
            // 3 人里 2 人到门口（过半）→ 开倒计时；Register 的 now 取"倒计时之前"，
            // 于是扫描一进来就是"倒计时已到点"的强制推进：逐个补回包、再给全员发 NOTI 29。
            var beforeCountdown = DateTimeOffset.UtcNow - DungeonDoorAssembly.Countdown;
            sweepInstance.Doors.Register(
                target, canceledMember, memberCount: 3, beforeCountdown, Reply(canceledMember));
            sweepInstance.Doors.Register(
                target, bravo, memberCount: 3, beforeCountdown, Reply(bravo));
        }

        await service.SweepDoorAssembliesAsync(CancellationToken.None);
        check(sweepInbox.Count == 2
                && sweepInbox.Any(packet => packet.ProtocolId == GameProtocolEngine.StartMapNotification),
            "a forced advance still delivers the reply and NOTI 29 past a canceled member");

        // ② PartyService 真通道（队长外观推送走的就是这条）：目标通道抛 OCE → 只告警、返回 0、
        //    不冒泡；不在队伍的角色（单人）走同一条路时零投递（P1 的"单人绝不多发一份包"护栏）。
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var bravoId = harness.IdOf("布拉沃");
        await harness.Service.AttachChannelAsync(
            "bobby",
            "布拉沃",
            bravoId,
            harness.UidOf("布拉沃"),
            harness.SessionIdOf("布拉沃"),
            _ => throw new OperationCanceledException(),
            CancellationToken.None);
        var delivered = await harness.Service.SendToOtherMembersAsync(
            harness.IdOf("阿尔法"),
            GameProtocolEngine.CreateEnableClearDungeon(),
            CancellationToken.None);
        check(delivered == 0,
            "a party send to a canceled channel returns 0 instead of throwing the recipient's OCE");

        var partyInbox = new List<GameServerPacket>();
        await harness.Service.AttachChannelAsync(
            "bobby",
            "布拉沃",
            bravoId,
            harness.UidOf("布拉沃"),
            harness.SessionIdOf("布拉沃"),
            packet => { partyInbox.Add(packet); return ValueTask.CompletedTask; },
            CancellationToken.None);
        var deliveredAgain = await harness.Service.SendToOtherMembersAsync(
            harness.IdOf("阿尔法"),
            GameProtocolEngine.CreateEnableClearDungeon(),
            CancellationToken.None);
        check(deliveredAgain == 1 && partyInbox.Count == 1,
            "a healthy member still receives the party send exactly once");

        partyInbox.Clear();
        harness.ClearAll();
        var soloDelivered = await harness.Service.SendToOtherMembersAsync(
            harness.IdOf("查理"),
            GameProtocolEngine.CreateEnableClearDungeon(),
            CancellationToken.None);
        check(soloDelivered == 0
                && partyInbox.Count == 0
                && harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0
                && harness.Of("查理").Count == 0,
            "a character outside any party delivers nothing (the solo path can never add a packet)");
    }
}

/// <summary>测试夹具：一份最小 PVF（3×1 迷宫 + 3 张地图）与实例工厂。</summary>
internal sealed class DungeonFixture : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"dflegacy-dungeon-party-{Guid.NewGuid():N}");

    public DungeonFixture()
    {
        foreach (var directory in new[] { "dungeon", "map", "quest" })
        {
            Directory.CreateDirectory(Path.Combine(_root, directory));
        }

        File.WriteAllText(Path.Combine(_root, "dungeon/dungeon.lst"), "14 `test.dgn`\n15 `hell.dgn`\n");
        File.WriteAllText(Path.Combine(_root, "dungeon/test.dgn"),
            "[minimum level] 1\n[basis level] 1\n[maze info]\n[size] 3 1\n"
            + "[greed]\n`PPP`\n[start map]\n0 0\n[/start map]\n"
            + "[boss map]\n2 0\n1 0\n[/boss map]\n");
        // 地狱副本（终审 I3 用例）：地狱房在 (1,0)、封门地图 = normal.map（2）；boss 房在 (2,0)。
        File.WriteAllText(Path.Combine(_root, "dungeon/hell.dgn"),
            "[minimum level] 1\n[basis level] 1\n[hell dungeon] 1\n"
            + "[seal door map index] 2\n[seal door pos] 1 0\n"
            + "[maze info]\n[size] 3 1\n"
            + "[greed]\n`PPP`\n[start map]\n0 0\n[/start map]\n"
            + "[boss map]\n2 0\n[/boss map]\n");
        File.WriteAllText(Path.Combine(_root, "map/map.lst"), "1 `start.map`\n2 `normal.map`\n3 `boss.map`\n");
        foreach (var name in new[] { "start", "normal", "boss" })
        {
            // 两张地图表共用这三张 .map（与真实 PVF 一样按 [dungeon] 列出多个副本 id）。
            File.WriteAllText(Path.Combine(_root, $"map/{name}.map"),
                "[dungeon]\n14\n15\n[/dungeon]\n"
                + $"[type] `[{(name == "boss" ? "boss" : "normal")}]`\n"
                + "[greed]\n`P`\n[monster]\n1 1 1 100 100 0 1 1 `[fixed]` `[normal]`\n[/monster]\n");
        }

        var options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = _root };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        Catalog = new DungeonCatalog(scripts,
            new ObjectDropGenerator(options,
                new ObjectDropCatalog(scripts, NullLogger<ObjectDropCatalog>.Instance),
                new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance)),
            NullLogger<DungeonCatalog>.Instance);
        Items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
        Random = new ZeroDropRandomSource();
        if (!Catalog.TryCreateLayout(14, out var layout, random: Random)
            || !Catalog.TryCreateLayout(15, out var hellLayout, random: Random))
        {
            throw new InvalidOperationException("the fixture dungeon layout is missing");
        }

        Layout = layout;
        HellLayout = hellLayout;
    }

    public DungeonCatalog Catalog { get; }
    public ItemCatalog Items { get; }
    public ZeroDropRandomSource Random { get; }
    public DungeonLayout Layout { get; }

    /// <summary>地狱副本（id 15，`[hell dungeon] 1`，地狱房 (1,0)）的布局，I3 的被动掉落 detach 用例用。</summary>
    public DungeonLayout HellLayout { get; }

    /// <summary>合成怪：只关心 UniqueId 与 Type（`EnterDetachedRoom` 路径不经过 PVF 解析）。</summary>
    public static GameDungeonMonster Monster(ushort uniqueId, byte type = GameDungeonMonsterTypes.Normal) =>
        new(MapListIndex: checked((byte)uniqueId), uniqueId, MonsterIndex: 1, Level: 2, Type: type);

    /// <summary>
    /// 合成房间（喂 `EnterDetachedRoom` 的"教学副本"路径）：`passiveDropCount` 个被动掉落。
    /// 用金币是为了不依赖物品脚本——`CreateGeneratedItem` 对金币直接返回 null，
    /// `GetClientAddInfo` 也走 `CountOrValue` 直通；非金币会落到 `CharacterItemWireProjection`。
    /// </summary>
    public static DungeonRoomDefinition Room(
        GameDungeonMonster[] monsters,
        int passiveDropCount = 0)
    {
        var spawns = new DungeonPassiveItemSpawn[passiveDropCount];
        for (var index = 0; index < passiveDropCount; index++)
        {
            spawns[index] = new DungeonPassiveItemSpawn(
                PassiveObjectIndex: checked((byte)index),
                AssociationSlot: checked((byte)index),
                new DungeonGeneratedDrop(IsGold: true, ItemId: 0, CountOrValue: 100))
            {
                X = 100,
                Y = 100
            };
        }

        return new DungeonRoomDefinition(
            MapId: 2,
            DungeonMapType.Normal,
            monsters,
            Topology: 'P',
            HasScriptBossActor: false,
            PassiveObjects: [],
            PassiveItemSpawns: spawns);
    }

    /// <summary>单人实例工厂（键 = 角色 id）。</summary>
    public DungeonInstance CreateSoloInstance(Guid characterId) =>
        new(partyId: 0, soloCharacterId: characterId, Catalog, Items);

    /// <summary>队伍实例工厂（键 = partyId）。</summary>
    public DungeonInstance CreatePartyInstance(ushort partyId) =>
        new(partyId: partyId, soloCharacterId: Guid.Empty, Catalog, Items);

    /// <summary>不关心键的独立实例（并发/房间快照用例用）。</summary>
    public DungeonInstance CreateInstance() => CreateSoloInstance(Guid.Empty);

    public DungeonInstanceMember Member(Guid characterId, byte slot, string name) =>
        new(characterId, AreaUserId: 1, slot, name, _ => ValueTask.CompletedTask);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// 可手动推进的 <see cref="TimeProvider"/>：倒计时扫描的用例不该真睡 10 秒。
/// 生产代码只经 `DungeonPartyService._timeProvider.GetUtcNow()` 取时刻，所以这一层就够。
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
