using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class DungeonBossDeathSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        var now = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var boss = DungeonFixture.Monster(0, GameDungeonMonsterTypes.Boss);
        var secondBoss = DungeonFixture.Monster(1, GameDungeonMonsterTypes.Boss);
        var normal = DungeonFixture.Monster(2);
        var boxed = DungeonFixture.Monster(3) with { IsBoxMonster = 1, BoxIndex = 5 };
        var room = DungeonFixture.Room([boss, secondBoss, normal, boxed]) with
        {
            MapType = DungeonMapType.Boss,
            HasScriptBossActor = true
        };
        var instance = fixture.CreatePartyInstance(2000);
        instance.EnterDetachedRoom(room);
        check(!instance.TryStartBossDeathCascade(out _),
            "an ALLDIE request cannot kill monsters while real bosses are still alive");
        instance.TryTakeMonsterDeath(boss.UniqueId, out _);
        instance.TryReportBossDeath(boss.UniqueId, [boss.UniqueId, secondBoss.UniqueId], out _);
        check(!instance.TryStartBossDeathCascade(out _),
            "defeating one of two bosses does not start the room death cascade");
        instance.TryTakeMonsterDeath(secondBoss.UniqueId, out _);
        instance.TryReportBossDeath(secondBoss.UniqueId, [boss.UniqueId, secondBoss.UniqueId], out _);
        check(instance.TryStartBossDeathCascade(out var first) && first
                && instance.TryStartBossDeathCascade(out first) && !first
                && instance.SnapshotBossDeathCascadeTargets().SequenceEqual([normal.UniqueId]),
            "the final boss starts one cascade and leaves an unopened box monster dormant");
        check(instance.TryAnnounceBossDeathCascade() && !instance.TryAnnounceBossDeathCascade(),
            "only one final-boss announcement is broadcast to all clients");

        var alpha = new DungeonBossDeathSweep();
        var bravo = new DungeonBossDeathSweep();
        check(alpha.Schedule(instance, now) == 1 && bravo.Schedule(instance, now) == 1
                && instance.AliveMonsterCount == 2
                && !instance.EvaluateRoomClear(secondBoss).ShouldEnableClear,
            "scheduling all-die keeps active monsters alive until their normal death settlement");
        alpha.Schedule(instance, now.AddSeconds(4));
        check(alpha.TakeDue(instance, now.AddSeconds(4)).Length == 0,
            "normal death confirmations receive the complete grace period");
        var aDue = alpha.TakeDue(instance, now.AddSeconds(5));
        var bDue = bravo.TakeDue(instance, now.AddSeconds(5));
        check(aDue.SequenceEqual([normal.UniqueId]) && bDue.SequenceEqual(aDue),
            "duplicate all-die requests do not postpone the death fallback deadline");

        // Both session loops can race on a native report and their due fallback.
        // This is the same atomic entry used before production generates rewards.
        var rewardPackets = new List<GameServerPacket>();
        var gate = new object();
        var deathGeneration = instance.RoomGeneration;
        Parallel.ForEach(aDue.Concat(bDue).Concat([normal.UniqueId]), id =>
        {
            if (!instance.TryBeginMonsterDeath(id, deathGeneration, out _, out var ordinal))
            {
                return;
            }
            try
            {
                var drop = instance.AddGroundItem(
                    new DungeonGeneratedDrop(true, 0, 100), 7, id, 0, 0, 100, 100);
                var packet = GameProtocolEngine.CreateMonsterDie(id,
                    [new GameDungeonDrop(drop.GroundId, drop.ItemId,
                        drop.GetClientAddInfo(fixture.Items), drop.GetClientDurability(fixture.Items), drop.OwnerUserId)], ordinal);
                lock (gate) { rewardPackets.Add(packet); }
            }
            finally
            {
                instance.CompleteMonsterDeath(deathGeneration);
            }
        });
        check(instance.NormalMonsterKills == 1 && instance.GetRoomGroundItems(0, 0).Length == 1
                && rewardPackets.Count == 1 && rewardPackets[0].Payload[2] == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(rewardPackets[0].Payload) == normal.UniqueId,
            "concurrent normal reports and fallback deaths generate exactly one kill, drop and drop-bearing NOTI38");
        check(instance.EvaluateRoomClear(normal).ShouldEnableClear && instance.TryEnableClear()
                && instance.SnapshotAliveMonsters().SetEquals([boxed.UniqueId]),
            "the boss room settles after present monsters die without deleting an unopened box monster");

        lock (instance.Gate) { instance.DestroyedPassiveObjects.Add(boxed.BoxIndex); }
        check(instance.SnapshotBossDeathCascadeTargets().SequenceEqual([boxed.UniqueId])
                && alpha.Schedule(instance, now.AddSeconds(6)) == 1,
            "breaking a box after settlement activates its pending monster death");
        check(instance.TryTakeMonsterDeath(boxed.UniqueId, out _)
                && !instance.TryTakeMonsterDeath(boxed.UniqueId, out _)
                && !instance.TryEnableClear()
                && alpha.TakeDue(instance, now.AddSeconds(20)).Length == 0,
            "a late box monster accepts one normal death without repeating settlement or fallback rewards");

        var stale = new DungeonBossDeathSweep();
        instance.EnterDetachedRoom(room);
        instance.TryTakeMonsterDeath(boss.UniqueId, out _);
        instance.TryTakeMonsterDeath(secondBoss.UniqueId, out _);
        instance.TryStartBossDeathCascade(out _);
        stale.Schedule(instance, now);
        var oldGeneration = instance.RoomGeneration;
        instance.EnterDetachedRoom(room);
        check(stale.TakeDue(instance, now.AddSeconds(10)).Length == 0
                && !instance.TryTakeMonsterDeath(normal.UniqueId, out _, oldGeneration)
                && instance.SnapshotAliveMonsters().SetEquals([0, 1, 2, 3])
                && instance.TryAnnounceBossDeathCascade(),
            "a new room generation discards old fallback deaths and final-boss broadcast state even when ids are reused");
        stale.Schedule(instance, now);
        check(stale.TakeDue(null, now.AddSeconds(10)).Length == 0 && !stale.HasPending,
            "leaving the instance cancels deferred boss deaths");

        CheckUnopenedBoxClearEvidence(check, fixture, boss, normal, boxed);
        CheckSettlementBarrier(check, fixture, boss, normal);
    }

    private static void CheckSettlementBarrier(
        Action<bool, string> check, DungeonFixture fixture,
        GameDungeonMonster boss, GameDungeonMonster normal)
    {
        var other = DungeonFixture.Monster(4);
        var room = DungeonFixture.Room([boss, normal, other]) with { MapType = DungeonMapType.Boss };
        var instance = fixture.CreatePartyInstance(2001);
        instance.EnterDetachedRoom(room);
        instance.TryTakeMonsterDeath(boss.UniqueId, out _);
        var generation = instance.RoomGeneration;
        check(instance.TryBeginMonsterDeath(normal.UniqueId, generation, out _, out _)
                && instance.TryBeginMonsterDeath(other.UniqueId, generation, out _, out _)
                && instance.AliveMonsterCount == 0
                && !instance.EvaluateRoomClear(normal).ShouldEnableClear
                && !instance.TryEnableClear(),
            "claiming every monster cannot open settlement while either death is still generating/broadcasting rewards");
        instance.CompleteMonsterDeath(generation);
        check(!instance.EvaluateRoomClear(other).ShouldEnableClear && !instance.TryEnableClear(),
            "one session finishing its rewards cannot pass another session's in-flight death");
        instance.CompleteMonsterDeath(generation);
        check(instance.EvaluateRoomClear(other).ShouldEnableClear && instance.TryEnableClear()
                && !instance.TryEnableClear(),
            "the last completed death opens settlement exactly once after every reward pipeline completes");

        instance.EnterDetachedRoom(room);
        var oldGeneration = instance.RoomGeneration;
        instance.TryBeginMonsterDeath(normal.UniqueId, oldGeneration, out _, out _);
        instance.EnterDetachedRoom(room);
        var newGeneration = instance.RoomGeneration;
        instance.TryTakeMonsterDeath(boss.UniqueId, out _);
        instance.TryBeginMonsterDeath(normal.UniqueId, newGeneration, out _, out _);
        instance.TryBeginMonsterDeath(other.UniqueId, newGeneration, out _, out _);
        check(!instance.CompleteMonsterDeath(oldGeneration),
            "an old room's delayed death completion cannot decrement the next room's settlement barrier");
        instance.CompleteMonsterDeath(newGeneration);
        check(!instance.TryEnableClear(), "the new room still waits for its own remaining death completion");
        instance.CompleteMonsterDeath(newGeneration);
        check(instance.TryEnableClear(), "a reset death barrier releases only for the new room's completed rewards");
    }

    private static void CheckUnopenedBoxClearEvidence(
        Action<bool, string> check, DungeonFixture fixture,
        GameDungeonMonster boss, GameDungeonMonster normal, GameDungeonMonster boxed)
    {
        var room = DungeonFixture.Room([boss, normal, boxed]) with { MapType = DungeonMapType.Boss };
        HashSet<ushort> defeated = [boss.UniqueId, normal.UniqueId];
        HashSet<byte> destroyed = [];
        check(DungeonRunState.IsRoomCleared(room, defeated, destroyed),
            "unopened boxes do not block a defeated boss room's clear-map quest evidence");
        destroyed.Add(boxed.BoxIndex);
        check(!DungeonRunState.IsRoomCleared(room, defeated, destroyed),
            "an already released box monster must die before the initial boss-room clear");
        defeated.Add(boxed.UniqueId);
        check(DungeonRunState.IsRoomCleared(room, defeated, destroyed),
            "the late box monster's normal death satisfies complete map-clear evidence");
        var run = new DungeonRunState(fixture.Catalog);
        check(run.TryStart(fixture.Layout, 0, 0, out _, fixture.Random), "map-clear latch fixture starts");
        var mapId = run.CurrentRoom!.MapId;
        run.MarkCurrentRoomCleared();
        check(run.HasClearedMap(mapId),
            "opening settlement latches map-clear evidence independently of later live monster changes");
        run.Stop();
        check(!run.HasClearedMap(mapId), "the next run cannot inherit latched map-clear evidence");
    }
}
