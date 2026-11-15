using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoItemUseSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check, string pvfPath)
    {
        const string user = "itemwire";
        const ushort targetItem = 12112;
        const ushort targetSlot = 9;
        const ushort braceletItem = 22107;
        const ushort braceletSlot = 10;
        const ushort coalSlot = 73;
        const ushort braceletCoalSlot = 74;
        const ushort qualityBoxSlot = 44;
        const ushort potionSlot = 45;
        const ushort potionItem = 1055;
        const ushort growthPotionSlot = 46;
        const ushort growthPotionItem = 7440;
        const int startingGold = 3_000_000;
        const int pantsGoldQuote = 40_000;
        const int braceletGoldQuote = 252_000;
        const uint startingCoins = 10;
        var targetInstance = Guid.Parse("b765eb81-41ad-4d85-aaf6-ee506896840a");
        var warehouseInstance = Guid.Parse("6af98d80-069b-4ff9-85c3-ad0516925e7a");
        var directory = Directory.CreateTempSubdirectory("dflegacy-c2-item-use-");
        var previousCipher = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");

        try
        {
            var options = new ServerOptions
            {
                ScriptPvfPath = pvfPath,
                DataPath = Path.Combine(directory.FullName, "state.json"),
                SkipUnsupportedChapterTwoCashContainers = true,
                SeedTestAccount = false,
                Channel = new() { Port = 47001 }
            };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
            var reinforcement = new EquipmentReinforcementCatalog(scripts, NullLogger<EquipmentReinforcementCatalog>.Instance);
            if (!items.TryGetDefinition(targetItem, out var pants)
                || !reinforcement.TryGetCost(0, pants, out var cost)
                || cost is not { MaterialItemId: 3171, MaterialCount: 1, FailureWeight: 0 })
            {
                check(false, "the Chapter Two PVF supplies a level-zero target and a guaranteed +1 costing one furnace coal");
                return;
            }

            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new(user, "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync(user, new("ItemWire", Level: 60))).Character!;
            await store.SaveCharacterItemSpacesAsync(user, character.Id, startingGold,
                [new(1, 1, startingCoins), new(targetSlot, targetItem, 0, Durability: 25, InstanceId: targetInstance),
                    new(braceletSlot, braceletItem, 1, EquipmentQualitySeed: 0),
                    new(41, 42, 2), new(42, 7180, 1), new(43, 7181, 1), new(qualityBoxSlot, 15, 1), new(potionSlot, potionItem, 2),
                    new(growthPotionSlot, growthPotionItem, 2),
                    new(coalSlot, 3171, 1), new(braceletCoalSlot, 3171, 1)],
                [], [new(0, targetItem, 0, Durability: 25, InstanceId: warehouseInstance)]);

            CharacterRecord before;
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
            await using (var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items))
            {
                var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
                await connection.LoginAsync(user, character.Slot);
                await connection.EnableChapterTwoAsync();
                before = (await store.GetCharacterAsync(user, character.Id))!;
                var projectedTarget = connection.MainList is { } firstBag && FindItem(firstBag, targetSlot, targetItem, 12) == 0;
                check(projectedTarget
                    && before.Inventory?.SingleOrDefault(item => item.Slot == targetSlot) is
                        { ItemId: targetItem, State: 0, CountOrValue: 1, EquipmentQualitySeed: 0 } target
                    && target.InstanceId == targetInstance,
                    "character selection keeps a legacy zero-quality bag target and its identity in memory, on wire, and on disk");
                check(before.Warehouse?.SingleOrDefault() is { Slot: 0, ItemId: targetItem, CountOrValue: 1, EquipmentQualitySeed: 0 } warehouseItem
                    && warehouseItem.InstanceId == warehouseInstance,
                    "character selection keeps a legacy zero-quality warehouse item and its identity");
                var beforeCoins = before.Inventory?.SingleOrDefault(item => item.Slot == 1)?.CountOrValue;
                check(beforeCoins == startingCoins, $"character selection loads the seeded coin balance ({beforeCoins})");

                var malformed = await SendAndRead(GameProtocolEngine.UpgradeItemCommand, "14000900502f490000000000000000000001");
                check(malformed.Payload.SequenceEqual(Convert.FromHexString("0004"))
                    && (await store.GetCharacterAsync(user, character.Id))?.Gold == startingGold,
                    "CMD83 rejects nonzero data after the native fields without deducting gold");

                var overpadded = await SendAndRead(GameProtocolEngine.UpgradeItemCommand,
                    "14000900502f49000000000000000000000000000000000000000000000000000000");
                check(overpadded.Payload.SequenceEqual(Convert.FromHexString("0004")),
                    "CMD83 rejects a forged second all-zero AES block");

                var earlyInventory = false;
                var reinforced = await SendAndRead(GameProtocolEngine.UpgradeItemCommand, "15000900502f490000000000000000000000",
                    packet => earlyInventory |= packet is { Type: 0, ProtocolId: GameProtocolEngine.ItemListNotification }
                        && packet.Payload[0] == 0);
                var reinforcedOk = reinforced.Payload.SequenceEqual(Convert.FromHexString("0149000000000000010900"));
                check(reinforcedOk, $"native encrypted CMD83 reinforces the target using its final coal (reply {Convert.ToHexString(reinforced.Payload)})");
                check(!earlyInventory, "CMD83 acknowledges material consumption before replacing the client bag");
                if (reinforcedOk)
                {
                    var bag = await ReadBag();
                    check(FindItem(bag, 0, 0) == startingGold - pantsGoldQuote
                        && FindItem(bag, coalSlot, 3171) is null,
                        $"the pants cost uses the native 40,000-gold quote (remaining {FindItem(bag, 0, 0)}) and removes the final coal");
                }

                // The negotiated session can also decode compatibility frames.
                // Its pricing and main-bag restrictions stay on Chapter Two rules.
                var earlyBraceletBag = default(GameServerPacket);
                await connection.SendAsync(connection.Command(GameProtocolEngine.UpgradeItemCommand,
                    0x0a, 0, 0x5b, 0x56, 0x4a, 0));
                var reinforcedBracelet = await connection.ReadAsync(1, GameProtocolEngine.UpgradeItemCommand,
                    observe: packet =>
                    {
                        if (packet is { Type: 0, ProtocolId: GameProtocolEngine.ItemListNotification }
                            && packet.Payload[0] == 0) earlyBraceletBag = packet;
                    });
                var braceletOk = reinforcedBracelet.Payload.SequenceEqual(Convert.FromHexString("014a000000000000010a00"));
                check(braceletOk, "the actual Bracelet 22107 from the original Kelly dialog can be reinforced");
                check(earlyBraceletBag is null, "CMD83 uses C2 reply ordering after the C2 handshake even for a compatibility frame");
                if (braceletOk)
                {
                    var bag = earlyBraceletBag ?? await ReadBag();
                    check(FindItem(bag, 0, 0) == startingGold - pantsGoldQuote - braceletGoldQuote
                        && FindItem(bag, braceletCoalSlot, 3171) is null,
                        $"Kelly charges exactly the 252,000-gold bracelet quote (remaining {FindItem(bag, 0, 0)}) and one coal");
                }

                var adjusted = await SendAndRead(GameProtocolEngine.ResetItemAttributeCommand, "16000900502f2c0000000000000000000000");
                var adjustedOk = adjusted.Payload.SequenceEqual(Convert.FromHexString("012c00000000000200"));
                check(adjustedOk, $"native encrypted CMD84 consumes one quality box and replies with operation 2 (reply {Convert.ToHexString(adjusted.Payload)})");
                if (adjustedOk)
                {
                    var targetUpdate = await connection.ReadAsync(0, GameProtocolEngine.UpdateItemListNotification);
                    check(FindItem(targetUpdate, targetSlot, targetItem) is > 0,
                        "CMD84 publishes the changed equipment quality after its reply");
                }

                var malformedExchange = await SendAndRead(GameProtocolEngine.IncreaseStatusCommand,
                    "170029000000000000000000000000000001");
                check(malformedExchange.Payload.SequenceEqual(Convert.FromHexString("0011")),
                    "native CMD32 rejects nonzero data after the source slot");

                uint coins = startingCoins;
                foreach (var (slot, source, count, grant, hex) in new (ushort, ushort, uint, uint, string)[]
                {
                    (41, 42, 1, 1, "180029000000000000000000000000000000"),
                    (42, 7180, 0, 1, "19002a000000000000000000000000000000"),
                    (43, 7181, 0, 5, "1a002b000000000000000000000000000000"),
                    (41, 42, 0, 1, "1b0029000000000000000000000000000000")
                })
                {
                    var expectedReply = Convert.FromHexString("010000ff000000000000");
                    BinaryPrimitives.WriteUInt16LittleEndian(expectedReply.AsSpan(1), slot);
                    var exchanged = await SendAndRead(GameProtocolEngine.IncreaseStatusCommand, hex);
                    var exchangedOk = exchanged.Payload.SequenceEqual(expectedReply);
                    check(exchangedOk, $"native CMD32 consumes one item {source} with a neutral item ack (reply {Convert.ToHexString(exchanged.Payload)})");
                    coins += grant;
                    if (exchangedOk)
                    {
                        var bag = await ReadBag();
                        var balance = FindItem(bag, 1, 1);
                        var remaining = FindItem(bag, slot, source);
                        check(balance == coins && remaining == (count == 0 ? null : count),
                            $"item {source} grants exactly {grant} virtual coins and leaves {count} source items (balance {balance}, remaining {remaining})");
                    }
                }

                var exhausted = await SendAndRead(GameProtocolEngine.IncreaseStatusCommand, "1c0029000000000000000000000000000000");
                check(exhausted.Payload.SequenceEqual(Convert.FromHexString("0011")),
                    "repeating CMD32 on an exhausted slot cannot grant more coins");

                var potionInTown = await SendAndRead(GameProtocolEngine.UseStackableItemCommand,
                    "1d002d000078563412000000000000000000");
                check(potionInTown.Payload.SequenceEqual(Convert.FromHexString("001100")),
                    "ordinary healing items keep their town use restriction");

                await connection.SendAsync(EncryptChapterTwoCommand(GameProtocolEngine.SelectDungeonCommand,
                    "1e0010270000000000000000000000000000"));
                await connection.ReadAsync(0, GameProtocolEngine.StartMapNotification);
                var potionInDungeon = await SendAndRead(GameProtocolEngine.UseStackableItemCommand,
                    "1f002d000078563412000000000000000000");
                check(potionInDungeon.Payload.SequenceEqual(Convert.FromHexString("012d0000")),
                    $"stock CMD47 still accepts a padded ordinary healing item in a dungeon (reply {Convert.ToHexString(potionInDungeon.Payload)})");

                var growth = await SendAndRead(GameProtocolEngine.UseStackableItemCommand,
                    "20002e000078563412000000000000000000");
                check(growth.Payload.SequenceEqual(Convert.FromHexString("012e0000")),
                    "C2 CMD47 acknowledges a maintained growth potion");
                var growthNotices = new List<GameServerPacket>();
                await connection.EnableChapterTwoAsync(growthNotices.Add);
                check(HasGrowthTimers(growthNotices), "C2 supplies both NOTI181 and NOTI182 for the active growth potion");
                var repeat = await SendAndRead(GameProtocolEngine.UseStackableItemCommand,
                    "21002e000078563412000000000000000000");
                check(repeat.Payload.Length > 0 && repeat.Payload[0] == 0
                    && (await store.GetCharacterAsync(user, character.Id))?.Inventory?
                        .SingleOrDefault(item => item.Slot == growthPotionSlot)?.CountOrValue == 1,
                    "C2 refuses a second growth potion without taking another potion");

                var liveTimer = (await store.GetCharacterAsync(user, character.Id))?.ChapterTwoGrowthPotion;
                await connection.SendAsync(EncryptChapterTwoCommand(GameProtocolEngine.DieCharacterCommand, "2200"));
                await connection.ReadAsync(0, GameProtocolEngine.DieStateNotification);
                var useWhileDead = await SendAndRead(GameProtocolEngine.UseStackableItemCommand,
                    "23002e000078563412000000000000000000");
                await connection.ReadAsync(0, GameProtocolEngine.EnterGameWorldCompleteNotification,
                    TimeSpan.FromSeconds(15));
                var returned = await store.GetCharacterAsync(user, character.Id);
                check(liveTimer is not null && returned?.ChapterTwoGrowthPotion == liveTimer
                    && useWhileDead.Payload is [0, ..]
                    && returned.Inventory?.SingleOrDefault(item => item.Slot == growthPotionSlot)?.CountOrValue == 1,
                    "C2 death and automatic town return preserve both original potion deadlines and the unused dose");

                async Task<GameServerPacket> SendAndRead(byte command, string hex, Action<GameServerPacket>? observe = null)
                {
                    await connection.SendAsync(EncryptChapterTwoCommand(command, hex));
                    return await connection.ReadAsync(1, command, observe: observe);
                }

                async Task<GameServerPacket> ReadBag()
                {
                    GameServerPacket packet;
                    do { packet = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification); }
                    while (packet.Payload[0] != 0);
                    return packet;
                }
            }

            var reopened = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await reopened.InitializeAsync();
            var durable = await reopened.GetCharacterAsync(user, character.Id);
            await using (var restoredServices = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, reopened, items))
            {
                var entrance = ActivatorUtilities.CreateInstance<EntranceService>(restoredServices);
                await using var restored = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
                var notices = new List<GameServerPacket>();
                await restored.LoginAsync(user, character.Slot, chapterTwo: true, observe: notices.Add);
                await restored.EnableChapterTwoAsync(notices.Add);
                check(HasGrowthTimers(notices), "C2 restores both growth potion timers after the store reopens and the original selection flow repeats");
            }

            // Move the deadlines in this isolated fixture back in time so the
            // original two-hour expiry can be exercised without a real-time wait.
            var state = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            var actor = state["Accounts"]!.AsArray()
                .SelectMany(account => account!["Characters"]!.AsArray())
                .Single(entry => entry!["Id"]!.GetValue<Guid>() == character.Id)!;
            var elapsed = DateTimeOffset.UtcNow.AddSeconds(-10);
            actor["ChapterTwoGrowthPotion"] =
                JsonSerializer.SerializeToNode(new ChapterTwoGrowthPotionState(elapsed, elapsed));
            await File.WriteAllTextAsync(options.DataPath, state.ToJsonString());
            var expiredStore = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await expiredStore.InitializeAsync();
            await using (var expiredServices = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, expiredStore, items))
            {
                var entrance = ActivatorUtilities.CreateInstance<EntranceService>(expiredServices);
                await using var restored = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
                var notices = new List<GameServerPacket>();
                await restored.LoginAsync(user, character.Slot, chapterTwo: true, observe: notices.Add);
                await restored.EnableChapterTwoAsync(notices.Add);
                check(!notices.Any(packet => packet.Type == 0 && packet.ProtocolId is 181 or 182),
                    "C2 does not republish elapsed potion cooldown or effect on selection");
                await restored.SendAsync(EncryptChapterTwoCommand(GameProtocolEngine.SelectDungeonCommand,
                    "240010270000000000000000000000000000"));
                await restored.ReadAsync(0, GameProtocolEngine.StartMapNotification);
                await restored.SendAsync(EncryptChapterTwoCommand(GameProtocolEngine.UseStackableItemCommand,
                    "25002e000078563412000000000000000000"));
                var newPotion = await restored.ReadAsync(1, GameProtocolEngine.UseStackableItemCommand);
                notices.Clear();
                await restored.EnableChapterTwoAsync(notices.Add);
                var renewed = await expiredStore.GetCharacterAsync(user, character.Id);
                check(newPotion.Payload is [1, 46, 0, 0] && HasGrowthTimers(notices)
                    && renewed?.Inventory?.Any(item => item.Slot == growthPotionSlot) == false,
                    "C2 accepts the remaining potion after expiry and publishes a fresh timer pair");
            }

            bool HasGrowthTimers(IEnumerable<GameServerPacket> packets)
            {
                return new byte[] { 181, 182 }.All(protocol => packets.Any(packet =>
                    packet.Type == 0 && packet.ProtocolId == protocol
                    && packet.Payload.Length == 9 && packet.Payload[0] == 1
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(1)) == growthPotionItem
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(5)) is > 0 and <= 7200));
            }
            check(durable?.Gold == startingGold - pantsGoldQuote - braceletGoldQuote
                && durable.Inventory?.SingleOrDefault(item => item.Slot == targetSlot) is { State: 1, EquipmentQualitySeed: > 0 } durableTarget
                && durableTarget.InstanceId == targetInstance
                && durable.Inventory?.SingleOrDefault(item => item.Slot == braceletSlot) is { ItemId: braceletItem, State: 1 }
                && durable.Inventory?.Any(item => item.ItemId is 15 or 3171) == false,
                "reinforcement and the grade change survive reopening the store with both materials consumed");
            var durableCoins = durable?.Inventory?.SingleOrDefault(item => item.Slot == 1);
            check(durableCoins is { ItemId: 1, CountOrValue: 18 }
                && durable is not null && durable.Inventory is not null
                && durable.Inventory.Any(item => item.ItemId is 42 or 7180 or 7181) == false
                && durable.Sp == before.Sp && durable.BonusSp == before.BonusSp,
                $"all exchanges survive reopening the store without modifying SP (balance {durableCoins?.CountOrValue}, SP {durable?.Sp}/{before.Sp}, bonus SP {durable?.BonusSp}/{before.BonusSp})");
            check(durable?.Inventory?.SingleOrDefault(item => item.Slot == potionSlot) is
                { ItemId: potionItem, CountOrValue: 1 }, "only the successful dungeon use spends a healing item");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previousCipher);
            directory.Delete(recursive: true);
        }
    }

    internal static byte[] EncryptChapterTwoCommand(byte command, string hex)
    {
        var plain = Convert.FromHexString(hex);
        var wire = plain.ToArray();
        using var aes = Aes.Create();
        aes.Key = Convert.FromHexString("46494A4946474546444A44404A424758");
        aes.EncryptEcb(plain.AsSpan(2), wire.AsSpan(2), PaddingMode.None);
        return new PacketFrame(1, command, Crc32.Compute(plain), wire).Encode();
    }

    internal static uint? FindItem(GameServerPacket packet, ushort slot, ushort itemId, int entryLength = 14)
    {
        var body = packet.Payload.AsSpan();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(body[1..]);
        for (var index = 0; index < count; index++)
        {
            var item = body.Slice(3 + index * entryLength, entryLength);
            if (BinaryPrimitives.ReadUInt16LittleEndian(item) == slot
                && BinaryPrimitives.ReadUInt16LittleEndian(item[2..]) == itemId)
                return BinaryPrimitives.ReadUInt32LittleEndian(item[4..]);
        }

        return null;
    }
}
