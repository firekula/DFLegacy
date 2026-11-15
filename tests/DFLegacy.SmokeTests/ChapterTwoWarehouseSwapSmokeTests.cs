using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoWarehouseSwapSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-warehouse-swap-");
        var previousTrial = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        try
        {
            var stackables = root.CreateSubdirectory("stackable");
            File.WriteAllText(Path.Combine(stackables.FullName, "stackable.lst"),
                "1055 `potion.stk`\n3037 `material.stk`\n3176 `heavy.stk`\n1085 `heavy-potion.stk`\n");
            foreach (var (file, type, weight) in new[] {
                ("potion", "usable", 1), ("material", "material", 1),
                ("heavy", "material", 500000), ("heavy-potion", "usable", 500000) })
                File.WriteAllText(Path.Combine(stackables.FullName, file + ".stk"),
                    $"[name] `{file}`\n[stackable type] `[{type}]`\n[attach type] `[free]`\n[stack limit] 1000\n[weight] {weight}\n");
            var equipment = root.CreateSubdirectory("equipment");
            File.WriteAllText(Path.Combine(equipment.FullName, "equipment.lst"), "26055 `sword.equ`\n");
            File.WriteAllText(Path.Combine(equipment.FullName, "sword.equ"),
                "[name] `Sword`\n[equipment type] `[weapon]`\n[durability] 50\n[weight] 10\n");

            CharacterItemRecord material = new(73, 3037, 495, State: 2);
            CharacterItemRecord potion = new(1, 1055, 1000);
            CharacterItemRecord sword = new(1, 26055, 1, State: 1, Durability: 33,
                SealState: 1, EquipmentQualitySeed: 987, InstanceId: Guid.NewGuid());
            var full = Enumerable.Range(41, 32).Concat(Enumerable.Range(3, 6))
                .Select(slot => new CharacterItemRecord((ushort)slot, 1055, 1000)).ToArray();
            await RunCase("material-potion", [material], potion, true, true);
            await RunCase("merge-and-split", [material, new(41, 1055, 995)], potion, true, true);
            await RunCase("equipment-identity", [material], sword, true, true);
            await RunCase("outgoing-equipment", [sword with { Slot = 9 }], potion, true, true);
            await RunCase("full-category", [material, ..full], potion, true, false);
            await RunCase("partial-space-is-atomic", [material, ..full.Select(x => x.Slot == 41 ? x with { CountOrValue = 999 } : x)], potion, true, false);
            await RunCase("overweight", [material], new(1, 1085, 1), true, false);
            await RunCase("net-weight", [new(73, 3176, 1)], potion, true, true);
            await RunCase("same-category", [material], new(1, 3037, 20, State: 1), true, true);
            await RunCase("c1-unchanged", [material], potion, false, false);
            await RunCase("escrowed-merge-target", [material, new(41, 1055, 995)], potion, true, false);

            async Task RunCase(string name, CharacterItemRecord[] bag, CharacterItemRecord cargo,
                bool chapterTwo, bool succeeds)
            {
                var options = new ServerOptions { DataPath = Path.Combine(root.FullName, name + ".json"),
                    ScriptPvfPath = "", SkillScriptPath = root.FullName };
                options.Channel.Port = 47001;
                var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
                var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
                var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
                await store.InitializeAsync();
                await store.CreateAccountAsync(new("storage", "password", OicqCode: "123456"));
                var character = (await store.CreateCharacterAsync("storage", new("Storage"))).Character!;
                await store.SaveCharacterItemSpacesAsync("storage", character.Id, 123, bag, [], [cargo]);
                await using var provider = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
                var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, options, 47001);
                await connection.LoginAsync("storage", character.Slot);
                if (chapterTwo) await connection.EnableChapterTwoAsync();
                var trades = provider.GetRequiredService<ItemTradeService>();
                if (name == "escrowed-merge-target")
                {
                    await store.CreateAccountAsync(new("partner", "password", OicqCode: "123456"));
                    var partner = (await store.CreateCharacterAsync("partner", new("Partner"))).Character!;
                    var towns = provider.GetRequiredService<TownSessionDirectory>();
                    foreach (var (member, account, uid) in new[] {
                        (character, "storage", (ushort)1), (partner, "partner", (ushort)2) })
                    {
                        await trades.AttachChannelAsync(account, member.Name, member.Id, uid,
                            Guid.NewGuid(), _ => ValueTask.CompletedTask);
                        await towns.EnterAsync(new TownEntryRequest(1, 2, uid, member.Id, 0,
                            System.Text.Encoding.ASCII.GetBytes(member.Name), member.Name, null,
                            474, 234, 5, (_, _) => ValueTask.CompletedTask), CancellationToken.None);
                    }
                    await trades.HandleRequestAsync(character.Id, 2, 0);
                    await trades.HandleResponseAsync(partner.Id, 1, true, 0);
                    check(await trades.HandleMoveAsync(character.Id, 0, 41, 1055, 1, 4, 0)
                            && trades.IsEscrowed(character.Id, 0, 41),
                        name + ": partial return stack reserved by real trade service");
                }
                var before = (await store.GetCharacterAsync("storage", character.Id))!;
                var source = before.Inventory!.Single(x => x.Slot == bag[0].Slot);
                var body = new byte[18];
                body[0] = 0;
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), source.Slot);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(3), source.ItemId);
                // Occupied slots exchange whole items even if the sender supplies a smaller amount.
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), 1);
                body[9] = 2;
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(10), cargo.Slot);
                await connection.SendAsync(connection.Command(GameProtocolEngine.MoveItemSpaceCommand, body));
                var reply = await connection.ReadAsync(1, GameProtocolEngine.MoveItemSpaceCommand);
                var lists = new HashSet<byte>();
                while (lists.Count < 2)
                    lists.Add((await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification)).Payload[0]);
                var after = (await store.GetCharacterAsync("storage", character.Id))!;
                check(lists.SetEquals(new byte[] { 0, 2 }), name + ": both displayed spaces refreshed");
                if (succeeds)
                {
                    check(reply.Payload is [0, 0, 0, 2], name + ": drag released with authoritative snapshots");
                    check(after.Warehouse!.Single() == source with { Slot = cargo.Slot }, name + ": whole source preserved in target cargo slot");
                    check(after.Inventory!.Where(x => x.ItemId == cargo.ItemId).Sum(x => (long)x.CountOrValue)
                            == before.Inventory!.Where(x => x.ItemId == cargo.ItemId).Sum(x => (long)x.CountOrValue)
                                + cargo.CountOrValue - (cargo.ItemId == source.ItemId ? source.CountOrValue : 0),
                        name + ": displaced quantity preserved in bag");
                    if (cargo.ItemId == 26055)
                        check(after.Inventory!.SingleOrDefault(x => x.ItemId == cargo.ItemId) == cargo with { Slot = 9 },
                            name + ": equipment identity, seal, quality and durability preserved");
                    if (name == "merge-and-split")
                        check(after.Inventory!.SingleOrDefault(x => x.Slot == 41)?.CountOrValue == 1000
                            && after.Inventory!.SingleOrDefault(x => x.Slot == 42)?.CountOrValue == 995,
                            name + ": returns to category and respects stack cap");
                }
                else
                    check(before.Inventory!.SequenceEqual(after.Inventory!) && before.Warehouse!.SequenceEqual(after.Warehouse!),
                        name + ": refusal changes neither side");
                check(after.Gold == before.Gold, name + ": gold unchanged");
                if (name == "escrowed-merge-target")
                    check(trades.IsEscrowed(character.Id, 0, 41), name + ": trade reservation unchanged");
                check(Totals(before) == Totals(after), name + ": total quantity conserved");
                var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
                await reloaded.InitializeAsync();
                var disk = (await reloaded.GetCharacterAsync("storage", character.Id))!;
                check(after.Inventory!.SequenceEqual(disk.Inventory!) && after.Warehouse!.SequenceEqual(disk.Warehouse!),
                    name + ": saved result survives store reload");
                if (succeeds && cargo.ItemId != source.ItemId)
                {
                    await connection.SendAsync(connection.Command(GameProtocolEngine.MoveItemSpaceCommand, body));
                    await connection.ReadAsync(1, GameProtocolEngine.MoveItemSpaceCommand);
                    var repeated = (await store.GetCharacterAsync("storage", character.Id))!;
                    // A new command with an empty bag origin can mean the
                    // client's reversed cargo withdrawal. It may move items
                    // back, but must never manufacture or lose any quantity.
                    check(Totals(after) == Totals(repeated),
                        name + ": another operation at the same coordinates conserves quantity");
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previousTrial);
            root.Delete(recursive: true);
        }
    }

    private static string Totals(CharacterRecord character) => string.Join(";",
        character.Inventory!.Concat(character.Warehouse!).GroupBy(x => (x.ItemId, x.State, x.Durability, x.SealState))
            .OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Sum(y => (long)y.CountOrValue)}"));
}
