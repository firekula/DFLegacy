using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static ChapterTwoItemUseSmokeTests;

internal static partial class CompoundItemSmokeTests
{
    internal static async Task CheckChapterTwoWireAsync(Action<bool, string> check, string pvfPath)
    {
        const string user = "recipewire";
        const int startingGold = 100_000;
        const ushort goldSlot = 0;
        const ushort clothSlot = 73;
        const ushort roughClothItem = 3134;
        const ushort blueprintSlot = 41;
        const ushort blueprintItem = 5538;
        const ushort materialSlot = 74;
        const ushort materialItem = 3216;
        const ushort braceletItem = 22107;
        var directory = Directory.CreateTempSubdirectory("dflegacy-c2-recipe-");
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
            var recipes = new CompoundCatalog(scripts, NullLogger<CompoundCatalog>.Instance);
            if (!recipes.TryGetDefinition(5057, out var cloth)
                || cloth.GoldCost != 50 || cloth.RecipeType != 0 || cloth.RequiredSkills.Count != 0
                || !cloth.Ingredients.SequenceEqual([new(3030, 1)]) || !cloth.Results.SequenceEqual([new(roughClothItem, 1)])
                || !recipes.TryGetDefinition(blueprintItem, out var blueprint)
                || blueprint.GoldCost != 0 || blueprint.RecipeType != 1 || blueprint.RequiredSkills.Count != 0
                || !blueprint.Ingredients.SequenceEqual([new(materialItem, 165)]) || !blueprint.Results.SequenceEqual([new(braceletItem, 1)]))
            {
                check(false, "the Chapter Two PVF supplies the NPC cloth recipe and consumable bracelet blueprint");
                return;
            }

            int injectedFaults = 0;
            bool failNextWrite = false;
            var replacement = new AtomicSnapshotReplacement((source, target) =>
            {
                if (Volatile.Read(ref failNextWrite))
                {
                    Volatile.Write(ref failNextWrite, false);
                    Interlocked.Increment(ref injectedFaults);
                    throw new IOException("Injected recipe write failure before replacing the snapshot.");
                }
                File.Move(source, target, overwrite: true);
            }, Task.Delay);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, replacement, items);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new(user, "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync(user, new("RecipeWire", Level: 60))).Character!;
            var zeroQualityInstance = Guid.NewGuid();
            await store.SaveCharacterItemSpacesAsync(user, character.Id, startingGold,
                [new(9, 12112, 0, Durability: 25, InstanceId: zeroQualityInstance),
                    new(blueprintSlot, blueprintItem, 1), new(clothSlot, 3030, 2), new(materialSlot, materialItem, 165)],
                [], []);

            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
            await using (var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items))
            {
                var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
                await connection.LoginAsync(user, character.Slot);
                await connection.EnableChapterTwoAsync();

                const string npcRecipe = "2200c1130102000100000000000000000000";
                var earlyBag = false;
                var madeCloth = await Send(npcRecipe, packet =>
                    earlyBag |= packet is { Type: 0, ProtocolId: GameProtocolEngine.ItemListNotification }
                        && packet.Payload[0] == 0);
                var expectedCloth = Convert.FromHexString("01020000003c860100004900000000000149003e0c02000000000000");
                check(madeCloth.Payload.SequenceEqual(expectedCloth),
                    $"encrypted stock NPC recipe consumes cotton and gold and returns native rows (reply {Convert.ToHexString(madeCloth.Payload)})");
                check(!earlyBag, "CMD27 uses the original source slots before refreshing the client bag");
                if (madeCloth.Payload[0] == 1)
                {
                    await CheckClothBag();
                    var replay = await Send(npcRecipe);
                    check(replay.Payload.SequenceEqual(Convert.FromHexString("010000")),
                        "a repeated C2 sequence acknowledges with zero deltas and does not touch already consumed source slots");
                    if (replay.Payload[0] == 1) await CheckClothBag();
                    var conflict = await Send("2200c1130101000100000000000000000000");
                    check(conflict.Payload.SequenceEqual(Convert.FromHexString("0002")),
                        "the same recipe sequence with changed fields is rejected");
                }

                var corruptPadding = await Send("230029000001000100000000000000000001");
                check(corruptPadding.Payload.SequenceEqual(Convert.FromHexString("0013")),
                    "CMD27 rejects a nonzero tail beyond the native six fields");
                var overpadding = await Send("23002900000100010000000000000000000000000000000000000000000000000000");
                check(overpadding.Payload.SequenceEqual(Convert.FromHexString("0013")),
                    "CMD27 rejects a second all-zero AES block");

                var forgedDirectBlueprint = await Send("2300a2150101000100000000000000000000");
                check(forgedDirectBlueprint.Payload.SequenceEqual(Convert.FromHexString("0013")),
                    "a consumable design cannot be crafted directly by spoofing an NPC recipe ID");
                if (forgedDirectBlueprint.Payload[0] == 1)
                {
                    await ReadBag();
                    return;
                }
                await connection.SendAsync(connection.Command(GameProtocolEngine.CompoundItemCommand,
                    0xa2, 0x15, 1, 1, 0, 1));
                var wrongCipherBlueprint = await connection.ReadAsync(1, GameProtocolEngine.CompoundItemCommand);
                check(wrongCipherBlueprint.Payload.SequenceEqual(Convert.FromHexString("0013")),
                    "a negotiated C2 session enforces the recipe source when the compatibility cipher decodes the command");
                if (wrongCipherBlueprint.Payload[0] == 1)
                {
                    await ReadBag();
                    return;
                }

                const string slottedRecipe = "240029000001000100000000000000000000";
                Volatile.Write(ref failNextWrite, true);
                GameServerPacket? denied = null;
                try { denied = await Send(slottedRecipe); }
                catch (IOException error) { check(false, $"the recipe write exception must not disconnect the session ({error.Message})"); }
                finally { Volatile.Write(ref failNextWrite, false); }
                check(Volatile.Read(ref injectedFaults) == 1
                    && denied is not null && denied.Payload.SequenceEqual(Convert.FromHexString("0013"))
                    && (await store.GetCharacterAsync(user, character.Id)) is { } beforeRetry
                    && beforeRetry.Inventory?.Any(item => item.ItemId == blueprintItem) == true
                    && beforeRetry.Inventory?.Any(item => item.ItemId == braceletItem) == false,
                    "a failed snapshot leaves the blueprint and materials intact without closing the channel");
                if (denied is null) return;

                var madeBracelet = await Send(slottedRecipe);
                var durability = items.GetInitialDurability(braceletItem);
                check(madeBracelet.Payload.SequenceEqual(Convert.FromHexString(
                    $"010200290000000000004a0000000000010a005b560100000000{(byte)durability:x2}{(byte)(durability >> 8):x2}")),
                    $"the same slotted request succeeds after a transient write failure (reply {Convert.ToHexString(madeBracelet.Payload)})");
                if (madeBracelet.Payload[0] == 1)
                {
                    var bag = await ReadBag();
                    check(FindItem(bag, 10, braceletItem) == 1
                        && FindItem(bag, blueprintSlot, blueprintItem) is null
                        && FindItem(bag, materialSlot, materialItem) is null,
                        "the post-craft bag installs the bracelet and removes both stacks");
                }

                async Task<GameServerPacket> Send(string hex, Action<GameServerPacket>? observe = null)
                {
                    await connection.SendAsync(EncryptChapterTwoCommand(GameProtocolEngine.CompoundItemCommand, hex));
                    return await connection.ReadAsync(1, GameProtocolEngine.CompoundItemCommand, observe: observe);
                }
                async Task<GameServerPacket> ReadBag()
                {
                    GameServerPacket bag;
                    do { bag = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification); }
                    while (bag.Payload[0] != 0);
                    return bag;
                }
                async Task CheckClothBag()
                {
                    var bag = await ReadBag();
                    check(FindItem(bag, goldSlot, 0) == startingGold - 100
                        && FindItem(bag, clothSlot, roughClothItem) == 2
                        && FindItem(bag, clothSlot, 3030) is null,
                        "the authoritative C2 snapshot claims the consumed cotton slot for its cloth result exactly once");
                }
            }

            var reopened = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await reopened.InitializeAsync();
            var durable = await reopened.GetCharacterAsync(user, character.Id);
            check(durable is { Gold: startingGold - 100, Inventory: { } bagAfterRestart }
                && bagAfterRestart.Any(item => item is { Slot: clothSlot, ItemId: roughClothItem, CountOrValue: 2 })
                && bagAfterRestart.Any(item => item.Slot == 10 && item.ItemId == braceletItem && item.InstanceId != Guid.Empty)
                && bagAfterRestart.Any(item => item is { Slot: 9, ItemId: 12112, CountOrValue: 1, EquipmentQualitySeed: 0 }
                    && item.InstanceId == zeroQualityInstance)
                && bagAfterRestart.All(item => item.ItemId is not (3030 or blueprintItem or materialItem)),
                "both crafts persist their results, deductions, and unrelated zero-quality equipment after reopening the store");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previousCipher);
            directory.Delete(recursive: true);
        }
    }
}
