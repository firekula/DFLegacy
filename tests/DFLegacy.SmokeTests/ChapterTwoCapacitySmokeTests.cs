using System.Text.Json;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;

internal static class ChapterTwoCapacitySmokeTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var section in Enum.GetValues<InventorySection>())
        {
            var count = section == InventorySection.Avatar ? 999_999 : 99_999;
            foreach (var index in new[] { -1, 0, 31, 32, 104, 105, 32_767, 32_768, 65_534, 65_535, 65_536, count - 1, count })
                check(new InventoryAddress(section, index).IsValid == (index >= 0 && index < count), $"{section} boundary {index}");
        }

        foreach (var avatar in new[] { false, true })
        {
            for (ushort slot = 0; slot < 138; slot++)
            {
                var maps = InventoryAddress.TryFromLegacy(avatar, slot, out var address);
                check(maps == (avatar ? slot < 105 : slot is >= 9 and < 137), $"legacy {avatar}/{slot}");
                if (maps)
                    check(address.TryGetLegacySlot(out var mapped) && mapped == slot, $"round trip {avatar}/{slot}");
            }
        }
        check(!new InventoryAddress(InventorySection.Avatar, 105).TryGetLegacySlot(out _), "first extended avatar");
        check(!new InventoryAddress(InventorySection.Equipment, 32).TryGetLegacySlot(out _), "first extended equipment");

        var item = new CharacterItemRecord(0, 39000, 1, Durability: 35, SealState: 1, AvatarAbilityIndex: 4,
            InstanceId: Guid.NewGuid(), AvatarExpiresAtUnixSeconds: 2_000_000_000, EnchantmentCardId: 42);
        var entry = new ExtendedInventoryItem(new InventoryAddress(InventorySection.Avatar, 999_998), item);
        var character = new CharacterRecord(Guid.NewGuid(), "sample", 1, 0, 60, 0, 0,
            Inventory: [new(3, 1001, 2), new(9, 27000, 1)], AvatarInventory: [new(104, 39001, 1)]);
        var account = new AccountRecord(Guid.NewGuid(), "sample", "", [character]);
        var options = new JsonSerializerOptions();
        options.Converters.Add(new InventoryAccountsConverter());
        string Encode(CharacterRecord value) => JsonSerializer.Serialize<List<AccountRecord>>(
            [account with { Characters = [value] }], options);
        List<AccountRecord> Decode(string json) => JsonSerializer.Deserialize<List<AccountRecord>>(json, options)!;
        void Reject(Action operation, string message)
        {
            try { operation(); check(false, message); }
            catch (JsonException) { check(true, message); }
        }

        var legacy = Encode(character);
        check(legacy.StartsWith('[') && !legacy.Contains("ExtendedInventory"), "legacy shape");
        var expanded = Encode(character with { ExtendedInventory = [entry] });
        var reopened = Decode(expanded).Single().Characters.Single();
        check(reopened.ExtendedInventory!.Single() == entry && reopened.Inventory!.SequenceEqual(character.Inventory!)
            && reopened.AvatarInventory!.SequenceEqual(character.AvatarInventory!), "old and extended items survive");
        check(Encode(reopened) == expanded, "idempotent save");
        Reject(() => JsonSerializer.Deserialize<List<AccountRecord>>(expanded), "previous server rejects v2");
        Reject(() => Decode(expanded.Replace("\"Version\":2", "\"Version\":3")), "future version rejected");
        Reject(() => Encode(character with { ExtendedInventory = [entry, entry] }), "duplicate address rejected");
        Reject(() => Encode(character with { ExtendedInventory = [entry], Equipment = [item] }), "duplicate identity rejected");
        Reject(() => Encode(character with { ExtendedInventory = [entry with { Address = new(InventorySection.Avatar, 104) }] }),
            "legacy entry cannot also be extended");
    }

    internal static void CheckWindows(Action<bool, string> check)
    {
        var packed = Convert.FromHexString("0100FFFFFF444649560101047F9698000007000000");
        // Bytes 0..1 are the normal move sequence. Bytes 2..4 encode an invalid stock source.
        var read = new InventoryWindowRequest(InventoryWindowOperation.Read, InventorySection.Avatar, 999_971, 7);
        var body = read.Encode(1);
        check(body.Length == 34 && body.AsSpan(2, 3).SequenceEqual(new byte[] {255, 255, 255}),
            "window requests are padded C2 commands with a rejected legacy source");
        check(InventoryWindowRequest.TryDecode(body, out var parsed) && parsed == read, "window request round trip");
        check(body.AsSpan(5, 14).SequenceEqual(Convert.FromHexString("4446495601010423420f00070000")),
            "window reader and native fixture have the same header bytes");
        body[^1] = 1;
        check(!InventoryWindowRequest.TryDecode(body, out _), "malformed padding rejected");
        check(!InventoryWindowRequest.TryDecode(packed, out _), "malformed request length rejected");
        var actor = new CharacterRecord(Guid.NewGuid(), "view", 0, 0, 1, 0, 0,
            Inventory: [new(3, 1000, 1), new(9, 27000, 1), new(41, 1001, 2)],
            AvatarInventory: [new(0, 39000, 1)])
        {
            ExtendedInventory = [new(new(InventorySection.Avatar, 999_998), new(0, 39001, 1)),
                new(new(InventorySection.Material, 99_998), new(0, 3000, 4))]
        };
        var view = new ChapterTwoInventoryView(actor);
        check(view.TryRead(read, out var page) && page is [{ Offset: 27, Item.ItemId: 39001 }],
            "last avatar is projected into the final visible cell");
        check(view.TryRead(read with { First = 999_998 }, out page) && page is [{ Offset: 0, Item.ItemId: 39001 }],
            "partial final window never reads past capacity");
        check(!view.TryRead(read with { First = int.MaxValue }, out _), "oversized first index rejected");
        check(view.TryRead(read with { Section = InventorySection.Material, First = 99_967 }, out page)
            && page is [{ Offset: 31, Item.CountOrValue: 4 }], "last material maps independently");
        check(view.TryRead(read with { Section = InventorySection.Consumable, First = 0 }, out page)
            && page is [{ Offset: 0, Item.ItemId: 1001 }], "quickbar and other category omitted");
        check(view.TryRead(read with { First = 1000 }, out page) && page.Length == 0, "empty page is sparse");
    }

    internal static async Task CheckStorageAsync(Action<bool, string> check)
    {
        var dir = Directory.CreateTempSubdirectory("capacity-fixture-");
        try
        {
            var path = Path.Combine(dir.FullName, "state.json");
            var opts = new ServerOptions { DataPath = path, ChapterTwoRemote = true, SeedTestAccount = false, ScriptPvfPath = "" };
            var injectFailure = false;
            var replacer = new AtomicSnapshotReplacement((temporary, live) =>
            {
                if (injectFailure) throw new IOException("capacity fixture failure");
                File.Move(temporary, live, overwrite: true);
            }, Task.Delay);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonGameStore>.Instance;
            var store = new JsonGameStore(opts, logger, replacer);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("capacity", "password", OicqCode: "123456"));
            var actor = (await store.CreateCharacterAsync("capacity", new("CapacityActor"))).Character!;
            var item = new CharacterItemRecord(104, 39001, 1, InstanceId: Guid.NewGuid());
            await store.SaveCharacterItemSpacesAsync("capacity", actor.Id, 100,
                [new(3, 1001, 3), new(9, 27000, 1)], [], [], avatarInventory: [item]);
            var low = new InventoryAddress(InventorySection.Avatar, 104);
            var high = new InventoryAddress(InventorySection.Avatar, 105);
            var last = new InventoryAddress(InventorySection.Avatar, 999_998);
            var live = (await store.GetCharacterAsync("capacity", actor.Id))!;
            item = live.AvatarInventory!.Single();
            var baseline = await File.ReadAllBytesAsync(path);
            injectFailure = true;
            try
            {
                await store.MoveToEmptyInventorySlotAsync("capacity", actor.Id, low, high, item);
                check(false, "snapshot rename failure propagated");
            }
            catch (IOException) { check(true, "injected rename failure"); }
            injectFailure = false;
            live = (await store.GetCharacterAsync("capacity", actor.Id))!;
            var failedSnapshot = await File.ReadAllBytesAsync(path);
            check(baseline.SequenceEqual(failedSnapshot) && live.AvatarInventory!.Single() == item
                && live.ExtendedInventory is null, "failed move restores disk and memory");
            live = (await store.MoveToEmptyInventorySlotAsync("capacity", actor.Id, low, high, item))!;
            check(live.AvatarInventory!.Count == 0 && live.ExtendedInventory!.Single().Address == high, "105th to 106th");
            check(await store.MoveToEmptyInventorySlotAsync("capacity", actor.Id, low, high, item) is null, "repeat rejected");

            var restarted = new JsonGameStore(opts, logger);
            await restarted.InitializeAsync();
            live = (await restarted.GetCharacterAsync("capacity", actor.Id))!;
            var stored = live.ExtendedInventory!.Single().Item;
            check(stored == item with { Slot = 0 }, "real store restart preserves original instance");
            live = (await restarted.MoveToEmptyInventorySlotAsync("capacity", actor.Id, high, last, stored))!;
            check(live.ExtendedInventory!.Single().Address == last, "last avatar slot persisted");
            var hello = new InventoryWindowRequest(InventoryWindowOperation.Hello, InventorySection.Equipment, 0, 9);
            var readOnly = new InventoryWindowRequest(InventoryWindowOperation.Read, InventorySection.Avatar, 999_971, 10);
            var session = new ChapterTwoInventoryWindowSession();
            using (var definitions = new ActiveDropItemFixture())
            {
                check(session.Handle(readOnly, live, definitions.Catalog) is null, "read requires negotiation");
                var ack = session.Handle(hello, null, definitions.Catalog);
                check(ack?.Payload is [255, 68, 70, 73, 86, 1, 0, 9, 0, 0, 0], "hello response fixed bytes");
                var reply = session.Handle(readOnly, live, definitions.Catalog);
                var data = reply?.Payload ?? [];
                check(data.Length == 64 && data[28] == 1 && data[29] == 27, "one last-page item and its offset");
                check(session.Handle(readOnly, live, definitions.Catalog) is null, "stale page request ignored");
            }
            using (var items = new ActiveDropItemFixture())
            {
                await using var provider = NewCharacterDefaultsSmokeTests.CreateSessionServices(opts, restarted, items.Catalog);
                var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, opts);
                await connection.LoginAsync("capacity", actor.Slot, expectSelection: false, chapterTwo: true);
                check(connection.SelectReply?.Payload is [0, ..], "legacy client cannot open a character with high slots");
                var updateNotice = await connection.ReadAsync(0, 126);
                check(updateNotice.Payload.Length > 8, "old client receives the update notice");
                await connection.SendAsync(NewCharacterDefaultsSmokeTests.Connection.EncodeChapterTwoCommand(
                    GameProtocolEngine.MoveItemSpaceCommand, hello.Encode(5)));
                var wireAck = await connection.ReadAsync(0, InventoryWindowRequest.ReplyNotification);
                check(wireAck.Payload is [255, 68, 70, 73, 86, 1, 0, 9, 0, 0, 0],
                    "authenticated C2 channel negotiates without mutating an inventory");

            }
            var validState = await File.ReadAllTextAsync(path);
            var badState = System.Text.Json.Nodes.JsonNode.Parse(validState)!;
            check(GameStoreAccountRows.Get(badState).Count == 1, "QA tools locate accounts in v2");
            badState["Accounts"]!["Version"] = 3;
            var futureState = badState.ToJsonString();
            await File.WriteAllTextAsync(path, futureState);
            try
            {
                await new JsonGameStore(opts, logger).InitializeAsync();
                check(false, "future store rejected before migration");
            }
            catch (JsonException) { check(true, "future store rejected before migration"); }
            check(await File.ReadAllTextAsync(path) == futureState, "invalid save bytes preserved");
            await File.WriteAllTextAsync(path, validState);
            live = (await restarted.MoveToEmptyInventorySlotAsync("capacity", actor.Id, last, low, stored))!;
            check(live.AvatarInventory!.Single() == item && live.ExtendedInventory is null, "return to original legacy slot");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            check(document.RootElement.GetProperty("Accounts").ValueKind == JsonValueKind.Array, "legacy-readable shape restored");
        }
        finally { dir.Delete(recursive: true); }
    }
}
