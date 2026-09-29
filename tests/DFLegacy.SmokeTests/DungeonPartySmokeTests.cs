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
    public static Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        CheckSoloInstanceKey(check, fixture);
        CheckInstanceMembershipLifecycle(check, fixture);
        CheckConcurrentKillSettlesOnce(check, fixture);
        CheckRoomSnapshotsAndPassiveDrops(check, fixture);
        CheckDoorAssemblyRules(check);
        CheckRegistryEntryRequestMailbox(check, fixture);
        return Task.CompletedTask;
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
        // 单人的键就是角色 id：GetOrCreateForSolo 落地即可被按角色查到（_byCharacter 既是单人键表
        // 也是会话绑定表）。未登记过的角色才查不到。
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
        check(wrongDoor.Decision == DungeonDoorDecision.Ignored && first.Arrivals.Count == 1,
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
