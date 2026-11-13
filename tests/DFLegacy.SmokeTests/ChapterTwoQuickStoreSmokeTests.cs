using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoQuickStoreSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var report = check;
        var assertions = 0;
        check = (passed, message) => { assertions++; report(passed, message); };
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-quick-store-");
        var previous = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        try
        {
            var stacks = root.CreateSubdirectory("stackable");
            File.WriteAllText(Path.Combine(stacks.FullName, "stackable.lst"), "3037 `material.stk`\n1055 `potion.stk`\n4000 `quest.stk`\n4001 `heavy.stk`\n");
            foreach (var (name, type) in new[] { ("material", "material"), ("potion", "usable"), ("quest", "quest") })
                File.WriteAllText(Path.Combine(stacks.FullName, name + ".stk"),
                    $"[name] `{name}`\n[stackable type] `[{type}]`\n[stack limit] 1000\n[weight] 1\n");
            File.WriteAllText(Path.Combine(stacks.FullName, "heavy.stk"),
                "[name] `Heavy`\n[stackable type] `[material]`\n[stack limit] 1000\n[weight] 1000000\n");
            var equipment = root.CreateSubdirectory("equipment");
            File.WriteAllText(Path.Combine(equipment.FullName, "equipment.lst"), "26055 `sword.equ`\n");
            File.WriteAllText(Path.Combine(equipment.FullName, "sword.equ"),
                "[name] `Sword`\n[equipment type] `[weapon]`\n[durability] 50\n[weight] 10\n");
            CharacterItemRecord material = new(73, 3037, 495, State: 2);
            var full = Enumerable.Range(0, 8).Select(x => new CharacterItemRecord((ushort)x, 1055, 1000)).ToArray();
            await RunCase("empty", material, [], 495, true, 0, 495);
            await RunCase("whole-merge", material, [material with { Slot = 3, CountOrValue = 500 }], 495, true, 3, 995);
            await RunCase("skip-small-stack", material, [material with { Slot = 0, CountOrValue = 999 }], 495, true, 1, 495);
            await RunCase("partial-quantity", material, [], 100, true, 0, 100);
            await RunCase("full", material, full, 495, true, null, 0);
            await RunCase("full-partial-only", material, [material with { Slot = 0, CountOrValue = 999 }, ..full.Skip(1)], 495, true, null, 0);
            await RunCase("stale-id", material, [], 495, true, null, 0, itemId: 1055);
            await RunCase("too-many", material, [], 496, true, null, 0);
            await RunCase("zero", material, [], 0, true, null, 0);
            await RunCase("quest", new(105, 4000, 10), [], 10, true, null, 0);
            await RunCase("equipment", new(9, 26055, 1, State: 1, Durability: 33,
                SealState: 1, EquipmentQualitySeed: 987, InstanceId: Guid.NewGuid()), [], 1, true, 0, 1);
            await RunCase("chapter-one-rejects-reserved", material, [], 495, false, null, 0);
            await RunCase("save-failure", material, [], 495, true, null, 0);
            await RunCase("concurrent-state", material, [], 495, true, null, 0);

            var storedMaterial = material with { Slot = 3 };
            var fullMaterial = Enumerable.Range(73, 32).Select(x => material with { Slot = (ushort)x, CountOrValue = 1000 }).ToArray();
            await RunCase("withdraw-empty", storedMaterial, [], 495, true, 73, 495, withdraw: true);
            await RunCase("withdraw-merge", storedMaterial, [material with { CountOrValue = 500 }], 495, true, 73, 995, withdraw: true);
            await RunCase("withdraw-skip-small", storedMaterial, [material with { CountOrValue = 999 }], 495, true, 74, 495, withdraw: true);
            await RunCase("withdraw-partial", storedMaterial, [], 17, true, 73, 17, withdraw: true);
            await RunCase("withdraw-full", storedMaterial, fullMaterial, 495, true, null, 0, withdraw: true);
            await RunCase("withdraw-full-partial-only", storedMaterial, [material with { CountOrValue = 999 }, ..fullMaterial.Skip(1)], 495, true, null, 0, withdraw: true);
            await RunCase("withdraw-other-category-full", storedMaterial,
                Enumerable.Range(9, 32).Select(x => new CharacterItemRecord((ushort)x, 26055, 1)).ToArray(),
                495, true, 73, 495, withdraw: true);
            await RunCase("withdraw-consumable", new(2, 1055, 20), [], 20, true, 41, 20, withdraw: true);
            await RunCase("withdraw-equipment", new(0, 26055, 1, State: 1, Durability: 33,
                SealState: 1, EquipmentQualitySeed: 987, InstanceId: Guid.NewGuid()), [], 1, true, 9, 1, withdraw: true);
            await RunCase("withdraw-overweight", new(0, 4001, 1), [], 1, true, null, 0, withdraw: true);
            await RunCase("withdraw-stale-id", storedMaterial, [], 495, true, null, 0, itemId: 1055, withdraw: true);
            await RunCase("withdraw-too-many", storedMaterial, [], 496, true, null, 0, withdraw: true);
            await RunCase("withdraw-zero", storedMaterial, [], 0, true, null, 0, withdraw: true);
            await RunCase("withdraw-quest", new(0, 4000, 10), [], 10, true, null, 0, withdraw: true);
            await RunCase("withdraw-invalid-slot", storedMaterial with { Slot = 120 }, [], 495, true, null, 0, withdraw: true);
            await RunCase("withdraw-chapter-one", storedMaterial, [], 495, false, null, 0, withdraw: true);
            await RunCase("withdraw-save-failure", storedMaterial, [], 495, true, null, 0, withdraw: true);

            await RunCase("withdraw-concurrent-state", storedMaterial, [], 495, true, null, 0, withdraw: true);

            async Task RunCase(string name, CharacterItemRecord source, CharacterItemRecord[] cargo,
                uint count, bool chapterTwo, ushort? target, uint targetCount, ushort? itemId = null, bool withdraw = false)
            {
                var options = new ServerOptions { DataPath = Path.Combine(root.FullName, name + ".json"), ScriptPvfPath = "", SkillScriptPath = root.FullName };
                options.Channel.Port = 47001;
                var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
                var catalog = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
                var failSave = false;
                var replacer = new AtomicSnapshotReplacement((from, to) =>
                {
                    if (failSave) throw new IOException("Injected quick-store replacement failure");
                    File.Move(from, to, overwrite: true);
                }, (_, _) => Task.CompletedTask);
                var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, replacer, itemCatalog: catalog);
                await store.InitializeAsync();
                await store.CreateAccountAsync(new("quickstore", "password", OicqCode: "123456"));
                var character = (await store.CreateCharacterAsync("quickstore", new("QuickStore"))).Character!;
                await store.SaveCharacterItemSpacesAsync("quickstore", character.Id, 123, withdraw ? cargo : [source], [], withdraw ? [source] : cargo);
                await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, catalog);
                var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
                await connection.LoginAsync("quickstore", character.Slot);
                if (chapterTwo) await connection.EnableChapterTwoAsync();
                var before = (await store.GetCharacterAsync("quickstore", character.Id))!;
                if (name.EndsWith("concurrent-state", StringComparison.Ordinal))
                {
                    await store.SaveCharacterItemSpacesAsync("quickstore", character.Id, 124,
                        withdraw ? [source with { Slot = 73, CountOrValue = 20 }] : [source with { CountOrValue = 600 }], before.Equipment!,
                        withdraw ? [source with { CountOrValue = 600 }] : [source with { Slot = 1, CountOrValue = 20 }]);
                    before = (await store.GetCharacterAsync("quickstore", character.Id))!;
                }
                failSave = name.EndsWith("save-failure", StringComparison.Ordinal);
                var body = new byte[18];
                body[0] = withdraw ? (byte)2 : (byte)0;
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), source.Slot);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(3), itemId ?? source.ItemId);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), count);
                body[9] = withdraw ? (byte)0 : (byte)2;
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(10), 0xfffe);
                await connection.SendAsync(connection.Command(GameProtocolEngine.MoveItemSpaceCommand, body));
                var reply = await connection.ReadAsync(1, GameProtocolEngine.MoveItemSpaceCommand);
                failSave = false;
                check(withdraw ? reply.Payload is [0, 0, 2, 0] : reply.Payload is [0, 0, 0, 2], name + ": completion uses safe snapshot form");
                var refreshed = new HashSet<byte>();
                byte[]? refreshedMain = null;
                byte[]? refreshedCargo = null;
                while (refreshed.Count < 2)
                {
                    var payload = (await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification)).Payload;
                    refreshed.Add(payload[0]);
                    if (payload[0] == 0) refreshedMain = payload;
                    if (payload[0] == 2) refreshedCargo = payload;
                }
                check(refreshed.SetEquals(new byte[] { 0, 2 }), name + ": both spaces refreshed");
                if (chapterTwo && !target.HasValue)
                {
                    GameServerPacket? notice = null;
                    try { notice = await connection.ReadAsync(0, GameProtocolEngine.CreatureMessageNotification, TimeSpan.FromSeconds(1)); }
                    catch (TimeoutException) { }
                    check(notice is not null, name + ": rejected shortcut explains the cause after refreshing both spaces");
                    if (notice is not null)
                    {
                        var payload = notice.Payload;
                        check(payload.Length > 7 && payload[0] == 0
                            && BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(3)) == payload.Length - 7,
                            name + ": feedback uses the existing native popup format");
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        var message = Encoding.GetEncoding(936).GetString(payload.AsSpan(7));
                        var expected = name.Contains("overweight") ? "负重"
                            : name.Contains("full") ? (withdraw ? "对应分类" : "仓库空间") : "重试";
                        check(message.Contains(expected, StringComparison.Ordinal), name + ": feedback identifies the actual refusal");
                        check(message.Contains("物品未移动", StringComparison.Ordinal), name + ": feedback makes non-consumption clear");
                    }
                }
                var after = (await store.GetCharacterAsync("quickstore", character.Id))!;
                if (target.HasValue)
                {
                    check((withdraw ? after.Inventory! : after.Warehouse!).SingleOrDefault(x => x.Slot == target)?.CountOrValue == targetCount, name + ": complete requested quantity in one target");
                    var bagSource = (withdraw ? after.Warehouse! : after.Inventory!).SingleOrDefault(x => x.Slot == source.Slot);
                    check(count == source.CountOrValue ? bagSource is null : bagSource?.CountOrValue == source.CountOrValue - count,
                        name + ": source decremented exactly");
                    if (name.EndsWith("equipment", StringComparison.Ordinal)) check((withdraw ? after.Inventory! : after.Warehouse!).SingleOrDefault() == source with { Slot = target.Value }, name + ": identity preserved");
                    if (name == "skip-small-stack") check(after.Warehouse!.Single(x => x.Slot == 0).CountOrValue == 999, name + ": small stack untouched");
                }
                else check(before.Inventory!.SequenceEqual(after.Inventory!) && before.Warehouse!.SequenceEqual(after.Warehouse!), name + ": rejection leaves both spaces unchanged");
                check(after.Gold == before.Gold, name + ": gold unchanged");
                check(before.Inventory!.Concat(before.Warehouse!).Sum(x => (long)x.CountOrValue) == after.Inventory!.Concat(after.Warehouse!).Sum(x => (long)x.CountOrValue), name + ": quantity conserved");
                var reload = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: catalog);
                await reload.InitializeAsync();
                var disk = (await reload.GetCharacterAsync("quickstore", character.Id))!;
                check(after.Inventory!.SequenceEqual(disk.Inventory!) && after.Warehouse!.SequenceEqual(disk.Warehouse!), name + ": persisted result");
                if (target.HasValue && count == source.CountOrValue)
                {
                    await connection.SendAsync(connection.Command(GameProtocolEngine.MoveItemSpaceCommand, body));
                    await connection.ReadAsync(1, GameProtocolEngine.MoveItemSpaceCommand);
                    var repeated = (await store.GetCharacterAsync("quickstore", character.Id))!;
                    check(after.Inventory!.SequenceEqual(repeated.Inventory!) && after.Warehouse!.SequenceEqual(repeated.Warehouse!), name + ": repeated full deposit cannot move twice");
                }
                if (name.EndsWith("concurrent-state", StringComparison.Ordinal))
                {
                    static uint? CountAt(byte[] payload, int start, ushort slot)
                    {
                        for (var offset = start; offset + 14 <= payload.Length; offset += 14)
                            if (BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset)) == slot)
                                return BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 4));
                        return null;
                    }
                    check(CountAt(refreshedMain!, 3, 0) == 124 && CountAt(refreshedMain!, 3, withdraw ? (ushort)73 : source.Slot) == (withdraw ? 20u : 600u)
                        && CountAt(refreshedCargo!, 5, withdraw ? source.Slot : (ushort)1) == (withdraw ? 600u : 20u), name + ": rejected operation displays authoritative gold and item quantities");
                    await connection.SendAsync(connection.Command(GameProtocolEngine.MoveItemSpaceCommand, body));
                    await connection.ReadAsync(1, GameProtocolEngine.MoveItemSpaceCommand);
                    var retried = (await store.GetCharacterAsync("quickstore", character.Id))!;
                    check(retried.Gold == 124 && retried.Inventory!.Single(x => x.Slot == (withdraw ? (ushort)73 : source.Slot)).CountOrValue == (withdraw ? 515u : 105u)
                        && retried.Warehouse!.Single(x => x.Slot == (withdraw ? source.Slot : (ushort)1)).CountOrValue == (withdraw ? 105u : 515u),
                        name + ": next operation uses refreshed bag, cargo and gold");
                }
            }
            Console.WriteLine($"C2 quick store: {assertions} assertions");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previous);
            root.Delete(recursive: true);
        }
    }
}
