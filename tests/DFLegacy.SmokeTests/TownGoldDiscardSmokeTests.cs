using System.Buffers.Binary;
using System.Text.Json;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TownGoldDiscardSmokeTests
{
    // Real CMD18 traffic catches town rejection, a ground-drop branch, early ACKs,
    // inventory loss, stale session balances and a debit retained after write failure.
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var items = new ActiveDropItemFixture();
        var root = Directory.CreateTempSubdirectory("dflegacy-town-gold-");
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root.FullName, "state.json"),
                SeedTestAccount = false, ScriptPvfPath = "", SkillScriptPath = root.FullName
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items.Catalog);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("discard", "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync("discard", new("Discard"))).Character!;
            await store.SaveCharacterItemSpacesAsync("discard", character.Id, 11646,
                [new(9, 50010, 1, InstanceId: Guid.NewGuid()), new(73, 3176, 5)],
                [new(10, 50011, 1)], [new(0, 3176, 3)],
                avatarInventory: [new(0, 50017, 1)], creatureInventory: [new(0, 2000, 1)]);

            await using var provider = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items.Catalog);
            var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, options);
            await connection.LoginAsync("discard", character.Slot);
            var original = (await store.GetCharacterAsync("discard", character.Id))!;
            var observed = new List<GameServerPacket>();

            await DiscardAsync([new(1, 0, 0, 1)], 11645);
            await DiscardAsync([new(1, 0, 0, 2000)], 9645);
            await DiscardAsync([new(1, 0, 0, 0)], 9645, rejected: true);
            await DiscardAsync([new(1, 0, 0, 9646)], 9645, rejected: true);
            await DiscardAsync([new(1, 0, 0, uint.MaxValue)], 9645, rejected: true);
            await DiscardAsync([new(0, 0, 0, 1)], 9645, rejected: true);
            await DiscardAsync([new(2, 0, 0, 1)], 9645, rejected: true);
            await DiscardAsync([new(1, 0, 3176, 1)], 9645, rejected: true);
            await DiscardAsync([new(1, 73, 0, 1)], 9645, rejected: true);
            await DiscardAsync([new(1, 0, 0, 1), new(1, 73, 3176, 1)], 9645, rejected: true);
            await DiscardAsync([new(1, 0, 0, 1), new(1, 0, 0, 1)], 9645, rejected: true);

            await connection.SendAsync(connection.Command(GameProtocolEngine.DeleteItemCommand,
                Arguments([new(1, 0, 0, 1)], space: 7)));
            try
            {
                var invalidSpace = await connection.ReadAsync(1, GameProtocolEngine.DeleteItemCommand,
                    TimeSpan.FromMilliseconds(150), observed.Add);
                check(invalidSpace.Payload[0] == 0, "gold in an unsupported item space has no success ACK");
            }
            catch (TimeoutException) { }
            await CheckStateAsync(9645, "unsupported item space");

            // A real failure before atomic snapshot replacement must neither debit
            // the store/session nor let the client accept the discard.
            using (var blocked = new FileStream(options.DataPath + ".tmp", FileMode.OpenOrCreate,
                       FileAccess.ReadWrite, FileShare.None))
            {
                await DiscardAsync([new(1, 0, 0, 100)], 9645, rejected: true);
            }
            await DiscardAsync([new(1, 0, 0, 100)], 9545);

            // Selection has no live dungeon either, but is not an eligible town state.
            await connection.SendAsync(connection.Command(GameProtocolEngine.StartGameCommand));
            await connection.ReadAsync(0, 27, observe: observed.Add);
            await DiscardAsync([new(1, 0, 0, 1)], 9545, rejected: true);
            await connection.SendAsync(connection.Command(GameProtocolEngine.BackToVillageCommand));
            await DiscardAsync([new(1, 0, 0, 9545)], 0);
            await DiscardAsync([new(1, 0, 0, 1)], 0, rejected: true);

            try
            {
                await connection.ReadAsync(0, 40, TimeSpan.FromMilliseconds(150), observed.Add);
            }
            catch (TimeoutException) { }
            check(!observed.Any(packet => packet.Type == 0 && packet.ProtocolId == 40),
                "town gold destruction never publishes a NOTI40 ground object, including after the ACK");
            var reopened = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items.Catalog);
            await reopened.InitializeAsync();
            check((await reopened.GetCharacterAsync("discard", character.Id))?.Gold == 0,
                "reopening the store retains every successful town debit exactly once");

            async Task DiscardAsync(Consumption[] entries, int expectedGold, bool rejected = false)
            {
                await connection.SendAsync(connection.Command(GameProtocolEngine.DeleteItemCommand, Arguments(entries)));
                var reply = await connection.ReadAsync(1, GameProtocolEngine.DeleteItemCommand, observe: observed.Add);
                var description = $"town gold delete [{string.Join(", ", entries)}]";
                if (rejected)
                    check(reply.Payload is [0, 17, 0], $"{description} rejects without deleting currency or items");
                else
                    check(reply.Payload.Length == 9 && reply.Payload[0] == 1 && reply.Payload[1] == 0
                        && reply.Payload[2] == 1
                        && BinaryPrimitives.ReadUInt16LittleEndian(reply.Payload.AsSpan(3)) == 0
                        && BinaryPrimitives.ReadUInt32LittleEndian(reply.Payload.AsSpan(5)) == entries[0].Count,
                        $"{description} acknowledges exactly the requested gold amount");
                await CheckStateAsync(expectedGold, description);
            }

            async Task CheckStateAsync(int expectedGold, string description)
            {
                var memory = (await store.GetCharacterAsync("discard", character.Id))!;
                using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(options.DataPath));
                var disk = snapshot.RootElement.GetProperty("Accounts").EnumerateArray()
                    .SelectMany(account => account.GetProperty("Characters").EnumerateArray())
                    .Single(entry => entry.GetProperty("Id").GetGuid() == character.Id)
                    .Deserialize<CharacterRecord>()!;
                check(memory.Gold == expectedGold && disk.Gold == expectedGold,
                    $"{description} has durable balance {expectedGold} before its reply is observed");
                foreach (var current in new[] { memory, disk })
                    check((current.Inventory ?? []).SequenceEqual(original.Inventory ?? [])
                        && (current.Equipment ?? []).SequenceEqual(original.Equipment ?? [])
                        && (current.Warehouse ?? []).SequenceEqual(original.Warehouse ?? [])
                        && (current.AvatarInventory ?? []).SequenceEqual(original.AvatarInventory ?? [])
                        && (current.CreatureInventory ?? []).SequenceEqual(original.CreatureInventory ?? []),
                        $"{description} preserves every item and item-space identity");
            }
        }
        finally { root.Delete(recursive: true); }
    }

    private readonly record struct Consumption(ushort Operation, ushort Slot, ushort ItemId, uint Count);

    private static byte[] Arguments(Consumption[] entries, byte space = 0)
    {
        // Captured native confirmation: sequence:u16, space:u8, entries:u8,
        // operation:u16, slot:u16, item:u16, count:u32, command-token:u32.
        var body = new byte[2 + 10 * entries.Length + 4];
        body[0] = space;
        body[1] = checked((byte)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 2 + 10 * index;
            var entry = entries[index];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), entry.Operation);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset + 2), entry.Slot);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset + 4), entry.ItemId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(offset + 6), entry.Count);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(body.Length - 4), 0x428B32D3);
        return body;
    }
}
