using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

internal static class ChapterTwoGuildSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check, string? wireDirectory = null)
    {
        var report = check;
        var assertions = 0;
        check = (passed, message) => { assertions++; report(passed, message); };
        var previous = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-guild-");
        try
        {
            await ChapterTwoGuildStoreSmokeTests.RunAsync(check);
            var info = GameProtocolEngine.CreateGuildInfo(GuildRules.Encode("Guild"), 1, 1);
            check(info.Payload[14] == 2 && info.Payload.Length == 35,
                "created guild info carries its native create state and both trailing strings");
            var memberPacket = GameProtocolEngine.CreateGuildMembers(70, 7, GuildRules.Encode("Guild"),
                [new(GuildRules.Encode("Hero"), 20, 1, 2, 0, 1, true)]);
            check(memberPacket.Payload.AsSpan(33).SequenceEqual(new byte[] { 20, 0, 1, 2, 0, 1, 0, 1, 0, 0, 0, 0 }),
                "guild member tail places rank in the original roster field consumed by the member context menu");
            if (wireDirectory is not null)
            {
                Directory.CreateDirectory(wireDirectory);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "guild-info.bin"), info.Payload);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "guild-created.bin"),
                    GameProtocolEngine.CreateGuildCreated(7, GuildRules.Encode("Guild")).Payload);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "guild-members.bin"),
                    memberPacket.Payload);
            }
            Directory.CreateDirectory(Path.Combine(root.FullName, "etc"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "etc", "guild.etc"), "[guild create cost] 300000\n");
            var options = new ServerOptions { DataPath = Path.Combine(root.FullName, "state.json"),
                ScriptPvfPath = "", SkillScriptPath = root.FullName, SeedTestAccount = false,
                Channel = new() { Port = 47001 } };
            var items = new ItemCatalog(new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance), NullLogger<ItemCatalog>.Instance);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("guildone", "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync("guildone", new("GuildOne"))).Character!;
            await store.SaveCharacterItemSpacesAsync("guildone", character.Id, 600000, character.Inventory ?? [], character.Equipment ?? [], []);
            uint persistedGuildId;
            {
                await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
                services.GetRequiredService<ILoggerFactory>().AddProvider(new FailureLoggerProvider());
                var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
                await connection.LoginAsync("guildone", character.Slot);
                await connection.EnableChapterTwoAsync();
                await connection.SendAsync(connection.Command(169));
                await connection.SendAsync(connection.Command(167, Text("测试公会")));
                GameServerPacket name;
                try { name = await connection.ReadAsync(1, 167, TimeSpan.FromSeconds(1)); }
                catch (TimeoutException) { check(false, "C2 native guild name check CMD167 receives a response"); return; }
                check(name.Payload.SequenceEqual(new byte[] { 1 }), "C2 guild name availability uses a single success byte");
                await connection.SendAsync(connection.Command(168, Text("guildone")));
                check((await connection.ReadAsync(1, 168)).Payload.SequenceEqual(new byte[] { 1 }), "C2 guild address can be validated independently");
                await connection.SendAsync(connection.Command(71));
                check((await connection.ReadAsync(1, 71)).Payload.SequenceEqual(new byte[] { 1 }), "native empty-body create command commits validated name and address");
                var created = await connection.ReadAsync(0, 71);
                check(created.Payload.Length > 8 && BinaryPrimitives.ReadUInt32LittleEndian(created.Payload) != 0
                    && created.Payload.AsSpan(4).SequenceEqual(Text("测试公会")), "guild creation notifies its real ID and CP936 name");
                check((await connection.ReadAsync(0, GameProtocolEngine.GuildSelfRankNotification)).Payload is [1],
                    "original guild creation installs the founder's local actor permission");
                await connection.SendAsync(connection.Command(70));
                var members = await connection.ReadAsync(1, 70);
                check(members.Payload.Length > 20 && members.Payload[0] == 1
                    && members.Payload.AsSpan().IndexOf(Encoding.ASCII.GetBytes("GuildOne")) >= 0,
                    "my guild member list contains the actual founding character");
                var stored = (await store.GetCharacterAsync("guildone", character.Id))!;
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(stored));
                check(stored.Gold == 300000 && json.RootElement.GetProperty("GuildId").GetUInt32() != 0,
                    "guild creation atomically persists membership and PVF-defined gold cost");
                persistedGuildId = stored.GuildId;
            }
            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await reloaded.InitializeAsync();
            await using var reloadedServices = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, reloaded, items);
            var reloadedEntrance = ActivatorUtilities.CreateInstance<EntranceService>(reloadedServices);
            await using var relog = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(reloadedEntrance, options, 47001);
            await relog.LoginAsync("guildone", character.Slot);
            await relog.EnableChapterTwoAsync();
            await relog.SendAsync(relog.Command(77));
            check((await relog.ReadAsync(1, 77)).Payload.SequenceEqual(new byte[] { 1 }),
                "restarted character can request its persisted guild information");
            var guildInfo = await relog.ReadAsync(0, 70);
            check(guildInfo.Payload.AsSpan().IndexOf(GuildRules.Encode("测试公会")) >= 0,
                "relogin guild information contains the persisted native name");
            await relog.SendAsync(relog.Command(70));
            var relogMembers = await relog.ReadAsync(1, 70);
            check(BinaryPrimitives.ReadUInt32LittleEndian(relogMembers.Payload.AsSpan(1)) == persistedGuildId
                && relogMembers.Payload.AsSpan().IndexOf(Encoding.ASCII.GetBytes("GuildOne")) >= 0
                && BinaryPrimitives.ReadUInt32LittleEndian(relog.MainList!.Payload.AsSpan(7)) == 300000,
                "relogin preserves guild membership and the single committed creation charge");
            await CheckDissolutionAsync(root.FullName, check);
            await CheckAnnouncementAsync(root.FullName, wireDirectory, check);
            Console.WriteLine($"Guild coverage: {assertions} assertions");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previous);
            root.Delete(true);
        }
    }

    private static async Task CheckAnnouncementAsync(string root, string? wireDirectory, Action<bool, string> check)
    {
        const byte changeAnnouncement = 165;
        const string ownerAccount = "noticeowner", deputyAccount = "noticedeputy", memberAccount = "noticemember";
        var options = new ServerOptions
        {
            ScriptPvfPath = "", SkillScriptPath = root, DataPath = Path.Combine(root, "announcement-state.json"),
            SeedTestAccount = false, Channel = new() { Port = 47001 }
        };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
        var seeding = NewStore();
        await seeding.InitializeAsync();
        await seeding.CreateAccountAsync(new(ownerAccount, "password", OicqCode: "123456"));
        await seeding.CreateAccountAsync(new(deputyAccount, "password", OicqCode: "123456"));
        await seeding.CreateAccountAsync(new(memberAccount, "password", OicqCode: "123456"));
        var owner = (await seeding.CreateCharacterAsync(ownerAccount, new("NoticeOwner"))).Character!;
        var deputy = (await seeding.CreateCharacterAsync(deputyAccount, new("NoticeDeputy"))).Character!;
        var member = (await seeding.CreateCharacterAsync(memberAccount, new("NoticeMember"))).Character!;
        await seeding.SaveCharacterItemSpacesAsync(ownerAccount, owner.Id, 600000, [], [], []);
        var guild = (await seeding.CreateGuildAsync(ownerAccount, owner.Id, "NoticeGuild", "noticeguild", 300000)).Guild!;
        var seeded = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
        foreach (var state in seeded["Accounts"]!.AsArray().SelectMany(account => account!["Characters"]!.AsArray()))
        {
            var id = state!["Id"]!.GetValue<Guid>();
            if (id == owner.Id) continue;
            state["GuildId"] = guild.Id;
            state["GuildName"] = guild.Name;
            state["GuildRank"] = id == deputy.Id ? 2 : 4;
        }
        await File.WriteAllTextAsync(options.DataPath, seeded.ToJsonString());

        var failSave = false;
        var replacement = new AtomicSnapshotReplacement((source, target) =>
        {
            if (failSave) throw new IOException("injected guild announcement write failure");
            File.Move(source, target, true);
        }, (_, _) => Task.CompletedTask);
        var store = NewStore(replacement);
        await store.InitializeAsync();
        var announcement = "明晚八点集合 " + new string('盾', 43) + "!"; // 100 CP936 bytes.
        var announcementField = Text(announcement);
        check(announcementField.Length == 104, "announcement fixture reaches the original 100-byte editor limit");
        await using (var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items))
        {
            await using var ownerConnection = await Connect(services, ownerAccount, owner.Slot);
            await using var deputyConnection = await Connect(services, deputyAccount, deputy.Slot);
            await using var memberConnection = await Connect(services, memberAccount, member.Slot);
            var before = await File.ReadAllBytesAsync(options.DataPath);
            var denied = await ChangeAsync(memberConnection, Text("越权发布"));
            var afterDenied = await File.ReadAllBytesAsync(options.DataPath);
            check(denied is [0, 36] && before.AsSpan().SequenceEqual(afterDenied),
                "an ordinary C2 guild member cannot change the persisted announcement");
            var deputyText = Text("副会长公告");
            var deputyReply = await ChangeAsync(deputyConnection, deputyText);
            var deputyPush = deputyReply is [1] ? await ReadAnnouncementAsync(deputyConnection) : [];
            var ownerSawDeputy = deputyReply is [1] ? await ReadAnnouncementAsync(ownerConnection) : [];
            var memberSawDeputy = deputyReply is [1] ? await ReadAnnouncementAsync(memberConnection) : [];
            check(deputyReply is [1] && deputyPush.AsSpan().SequenceEqual(deputyText)
                && ownerSawDeputy.AsSpan().SequenceEqual(deputyText)
                && memberSawDeputy.AsSpan().SequenceEqual(deputyText),
                "the native rank-two deputy can publish an announcement to the founder and other members");
            var empty = await ChangeAsync(ownerConnection, Text(""));
            var oversized = await ChangeAsync(ownerConnection, Text(announcement + "x"));
            var invalid = await ChangeAsync(ownerConnection, [1, 0, 0, 0, 0x81]);
            var nonZeroPadding = await ChangeAsync(ownerConnection, [.. Text("公告"), 1]);
            check(empty is [0, 108] && oversized is [0, 108] && invalid is [0, 108] && nonZeroPadding is [0, 108],
                "CMD165 rejects empty, oversized, malformed CP936 and nonzero padding");
            var updated = await ChangeAsync(ownerConnection, announcementField);
            check(updated is [1], "the founder's original AES/CRC CMD165 receives a native success reply");
            var ownerNotice = await ReadAnnouncementAsync(ownerConnection);
            var memberNotice = await ReadAnnouncementAsync(memberConnection);
            check(ownerNotice.AsSpan().SequenceEqual(announcementField) && memberNotice.AsSpan().SequenceEqual(announcementField),
                "NOTI153 pushes the exact announcement to both the founder and an idle online member");
            var after = await ReadInfoAsync(memberConnection);
            check(HasAnnouncement(after, announcementField),
                "a connected guild member's next NOTI70 contains the full native announcement field");
            if (wireDirectory is not null)
            {
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "announcement-command.bin"), announcementField);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "announcement-reply.bin"), updated);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "announcement-denied.bin"), denied);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "announcement-invalid.bin"), invalid);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "announcement-noti153.bin"), memberNotice);
                await File.WriteAllBytesAsync(Path.Combine(wireDirectory, "announcement-info.bin"), after);
            }
            var committed = await File.ReadAllBytesAsync(options.DataPath);
            failSave = true;
            var repeated = await ChangeAsync(ownerConnection, announcementField);
            var failed = await ChangeAsync(ownerConnection, Text("另一个公告"));
            failSave = false;
            var afterFailed = await File.ReadAllBytesAsync(options.DataPath);
            check(repeated is [1], "repeating the same C2 announcement does not require a second snapshot write");
            check(failed is [0, 17] && committed.AsSpan().SequenceEqual(afterFailed)
                && HasAnnouncement(await ReadInfoAsync(memberConnection), announcementField),
                "CMD165 write failure preserves the last committed announcement in memory and on disk");
        }
        var reloaded = NewStore();
        await reloaded.InitializeAsync();
        await using var reloadedServices = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, reloaded, items);
        await using var relog = await Connect(reloadedServices, memberAccount, member.Slot);
        check(HasAnnouncement(await ReadInfoAsync(relog), announcementField),
            "guild announcement survives store restart and member reconnect");

        JsonGameStore NewStore(AtomicSnapshotReplacement? writer = null) => writer is null
            ? new(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items)
            : new(options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: items);

        async Task<NewCharacterDefaultsSmokeTests.Connection> Connect(ServiceProvider provider, string account, byte slot)
        {
            var entrance = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
            await connection.LoginAsync(account, slot);
            await connection.EnableChapterTwoAsync();
            return connection;
        }

        static bool HasAnnouncement(byte[] body, byte[] field)
        {
            var start = 4 + "NoticeGuild".Length + 18;
            return body.Length == start + field.Length + 4 && body.AsSpan(start, field.Length).SequenceEqual(field)
                && body.AsSpan(start + field.Length).SequenceEqual(new byte[4]);
        }

        static async Task<byte[]> ReadInfoAsync(NewCharacterDefaultsSmokeTests.Connection connection)
        {
            await connection.SendAsync(connection.Command(77));
            if ((await connection.ReadAsync(1, 77)).Payload is not [1]) return [];
            return (await connection.ReadAsync(0, 70)).Payload;
        }

        static async Task<byte[]> ReadAnnouncementAsync(NewCharacterDefaultsSmokeTests.Connection connection)
        {
            try { return (await connection.ReadAsync(0, 153, TimeSpan.FromSeconds(1.5))).Payload; }
            catch (TimeoutException) { return []; }
        }

        static async Task<byte[]> ChangeAsync(NewCharacterDefaultsSmokeTests.Connection connection, byte[] payload)
        {
            var padded = new byte[2 + (payload.Length + 15) / 16 * 16];
            BinaryPrimitives.WriteUInt16LittleEndian(padded, 100);
            payload.CopyTo(padded, 2);
            await connection.SendAsync(ChapterTwoItemUseSmokeTests.EncryptChapterTwoCommand(changeAnnouncement,
                Convert.ToHexString(padded)));
            return (await connection.ReadAsync(1, changeAnnouncement)).Payload;
        }
    }

    private static async Task CheckDissolutionAsync(string root, Action<bool, string> check)
    {
        var options = new ServerOptions
        {
            ScriptPvfPath = "", SkillScriptPath = root, DataPath = Path.Combine(root, "dissolution-state.json"),
            SeedTestAccount = false, Channel = new() { Port = 47001 }
        };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
        var seeding = NewStore();
        await seeding.InitializeAsync();
        var owner = await NewCharacterAsync("breakowner", "BreakOwner");
        var member = await NewCharacterAsync("breakmember", "BreakMember");
        var offline = await NewCharacterAsync("breakoffline", "BreakOffline");
        await seeding.SaveCharacterItemSpacesAsync("breakowner", owner.Id, 700000, [], [], []);
        var result = await seeding.CreateGuildAsync("breakowner", owner.Id, "BreakGuild", "breakguild", 300000);
        var guild = result.Guild!;
        var seededState = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
        foreach (var character in seededState["Accounts"]!.AsArray()
            .SelectMany(account => account!["Characters"]!.AsArray()))
        {
            if (character!["Id"]!.GetValue<Guid>() != owner.Id)
            {
                character["GuildId"] = guild.Id;
                character["GuildName"] = guild.Name;
                character["GuildRank"] = 2;
            }
        }
        await File.WriteAllTextAsync(options.DataPath, seededState.ToJsonString());

        const byte dissolve = 193, notice = 187;
        var failSave = false;
        var replacement = new AtomicSnapshotReplacement((source, target) =>
        {
            if (failSave) throw new IOException("injected guild dissolution failure");
            File.Move(source, target, true);
        }, (_, _) => Task.CompletedTask);
        var failedStore = NewStore(replacement);
        await failedStore.InitializeAsync();
        await using (var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, failedStore, items))
        {
            await using var connection = await Connect(services, "breakowner", owner.Slot);
            var before = await File.ReadAllBytesAsync(options.DataPath);
            failSave = true;
            await connection.SendAsync(connection.Command(dissolve));
            var failure = await ReadResult(connection);
            failSave = false;
            var afterFailure = await File.ReadAllBytesAsync(options.DataPath);
            check(failure == 1 && before.SequenceEqual(afterFailure)
                && (await failedStore.GetGuildAsync(guild.Id)).Members.Length == 3,
                "a failed guild dissolution keeps the prior guild and all three characters in memory and on disk");
        }

        var store = NewStore();
        await store.InitializeAsync();
        await using (var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items))
        {
            await using var ownerConnection = await Connect(services, "breakowner", owner.Slot);
            await using var memberConnection = await Connect(services, "breakmember", member.Slot);
            await memberConnection.SendAsync(memberConnection.Command(dissolve));
            var nonOwner = await ReadResult(memberConnection);
            await ownerConnection.SendAsync(ownerConnection.Command(dissolve, 7));
            var malformed = await ReadResult(ownerConnection);
            check(nonOwner == 1 && malformed == 1 && (await store.GetGuildAsync(guild.Id)).Members.Length == 3,
                "only the founder may send an empty dissolution request");

            await ownerConnection.SendAsync(ownerConnection.Command(dissolve));
            var ownerNotice = await ReadResult(ownerConnection);
            var memberNotice = await ReadResult(memberConnection);
            check(ownerNotice == 0 && memberNotice == 0,
                "NOTI187 tells both the founder and the idle connected member that their guild dissolved");
            if (ownerNotice == 0 && memberNotice == 0)
            {
                await memberConnection.SendAsync(memberConnection.Command(77));
                check((await memberConnection.ReadAsync(1, 77)).Payload is [0, 32],
                    "an online former member cannot obtain the dissolved guild panel");
            }
            var snapshot = await store.GetGuildAsync(guild.Id);
            check(snapshot.Guild is null && snapshot.Members.Length == 0
                && (await store.GetCharacterAsync("breakowner", owner.Id)) is { GuildId: 0, GuildRank: 0, GuildName: null, Gold: 400000 }
                && (await store.GetCharacterAsync("breakmember", member.Id)) is { GuildId: 0, GuildRank: 0, GuildName: null }
                && (await store.GetCharacterAsync("breakoffline", offline.Id)) is { GuildId: 0, GuildRank: 0, GuildName: null }
                && await store.IsGuildNameAvailableAsync(guild.Name) && await store.IsGuildAddressAvailableAsync(guild.Address),
                "dissolution atomically clears all local members and releases the name without refunding gold");
            await ownerConnection.SendAsync(ownerConnection.Command(dissolve));
            check(await ReadResult(ownerConnection) == 1 && (await store.GetGuildAsync(guild.Id)).Guild is null,
                "repeating a dissolution after the guild is gone cannot remove anything else");
        }
        var reloaded = NewStore();
        await reloaded.InitializeAsync();
        check((await reloaded.GetGuildAsync(guild.Id)).Guild is null
            && (await reloaded.GetCharacterAsync("breakmember", member.Id))!.GuildId == 0
            && (await reloaded.GetCharacterAsync("breakoffline", offline.Id))!.GuildId == 0,
            "guild destruction and clearing online and offline memberships survive reconnect and a fresh store");

        JsonGameStore NewStore(AtomicSnapshotReplacement? writer = null) => writer is null
            ? new(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items)
            : new(options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: items);

        async Task<CharacterRecord> NewCharacterAsync(string account, string name)
        {
            await seeding.CreateAccountAsync(new(account, "password", OicqCode: "123456"));
            return (await seeding.CreateCharacterAsync(account, new(name, Level: 60))).Character!;
        }

        async Task<NewCharacterDefaultsSmokeTests.Connection> Connect(ServiceProvider provider, string account, byte slot)
        {
            var entrance = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
            await connection.LoginAsync(account, slot);
            await connection.EnableChapterTwoAsync();
            return connection;
        }

        static async Task<uint?> ReadResult(NewCharacterDefaultsSmokeTests.Connection connection)
        {
            try
            {
                var packet = await connection.ReadAsync(0, notice, TimeSpan.FromSeconds(1.5));
                return packet.Payload.Length == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload) : null;
            }
            catch (TimeoutException) { return null; }
        }
    }

    private static byte[] Text(string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(936).GetBytes(value);
        var result = new byte[4 + bytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, bytes.Length);
        bytes.CopyTo(result, 4);
        return result;
    }

    private sealed class FailureLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new FailureLogger();
        public void Dispose() { }
        private sealed class FailureLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (level >= LogLevel.Warning && exception is not null) Console.Error.WriteLine(exception.ToString());
            }
        }
    }
}
