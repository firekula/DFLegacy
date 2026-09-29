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
        CheckConcurrentKillSettlesOnce(check, fixture);
        CheckRoomSnapshotsAndPassiveDrops(check, fixture);
        CheckLiveJoinDoesNotReplenishPassiveDrops(check, fixture);
        CheckSharedCollectionAccessors(check, fixture);
        CheckDoorAssemblyRules(check);
        CheckRegistryEntryRequestMailbox(check, fixture);
        CheckSoloRunBindsByCharacterId(check, fixture);
        CheckSoloRoomChangeKeepsImmediateBehaviour(check, fixture);
        CheckSoloLookupNeverReusesPartyBinding(check, fixture);
        CheckStoppedInstanceCannotBeJoined(check, fixture);
        await CheckGatheringValidationAsync(check, fixture);
        await CheckRequestTargetingAsync(check, fixture);
        CheckMemberEntryPacketOrder(check, fixture);
        await CheckBroadcastReachesEveryMemberOnceAsync(check, fixture);
        await CheckPickupBroadcastOmitsSenderAsync(check, fixture);
    }

    /// <summary>
    /// 共享状态广播（设计稿 §3.3）：击杀/房间开放只判定一次（实例的原子方法），
    /// 但**每个成员各收一份**；单人实例的广播只及于自己。
    /// **变异验证**：把 sent 累加改成"只发给第一个成员"，本用例变红。
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
    /// 门口集结（设计稿 §3.3）：
    /// ①第一个请求只登记、不推进；②不同目标的请求忽略；③全员到齐立即推进；
    /// ④过半开 10 秒倒计时、到点强制推进；⑤推进后状态清空（下一次从零开始）。
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

        static GameServerPacket Reply(Guid _) =>
            GameProtocolEngine.CreateCommandReply(
                new PacketFrame(
                    GameProtocolEngine.CommandPacketType,
                    GameProtocolEngine.MoveMapCommand,
                    DeclaredCrc32: 0,
                    Body: [0, 0, 1, 0]),
                success: true);
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

        File.WriteAllText(Path.Combine(_root, "dungeon/dungeon.lst"), "14 `test.dgn`\n");
        File.WriteAllText(Path.Combine(_root, "dungeon/test.dgn"),
            "[minimum level] 1\n[basis level] 1\n[maze info]\n[size] 3 1\n"
            + "[greed]\n`PPP`\n[start map]\n0 0\n[/start map]\n"
            + "[boss map]\n2 0\n1 0\n[/boss map]\n");
        File.WriteAllText(Path.Combine(_root, "map/map.lst"), "1 `start.map`\n2 `normal.map`\n3 `boss.map`\n");
        foreach (var name in new[] { "start", "normal", "boss" })
        {
            File.WriteAllText(Path.Combine(_root, $"map/{name}.map"),
                "[dungeon]\n14\n[/dungeon]\n"
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
        if (!Catalog.TryCreateLayout(14, out var layout, random: Random))
        {
            throw new InvalidOperationException("the fixture dungeon layout is missing");
        }

        Layout = layout;
    }

    public DungeonCatalog Catalog { get; }
    public ItemCatalog Items { get; }
    public ZeroDropRandomSource Random { get; }
    public DungeonLayout Layout { get; }

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
