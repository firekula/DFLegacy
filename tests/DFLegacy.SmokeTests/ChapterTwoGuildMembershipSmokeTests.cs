using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Connection = NewCharacterDefaultsSmokeTests.Connection;

internal static class ChapterTwoGuildMembershipSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check, string? wireDirectory = null)
    {
        var previousCipher = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        var root = Directory.CreateTempSubdirectory("dflegacy-guild-membership-");
        try
        {
            var names = new[] { "GuildFounder", "GuildDeputy", "GuildLeaver", "GuildKicked" };
            var options = new ServerOptions { DataPath = Path.Combine(root.FullName, "state.json"),
                ScriptPvfPath = "", SkillScriptPath = root.FullName, SeedTestAccount = false,
                Channel = new() { Port = 47001 } };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
            var seed = CreateStore();
            await seed.InitializeAsync();
            var roster = new List<CharacterRecord>();
            foreach (var name in names)
            {
                await seed.CreateAccountAsync(new(name, "password", OicqCode: "123456"));
                roster.Add((await seed.CreateCharacterAsync(name, new(name))).Character!);
            }
            await seed.SaveCharacterItemSpacesAsync(names[0], roster[0].Id, 600000, [], [], []);
            var guild = (await seed.CreateGuildAsync(names[0], roster[0].Id, "测试公会", "rankguild", 300000)).Guild!;
            var state = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            foreach (var entry in state["Accounts"]!.AsArray().SelectMany(account => account!["Characters"]!.AsArray()))
            {
                if (entry!["Id"]!.GetValue<Guid>() == roster[0].Id) continue;
                entry["GuildId"] = guild.Id;
                entry["GuildName"] = guild.Name;
                entry["GuildRank"] = 4;
            }
            await File.WriteAllTextAsync(options.DataPath, state.ToJsonString());

            bool rejectWrites = false;
            var replacement = new AtomicSnapshotReplacement((source, target) =>
            {
                if (rejectWrites) throw new IOException("injected membership snapshot failure");
                File.Move(source, target, true);
            }, (_, _) => Task.CompletedTask);
            var store = CreateStore(replacement);
            await store.InitializeAsync();
            await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
            var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
            await using var founder = await Connect(0);
            await using var deputy = await Connect(1);
            await using var leaver = await Connect(2);
            await using var kicked = await Connect(3);

            await Reject(founder, 164, new byte[4], 87, "founder cannot leave a guild without transferring leadership");
            await Reject(founder, 166, Text(names[1]), 86, "only a deputy can receive leadership");
            await Reject(leaver, 136, [.. Text(names[1]), 2], 83, "a member cannot appoint an unrelated deputy");
            await Reject(leaver, 164, Text(names[3]), 36, "a regular member cannot expel another member");

            await founder.SendAsync(founder.Command(136, [.. Text(names[1]), 2]));
            var ranked = await founder.ReadAsync(1, 136);
            check(ranked.Payload.AsSpan().SequenceEqual((byte[])[1, 0, .. Text(names[1]), 2]),
                "CMD136 succeeds with native status, target name and deputy rank");
            var deputyRank = await deputy.ReadAsync(0, 139);
            var rankNotice = await deputy.ReadAsync(0, 152);
            check(deputyRank.Payload is [2] && rankNotice.Payload is [2]
                && (await deputy.ReadAsync(0, 70)).Payload[Text(guild.Name).Length] == 0,
                "idle deputy gets both local permissions and roster rank updates");
            check((await store.GetCharacterAsync(names[1], roster[1].Id))?.GuildRank == 2,
                "deputy appointment commits the target's rank");

            await founder.SendAsync(founder.Command(166, Text(names[1])));
            var transferred = await founder.ReadAsync(1, 166);
            check(transferred.Payload is [1] && (await founder.ReadAsync(0, 139)).Payload is [2]
                && (await founder.ReadAsync(0, 152)).Payload is [2]
                && (await founder.ReadAsync(0, 70)).Payload[Text(guild.Name).Length] == 0,
                "CMD166 acknowledges transfer then refreshes the old founder as deputy");
            var formerFounderRoster = await founder.ReadAsync(1, GameProtocolEngine.GuildMembersCommand);
            var newFounderName = Text(names[1]);
            var nameOffset = formerFounderRoster.Payload.AsSpan().IndexOf(newFounderName);
            check(nameOffset >= 0 && formerFounderRoster.Payload[nameOffset + newFounderName.Length + 11] == 1,
                "the old founder's open roster shows the new founder immediately after the handover");
            var masterRank = await deputy.ReadAsync(0, 139);
            check(masterRank.Payload is [1] && (await deputy.ReadAsync(0, 70)).Payload[Text(guild.Name).Length] == 1,
                "idle new founder gets the original rank-one permissions and founder flag");
            var appointed = await deputy.ReadAsync(0, 162);
            var delegation = await leaver.ReadAsync(0, 161);
            check(appointed.Payload.Length == 0 && delegation.Payload.SequenceEqual(Text(names[1])),
                "NOTI162 reaches the new founder and NOTI161 names them to the other guild members");
            var owner = await store.GetGuildAsync(guild.Id);
            check(owner.Guild?.FounderCharacterId == roster[1].Id
                && owner.Members.Count(member => member.GuildRank == 1) == 1,
                "leader identity and ranks commit as one guild change");
            await Reject(founder, 166, Text(names[2]), 36, "the prior founder immediately loses transfer authority");
            await Reject(deputy, 164, new byte[4], 87, "the new founder cannot leave directly");

            await leaver.SendAsync(leaver.Command(164, new byte[4]));
            var selfReply = await leaver.ReadAsync(1, 164);
            var selfExit = await leaver.ReadAsync(0, 163);
            var departure = await deputy.ReadAsync(0, 59);
            check(selfReply.Payload.AsSpan().SequenceEqual((byte[])[1, 1, .. Text(guild.Name), .. Text(names[2])])
                && selfExit.Payload is [1], "CMD164 self exit uses its name-bearing acknowledgement then native NOTI163 reason 1");
            check((await leaver.ReadAsync(0, 139)).Payload is [5], "self exit clears the native guild permissions");
            check(DepartureMatches(departure.Payload, guild.Id, roster[2].CharacterNo, 1, names[2]),
                "idle remaining member sees the exact NOTI59 guild, character, action and names");
            await Reject(leaver, 164, new byte[4], 34, "a repeated self exit has no remaining membership to mutate");

            await founder.SendAsync(founder.Command(164, Text(names[3])));
            var kickReply = await founder.ReadAsync(1, 164);
            var kickedExit = await kicked.ReadAsync(0, 163);
            var kickNotice = await deputy.ReadAsync(0, 59);
            check(kickReply.Payload.AsSpan().SequenceEqual((byte[])[1, 2, .. Text(guild.Name), .. Text(names[3])])
                && kickedExit.Payload is [2] && DepartureMatches(kickNotice.Payload, guild.Id, roster[3].CharacterNo, 2, names[3]),
                "deputy expulsion reaches the target and remaining members with native reason 2");
            check((await kicked.ReadAsync(0, 139)).Payload is [5], "expulsion clears the idle client's guild permissions");

            rejectWrites = true;
            await Reject(deputy, 166, Text(names[0]), 17, "failed transfer keeps the current leader and sends the failure reply");
            var afterFailure = await store.GetGuildAsync(guild.Id);
            check(afterFailure.Guild?.FounderCharacterId == roster[1].Id
                && afterFailure.Members.Select(member => member.GuildRank).Order().SequenceEqual(new byte[] { 1, 2 }),
                "an injected snapshot failure exposes no partial leader swap");

            var reloaded = CreateStore();
            await reloaded.InitializeAsync();
            var durable = await reloaded.GetGuildAsync(guild.Id);
            var selfAfterReload = await reloaded.GetCharacterAsync(names[2], roster[2].Id);
            var kickedAfterReload = await reloaded.GetCharacterAsync(names[3], roster[3].Id);
            check(durable.Guild?.FounderCharacterId == roster[1].Id
                && durable.Members.Select(member => member.Id).ToHashSet().SetEquals(roster.Take(2).Select(member => member.Id))
                && selfAfterReload is { GuildId: 0, GuildName: null, GuildRank: 0, GuildDepartedAtUtc: not null }
                && kickedAfterReload is { GuildId: 0, GuildName: null, GuildRank: 0, GuildDepartedAtUtc: not null },
                "leader transfer and both departures survive restart with the invitation cooldown timestamp");

            if (wireDirectory is not null)
            {
                Directory.CreateDirectory(wireDirectory);
                foreach (var (file, packet) in new[] { ("rank-reply", ranked), ("rank-notice", rankNotice),
                    ("deputy-permission", deputyRank), ("master-permission", masterRank),
                    ("transfer-reply", transferred), ("appointed", appointed), ("delegation", delegation),
                    ("self-reply", selfReply), ("self-exit", selfExit), ("peer-departure", departure),
                    ("kick-reply", kickReply), ("kicked-exit", kickedExit), ("peer-kick", kickNotice) })
                    await File.WriteAllBytesAsync(Path.Combine(wireDirectory, file + ".bin"), packet.Payload);
            }

            bool DepartureMatches(byte[] payload, uint guildId, uint characterNo, byte mode, string characterName) =>
                payload.Length >= 9 && BinaryPrimitives.ReadUInt32LittleEndian(payload) == guildId
                && BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4)) == characterNo && payload[8] == mode
                && payload.AsSpan(9).SequenceEqual((byte[])[.. Text(guild.Name), .. Text(characterName)]);

            JsonGameStore CreateStore(AtomicSnapshotReplacement? replacement = null) =>
                replacement is null
                    ? new(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items)
                    : new(options, NullLogger<JsonGameStore>.Instance, replacement, itemCatalog: items);

            async Task<Connection> Connect(int member)
            {
                var connection = await Connection.CreateAsync(entrance, options, 47001);
                await connection.LoginAsync(names[member], roster[member].Slot);
                await connection.EnableChapterTwoAsync();
                return connection;
            }

            async Task Reject(Connection connection, byte code, byte[] body, byte error, string message)
            {
                await connection.SendAsync(connection.Command(code, body));
                check((await connection.ReadAsync(1, code)).Payload.AsSpan().SequenceEqual(new byte[] { 0, error }), message);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previousCipher);
            root.Delete(true);
        }
    }

    private static byte[] Text(string name)
    {
        var encoded = GuildRules.Encode(name);
        var field = new byte[4 + encoded.Length];
        BinaryPrimitives.WriteInt32LittleEndian(field, encoded.Length);
        encoded.CopyTo(field, 4);
        return field;
    }
}
