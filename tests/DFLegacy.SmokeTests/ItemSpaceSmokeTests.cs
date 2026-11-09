using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ItemSpaceSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-item-spaces-");
        var stackables = root.CreateSubdirectory("stackable");
        File.WriteAllText(Path.Combine(stackables.FullName, "stackable.lst"),
            "1055 `potion.stk`\n3037 `first.stk`\n3176 `second.stk`\n");
        File.WriteAllText(Path.Combine(stackables.FullName, "potion.stk"),
            "[name] `Potion`\n[stackable type] `[usable]`\n[stack limit] 1000\n");
        foreach (var item in new[] { "first", "second" })
        {
            File.WriteAllText(Path.Combine(stackables.FullName, item + ".stk"),
                $"[name] `{item}`\n[stackable type] `[material]`\n[stack limit] 1000\n");
        }

        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root.FullName, "state.json"),
                ScriptPvfPath = "",
                SkillScriptPath = root.FullName
            };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
            check(items.TryGetDefinition(3037, out var material)
                && material.InventoryCategory == ItemInventoryCategory.Material
                && CharacterInventoryPlanner.GetStackLimit(material) == 1000,
                "fixture uses the material bag and a PVF stack limit of 1000");
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("storage", "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync("storage", new("Storage"))).Character!;
            var occupiedSlots = Enumerable.Range(4, 5).Concat(Enumerable.Range(77, 28));
            var bagItems = occupiedSlots.Select(slot => new CharacterItemRecord((ushort)slot, 3176, 1000))
                .Concat([
                    new CharacterItemRecord(3, 1055, 27),
                    new CharacterItemRecord(44, 1055, 600),
                    new CharacterItemRecord(46, 1055, 650),
                    new CharacterItemRecord(49, 1055, 200, State: 1),
                    new CharacterItemRecord(73, 3037, 495)
                ]);
            await store.SaveCharacterItemSpacesAsync("storage", character.Id, 0, bagItems, [],
                [new(1, 3176, 1000), new(4, 3037, 100), new(5, 3037, 200),
                    new(6, 3037, 1000), new(7, 3037, 40, State: 1)]);
            var mail = (await store.SendCharacterMailAsync(character.Id,
                new("GM", "Large attachment", 500, new(3037, 2501)))).Mail!;
            var secondMail = (await store.SendCharacterMailAsync(character.Id,
                new("GM", "Another large attachment", 100, new(3037, 1200)))).Mail!;

            await using var provider = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
            var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, options);
            await connection.LoginAsync("storage", character.Slot);

            // Original same-list sender reverses origin and target and sends zero count.
            var moved = await MoveAsync(2, 2, 0, 0, 2, 1);
            var warehouse = await ReadItemsAsync(2);
            check(ReadCount(moved.Payload.AsSpan(4)) == 1000
                && warehouse.GetValueOrDefault((ushort)2) == (3176, 1000u)
                && !warehouse.ContainsKey(1),
                "reversed warehouse drag moves the whole stack into the empty target");

            // On the old server, an occupied bag -> warehouse drag never sends the corrected bag.
            var swapped = await MoveAsync(0, 73, 3037, 495, 2, 2);
            check(swapped.Payload is [0, 0, 0, 2],
                "an occupied cross-space swap releases both drag locks without invoking the client's incompatible move");
            var firstRefresh = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification);
            check(firstRefresh.Payload[0] == 0 && ReadItems(firstRefresh).GetValueOrDefault((ushort)73) == (3176, 1000u),
                "occupied cross-space swap sends the corrected bag stack");
            warehouse = firstRefresh.Payload[0] == 2 ? ReadItems(firstRefresh) : await ReadItemsAsync(2);
            check(warehouse.GetValueOrDefault((ushort)2) == (3037, 495u),
                "occupied cross-space swap sends the corrected warehouse stack");

            await MoveAsync(2, 4, 3037, 0, 2, 5);
            warehouse = await ReadItemsAsync(2);
            check(warehouse.GetValueOrDefault((ushort)4) == (3037, 200u)
                && warehouse.GetValueOrDefault((ushort)5) == (3037, 100u),
                "rearranging two warehouse stacks swaps their slots without combining them");

            await connection.SendAsync(connection.Command(GameProtocolEngine.SortItemCommand, 2));
            var sorted = await connection.ReadAsync(1, GameProtocolEngine.SortItemCommand);
            warehouse = await ReadItemsAsync(2);
            check(sorted.Payload is [1, 2]
                && warehouse.Values.Select(entry => entry.Count).SequenceEqual(new uint[] { 1000, 795, 40 }),
                "warehouse sort fills stacks up to the PVF limit and leaves different item state separate");

            var mailId = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(mailId, mail.Id);
            await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, mailId));
            var extracted = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
            var bag = await ReadItemsAsync(0);
            check(extracted.Payload[0] == 1
                && bag.Where(pair => pair.Value.ItemId == 3037)
                    .Select(pair => pair.Value.Count).Order().SequenceEqual(new uint[] { 501, 1000, 1000 }),
                "claiming a single 2501-unit mail distributes it across three legal inventory stacks");

            await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, mailId));
            var repeat = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
            var persisted = (await store.GetCharacterAsync("storage", character.Id))!;
            check(repeat.Payload[0] == 0 && persisted.Gold == 500
                && persisted.Mailbox!.Single(entry => entry.Id == mail.Id).Attachment is null
                && persisted.Inventory!.Where(item => item.ItemId == 3037).Sum(item => (long)item.CountOrValue) == 2501,
                "retry cannot duplicate the split attachment or gold");

            BinaryPrimitives.WriteUInt32LittleEndian(mailId, secondMail.Id);
            await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, mailId));
            var partial = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
            if (partial.Payload[0] == 1)
            {
                await ReadItemsAsync(0);
                await connection.ReadAsync(0, GameProtocolEngine.MailboxMailListNotification);
            }
            persisted = (await store.GetCharacterAsync("storage", character.Id))!;
            check(partial.Payload[0] == 1 && persisted.Gold == 600
                && persisted.Inventory!.Where(item => item.ItemId == 3037).Sum(item => (long)item.CountOrValue) == 3000
                && persisted.Mailbox!.Single(entry => entry.Id == secondMail.Id) is
                    { Gold: 0, Attachment.CountOrValue: 701 },
                "a limited-space claim fills the available stack and keeps the remainder on the same mail");

            await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, mailId));
            var full = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
            persisted = (await store.GetCharacterAsync("storage", character.Id))!;
            check(full.Payload[0] == 0 && persisted.Gold == 600
                && persisted.Mailbox!.Single(entry => entry.Id == secondMail.Id).Attachment?.CountOrValue == 701,
                "a completely full bag preserves the unclaimed remainder without repeating the gold grant");

            check(await store.ClaimCharacterMailAsync("storage", character.Id, secondMail.Id,
                    persisted.Gold, persisted.Inventory!, persisted.Equipment!, expectedMail: secondMail) is null,
                "a stale claim planned before the partial receipt cannot clear the remaining attachment");

            await MoveAsync(0, 74, 3037, 400, 2, 3);
            await ReadItemsAsync(2);
            await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, mailId));
            var smallRemainder = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
            if (smallRemainder.Payload[0] == 1)
            {
                await ReadItemsAsync(0);
                await connection.ReadAsync(0, GameProtocolEngine.MailboxMailListNotification);
            }
            persisted = (await store.GetCharacterAsync("storage", character.Id))!;
            check(smallRemainder.Payload[0] == 1
                && persisted.Mailbox!.Single(entry => entry.Id == secondMail.Id).Attachment?.CountOrValue == 301,
                "a remainder below the stack limit can still be partially claimed");

            await MoveAsync(0, 75, 3037, 1000, 2, 4);
            await ReadItemsAsync(2);
            await connection.SendAsync(connection.Command(GameProtocolEngine.MailboxExtractItemCommand, mailId));
            var claimedRest = await connection.ReadAsync(1, GameProtocolEngine.MailboxExtractItemCommand);
            bag = await ReadItemsAsync(0);
            persisted = (await store.GetCharacterAsync("storage", character.Id))!;
            check(claimedRest.Payload[0] == 1
                && bag.Values.Where(item => item.ItemId == 3037).Sum(item => (long)item.Count) == 2301
                && persisted.Gold == 600
                && persisted.Mailbox!.Single(entry => entry.Id == secondMail.Id).Attachment is null,
                "after freeing space, the same mail grants exactly the remaining quantity and clears its attachment");

            var quickSlots = persisted.Inventory!.Where(item => CharacterInventoryLayout.IsQuickSlot(item.Slot)).ToArray();
            await connection.SendAsync(connection.Command(GameProtocolEngine.SortItemCommand, 0));
            var sortedBag = await connection.ReadAsync(1, GameProtocolEngine.SortItemCommand);
            bag = await ReadItemsAsync(0);
            var afterSort = (await store.GetCharacterAsync("storage", character.Id))!.Inventory!;
            check(sortedBag.Payload is [1, 0]
                && bag.GetValueOrDefault((ushort)41) == (1055, 1000u)
                && bag.GetValueOrDefault((ushort)42) == (1055, 250u)
                && bag.GetValueOrDefault((ushort)43) == (1055, 200u)
                && afterSort.Single(item => item.Slot == 43).State == 1,
                "main-bag sort fills a stack to the PVF limit, keeps the remainder and separates different item state");
            check(quickSlots.SequenceEqual(afterSort.Where(item => CharacterInventoryLayout.IsQuickSlot(item.Slot)))
                && bag.GetValueOrDefault((ushort)3) == (1055, 27u),
                "main-bag sort preserves quick slots even when a matching item exists in the bag");

            async Task<GameServerPacket> MoveAsync(byte sourceType, ushort sourceSlot, ushort itemId,
                uint count, byte destinationType, ushort destinationSlot)
            {
                var body = new byte[18];
                body[0] = sourceType;
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), sourceSlot);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(3), itemId);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), count);
                body[9] = destinationType;
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(10), destinationSlot);
                await connection.SendAsync(connection.Command(GameProtocolEngine.MoveItemSpaceCommand, body));
                return await connection.ReadAsync(1, GameProtocolEngine.MoveItemSpaceCommand);
            }

            async Task<Dictionary<ushort, (ushort ItemId, uint Count)>> ReadItemsAsync(byte type)
            {
                GameServerPacket list;
                do { list = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification); }
                while (list.Payload[0] != type);
                return ReadItems(list);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static Dictionary<ushort, (ushort ItemId, uint Count)> ReadItems(GameServerPacket packet)
    {
        var payload = packet.Payload.AsSpan();
        var offset = payload[0] == 2 ? 5 : 3;
        var items = new Dictionary<ushort, (ushort, uint)>();
        for (; offset + 12 <= payload.Length; offset += 12)
        {
            items.Add(BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]),
                (BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..]),
                    ReadCount(payload[(offset + 4)..])));
        }

        return items;
    }

    private static uint ReadCount(ReadOnlySpan<byte> bytes) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
