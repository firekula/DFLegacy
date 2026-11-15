using System.Buffers.Binary;
using System.Collections.Concurrent;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

// Managed service/store integration coverage; no live server or native client is started.
internal static class ActiveDropFourClientSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> report)
    {
        var assertions = 0;
        void check(bool condition, string message) { assertions++; report(condition, message); }
        var output = Path.GetFullPath("../../../../../.tmp/smoke-tests/active-drop-four", AppContext.BaseDirectory);
        var runRoot = Path.Combine(output, "managed-" + Guid.NewGuid().ToString("N"));
        var fixture = new FourFixture(runRoot);
        await using var harness = await PartyHarness.CreateAsync(tempParent: runRoot);
        using var service = new DungeonPartyService(harness.Service, harness.Directory,
            new DungeonInstanceRegistry(), TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        var players = new[]
        {
            new Player("A", "alice", 91, 2), new Player("B", "bobby", 503, 0),
            new Player("C", "carol", 17, 1), new Player("D", "dave", 244, 3)
        };
        for (var index = 0; index < players.Length; index++)
        {
            var player = players[index];
            player.Character = (await harness.Store.GetCharactersAsync(player.Account))[0];
            var item = new CharacterItemRecord(9, index % 2 == 0 ? (ushort)50011 : (ushort)50014, 1,
                State: checked((byte)(7 + index)), Durability: checked((byte)(23 - index)),
                SealState: index % 2 == 0 ? (byte)0 : (byte)1,
                EquipmentQualitySeed: checked((uint)(123456 + index)), InstanceId: Guid.NewGuid());
            var rejected = new CharacterItemRecord(10, 50014, 1, State: 4, Durability: 19,
                SealState: 3, EquipmentQualitySeed: checked((uint)(987654 + index)), InstanceId: Guid.NewGuid());
            player.Character = (await harness.Store.SaveCharacterInventoryAsync(player.Account,
                player.Character.Id, 1000, [item, rejected], []))!;
            check(Inventory(player)[9] == item && Inventory(player)[10] == rejected,
                $"{player.Label}: seeded equipment identity and metadata survive initial persistence");
        }
        var instance = fixture.Instance(4200);
        foreach (var index in new[] { 2, 0, 3, 1 }) instance.AddMember(players[index].Member());
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _), "four active-drop members enter one run");
        check(instance.Members.Length == 4 && players.Select(p => p.Character.Id).Distinct().Count() == 4,
            "four active-drop members have distinct persisted character identities");

        // Catches relaxed seal policy before any debit, actor publication, or ACK is attempted.
        foreach (var player in players)
        {
            var original = player.Character;
            check(!DungeonItemDropPlanner.TryPlan(Inventory(player), 0, 10, 1, fixture.Items,
                    out _, out var failure, currentGold: 1000)
                    && failure == DungeonItemDropFailure.NotTradeable
                    && DungeonItemDropPlanner.GetErrorCode(failure) == 23,
                $"{player.Label}: sealing byte3 rejects active discard with native error23");
            var saved = (await harness.Store.GetCharactersAsync(player.Account))[0];
            check((saved.Inventory ?? []).SequenceEqual(original.Inventory ?? []) && saved.Gold == 1000
                    && player.Packets.IsEmpty && instance.GetRoomGroundItems(0, 0).Length == 0,
                $"{player.Label}: rejected seal3 preserves stored inventory and emits no success packets");
        }

        foreach (var (from, to) in new[] { (0, 3), (1, 2), (2, 1), (3, 0) })
        {
            Clear(players);
            var source = Inventory(players[from])[9];
            var ground = await DropAsync(players[from], 9, 1);
            check(ground is not null && ground.PreservedItem == source with { Slot = ushort.MaxValue }
                    && ground.RoutingSource == 0,
                $"{players[from].Label}->{players[to].Label}: active drop retains the complete equipment record");
            if (ground is null) continue;
            CheckDropPackets(players[from], ground, players, check);
            var dropDisk = await ReopenAsync(harness.Options);
            check(!(await dropDisk.GetCharactersAsync(players[from].Account))[0].Inventory!
                    .Any(item => item.InstanceId == source.InstanceId),
                $"{players[from].Label}: item debit exists on disk before pickup");
            var picked = await PickAsync(players[to], ground);
            check(picked, $"{players[to].Label}: another member commits the active-drop pickup");
            var reloaded = await ReopenAsync(harness.Options);
            var copies = new List<(int Player, CharacterItemRecord Item)>();
            for (var i = 0; i < players.Length; i++)
                copies.AddRange(((await reloaded.GetCharactersAsync(players[i].Account))[0].Inventory ?? [])
                    .Where(item => item.InstanceId == source.InstanceId).Select(item => (i, item)));
            check(copies.Count == 1 && copies[0].Player == to
                    && copies[0].Item with { Slot = source.Slot } == source,
                $"{players[from].Label}->{players[to].Label}: restart retains exactly one copy, reinforcement, durability, seal, quality seed and instance id");
            check(players.All(p => p.Packets.Count(packet => packet.ProtocolId == 86) == 1)
                    && players[to].Packets.Count(packet => packet.ProtocolId == 39) == 1
                    && players.Where(p => p != players[to]).All(p => p.Packets.All(packet => packet.ProtocolId != 39)),
                "direct pickup result is private to its winner; removal reaches all four members");
        }

        // Catches double awards from same-member duplicates and concurrent requests by other members.
        var raceSource = Inventory(players[0]).Values.Single(item => item.ItemId == 50014 && item.SealState == 1);
        Clear(players);
        var raceGround = await DropAsync(players[0], raceSource.Slot, 1);
        if (raceGround is not null)
        {
            var winners = await Task.WhenAll(Enumerable.Range(0, 32).Select(index =>
                Task.Run(async () => await PickAsync(players[index % 4], raceGround))));
            check(winners.Count(won => won) == 1 && !instance.TryGetGroundItem(raceGround.GroundId, 0, 0, out _),
                "32 concurrent duplicate requests from four members commit exactly one pickup");
            var reopened = await ReopenAsync(harness.Options);
            var copies = 0;
            foreach (var player in players)
            {
                player.Character = (await harness.Store.GetCharactersAsync(player.Account))[0];
                copies += ((await reopened.GetCharactersAsync(player.Account))[0].Inventory ?? [])
                    .Count(item => item.InstanceId == raceSource.InstanceId);
            }
            check(copies == 1 && players.Sum(p => p.Packets.Count(packet => packet.ProtocolId == 39)) == 1
                    && players.All(p => p.Packets.Count(packet => packet.ProtocolId == 86) == 1),
                "concurrent pickup has one stored equipment copy, one winner result and one removal per member");
        }

        // Catches UID/slot mixups and credit replay; 23 / 4 = 5, remainder 3 belongs to slot3.
        Clear(players);
        var gold = await DropAsync(players[0], 0, 23);
        check(gold is { IsGold: true, CountOrValue: 23, RoutingSource: 0 }
                && players[0].Character.Gold == 977, "A durably debits 23 gold before publication");
        if (gold is not null)
        {
            CheckDropPackets(players[0], gold, players, check);
            check(instance.TryBeginLootPickup(players[3].Character.Id, gold, false, [], _ => 0, out var decision)
                    && instance.TryClaimGoldPickup(players[3].Character.Id, gold, decision.PickupId, out _),
                "D reserves and claims the source0 gold entity");
            var members = instance.CaptureGoldEligibleMembers();
            var commits = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                harness.Store.CommitDungeonGoldDistributionAsync(decision.PickupId, members, 3, 23,
                    routingSource: gold.RoutingSource)));
            check(commits.All(commit => commit.Status == DungeonRewardLedgerStatus.Committed
                    && commit.Amounts.SequenceEqual(new uint[] { 5, 5, 5, 8 })),
                "eight repeated durable distribution calls retain slot shares 5/5/5/8");
            // Reload before consumption to prove the four credits, not only balances in memory, persisted.
            var reopened = await ReopenAsync(harness.Options);
            var balances = new[] { 982, 1005, 1005, 1008 };
            for (var i = 0; i < players.Length; i++)
            {
                var paid = await reopened.ConsumeDungeonGoldCreditsAsync(players[i].Character.Id);
                check(paid?.Gold == balances[i]
                        && await reopened.ConsumeDungeonGoldCreditsAsync(players[i].Character.Id) is null,
                    $"{players[i].Label}: durable gold credit pays {balances[i]} exactly once after restart");
            }
            var final = await ReopenAsync(harness.Options);
            var total = 0;
            foreach (var player in players)
                total += (await final.GetCharactersAsync(player.Account))[0].Gold;
            var replay = await final.CommitDungeonGoldDistributionAsync(decision.PickupId, members, 3, 23,
                routingSource: gold.RoutingSource);
            check(total == 4000 && replay.Status == DungeonRewardLedgerStatus.Committed
                    && !instance.TryClaimGoldPickup(players[3].Character.Id, gold, decision.PickupId, out _),
                "reopened four-member balances conserve 4000 gold and the claimed entity cannot pay twice");
            foreach (var player in players)
                check(await final.ConsumeDungeonGoldCreditsAsync(player.Character.Id) is null,
                    $"{player.Label}: replay after restart creates no second credit");
            File.Copy(harness.Options.DataPath, Path.Combine(runRoot, "final-state.json"));
        }

        // A committed item may remain in its old room, but cannot be picked in the next room or another instance.
        // Refresh after the reopened gold-credit store writes before using the original store again.
        var boundaryStore = await ReopenAsync(harness.Options);
        foreach (var player in players) player.Character = (await boundaryStore.GetCharactersAsync(player.Account))[0];
        var boundarySource = Inventory(players[1]).Values.First(item => item.SealState == 0);
        DungeonItemDropPlanner.TryPlan(Inventory(players[1]), 0, boundarySource.Slot, 1, fixture.Items,
            out var boundaryPlan, out _, currentGold: checked((uint)players[1].Character.Gold));
        Clear(players);
        var outsiderPackets = new ConcurrentQueue<GameServerPacket>();
        var other = fixture.Instance(4201);
        var outsider = Guid.NewGuid();
        other.AddMember(new(outsider, 999, 0, "other instance", packet =>
        { outsiderPackets.Enqueue(packet); return ValueTask.CompletedTask; },
            SendConditional: (packet, allowed) =>
            { if (allowed()) outsiderPackets.Enqueue(packet); return ValueTask.CompletedTask; }));
        other.TryStartRun(fixture.Layout, 0, 0, out _);
        var oldGround = await DungeonActiveDropTransaction.CommitAsync(instance, boundaryPlan, fixture.Items,
            service, players[1].Uid, 0, 0, 100, 120, async token =>
            {
                var saved = await SaveAsync(boundaryStore, players[1], boundaryPlan.Inventory, boundaryPlan.Gold, token);
                check(instance.TryMoveRoom(1, 0, out _, new ZeroDropRandomSource()),
                    "four-member run changes rooms while active-drop persistence is pending");
                return saved;
            }, players[1].Send, players[1].SendConditional);
        check(oldGround is not null && players[1].Packets.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 50 })
                && players.Where(p => p != players[1]).All(p => p.Packets.IsEmpty) && outsiderPackets.IsEmpty,
            "committed old-room debit sends only the truthful private ACK, with no stale actor in either instance");
        if (oldGround is not null)
        {
            var foreignGround = other.AddPlayerDroppedGroundItem(boundarySource, 999, 0, 0, 0, 0);
            while (foreignGround.GroundId < oldGround.GroundId)
                foreignGround = other.AddPlayerDroppedGroundItem(boundarySource, 999, 0, 0, 0, 0);
            check(foreignGround.GroundId == oldGround.GroundId
                    && !instance.TryBeginLootPickup(players[0].Character.Id, oldGround, false, [], _ => 0, out _)
                    && !instance.TryClaimGroundItem(oldGround.GroundId, 0, 0, out _, oldGround)
                    && !other.TryBeginLootPickup(outsider, oldGround, false, [], _ => 0, out _)
                    && !other.TryClaimGroundItem(oldGround.GroundId, 0, 0, out _, oldGround)
                    && other.TryGetGroundItem(foreignGround.GroundId, 0, 0, out _),
                "old-room pickup is denied; same-number ground identity in another instance cannot be stolen");
        }
        Console.WriteLine($"Active drop four-member coverage: {assertions} assertions");
        Console.WriteLine($"Active drop four-member artifacts: {runRoot}");

        async Task<DungeonGroundItem?> DropAsync(Player player, ushort slot, uint count)
        {
            if (!DungeonItemDropPlanner.TryPlan(Inventory(player), 0, slot, count, fixture.Items,
                    out var plan, out _, currentGold: checked((uint)player.Character.Gold)))
            {
                check(false, $"{player.Label}: eligible item/gold produces an active-drop plan");
                return null;
            }
            return await DungeonActiveDropTransaction.CommitAsync(instance, plan, fixture.Items, service,
                player.Uid, 0, 0, 100, 120,
                token => SaveAsync(harness.Store, player, plan.Inventory, plan.Gold, token),
                player.Send, player.SendConditional);
        }

        async Task<bool> PickAsync(Player player, DungeonGroundItem item)
        {
            if (!instance.TryBeginLootPickup(player.Character.Id, item, false, [], _ => 0, out var decision)) return false;
            if (decision.WinnerCharacterId != player.Character.Id
                || !DungeonPickupPlanner.TryPlanItem(Inventory(player), item, fixture.Items, uint.MaxValue,
                    out var plan, out _))
            {
                instance.ReleaseLootPickup(item, decision.PickupId);
                return false;
            }
            if (!instance.TryClaimGroundItem(item.GroundId, 0, 0, out _, item, decision.PickupId)) return false;
            if (!await SaveAsync(harness.Store, player, plan.Inventory, checked((uint)player.Character.Gold)))
            {
                instance.TryReAddGroundItem(item);
                return false;
            }
            await service.BroadcastInventoryItemPickupAsync(instance, item.GroundId, player.Uid, player.Uid,
                plan.DestinationSlot, null, player.Send);
            await service.BroadcastPickupRemovalAsync(instance, item.GroundId, player.Uid, item.ItemId, player.Send);
            return true;
        }
    }

    private static Dictionary<ushort, CharacterItemRecord> Inventory(Player player) =>
        (player.Character.Inventory ?? []).ToDictionary(item => item.Slot);

    private static async Task<bool> SaveAsync(JsonGameStore store, Player player,
        Dictionary<ushort, CharacterItemRecord> inventory, uint gold, CancellationToken token = default)
    {
        var saved = await store.SaveCharacterItemSpacesAsync(player.Account, player.Character.Id,
            checked((int)gold), inventory.Values, player.Character.Equipment ?? [], player.Character.Warehouse ?? [],
            token, avatarInventory: player.Character.AvatarInventory, creatureInventory: player.Character.CreatureInventory);
        if (saved is null) return false;
        player.Character = saved;
        return true;
    }

    private static async Task<JsonGameStore> ReopenAsync(ServerOptions options)
    {
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        return store;
    }

    private static void Clear(Player[] players)
    {
        foreach (var player in players) player.Packets.Clear();
    }

    private static void CheckDropPackets(Player sender, DungeonGroundItem ground, Player[] players,
        Action<bool, string> check)
    {
        foreach (var player in players)
        {
            var actors = player.Packets.Where(p => p.Type == 0 && p.ProtocolId == 40).ToArray();
            check(actors.Length == 1 && actors[0].Payload.Length == 17
                    && BinaryPrimitives.ReadUInt16LittleEndian(actors[0].Payload) == sender.Uid
                    && BinaryPrimitives.ReadUInt16LittleEndian(actors[0].Payload.AsSpan(6)) == ground.GroundId,
                $"{sender.Label}->{player.Label}: one actor identifies actual dropper UID and shared ground id");
            check(player.Packets.Count(p => p.Type == 1 && p.ProtocolId == 50) == (player == sender ? 1 : 0),
                $"{player.Label}: inventory debit ACK remains private to {sender.Label}");
        }
    }

    private sealed class Player(string label, string account, ushort uid, byte slot)
    {
        internal string Label => label;
        internal string Account => account;
        internal ushort Uid => uid;
        internal CharacterRecord Character = null!;
        internal ConcurrentQueue<GameServerPacket> Packets = new();
        internal ValueTask Send(GameServerPacket packet) { Packets.Enqueue(packet); return ValueTask.CompletedTask; }
        internal ValueTask SendConditional(GameServerPacket packet, Func<bool> allowed) =>
            allowed() ? Send(packet) : ValueTask.CompletedTask;
        internal DungeonInstanceMember Member() => new(Character.Id, uid, slot, Character.Name, Send,
            AccountName: account, SendConditional: SendConditional);
    }

    private sealed class FourFixture
    {
        internal ItemCatalog Items { get; }
        internal DungeonCatalog Dungeons { get; }
        internal DungeonLayout Layout { get; }

        internal FourFixture(string root)
        {
            foreach (var folder in new[] { "equipment", "dungeon", "map" }) Directory.CreateDirectory(Path.Combine(root, folder));
            File.WriteAllText(Path.Combine(root, "equipment/equipment.lst"), "50011 `free.equ`\n50014 `sealed.equ`\n");
            foreach (var (file, attach) in new[] { ("free", "free"), ("sealed", "sealing") })
                File.WriteAllText(Path.Combine(root, $"equipment/{file}.equ"),
                    $"[name] `{file}`\n[equipment type] `[weapon]`\n[attach type] `[{attach}]`\n[rarity] 1\n[weight] 1\n[minimum level] 1\n[durability] 30\n");
            File.WriteAllText(Path.Combine(root, "dungeon/dungeon.lst"), "14 `test.dgn`\n");
            File.WriteAllText(Path.Combine(root, "dungeon/test.dgn"),
                "[minimum level] 1\n[basis level] 1\n[maze info]\n[size] 2 1\n[greed]\n`PP`\n[start map]\n0 0\n[/start map]\n[boss map]\n1 0\n[/boss map]\n");
            File.WriteAllText(Path.Combine(root, "map/map.lst"), "1 `start.map`\n2 `boss.map`\n");
            foreach (var file in new[] { "start", "boss" })
                File.WriteAllText(Path.Combine(root, $"map/{file}.map"),
                    $"[dungeon]\n14\n[/dungeon]\n[type] `[{(file == "boss" ? "boss" : "normal")}]`\n[greed]\n`P`\n");
            var options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            Items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
            Dungeons = new DungeonCatalog(scripts,
                new ObjectDropGenerator(options, new ObjectDropCatalog(scripts, NullLogger<ObjectDropCatalog>.Instance),
                    new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance)), NullLogger<DungeonCatalog>.Instance);
            if (!Dungeons.TryCreateLayout(14, out var layout, random: new ZeroDropRandomSource()))
                throw new InvalidOperationException("four-member fixture layout is missing");
            Layout = layout;
        }

        internal DungeonInstance Instance(ushort id) => new(id, Guid.Empty, Dungeons, Items);
    }
}
