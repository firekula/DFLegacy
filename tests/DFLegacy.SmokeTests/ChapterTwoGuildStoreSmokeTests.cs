using System.Buffers.Binary;
using System.Text.Json;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoGuildStoreSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-guild-store-");
        try
        {
            var options = new ServerOptions { DataPath = Path.Combine(root.FullName, "state.json"), SeedTestAccount = false };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("guilds", "password", OicqCode: "123456"));
            var first = (await store.CreateCharacterAsync("guilds", new("First"))).Character!;
            var second = (await store.CreateCharacterAsync("guilds", new("Second"))).Character!;
            var third = (await store.CreateCharacterAsync("guilds", new("Third"))).Character!;
            await store.SaveCharacterItemSpacesAsync("guilds", first.Id, 600000, [], [], []);
            await store.SaveCharacterItemSpacesAsync("guilds", second.Id, 600000, [], [], []);
            await store.SaveCharacterItemSpacesAsync("guilds", third.Id, 600000, [], [], []);
            var legacyAccounts = JsonSerializer.Serialize(await store.GetAccountsAsync());
            check(!GuildRules.IsName("") && !GuildRules.IsName(" name") && !GuildRules.IsName("a\0b")
                && !GuildRules.IsName("😀") && GuildRules.IsName("一二三四五六七") && !GuildRules.IsName("一二三四五六七八"),
                "guild names enforce strict CP936 round-trip, fourteen-byte length and control/edge whitespace rejection");
            check(GuildRules.IsAddress("Guild_1") && !GuildRules.IsAddress("_bad") && !GuildRules.IsAddress("ab")
                && !GuildRules.IsAddress("一二三") && !GuildRules.IsAddress("bad/path"),
                "guild addresses enforce the native bounded ASCII identifier");
            var poor = await store.CreateGuildAsync("guilds", first.Id, "Poor", "poor", 700000);
            check(poor.Error == 31 && (await store.GetCharacterAsync("guilds", first.Id))!.GuildId == 0,
                "insufficient guild creation gold leaves character and guild state unchanged");
            var results = await Task.WhenAll(
                store.CreateGuildAsync("guilds", first.Id, "Shared", "first", 300000),
                store.CreateGuildAsync("guilds", second.Id, "shared", "second", 300000));
            check(results.Count(result => result.Error == 0) == 1 && results.Count(result => result.Error == 107) == 1,
                "concurrent case-equivalent guild names commit exactly one founder and one gold charge");
            var winner = results.Single(result => result.Error == 0);
            var guild = winner.Guild!;
            var repeated = await store.CreateGuildAsync("guilds", winner.Character!.Id, guild.Name, guild.Address, 300000);
            check(repeated.Replayed && repeated.Character!.Gold == 300000,
                "replayed guild creation preserves the original identity and charges once");
            check((await store.CreateGuildAsync("guilds", winner.Character.Id, "Another", "another", 300000)).Error == 32,
                "an existing member cannot found another guild");
            check((await store.CreateGuildAsync("guilds", third.Id, "Unique", guild.Address.ToUpperInvariant(), 300000)).Error == 111,
                "guild address uniqueness is checked atomically and case-insensitively");
            var loser = winner.Character.Id == first.Id ? second : first;
            var addressRace = await Task.WhenAll(
                store.CreateGuildAsync("guilds", loser.Id, "AddressOne", "sameaddr", 300000),
                store.CreateGuildAsync("guilds", third.Id, "AddressTwo", "SAMEADDR", 300000));
            check(addressRace.Count(result => result.Error == 0) == 1
                && addressRace.Count(result => result.Error == 111) == 1,
                "concurrent case-equivalent guild addresses commit exactly one guild");
            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync();
            var snapshot = await reloaded.GetGuildAsync(guild.Id);
            check(snapshot.Guild == guild && snapshot.Members.Length == 1 && snapshot.Members[0].Id == winner.Character.Id,
                "guild and real membership survive a complete store restart");
            var session = new GuildSession(reloaded, new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance),
                new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance));
            PacketFrame Command(byte id, byte[]? body = null) => new(1, id, 0, body ?? [0, 0]);
            byte[] Name(string value)
            {
                var bytes = GuildRules.Encode(value);
                var body = new byte[6 + bytes.Length];
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), bytes.Length);
                bytes.CopyTo(body, 6);
                return body;
            }
            await session.HandleAsync(Command(167, Name("Fresh")), "guilds", first, 1, true, default);
            await session.HandleAsync(Command(168, Name("fresh")), "guilds", first, 1, true, default);
            var changed = await session.HandleAsync(Command(71), "guilds", third, 3, true, default);
            check(changed.Packets[0].Payload.SequenceEqual(new byte[] { 0, 109 }),
                "switching character clears a different character's checked name and address");
            await session.HandleAsync(Command(167, Name("Fresh")), "guilds", third, 3, true, default);
            await session.HandleAsync(Command(168, Name("fresh")), "guilds", third, 3, true, default);
            session.Reset();
            var reset = await session.HandleAsync(Command(71), "guilds", third, 3, true, default);
            check(reset.Packets[0].Payload.SequenceEqual(new byte[] { 0, 109 }), "reselection resets pending guild validation");
            var malformed = await session.HandleAsync(Command(167, [0, 0, 255, 255, 255, 127]), "guilds", third, 3, true, default);
            check(malformed.Packets[0].Payload.SequenceEqual(new byte[] { 0, 106 }), "malformed native guild lengths receive a bounded failure");
            var badName = await session.HandleAsync(Command(167, Name("一二三四五六七八")), "guilds", third, 3, true, default);
            var badAddress = await session.HandleAsync(Command(168, Name("bad/name")), "guilds", third, 3, true, default);
            check(badName.Packets[0].Payload.SequenceEqual(new byte[] { 0, 108 })
                && badAddress.Packets[0].Payload.SequenceEqual(new byte[] { 0, 112 }),
                "invalid name and address preserve the native distinct validation error codes");
            var padded = new byte[18];
            Name("A").CopyTo(padded, 0);
            check(GameProtocolEngine.TryReadGuildText(padded, out var parsed) && parsed.SequenceEqual(new byte[] { 65 }),
                "guild string reader accepts the exact native C2 zero padding");
            padded[^1] = 1;
            check(!GameProtocolEngine.TryReadGuildText(padded, out _), "nonzero guild command padding is rejected");
            foreach (var command in new byte[] { 76, 78, 162, 163, 164, 165, 166 })
            {
                var unsupported = await session.HandleAsync(Command(command), "guilds", third, 3, true, default);
                check(unsupported.Packets.Any(packet => packet.Type == 0 && packet.ProtocolId == GameProtocolEngine.MessageNotification),
                    $"unimplemented guild menu command {command} returns explicit in-game feedback");
            }
            var legacyOptions = new ServerOptions { DataPath = Path.Combine(root.FullName, "legacy.json"), SeedTestAccount = false };
            await File.WriteAllTextAsync(legacyOptions.DataPath, legacyAccounts);
            var legacy = new JsonGameStore(legacyOptions, NullLogger<JsonGameStore>.Instance);
            await legacy.InitializeAsync();
            check((await legacy.GetGuildAsync(1)).Guild is null
                && (await legacy.GetCharactersAsync("guilds")).All(character => character.GuildId == 0),
                "legacy account-array saves migrate with an empty guild collection and no invented memberships");
            await CheckFailureAsync(root.FullName, false, check);
            await CheckFailureAsync(root.FullName, true, check);
            await CheckPendingTradeAsync(root.FullName, check);
            await CheckGuildRemovalAsync(root.FullName, check);
            await CheckGuildRemovalFailureAsync(root.FullName, false, check);
            await CheckGuildRemovalFailureAsync(root.FullName, true, check);
        }
        finally { root.Delete(true); }
    }

    private static async Task CheckGuildRemovalAsync(string root, Action<bool, string> check)
    {
        foreach (var operation in new[] { "account-name", "account-uid", "replace-roster" })
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root, $"remove-{operation}.json"),
                SeedTestAccount = false
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("owner", "password", OicqCode: "123456"));
            var owner = (await store.CreateCharacterAsync("owner", new("Owner"))).Character!;
            await store.SaveCharacterItemSpacesAsync("owner", owner.Id, 600000, [], [], []);
            var created = await store.CreateGuildAsync("owner", owner.Id, "OwnerGuild", "owner", 300000);
            var removed = operation switch
            {
                "account-name" => (await store.DeleteCharacterAsync("owner", owner.Slot)).Deleted,
                "account-uid" => (await store.DeleteCharacterAsync(owner.AccountUid, owner.Slot)).Deleted,
                _ => (await store.ReplaceAllCharactersAsync("owner", [])).Replaced
            };
            var remaining = await store.GetGuildAsync(created.Guild!.Id);
            check(removed && remaining.Guild is null && remaining.Members.Length == 0
                && await store.IsGuildNameAvailableAsync("OwnerGuild")
                && await store.IsGuildAddressAvailableAsync("owner"),
                $"{operation} atomically removes the empty guild and releases its name and address");
            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync();
            check((await reloaded.GetGuildAsync(created.Guild.Id)).Guild is null
                && (await reloaded.GetCharactersAsync("owner")).Length == 0,
                $"{operation} persists the character and empty-guild removal together");
        }
    }

    private static async Task CheckGuildRemovalFailureAsync(string root, bool after, Action<bool, string> check)
    {
        var options = new ServerOptions
        {
            DataPath = Path.Combine(root, $"remove-failure-{after}.json"),
            SeedTestAccount = false
        };
        var writer = new FailureWriter(after);
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, writer);
        await store.InitializeAsync();
        await store.CreateAccountAsync(new("owner", "password", OicqCode: "123456"));
        var owner = (await store.CreateCharacterAsync("owner", new("Owner"))).Character!;
        await store.SaveCharacterItemSpacesAsync("owner", owner.Id, 600000, [], [], []);
        var guild = (await store.CreateGuildAsync("owner", owner.Id, "OwnerGuild", "owner", 300000)).Guild!;
        writer.Armed = true;
        var deletion = await store.DeleteCharacterAsync("owner", owner.Slot);
        check(!deletion.Deleted && (await store.GetGuildAsync(guild.Id)).Guild is not null
            && (await store.GetCharactersAsync("owner")).Length == 1,
            $"guild removal failure afterReplace={after} exposes neither an unconfirmed deletion nor name release");
        var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        await reloaded.InitializeAsync();
        check(((await reloaded.GetGuildAsync(guild.Id)).Guild is null) == after
            && ((await reloaded.GetCharactersAsync("owner")).Length == 0) == after,
            $"guild removal recovery afterReplace={after} restores a consistent guild and roster snapshot");
    }

    private static async Task CheckPendingTradeAsync(string root, Action<bool, string> check)
    {
        var options = new ServerOptions
        {
            DataPath = Path.Combine(root, "pending-trade.json"),
            SeedTestAccount = false,
            ScriptPvfPath = "",
            SkillScriptPath = root
        };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var catalog = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
        var stats = new CharacterStatCatalog(scripts, NullLogger<CharacterStatCatalog>.Instance);
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance,
            itemCatalog: catalog, statCatalog: stats);
        await store.InitializeAsync();
        await store.CreateAccountAsync(new("pending", "password", OicqCode: "123456"));
        var first = (await store.CreateCharacterAsync("pending", new("Trader"))).Character!;
        var second = (await store.CreateCharacterAsync("pending", new("Partner"))).Character!;
        first = (await store.SaveCharacterItemSpacesAsync("pending", first.Id, 600000, [], [], []))!;
        second = (await store.SaveCharacterItemSpacesAsync("pending", second.Id, 600000, [], [], []))!;
        var trade = await store.CommitItemTradeAsync(new(first.Id, "pending", second.Id, "pending", [], [], 0, 0));
        check(trade.Committed && store.IsTradeDeliveryPending(first.Id),
            "the guild regression fixture has a committed trade awaiting client installation");

        var blocked = await store.CreateGuildAsync("pending", first.Id, "Pending", "pending", 300000);
        var current = (await store.GetCharacterAsync("pending", first.Id))!;
        check(blocked.Error == 17 && current.Gold == 600000 && current.GuildId == 0
            && await store.IsGuildNameAvailableAsync("Pending"),
            "guild creation waits for pending trade delivery without charging gold or reserving a guild name");

        store.AcknowledgeTradeDelivery(first.Id, trade.TradeId);
        var delivered = trade.Initiator!;
        await store.SaveCharacterItemSpacesAsync("pending", first.Id, delivered.Gold,
            delivered.Inventory ?? [], delivered.Equipment ?? [], delivered.Warehouse ?? []);
        var created = await store.CreateGuildAsync("pending", first.Id, "Pending", "pending", 300000);
        check(created.Error == 0 && created.Character!.Gold == 300000,
            "guild creation succeeds and charges once after the trade snapshot is installed");
    }

    private static async Task CheckFailureAsync(string root, bool after, Action<bool, string> check)
    {
        var options = new ServerOptions { DataPath = Path.Combine(root, $"failure-{after}.json"), SeedTestAccount = false };
        var writer = new FailureWriter(after);
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, writer);
        await store.InitializeAsync();
        await store.CreateAccountAsync(new("failure", "password", OicqCode: "123456"));
        var character = (await store.CreateCharacterAsync("failure", new("Failure"))).Character!;
        await store.SaveCharacterItemSpacesAsync("failure", character.Id, 600000, [], [], []);
        writer.Armed = true;
        var result = await store.CreateGuildAsync("failure", character.Id, "Failure", "failure", 300000);
        check(result.Unknown && (await store.GetCharacterAsync("failure", character.Id))!.GuildId == 0
            && (await store.GetCharacterAsync("failure", character.Id))!.Gold == 600000,
            $"guild write failure afterReplace={after} publishes no unconfirmed membership or gold");
        var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        await reloaded.InitializeAsync();
        var retry = await reloaded.CreateGuildAsync("failure", character.Id, "Failure", "failure", 300000);
        check(retry.Error == 0 && retry.Replayed == after && retry.Character!.Gold == 300000
            && (await reloaded.GetGuildAsync(retry.Guild!.Id)).Members.Length == 1,
            $"guild restart recovery afterReplace={after} creates once and retains one gold charge");
        await reloaded.GrantAdminVictoryPointsAsync("gm", Guid.NewGuid(), character.Id, 5);
        var reread = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        await reread.InitializeAsync();
        check((await reread.GetGuildAsync(retry.Guild!.Id)).Members.Length == 1,
            "unrelated privileged snapshot writes retain the guild collection");
    }

    private sealed class FailureWriter(bool after) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (Armed && !after) throw new IOException("guild before replacement");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, bytes, cancellationToken);
            if (Armed) throw new IOException("guild after replacement");
        }
    }
}
