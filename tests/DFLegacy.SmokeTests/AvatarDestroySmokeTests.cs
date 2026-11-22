using System.Buffers.Binary;
using System.Text.Json;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AvatarDestroySmokeTests
{
    // Real CMD18 traffic for the avatar bag (item space 1): the client's destroy
    // button must delete a bag avatar, persist it, and leave the worn list and the
    // main inventory untouched. Worn avatars (space 3) are unequipped first, so a
    // list-3 delete is not part of this transaction.
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var items = new ActiveDropItemFixture();
        var root = Directory.CreateTempSubdirectory("dflegacy-avatar-destroy-");
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root.FullName, "state.json"),
                SeedTestAccount = false, ScriptPvfPath = "", SkillScriptPath = root.FullName
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items.Catalog);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("avatar", "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync("avatar", new("AvatarDestroy"))).Character!;
            await store.SaveCharacterItemSpacesAsync("avatar", character.Id, 1000,
                [new(9, 50010, 1, InstanceId: Guid.NewGuid())], [], [],
                avatarInventory: [new(0, 50017, 1), new(10, 50017, 1)]);
            var original = (await store.GetCharacterAsync("avatar", character.Id))!;

            await using var provider = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items.Catalog);
            var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, options);
            await connection.LoginAsync("avatar", character.Slot);

            await connection.SendAsync(connection.Command(GameProtocolEngine.DeleteItemCommand,
                Arguments(1, (1, 0, 50017, 1))));
            var reply = await connection.ReadAsync(1, GameProtocolEngine.DeleteItemCommand);
            check(reply.Payload is [1, 1, 1, 0, 0, 1, 0, 0, 0],
                "avatar destroy acknowledges list 1, slot 0, count 1");

            var refresh = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification);
            check(refresh.Payload.Length >= 15
                && refresh.Payload[0] == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(refresh.Payload.AsSpan(1)) == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(refresh.Payload.AsSpan(3)) == 10,
                "avatar destroy refreshes list 1 with the remaining slot 10 only");

            var after = (await store.GetCharacterAsync("avatar", character.Id))!;
            var disk = await ReadDiskCharacterAsync(options.DataPath, character.Id);
            foreach (var current in new[] { after, disk })
            {
                check(!(current.AvatarInventory ?? []).Any(item => item.Slot == 0),
                    "destroyed avatar slot is gone from avatar inventory");
                check((current.AvatarInventory ?? []).Any(item => item.Slot == 10 && item.ItemId == 50017),
                    "other avatar slots survive the destroy");
                check((current.Inventory ?? []).SequenceEqual(original.Inventory ?? [])
                    && (current.Equipment ?? []).SequenceEqual(original.Equipment ?? []),
                    "main inventory and worn equipment are untouched by an avatar destroy");
            }

            // A stale or wrong id is rejected without deleting the remaining slot.
            await connection.SendAsync(connection.Command(GameProtocolEngine.DeleteItemCommand,
                Arguments(1, (1, 10, 50018, 1))));
            var stale = await connection.ReadAsync(1, GameProtocolEngine.DeleteItemCommand);
            check(stale.Payload is [0, 17, 1], "avatar destroy rejects a mismatched item id");
            var final = (await store.GetCharacterAsync("avatar", character.Id))!;
            check((final.AvatarInventory ?? []).Any(item => item.Slot == 10 && item.ItemId == 50017),
                "rejected avatar destroys keep the remaining slot");
        }
        finally { root.Delete(recursive: true); }
    }

    private static async Task<CharacterRecord> ReadDiskCharacterAsync(string path, Guid id)
    {
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return snapshot.RootElement.GetProperty("Accounts").EnumerateArray()
            .SelectMany(account => account.GetProperty("Characters").EnumerateArray())
            .Single(entry => entry.GetProperty("Id").GetGuid() == id)
            .Deserialize<CharacterRecord>()!;
    }

    private static byte[] Arguments(byte space, params (ushort Operation, ushort Slot, ushort ItemId, uint Count)[] entries)
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
