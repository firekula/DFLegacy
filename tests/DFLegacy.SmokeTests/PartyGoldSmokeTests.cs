using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PartyGoldSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        uint[] Split(uint gold, byte picker, params byte[] slots) =>
            DungeonGoldSharing.Allocate(gold, picker, slots);
        check(Split(101, 0, 0, 1).SequenceEqual(new uint[] { 51, 50, 0, 0 }),
            "two members split gold equally and the picker receives the remainder");
        check(Split(101, 3, 0, 2, 3).SequenceEqual(new uint[] { 33, 0, 33, 35 }),
            "three noncontiguous party slots keep their identities and picker remainder");
        check(Split(3, 2, 0, 1, 2, 3).SequenceEqual(new uint[] { 0, 0, 3, 0 }),
            "a drop smaller than party size awards the remainder only to the picker");
        check(Split(101, 2, 2).SequenceEqual(new uint[] { 0, 0, 101, 0 }),
            "one eligible member receives all gold without changing its slot");
        check(Split(101, 2, 0, 1).SequenceEqual(new uint[] { 50, 50, 0, 0 }),
            "a picker without room eligibility does not retain the remainder after the official eligibility gate");
        check(Split(uint.MaxValue, 0, 0, 1, 2, 3).Select(x => (long)x).Sum() == uint.MaxValue,
            "allocation preserves the total without unsigned overflow");
        using var fixture = new DungeonFixture();
        var instance = fixture.CreatePartyInstance(3100);
        var alive = Guid.NewGuid();
        var dead = Guid.NewGuid();
        instance.AddMember(fixture.Member(alive, 0, "alive"));
        instance.AddMember(fixture.Member(dead, 2, "corpse"));
        instance.TryStartRun(fixture.Layout, 0, 0, out _);
        DungeonInstanceMember[] Eligible() => instance.CaptureGoldEligibleMembers();
        instance.TrySetPartyMemberAlive(dead, false, DateTimeOffset.UtcNow);
        check(Eligible().Length == 2, "a death in the current room retains gold eligibility");
        instance.TryMoveRoom(1, 0, out _, fixture.Random);
        check(Eligible().Select(m => m.CharacterId).SequenceEqual([alive]),
            "a dead spectator loses gold eligibility on moving to the next room");
        instance.TrySetPartyMemberAlive(dead, false, DateTimeOffset.UtcNow);
        check(Eligible().Length == 1, "a duplicate death cannot recreate room eligibility");
        instance.TrySetPartyMemberAlive(dead, true, DateTimeOffset.UtcNow);
        check(Eligible().Length == 2, "a committed revival restores gold qualification");
        instance.RemoveMember(dead);
        check(Eligible().Length == 1, "a departed member gets no future gold share");
        var gold = instance.AddGroundItem(new(true, 0, 101), 1, 1, 1, 0, 0, 0);
        var attempts = new System.Collections.Concurrent.ConcurrentBag<LootRoutingDecision>();
        Parallel.For(0, 8, _ =>
        {
            if (instance.TryBeginLootPickup(alive, gold, false, [], _ => 0, out var decision)) attempts.Add(decision);
        });
        check(attempts.Count == 1, "concurrent gold requests reserve one distribution only");
        var token = attempts.Single().PickupId;
        check(!instance.TryClaimGoldPickup(alive, gold, Guid.Empty, out _),
            "a gold pickup cannot bypass its reservation with an empty token");
        check(instance.TryClaimGoldPickup(alive, gold, token, out var claimed) && claimed.Length == 1
                && !instance.TryClaimGoldPickup(alive, gold, token, out _),
            "a gold object is claimed once with its original reservation identity");
        var registry = new DungeonInstanceRegistry();
        registry.BindCharacter(alive, instance);
        registry.PostGoldCreditUpdate(alive, token);
        registry.UnbindCharacter(alive);
        check(registry.TryTakeGoldCreditUpdate(alive, out var notification) && notification == token,
            "returning to town cannot discard the wakeup for an already committed gold share");
        await CheckStoreAsync(check, fixture);
        var packet = GameProtocolEngine.CreateGoldItemPickup(42, 9, new uint[] { 51, 0, 50, 0 });
        {
            check(packet.Payload.Length == 24 && packet.ProtocolId == 39
                    && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload) == 42
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(4)) == 51
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(14)) == 50,
                "native gold result preserves the ground/picker prefix and four u32-plus-zero-adjustment slots");
        }
        await CheckWriteFailuresAsync(check, fixture);
        await CheckTradeDeliveryAsync(check, fixture);
    }

    private static async Task CheckStoreAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var alice = (await harness.Store.GetCharactersAsync("alice"))[0];
        var bob = (await harness.Store.GetCharactersAsync("bobby"))[0];
        var members = new[] { fixture.Member(alice.Id, 0, alice.Name), fixture.Member(bob.Id, 2, bob.Name) };
        var id = Guid.NewGuid();
        Task<DungeonGoldCommitResult> Commit(JsonGameStore store, Guid key, uint amount) =>
            store.CommitDungeonGoldDistributionAsync(key, members, 0, amount);
        Task<CharacterRecord?> Consume(JsonGameStore store, Guid who) =>
            store.ConsumeDungeonGoldCreditsAsync(who);
        var first = await Commit(harness.Store, id, 101);
        check(first.Status == DungeonRewardLedgerStatus.Committed
                && first.Amounts.SequenceEqual(new uint[] { 51, 0, 50, 0 }),
            "all gold shares commit in a single durable distribution");
        var ordinary = await harness.Store.SaveCharacterInventoryAsync("alice", alice.Id, alice.Gold + 5,
            alice.Inventory ?? [], alice.Equipment ?? []);
        check(ordinary is not null, "an ordinary inventory save can interleave before credit consumption");
        check((await Consume(harness.Store, alice.Id))?.Gold == alice.Gold + 56,
            "the session consumes its credit without losing an interleaved gold increase");
        check(await Consume(harness.Store, alice.Id) is null, "consumed credits cannot be applied twice");

        var reloaded = new JsonGameStore(harness.Options, NullLogger<JsonGameStore>.Instance);
        await reloaded.InitializeAsync();
        check((await Consume(reloaded, bob.Id))?.Gold == bob.Gold + 50,
            "a disconnected recipient's durable share survives server restart");
        var replay = await Commit(reloaded, id, 101);
        check(replay.Status == DungeonRewardLedgerStatus.Committed
                && await Consume(reloaded, bob.Id) is null && await Consume(reloaded, alice.Id) is null,
            "a persisted distribution receipt prevents replay after every credit was consumed");

        alice = (await reloaded.GetCharactersAsync("alice"))[0];
        await reloaded.SaveCharacterInventoryAsync("alice", alice.Id, int.MaxValue - 10,
            alice.Inventory ?? [], alice.Equipment ?? []);
        var capped = await Commit(reloaded, Guid.NewGuid(), 101);
        check(capped.Amounts[0] == 10 && capped.Amounts[2] == 50,
            "the notification uses actual accepted gold and does not redistribute a capped share");
        check(await reloaded.SaveCharacterInventoryAsync("alice", alice.Id, int.MaxValue,
                alice.Inventory ?? [], alice.Equipment ?? []) is null,
            "a concurrent ordinary increase cannot spend capacity reserved by an announced gold credit");
        check((await Consume(reloaded, alice.Id))?.Gold == int.MaxValue,
            "a reserved boundary credit remains fully consumable");
        var mismatch = await Commit(reloaded, id, 103);
        check(mismatch.Status == DungeonRewardLedgerStatus.Rejected,
            "reusing a pickup identity with a different distribution never creates a second credit");
        await Consume(reloaded, bob.Id);
        var bobBefore = (await reloaded.GetCharactersAsync("bobby"))[0].Gold;
        var one = Guid.NewGuid(); var two = Guid.NewGuid();
        var awardOne = await Commit(reloaded, one, 30);
        var awardTwo = await Commit(reloaded, two, 70);
        check((await reloaded.ConsumeDungeonGoldCreditsAsync(bob.Id, default, one))?.Gold == bobBefore + awardOne.Amounts[2],
            "publishing one pickup consumes only its own credit, not a newer unannounced pickup");
        check((await reloaded.ConsumeDungeonGoldCreditsAsync(bob.Id, default, two))?.Gold
                == bobBefore + awardOne.Amounts[2] + awardTwo.Amounts[2],
            "the next published pickup keeps its independent amount and is applied once");
    }

    private static async Task CheckWriteFailuresAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        foreach (var afterReplace in new[] { false, true })
        {
            var root = Path.Combine(Path.GetTempPath(), "dflegacy-gold-failure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var options = new ServerOptions { DataPath = Path.Combine(root, "state.json") };
                var writer = new GoldFailingWriter(afterReplace);
                var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, writer);
                await store.InitializeAsync();
                var character = (await store.GetCharactersAsync("test"))[0];
                var key = Guid.NewGuid();
                var members = new[] { fixture.Member(character.Id, 0, character.Name) };
                var result = await store.CommitDungeonGoldDistributionAsync(key, members, 0, 13);
                check(result.Status == DungeonRewardLedgerStatus.Unknown,
                    $"gold snapshot failure afterReplace={afterReplace} is uncertain and never reports success");
                var replay = await store.CommitDungeonGoldDistributionAsync(key, members, 0, 13);
                check(replay.Status == DungeonRewardLedgerStatus.Unknown && writer.Calls == 1,
                    "an uncertain gold write quarantines the store without retrying distribution");
                var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
                await reloaded.InitializeAsync();
                var recovered = await reloaded.ConsumeDungeonGoldCreditsAsync(character.Id);
                check(afterReplace ? recovered?.Gold == character.Gold + 13 : recovered is null,
                    "restart follows the actual atomic snapshot outcome without duplicated or partial gold");

                writer = new GoldFailingWriter(afterReplace) { Armed = false };
                store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, writer);
                await store.InitializeAsync();
                var before = (await store.GetCharactersAsync("test"))[0].Gold;
                await store.CommitDungeonGoldDistributionAsync(Guid.NewGuid(), members, 0, 17);
                writer.Armed = true;
                var failed = false;
                try { await store.ConsumeDungeonGoldCreditsAsync(character.Id); }
                catch (IOException) { failed = true; }
                check(failed, "an uncertain credit-consumption write fails without installing a cached balance");
                reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
                await reloaded.InitializeAsync();
                await reloaded.ConsumeDungeonGoldCreditsAsync(character.Id);
                check((await reloaded.GetCharactersAsync("test"))[0].Gold == before + 17
                        && await reloaded.ConsumeDungeonGoldCreditsAsync(character.Id) is null,
                    "consumption failure on either side of snapshot replacement recovers the credit exactly once");
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    private sealed class GoldFailingWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        internal int Calls;
        internal bool Armed = true;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken cancellationToken)
        {
            Calls++;
            if (!Armed)
            {
                await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, cancellationToken);
                return;
            }
            if (afterReplace) await File.WriteAllBytesAsync(path, snapshot.ToArray(), cancellationToken);
            throw new IOException("injected gold snapshot failure");
        }
    }

    private static async Task CheckTradeDeliveryAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await TradeHarness.CreateAsync();
        var alice = (await harness.Store.GetCharactersAsync("alice"))[0];
        var bob = (await harness.Store.GetCharactersAsync("bobby"))[0];
        var trade = await harness.Store.CommitItemTradeAsync(new ItemTradeCommitRequest(
            alice.Id, "alice", bob.Id, "bobby", [], [], 0, 0));
        check(trade.Committed, "trade delivery is pending before a dungeon gold award");
        var award = await harness.Store.CommitDungeonGoldDistributionAsync(Guid.NewGuid(),
            [fixture.Member(alice.Id, 0, alice.Name), fixture.Member(bob.Id, 1, bob.Name)], 0, 101);
        check(award.Status == DungeonRewardLedgerStatus.Committed
                && await harness.Store.ConsumeDungeonGoldCreditsAsync(alice.Id) is null,
            "gold consumption waits for the pending trade snapshot to be installed");
        harness.Store.AcknowledgeTradeDelivery(alice.Id, trade.TradeId);
        check((await harness.Store.ConsumeDungeonGoldCreditsAsync(alice.Id))?.Gold == trade.Initiator!.Gold + 51,
            "after trade installation the durable gold credit is applied to the settled trade balance");
    }
}
