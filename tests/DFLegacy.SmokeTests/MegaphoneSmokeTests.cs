using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MegaphoneSmokeTests
{
    // Real CMD 17 traffic: type 11/12 consumes a server megaphone (item 36) and
    // broadcasts NOTI 118; type 13 consumes a channel megaphone (item 37). The
    // original client never sends CMD 47 for a megaphone.
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-megaphone-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "stackable"));
            File.WriteAllText(Path.Combine(root.FullName, "stackable/stackable.lst"),
                "36 `mega36.stk`\n37 `mega37.stk`\n3176 `free.stk`\n");
            foreach (var (id, name) in new[] { (36, "ServerMegaphone"), (37, "ChannelMegaphone") })
            {
                File.WriteAllText(Path.Combine(root.FullName, $"stackable/mega{id}.stk"),
                    $"[name] `{name}`\n[stackable type] `[waste]`\n[attach type] `[free]`\n"
                    + "[weight] 1\n[stack limit] 10\n[price] 100\n");
            }
            File.WriteAllText(Path.Combine(root.FullName, "stackable/free.stk"),
                "[name] `Free stack`\n[stackable type] `[material]`\n[attach type] `[free]`\n"
                + "[weight] 1\n[stack limit] 99\n");

            var options = new ServerOptions
            {
                DataPath = Path.Combine(root.FullName, "state.json"),
                SeedTestAccount = false, ScriptPvfPath = "", SkillScriptPath = root.FullName
            };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("mega", "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync("mega", new("Mega"))).Character!;
            await store.SaveCharacterItemSpacesAsync("mega", character.Id, 0,
                [new(41, 36, 2), new(42, 37, 2), new(73, 3176, 1)], [], [],
                avatarInventory: [], creatureInventory: []);

            await using var provider = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
            var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, options);
            await connection.LoginAsync("mega", character.Slot);

            await connection.SendAsync(connection.Command(GameProtocolEngine.SendMessageCommand,
                ChatArguments(11, 41, "server hello")));
            var refresh = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification);
            check(refresh.Payload[0] == 0, "the megaphone use refreshes the main inventory");
            var broadcast = await connection.ReadAsync(0, GameProtocolEngine.MegaphoneMessageNotification);
            check(broadcast.Payload[0] == 11
                && BinaryPrimitives.ReadUInt32LittleEndian(broadcast.Payload.AsSpan(4)) == 4
                && Encoding.ASCII.GetString(broadcast.Payload, 8, 4) == "Mega"
                && Encoding.ASCII.GetString(
                    broadcast.Payload, 12 + 4, "server hello".Length) == "server hello",
                "a server megaphone broadcasts NOTI 118 type 11 with the sender and text");
            var afterServerMega = (await store.GetCharacterAsync("mega", character.Id))!;
            check(afterServerMega.Inventory!.Single(item => item.Slot == 41).CountOrValue == 1,
                "using a server megaphone consumed exactly one item");

            await connection.SendAsync(connection.Command(GameProtocolEngine.SendMessageCommand,
                ChatArguments(13, 42, "channel hello")));
            var channelRefresh = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification);
            check(channelRefresh.Payload[0] == 0, "the channel megaphone refreshes the main inventory");
            var channelBroadcast = await connection.ReadAsync(0, GameProtocolEngine.MegaphoneMessageNotification);
            check(channelBroadcast.Payload[0] == 13
                && channelBroadcast.Payload[1] == checked((byte)options.Channel.ChannelNumber),
                "a channel megaphone broadcasts NOTI 118 type 13 with the sender channel");
            var afterChannel = (await store.GetCharacterAsync("mega", character.Id))!;
            check(afterChannel.Inventory!.Single(item => item.Slot == 42).CountOrValue == 1,
                "using a channel megaphone consumed exactly one item");

            // Wrong item for the type: no broadcast, no consume, CMD 17 error 17.
            await connection.SendAsync(connection.Command(GameProtocolEngine.SendMessageCommand,
                ChatArguments(13, 41, "wrong")));
            var error = await connection.ReadAsync(1, GameProtocolEngine.SendMessageCommand);
            check(error.Payload[0] == 0 && error.Payload[1] == 17,
                "a channel megaphone rejects the server megaphone item");
            var afterRejected = (await store.GetCharacterAsync("mega", character.Id))!;
            check(afterRejected.Inventory!.Single(item => item.Slot == 41).CountOrValue == 1,
                "a rejected megaphone does not consume the item");
        }
        finally { root.Delete(recursive: true); }
    }

    private static byte[] ChatArguments(byte type, ushort slot, string text)
    {
        var message = Encoding.ASCII.GetBytes(text);
        var body = new byte[1 + 2 + 4 + 4 + message.Length];
        body[0] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(3), slot);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(7), checked((uint)message.Length));
        message.CopyTo(body, 11);
        return body;
    }
}
