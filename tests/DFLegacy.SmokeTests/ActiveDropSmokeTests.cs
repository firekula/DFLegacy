using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ActiveDropSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new ActiveDropItemFixture();
        foreach (var (id, seal, allowed) in new (ushort, byte, bool)[]
        {
            (50010, 0, true), (50011, 0, true), (50012, 0, true),
            (50013, 0, false), (50014, 0, false), (50014, 1, true),
            (50014, 3, false), (50015, 0, true), (50016, 0, false),
            (50017, 0, false), (50018, 0, false)
        })
        {
            var item = new CharacterItemRecord(9, id, 1, State: 7, Durability: 23,
                SealState: seal, EquipmentQualitySeed: 123456, InstanceId: Guid.NewGuid());
            var inventory = new Dictionary<ushort, CharacterItemRecord> { [9] = item };
            var actual = DungeonItemDropPlanner.TryPlan(inventory, 0, 9, 1,
                fixture.Catalog, out var plan, out _);
            check(actual == allowed, $"active discard template {id}, seal {seal}: allowed={allowed}");
            check(inventory[9] == item, "discard planning never mutates the caller's inventory");
            if (actual)
                check(plan.DroppedItem == item with { Slot = ushort.MaxValue }
                        && !plan.Inventory.ContainsKey(9),
                    "discard retains equipment quality, reinforcement, durability, seal and identity");
        }
        check(fixture.Catalog.TryGetDefinition(50015, out var sealingTrade)
                && !CharacterItemSealing.CanTrade(new CharacterItemRecord(9, 50015, 1), sealingTrade),
            "discard eligibility does not relax the existing trade/mail policy");
        var stack = new CharacterItemRecord(73, 3176, 5, State: 4, Durability: 7);
        check(DungeonItemDropPlanner.TryPlan(new Dictionary<ushort, CharacterItemRecord> { [73] = stack },
                0, 73, 2, fixture.Catalog, out var stackPlan, out _, currentGold: 321)
                && stackPlan.DroppedItem.CountOrValue == 2 && stackPlan.Inventory[73].CountOrValue == 3
                && stackPlan.Gold == 321 && stackPlan.DroppedItem.State == 4,
            "an ordinary free stack discards only the requested count while preserving gold and metadata");
        check(DungeonItemDropPlanner.GetErrorCode(DungeonItemDropFailure.RarityRestricted) == 13
                && DungeonItemDropPlanner.GetErrorCode(DungeonItemDropFailure.NotTradeable) == 23,
            "discard rejects excessive rarity with native error13 and attachment with error23");
        foreach (var (balance, amount, allowed, error) in new (uint, uint, bool, byte)[]
        {
            (2000, 1, true, 0), (2000, 1000, true, 0), (2000, 1001, false, 70),
            (2000, 0, false, 17), (9, 10, false, 17), (10, 10, true, 0)
        })
        {
            var inventory = new Dictionary<ushort, CharacterItemRecord> { [9] = new(9, 50010, 1) };
            var allowedActual = DungeonItemDropPlanner.TryPlan(inventory, 0, 0, amount,
                fixture.Catalog, out var goldPlan, out var failure, currentGold: balance);
            check(allowedActual == allowed, $"active gold drop balance {balance}, amount {amount}: allowed={allowed}");
            if (allowedActual)
                check(goldPlan.IsGold && goldPlan.Gold == balance - amount
                        && goldPlan.DroppedItem.ItemId == 0 && goldPlan.DroppedItem.CountOrValue == amount
                        && goldPlan.Inventory.Count == 1 && goldPlan.Inventory[9] == inventory[9],
                    "gold planning debits only currency and retains every inventory item");
            else
                check(DungeonItemDropPlanner.GetErrorCode(failure) == error,
                    $"gold drop {amount} returns native error{error}");
        }
        await CheckTransactionAsync(check, fixture.Catalog);
        await CheckGoldConservationAsync(check, fixture.Catalog);
        await CheckPostCommitLogFailureAsync(check, fixture.Catalog);
        await CheckQueuedRoomPublicationAsync(check, fixture.Catalog);
        await CheckSerializedGuardAsync(check);
        await CheckMissingConditionalSenderAsync(check, fixture.Catalog);
    }

    private static async Task CheckQueuedRoomPublicationAsync(Action<bool, string> check, ItemCatalog catalog)
    {
        using var fixture = new DungeonFixture();
        await using var harness = await PartyHarness.CreateAsync(tempParent: Path.GetFullPath("../../../../../.tmp/active-drop-tests", AppContext.BaseDirectory));
        using var service = new DungeonPartyService(harness.Service, harness.Directory,
            new DungeonInstanceRegistry(), TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        foreach (var (party, returnToSameCoordinates, replaceMember) in new[]
        {
            (true, false, false), (true, true, false), (false, false, false), (false, true, false),
            (true, false, true)
        })
        {
            using var wire = new MemoryStream();
            using var serialized = new SerializedWriteStream(wire);
            var enteredSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask Send(GameServerPacket packet)
            {
                if (packet.ProtocolId == 40)
                {
                    enteredSend.TrySetResult();
                    await releaseSend.Task;
                }
                await packet.WriteAsync(serialized);
            }
            async ValueTask SendConditional(GameServerPacket packet, Func<bool> canWrite)
            {
                if (packet.ProtocolId == 40)
                {
                    enteredSend.TrySetResult();
                    await releaseSend.Task;
                }
                await serialized.WritePacketIfAsync(packet, canWrite);
            }
            var member = Guid.NewGuid();
            var instance = party ? fixture.CreatePartyInstance(4190) : fixture.CreateSoloInstance(member);
            instance.AddMember(new(member, 7, 0, "queued actor", Send, SendConditional: SendConditional));
            instance.TryStartRun(fixture.Layout, 0, 0, out _);
            DungeonItemDropPlanner.TryPlan(new Dictionary<ushort, CharacterItemRecord> { [9] = new(9, 50010, 1) },
                0, 9, 1, catalog, out var plan, out _);
            var pending = DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
                7, 0, 0, 100, 120, _ => Task.FromResult(true), Send, SendConditional);
            await enteredSend.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (replaceMember)
            {
                instance.RemoveMember(member);
                instance.AddMember(new(member, 7, 0, "new incarnation", Send, SendConditional: SendConditional));
            }
            else
            {
                instance.TryMoveRoom(1, 0, out _, fixture.Random);
                await new GameServerPacket(0, 29, []).WriteAsync(serialized);
                if (returnToSameCoordinates)
                {
                    instance.TryMoveRoom(0, 0, out _, fixture.Random);
                    await new GameServerPacket(0, 29, []).WriteAsync(serialized);
                }
            }
            releaseSend.SetResult();
            await pending;
            var ids = PacketIds(wire.ToArray());
            var expected = replaceMember ? new byte[] { 50 }
                : returnToSameCoordinates ? new byte[] { 29, 29, 50 } : new byte[] { 29, 50 };
            check(ids.SequenceEqual(expected),
                $"queued actor guard: party={party}, revisit={returnToSameCoordinates}, replaced member={replaceMember}");
        }
    }

    private static async Task CheckMissingConditionalSenderAsync(Action<bool, string> check, ItemCatalog catalog)
    {
        using var fixture = new DungeonFixture();
        await using var harness = await PartyHarness.CreateAsync(tempParent: Path.GetFullPath("../../../../../.tmp/active-drop-tests", AppContext.BaseDirectory));
        using var service = new DungeonPartyService(harness.Service, harness.Directory,
            new DungeonInstanceRegistry(), TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        var id = Guid.NewGuid();
        var instance = fixture.CreatePartyInstance(4191);
        var ordinaryCalls = 0;
        instance.AddMember(new(id, 7, 0, "unguarded channel", _ => { ordinaryCalls++; return ValueTask.CompletedTask; }));
        instance.TryStartRun(fixture.Layout, 0, 0, out _);
        DungeonItemDropPlanner.TryPlan(new Dictionary<ushort, CharacterItemRecord> { [9] = new(9, 50010, 1) },
            0, 9, 1, catalog, out var plan, out _);
        var privatePackets = new List<GameServerPacket>();
        ValueTask SendPrivate(GameServerPacket packet) { privatePackets.Add(packet); return ValueTask.CompletedTask; }
        await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service, 7, 0, 0, 100, 120,
            _ => Task.FromResult(true), SendPrivate, Guarded(SendPrivate));
        check(ordinaryCalls == 0 && privatePackets.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 50 }),
            "active publication never falls back to ordinary Send when a member lacks the conditional write contract");
    }

    private static Func<GameServerPacket, Func<bool>, ValueTask> Guarded(Func<GameServerPacket, ValueTask> send) =>
        (packet, canWrite) => canWrite() ? send(packet) : ValueTask.CompletedTask;

    private static async Task CheckSerializedGuardAsync(Action<bool, string> check)
    {
        using var inner = new BlockingFirstWriteStream();
        using var serialized = new SerializedWriteStream(inner);
        var roomPacket = new GameServerPacket(0, 29, []).WriteAsync(serialized).AsTask();
        await inner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var current = true;
        var checks = 0;
        var actor = new GameServerPacket(0, 40, []);
        var queued = serialized.WritePacketIfAsync(actor, () => { checks++; return current; }).AsTask();
        check(checks == 0 && !queued.IsCompleted,
            "a guarded packet checks state only after obtaining the actual SerializedWriteStream gate");
        current = false;
        inner.Release.SetResult();
        await roomPacket;
        check(!await queued && checks == 1 && PacketIds(inner.ToArray()).SequenceEqual(new byte[] { 29 }),
            "room invalidation while waiting for the write gate suppresses the old actor's bytes");
        check(await serialized.WritePacketIfAsync(actor, () => true)
                && PacketIds(inner.ToArray()).SequenceEqual(new byte[] { 29, 40 }),
            "a current guarded packet still writes one complete native frame");
    }

    private sealed class BlockingFirstWriteStream : MemoryStream
    {
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool first = true;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            if (first)
            {
                first = false;
                Entered.SetResult();
                await Release.Task.WaitAsync(token);
            }
            await base.WriteAsync(bytes, token);
        }
    }

    private static byte[] PacketIds(byte[] wire)
    {
        var ids = new List<byte>();
        for (var offset = 0; offset < wire.Length;)
        {
            ids.Add(wire[offset + 1]);
            offset += checked((int)BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(offset + 2)));
        }
        return ids.ToArray();
    }

    private static async Task CheckPostCommitLogFailureAsync(Action<bool, string> check, ItemCatalog catalog)
    {
        using var fixture = new DungeonFixture();
        await using var harness = await PartyHarness.CreateAsync(tempParent: Path.GetFullPath("../../../../../.tmp/active-drop-tests", AppContext.BaseDirectory));
        var logger = new ThrowAfterSnapshotLogger();
        var store = new JsonGameStore(harness.Options, logger);
        await store.InitializeAsync();
        var original = (await store.GetCharactersAsync("alice"))[0];
        DungeonItemDropPlanner.TryPlan((original.Inventory ?? []).ToDictionary(i => i.Slot),
            0, 0, 100, catalog, out var plan, out _, currentGold: checked((uint)original.Gold));
        using var service = new DungeonPartyService(harness.Service, harness.Directory,
            new DungeonInstanceRegistry(), TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreateSoloInstance(original.Id);
        instance.TryStartRun(fixture.Layout, 0, 0, out _);
        var packets = new List<GameServerPacket>();
        ValueTask SendCommitted(GameServerPacket packet) { packets.Add(packet); return ValueTask.CompletedTask; }
        DungeonGroundItem? ground = null;
        logger.Armed = true;
        try
        {
            ground = await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
                7, 0, 0, 100, 120, async token =>
                    await store.SaveCharacterItemSpacesAsync("alice", original.Id, checked((int)plan.Gold),
                        plan.Inventory.Values, original.Equipment ?? [], original.Warehouse ?? [], token,
                        avatarInventory: original.AvatarInventory, creatureInventory: original.CreatureInventory) is not null,
                SendCommitted, Guarded(SendCommitted));
        }
        catch (IOException) { }
        var saved = (await store.GetCharactersAsync("alice"))[0];
        check(logger.Throws == 1 && saved.Gold == original.Gold - 100
                && ground is { IsGold: true, Activated: true }
                && instance.TryGetGroundItem(ground.GroundId, 0, 0, out _)
                && packets.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 40, 50 }),
            "a logging IOException after atomic snapshot replacement keeps the committed drop and sends actor/ACK");
        logger.Armed = false;
        var disk = new JsonGameStore(harness.Options, NullLogger<JsonGameStore>.Instance);
        await disk.InitializeAsync();
        check((await disk.GetCharactersAsync("alice"))[0].Gold == original.Gold - 100,
            "post-commit diagnostics do not restore or retry an already persisted gold debit");
        logger.Armed = true;
        var otherApiThrew = false;
        try
        {
            await store.SaveCharacterInventoryAsync("alice", original.Id, saved.Gold,
                saved.Inventory ?? [], saved.Equipment ?? []);
        }
        catch (IOException) { otherApiThrew = true; }
        check(otherApiThrew && logger.Throws == 2,
            "isolating committed item-space diagnostics does not change other save API exception behavior");
    }

    private sealed class ThrowAfterSnapshotLogger : ILogger<JsonGameStore>
    {
        internal bool Armed;
        internal int Throws;
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (Armed && formatter(state, exception).StartsWith("Saved emulator state", StringComparison.Ordinal))
            {
                Throws++;
                throw new IOException("controlled diagnostic failure after atomic replacement");
            }
        }
    }

    private static async Task CheckTransactionAsync(Action<bool, string> check, ItemCatalog catalog)
    {
        using var fixture = new DungeonFixture();
        await using var harness = await PartyHarness.CreateAsync(tempParent: Path.GetFullPath("../../../../../.tmp/active-drop-tests", AppContext.BaseDirectory));
        using var service = new DungeonPartyService(harness.Service, harness.Directory,
            new DungeonInstanceRegistry(), TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        var sender = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var mine = new List<GameServerPacket>();
        var theirs = new List<GameServerPacket>();
        ValueTask SendMine(GameServerPacket packet) { mine.Add(packet); return ValueTask.CompletedTask; }
        ValueTask SendPeer(GameServerPacket packet) { theirs.Add(packet); return ValueTask.CompletedTask; }
        var instance = fixture.CreatePartyInstance(4100);
        instance.AddMember(new(sender, 7, 0, "dropper", SendMine, SendConditional: Guarded(SendMine)));
        instance.AddMember(new(peer, 9, 1, "picker", SendPeer, SendConditional: Guarded(SendPeer)));
        instance.TryStartRun(fixture.Layout, 0, 0, out _);
        var source = new CharacterItemRecord(9, 50011, 1, State: 7, Durability: 23, EquipmentQualitySeed: 123456);
        DungeonItemDropPlanner.TryPlan(new Dictionary<ushort, CharacterItemRecord> { [9] = source },
            0, 9, 1, catalog, out var plan, out _, currentGold: 123);
        var saves = 0;
        var ground = await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
            7, 0, 0, 100, 120, token =>
            {
                saves++;
                check(instance.GetRoomGroundItems(0, 0).Length == 0
                        && !instance.TryGetGroundItem(1, 0, 0, out _),
                    "an active discard cannot be seen or picked while its inventory save is pending");
                return Task.FromResult(true);
            }, SendMine, Guarded(SendMine));
        check(saves == 1 && ground is not null, "a successful active discard persists exactly once");
        check(mine.Select(p => (p.Type, p.ProtocolId)).SequenceEqual(new[] { ((byte)0, (byte)40), ((byte)1, (byte)50) })
                && theirs.Count == 1 && theirs[0].ProtocolId == 40,
            "sender and peer receive one ground actor, then only sender receives CMD50 inventory ACK");
        if (ground is not null)
        {
            check(theirs.Count == 1 && mine[0].Payload.SequenceEqual(theirs[0].Payload) && mine[0].Payload.Length == 17
                    && BinaryPrimitives.ReadUInt16LittleEndian(mine[0].Payload) == 7
                    && BinaryPrimitives.ReadUInt16LittleEndian(mine[0].Payload.AsSpan(6)) == ground.GroundId,
                "all recipients see the same ACT1 ground identity and original wire dropper UID");
            check(ground.RoutingSource == 0 && ground.PreservedItem?.EquipmentQualitySeed == 123456,
                "active discard retains source0 and preserved equipment metadata");
            check(instance.TryBeginLootPickup(peer, ground, false, [], _ => 0, out var decision)
                    && decision.WinnerCharacterId == peer,
                "a teammate directly receives the active drop without dropper ownership or dice");
            check(instance.TryClaimGroundItem(ground.GroundId, 0, 0, out _, ground, decision.PickupId)
                    && !instance.TryClaimGroundItem(ground.GroundId, 0, 0, out _),
                "an actively discarded item can be claimed exactly once");
            await service.BroadcastPickupRemovalAsync(instance, ground.GroundId, 9, 50011, SendPeer);
            check(mine.Count(p => p.ProtocolId == 86) == 1 && theirs.Count(p => p.ProtocolId == 86) == 1,
                "the existing shared pickup removal removes the active actor on both clients");
        }
        mine.Clear(); theirs.Clear();
        var rejected = await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
            7, 0, 0, 100, 120, _ => Task.FromResult(false), SendMine, Guarded(SendMine));
        check(rejected is null && instance.GetRoomGroundItems(0, 0).Length == 0
                && mine.Count == 0 && theirs.Count == 0,
            "failed persistence rolls back the reserved ground entity without notification or success ACK");
        try
        {
            await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
                7, 0, 0, 100, 120, _ => throw new IOException("synthetic save failure"), SendMine, Guarded(SendMine));
            check(false, "persistence exceptions remain visible to the session failure handler");
        }
        catch (IOException)
        {
            check(instance.GetRoomGroundItems(0, 0).Length == 0 && mine.Count == 0 && theirs.Count == 0,
                "exceptional persistence also rolls back its unpublished ground reservation");
        }
        var poison = Guid.NewGuid();
        async ValueTask SendClosed(GameServerPacket packet)
        {
            await Task.Yield();
            throw new OperationCanceledException("closed peer's token");
        }
        instance.AddMember(new(poison, 10, 2, "closed", SendClosed, SendConditional: Guarded(SendClosed)));
        ground = await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
            7, 0, 0, 100, 120, _ => Task.FromResult(true), SendMine, Guarded(SendMine));
        check(ground is not null && mine.Count == 2 && theirs.Count == 1,
            "an asynchronously canceled peer cannot prevent healthy delivery or the dropper ACK");
        if (ground is not null) instance.RemoveGroundItem(ground.GroundId);
        mine.Clear(); theirs.Clear();
        var resetInstance = fixture.CreatePartyInstance(4102);
        resetInstance.AddMember(new(sender, 7, 0, "reset dropper", SendMine, SendConditional: Guarded(SendMine)));
        resetInstance.TryStartRun(fixture.Layout, 0, 0, out _);
        DungeonGroundItem? replacement = null;
        rejected = await DungeonActiveDropTransaction.CommitAsync(resetInstance, plan, catalog, service,
            7, 0, 0, 100, 120, token =>
            {
                resetInstance.ResetGroundItems();
                replacement = resetInstance.AddGroundItem(new(true, 0, 9), 7, 0, 0, 0, 100, 120);
                return Task.FromResult(false);
            }, SendMine, Guarded(SendMine));
        check(rejected is null && replacement is not null
                && resetInstance.TryGetGroundItem(replacement.GroundId, 0, 0, out var stillThere)
                && ReferenceEquals(stillThere, replacement) && mine.Count == 0,
            "rollback of an old reservation cannot remove a new run's reused ground id");
        ground = await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
            7, 0, 0, 100, 120, token =>
            {
                instance.TryMoveRoom(1, 0, out _, fixture.Random);
                return Task.FromResult(true);
            }, SendMine, Guarded(SendMine));
        check(ground is not null && mine.Count == 1 && mine[0].ProtocolId == 50 && theirs.Count == 0,
            "a room change during saving does not spawn an old-room actor in the new room");
        var solo = fixture.CreateSoloInstance(sender);
        solo.TryStartRun(fixture.Layout, 0, 0, out _);
        mine.Clear();
        ground = await DungeonActiveDropTransaction.CommitAsync(solo, plan, catalog, service,
            7, 0, 0, 100, 120, _ => Task.FromResult(true), SendMine, Guarded(SendMine));
        check(ground is not null && mine.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 40, 50 }) && theirs.Count == 0,
            "a solo active drop sends one actor and one ACK without notifying another instance");
        var tutorial = fixture.CreateSoloInstance(sender);
        tutorial.EnterDetachedRoom(DungeonFixture.Room([]));
        mine.Clear();
        ground = await DungeonActiveDropTransaction.CommitAsync(tutorial, plan, catalog, service,
            7, 1, 0, 100, 120, _ => Task.FromResult(true), SendMine, Guarded(SendMine));
        check(ground is not null && mine.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 50 }),
            "a detached tutorial room at0,0 does not receive an old-room1,0 ground actor");
    }

    private static async Task CheckGoldConservationAsync(Action<bool, string> check, ItemCatalog catalog)
    {
        using var fixture = new DungeonFixture();
        await using var harness = await PartyHarness.CreateAsync(tempParent: Path.GetFullPath("../../../../../.tmp/active-drop-tests", AppContext.BaseDirectory));
        var alice = (await harness.Store.GetCharactersAsync("alice"))[0];
        var bob = (await harness.Store.GetCharactersAsync("bobby"))[0];
        alice = (await harness.Store.SaveCharacterInventoryAsync("alice", alice.Id, 1000,
            alice.Inventory ?? [], alice.Equipment ?? []))!;
        using var service = new DungeonPartyService(harness.Service, harness.Directory,
            new DungeonInstanceRegistry(), TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(4101);
        var mine = new List<GameServerPacket>();
        var theirs = new List<GameServerPacket>();
        ValueTask SendMine(GameServerPacket p) { mine.Add(p); return ValueTask.CompletedTask; }
        instance.AddMember(new(alice.Id, 7, 0, "gold dropper", SendMine, SendConditional: Guarded(SendMine)));
        ValueTask SendGoldPeer(GameServerPacket packet) { theirs.Add(packet); return ValueTask.CompletedTask; }
        instance.AddMember(new(bob.Id, 9, 1, "gold picker", SendGoldPeer, SendConditional: Guarded(SendGoldPeer)));
        instance.TryStartRun(fixture.Layout, 0, 0, out _);
        DungeonItemDropPlanner.TryPlan((alice.Inventory ?? []).ToDictionary(i => i.Slot), 0, 0, 101,
            catalog, out var plan, out _, currentGold: 1000);
        var ground = await DungeonActiveDropTransaction.CommitAsync(instance, plan, catalog, service,
            7, 0, 0, 100, 120, async _ =>
            {
                alice = (await harness.Store.SaveCharacterInventoryAsync("alice", alice.Id,
                    checked((int)plan.Gold), plan.Inventory.Values, alice.Equipment ?? []))!;
                return alice is not null;
            }, SendMine, Guarded(SendMine));
        check(ground is { IsGold: true, ItemId: 0, CountOrValue: 101, RoutingSource: 0 }
                && alice.Gold == 899 && mine.Count == 2 && theirs.Count == 1,
            "gold discard durably debits currency and broadcasts one source0 currency entity");
        if (ground is null) return;
        check(mine[0].Payload.Length == 17 && BinaryPrimitives.ReadUInt16LittleEndian(mine[0].Payload.AsSpan(8)) == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(mine[0].Payload.AsSpan(11)) == 101,
            "ACT1 gold NOTI40 uses itemId0 and the amount in addInfo");
        check(instance.TryBeginLootPickup(bob.Id, ground, false, [], _ => 0, out var decision)
                && instance.TryClaimGoldPickup(bob.Id, ground, decision.PickupId, out _),
            "a teammate can reserve and claim the active gold distribution");
        var committed = await harness.Store.CommitDungeonGoldDistributionAsync(decision.PickupId,
            instance.CaptureGoldEligibleMembers(), 1, 101, routingSource: ground.RoutingSource);
        var paidAlice = await harness.Store.ConsumeDungeonGoldCreditsAsync(alice.Id);
        var paidBob = await harness.Store.ConsumeDungeonGoldCreditsAsync(bob.Id);
        check(committed.Amounts.SequenceEqual(new uint[] { 50, 51, 0, 0 })
                && paidAlice?.Gold == 949 && paidBob?.Gold == bob.Gold + 51
                && paidAlice.Gold + paidBob.Gold == 1000 + bob.Gold,
            "existing gold sharing preserves total balances and gives the picker the remainder");
        check(!instance.TryClaimGoldPickup(bob.Id, ground, decision.PickupId, out _)
                && await harness.Store.ConsumeDungeonGoldCreditsAsync(alice.Id) is null
                && await harness.Store.ConsumeDungeonGoldCreditsAsync(bob.Id) is null,
            "active gold pickup cannot grant or consume currency a second time");
        mine.Clear(); theirs.Clear();
        var original = (await harness.Store.GetCharactersAsync("alice"))[0];
        DungeonItemDropPlanner.TryPlan((original.Inventory ?? []).ToDictionary(i => i.Slot), 0, 0, 100,
            catalog, out var failingPlan, out _, currentGold: checked((uint)original.Gold));
        using (var blocked = new FileStream(Path.GetFullPath(harness.Options.DataPath) + ".tmp",
                   FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            try
            {
                await DungeonActiveDropTransaction.CommitAsync(instance, failingPlan, catalog, service,
                    7, 0, 0, 100, 120, async token =>
                        await harness.Store.SaveCharacterItemSpacesAsync("alice", original.Id,
                            checked((int)failingPlan.Gold), failingPlan.Inventory.Values,
                            original.Equipment ?? [], original.Warehouse ?? [], token,
                            avatarInventory: original.AvatarInventory, creatureInventory: original.CreatureInventory) is not null,
                    SendMine, Guarded(SendMine));
                check(false, "the synthetic file lock actually rejects the active-drop save");
            }
            catch (IOException)
            {
                var afterFailure = (await harness.Store.GetCharactersAsync("alice"))[0];
                check(afterFailure.Gold == original.Gold
                        && (afterFailure.Inventory ?? []).SequenceEqual(original.Inventory ?? [])
                        && instance.GetRoomGroundItems(0, 0).Length == 0 && mine.Count == 0 && theirs.Count == 0,
                    "a real inventory snapshot write failure preserves currency/inventory and publishes no active gold");
            }
        }
        var reopened = new JsonGameStore(harness.Options, NullLogger<JsonGameStore>.Instance);
        await reopened.InitializeAsync();
        check((await reopened.GetCharactersAsync("alice"))[0].Gold == original.Gold,
            "a failed temporary snapshot leaves the previously committed gold balance on disk");
    }
}

internal sealed class ActiveDropItemFixture : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dflegacy-active-drop-" + Guid.NewGuid().ToString("N"));
    public ItemCatalog Catalog { get; }

    public ActiveDropItemFixture()
    {
        Directory.CreateDirectory(Path.Combine(root, "equipment"));
        Directory.CreateDirectory(Path.Combine(root, "stackable"));
        File.WriteAllText(Path.Combine(root, "stackable/stackable.lst"), "3176 `free.stk`\n");
        File.WriteAllText(Path.Combine(root, "stackable/free.stk"),
            "[name] `Free stack`\n[stackable type] `[material]`\n[attach type] `[free]`\n[rarity] 0\n[stack limit] 99\n[weight] 1\n");
        var rows = new List<string>();
        foreach (var (id, rarity, attach, type) in new[]
        {
            (50010, 0, "free", "weapon"), (50011, 1, "free", "coat"),
            (50012, 2, "free", "weapon"), (50013, 3, "free", "weapon"),
            (50014, 1, "sealing", "weapon"), (50015, 1, "sealing trade", "weapon"),
            (50016, 0, "free", "title name"), (50017, 0, "free", "hat avatar"),
            (50018, 0, "trade", "weapon")
        })
        {
            rows.Add($"{id} `{id}.equ`");
            File.WriteAllText(Path.Combine(root, $"equipment/{id}.equ"),
                $"[name] `Drop {id}`\n[equipment type] `[{type}]`\n[attach type] `[{attach}]`\n"
                + $"[rarity] {rarity}\n[weight] 1\n[minimum level] 1\n[durability] 30\n");
        }
        File.WriteAllLines(Path.Combine(root, "equipment/equipment.lst"), rows);
        var options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        Catalog = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
    }

    public void Dispose() => Directory.Delete(root, true);
}
