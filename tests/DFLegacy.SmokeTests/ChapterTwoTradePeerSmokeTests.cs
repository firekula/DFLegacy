using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Connection = NewCharacterDefaultsSmokeTests.Connection;

internal static class ChapterTwoTradePeerSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-trade-peer-");
        var previousCipher = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        try
        {
            var options = new ServerOptions { DataPath = Path.Combine(root.FullName, "state.json"),
                ScriptPvfPath = "", SkillScriptPath = root.FullName, SeedTestAccount = false,
                Channel = new() { Port = 47001 } };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
            var seed = CreateStore();
            await seed.InitializeAsync();
            var createdAfter = DateTimeOffset.UtcNow;
            var owner = await Create("TradeOwner");
            var partner = await Create("TradePartner");
            var older = await Create("TradeLegacy");
            var createdBefore = DateTimeOffset.UtcNow;
            var state = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            var rows = state["Accounts"]!.AsArray().SelectMany(account => account!["Characters"]!.AsArray())
                .ToDictionary(row => row!["Id"]!.GetValue<Guid>(), row => row!.AsObject());
            check(new[] { owner, partner, older }.All(character =>
                    rows[character.Id]["CreatedAtUtc"]?.GetValue<DateTimeOffset>() is { } time
                    && time >= createdAfter && time <= createdBefore),
                "new characters persist their actual UTC creation time in the character snapshot");

            const uint ownerTime = 1700000000;
            const uint partnerTime = 1500000000;
            rows[owner.Id]["CreatedAtUtc"] = DateTimeOffset.FromUnixTimeSeconds(ownerTime);
            rows[partner.Id]["CreatedAtUtc"] = DateTimeOffset.FromUnixTimeSeconds(partnerTime);
            rows[older.Id].Remove("CreatedAtUtc");
            await File.WriteAllTextAsync(options.DataPath, state.ToJsonString());

            var store = CreateStore();
            await store.InitializeAsync();
            await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
            var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
            var towns = services.GetRequiredService<TownSessionDirectory>();
            var trade = services.GetRequiredService<ItemTradeService>();
            await using var c2 = await Connect(owner, chapterTwo: true);
            await using var c1 = await Connect(partner, chapterTwo: false);
            await using var c2Unknown = await Connect(older, chapterTwo: true);
            var ownerUid = Uid(owner);
            var partnerUid = Uid(partner);
            var olderUid = Uid(older);
            const uint requestWeight = 0xffff8c1d;
            const uint acceptWeight = 0xffff8c55;

            await c2.SendAsync(c2.Command(GameProtocolEngine.RequestPeerCommand, Peer(partnerUid, requestWeight)));
            var c1Request = await c1.ReadAsync(0, GameProtocolEngine.RequestPeerNotification);
            check(c1Request.Payload.AsSpan().SequenceEqual(Peer(ownerUid, requestWeight)),
                "a Chapter Two initiator sends the ACT1 target the ordinary seven-byte NOTI7");
            await c1.SendAsync(c1.Command(GameProtocolEngine.ResponsePeerCommand, Peer(ownerUid, acceptWeight)));
            var c2Accepted = await c2.ReadAsync(0, GameProtocolEngine.ResponsePeerNotification);
            check(c2Accepted.Payload.AsSpan().SequenceEqual((byte[])[.. Peer(partnerUid, acceptWeight), 0, .. UInt(partnerTime)]),
                "the Chapter Two initiator receives the ACT1 accepter's creation time after mode in NOTI8");
            await c1.ReadAsync(1, GameProtocolEngine.ResponsePeerCommand);
            await trade.CancelForAreaChangeAsync(owner.Id);

            await c1.SendAsync(c1.Command(GameProtocolEngine.RequestPeerCommand, Peer(ownerUid, requestWeight)));
            var c2Request = await c2.ReadAsync(0, GameProtocolEngine.RequestPeerNotification);
            check(c2Request.Payload.AsSpan().SequenceEqual((byte[])[.. Peer(partnerUid, requestWeight), .. UInt(partnerTime)]),
                "the Chapter Two target receives the ACT1 initiator's creation time in NOTI7");
            await c2.SendAsync(c2.Command(GameProtocolEngine.ResponsePeerCommand, Peer(partnerUid, acceptWeight)));
            var c1Accepted = await c1.ReadAsync(0, GameProtocolEngine.ResponsePeerNotification);
            check(c1Accepted.Payload.AsSpan().SequenceEqual((byte[])[.. Peer(ownerUid, acceptWeight), 0]),
                "a Chapter Two accepter sends the ACT1 initiator the ordinary eight-byte NOTI8");
            await c2.ReadAsync(1, GameProtocolEngine.ResponsePeerCommand);
            await trade.CancelForAreaChangeAsync(owner.Id);

            await c2Unknown.SendAsync(c2Unknown.Command(GameProtocolEngine.RequestPeerCommand, Peer(ownerUid, requestWeight)));
            var unknown = await c2.ReadAsync(0, GameProtocolEngine.RequestPeerNotification);
            check(unknown.Payload.AsSpan().SequenceEqual((byte[])[.. Peer(olderUid, requestWeight), 0, 0, 0, 0]),
                "a pre-existing character without a saved creation time supplies an explicit zero to Chapter Two");
            await c2.SendAsync(c2.Command(GameProtocolEngine.ResponsePeerCommand, Peer(olderUid, acceptWeight)));
            var peerAccepted = await c2Unknown.ReadAsync(0, GameProtocolEngine.ResponsePeerNotification);
            check(peerAccepted.Payload.AsSpan().SequenceEqual((byte[])[.. Peer(ownerUid, acceptWeight), 0, .. UInt(ownerTime)]),
                "both Chapter Two connections retain the correct party's independently recorded time after store reload");

            JsonGameStore CreateStore() => new(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);

            async Task<CharacterRecord> Create(string account)
            {
                await seed.CreateAccountAsync(new(account, "password", OicqCode: "123456"));
                return (await seed.CreateCharacterAsync(account, new(account))).Character!;
            }

            async Task<Connection> Connect(CharacterRecord character, bool chapterTwo)
            {
                var connection = await Connection.CreateAsync(entrance, options, 47001);
                await connection.LoginAsync(character.Name, character.Slot);
                if (chapterTwo) await connection.EnableChapterTwoAsync();
                await connection.SendAsync(connection.Command(GameProtocolEngine.SetUserAreaCommand,
                    1, 1, 0xda, 1, 0xea, 0));
                await connection.ReadAsync(0, GameProtocolEngine.EnterGameWorldCompleteNotification);
                return connection;
            }

            ushort Uid(CharacterRecord character) => towns.TryGetSnapshot(character.Id, out var snapshot)
                ? snapshot.AreaUserId : throw new InvalidOperationException($"{character.Name} did not join town");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previousCipher);
            root.Delete(true);
        }
    }

    private static byte[] Peer(ushort uid, uint weight)
    {
        var bytes = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, uid);
        bytes[2] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3), weight);
        return bytes;
    }

    private static byte[] UInt(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
