using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
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
            check(memberPacket.Payload.AsSpan(33).SequenceEqual(new byte[] { 20, 0, 1, 2, 0, 1, 0, 0, 0, 0, 0, 0 }),
                "guild member tail stores ushort level, job, growth/awakening and offline seconds in native order");
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
            Console.WriteLine($"Guild coverage: {assertions} assertions");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previous);
            root.Delete(true);
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
