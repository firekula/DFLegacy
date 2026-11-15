using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Connection = NewCharacterDefaultsSmokeTests.Connection;

internal static class ChapterTwoGuildInvitationSmokeTests
{
    private const byte Invite = 162, Answer = 163, Invitation = 159, Result = 160, Joined = 58;

    internal static async Task RunAsync(Action<bool, string> check, string? output = null)
    {
        var prior = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-guild-invitations-");
        try
        {
            await CheckInvitationsAsync(root.FullName, output, check);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", prior);
            root.Delete(true);
        }
    }

    private static async Task CheckInvitationsAsync(string root, string? output, Action<bool, string> check)
    {
        const string ownerAccount = "inviteowner", guestAccount = "inviteguest", awayAccount = "inviteaway",
            coolingAccount = "invitecooling";
        var options = new ServerOptions
        {
            DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false,
            ScriptPvfPath = "", SkillScriptPath = root, Channel = new() { Port = 47001 }
        };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
        var failSave = false;
        var replacement = new AtomicSnapshotReplacement((source, destination) =>
        {
            if (failSave) throw new IOException("injected guild invitation snapshot failure");
            File.Move(source, destination, true);
        }, (_, _) => Task.CompletedTask);
        var store = NewStore();
        await store.InitializeAsync();
        var owner = await AddCharacterAsync(ownerAccount, "InviteOwner");
        var guest = await AddCharacterAsync(guestAccount, "InviteGuest");
        var away = await AddCharacterAsync(awayAccount, "InviteAway");
        var cooling = await AddCharacterAsync(coolingAccount, "InviteCool");
        await store.SaveCharacterItemSpacesAsync(ownerAccount, owner.Id, 700000, [], [], []);
        var guild = (await store.CreateGuildAsync(ownerAccount, owner.Id, "InviteGuild", "inviteguild", 300000)).Guild!;
        var seeded = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
        var coolingState = seeded["Accounts"]!.AsArray().SelectMany(account => account!["Characters"]!.AsArray())
            .Single(character => character!["Id"]!.GetValue<Guid>() == cooling.Id)!;
        coolingState["GuildDepartedAtUtc"] = DateTimeOffset.UtcNow.AddDays(-1);
        await File.WriteAllTextAsync(options.DataPath, seeded.ToJsonString());
        store = NewStore(replacement);
        await store.InitializeAsync();
        var guestName = Field(guest.Name);
        var guildName = Field(guild.Name);
        var ownerName = Field(owner.Name);
        byte[] invitationBody = [.. guildName, .. ownerName];
        byte[] joinedNames = [.. guildName, .. guestName];
        byte[] declineBody = [0, .. guestName];
        byte[] acceptedBody = [1, .. guestName];
        await using (var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items))
        {
            await using var ownerConnection = await Connect(services, ownerAccount, owner);
            await using var guestConnection = await Connect(services, guestAccount, guest);

            await SendAsync(ownerConnection, Invite, guestName, 100);
            var sent = (await ownerConnection.ReadAsync(1, Invite)).Payload;
            check(sent is [1], "native CMD162 acknowledges an invitation to an online unjoined character");
            if (sent is not [1]) return;
            var invite = (await guestConnection.ReadAsync(0, Invitation)).Payload;
            check(invite.AsSpan().SequenceEqual(invitationBody),
                "NOTI159 sends the guild and inviter names in original order");

            await SendAsync(guestConnection, Answer, [0], 101);
            var declined = (await ownerConnection.ReadAsync(0, Result)).Payload;
            check(declined.AsSpan().SequenceEqual(declineBody),
                "declining tells the proper inviter using original NOTI160 status zero");
            var afterDecline = await store.GetCharacterAsync(guestAccount, guest.Id);
            check(afterDecline is { GuildId: 0, GuildName: null }
                && (await store.GetGuildAsync(guild.Id)).Members.Length == 1,
                "inviting and declining leave authoritative membership untouched");
            await SendAsync(guestConnection, Answer, [1], 102);
            check((await guestConnection.ReadAsync(1, Answer)).Payload is [0, 103],
                "a declined invitation cannot be replayed as an acceptance");
            await SendAsync(ownerConnection, Invite, Field(away.Name), 103);
            check((await ownerConnection.ReadAsync(1, Invite)).Payload is [0, 3],
                "the original CMD162 offline failure reports error three");
            await SendAsync(guestConnection, Invite, ownerName, 104);
            check((await guestConnection.ReadAsync(1, Invite)).Payload is [0, 34],
                "a character without a guild cannot invite");
            await SendAsync(ownerConnection, Invite, guestName, 105);
            check((await ownerConnection.ReadAsync(1, Invite)).Payload is [1], "invitation can be repeated after a decline");
            check((await guestConnection.ReadAsync(0, Invitation)).Payload.AsSpan().SequenceEqual(invitationBody),
                "a new invite opens a fresh confirmation");
            await SendAsync(guestConnection, Answer, [2], 106);
            check((await guestConnection.ReadAsync(1, Answer)).Payload is [0, 17],
                "CMD163 rejects invalid flags without consuming the invitation");
            await SendAsync(guestConnection, Answer, [1], 107);
            var accepted = (await guestConnection.ReadAsync(1, Answer)).Payload;
            check(accepted is [1], "accepting a current invitation returns the original CMD163 success byte");
            if (accepted is not [1]) return;
            var joined = (await guestConnection.ReadAsync(0, Joined)).Payload;
            var localRank = (await guestConnection.ReadAsync(0, 139)).Payload;
            check(localRank is [0],
                "the joining character receives the original local newcomer role update");
            var info = (await guestConnection.ReadAsync(0, 70)).Payload;
            var ownerResult = (await ownerConnection.ReadAsync(0, Result)).Payload;
            check(ownerResult.AsSpan().SequenceEqual(acceptedBody),
                "inviter receives the native NOTI160 accepted status");
            check((await ownerConnection.ReadAsync(0, Joined)).Payload.AsSpan().SequenceEqual(joined),
                "the inviter's NOTI58 reports the same join and triggers member refresh");
            check(BinaryPrimitives.ReadUInt32LittleEndian(joined) == guild.Id
                && BinaryPrimitives.ReadUInt32LittleEndian(joined.AsSpan(4)) == guest.CharacterNo
                && joined.AsSpan(8).SequenceEqual(joinedNames),
                "NOTI58 projects the durable guild identity to the joining client");
            check(info.AsSpan().StartsWith(guildName)
                && BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(guildName.Length + 3)) == 2,
                "the new member receives original NOTI70 profile with the updated member count");
            await SendAsync(guestConnection, Answer, [1], 108);
            check((await guestConnection.ReadAsync(1, Answer)).Payload is [0, 103],
                "a duplicate acceptance cannot append a second membership");
            await SendAsync(ownerConnection, Invite, guestName, 109);
            check((await ownerConnection.ReadAsync(1, Invite)).Payload is [0, 39],
                "another invitation correctly identifies the existing membership");
            await using (var coolingConnection = await Connect(services, coolingAccount, cooling))
            {
                var coolingName = Field(cooling.Name);
                await SendAsync(ownerConnection, Invite, coolingName, 118);
                await ownerConnection.ReadAsync(1, Invite);
                await coolingConnection.ReadAsync(0, Invitation);
                await SendAsync(coolingConnection, Answer, [1], 119);
                var cooldown = (await coolingConnection.ReadAsync(1, Answer)).Payload;
                var cooldownNotice = (await ownerConnection.ReadAsync(0, Result)).Payload;
                byte[] expectedCooldown = [104, .. coolingName];
                check(cooldown is [0, 104] && cooldownNotice.AsSpan().SequenceEqual(expectedCooldown)
                    && (await store.GetCharacterAsync(coolingAccount, cooling.Id)) is { GuildId: 0 },
                    "a recent guild departure rejects joining and reports the original cooldown to both clients");
            }
            await using (var awayConnection = await Connect(services, awayAccount, away))
            {
                await store.AddBlacklistAsync(awayAccount, owner.Name);
                var awayName = Field(away.Name);
                await SendAsync(ownerConnection, Invite, awayName, 110);
                check((await ownerConnection.ReadAsync(1, Invite)).Payload is [0, 77],
                    "CMD162 honors the online recipient's character blacklist");
                await store.RemoveBlacklistAsync(awayAccount, owner.Name);
                await SendAsync(ownerConnection, Invite, awayName, 111);
                check((await ownerConnection.ReadAsync(1, Invite)).Payload is [1],
                    "removing the blacklist allows a new invitation");
                await awayConnection.ReadAsync(0, Invitation);
            }
            await using (var relog = await Connect(services, awayAccount, away))
            {
                await SendAsync(relog, Answer, [1], 112);
                check((await relog.ReadAsync(1, Answer)).Payload is [0, 103]
                    && (await store.GetCharacterAsync(awayAccount, away.Id)) is { GuildId: 0 },
                    "a reconnect cannot accept the prior connection's outstanding invitation");
                var awayName = Field(away.Name);
                await SendAsync(ownerConnection, Invite, awayName, 115);
                await ownerConnection.ReadAsync(1, Invite);
                await relog.ReadAsync(0, Invitation);
                failSave = true;
                await SendAsync(relog, Answer, [1], 116);
                var failed = (await relog.ReadAsync(1, Answer)).Payload;
                failSave = false;
                check(failed is [0, 17] && (await store.GetCharacterAsync(awayAccount, away.Id)) is { GuildId: 0 }
                    && (await store.GetGuildAsync(guild.Id)).Members.Length == 2,
                    "an uncertain persistence failure must not add the invitee to the in-memory guild");
            }
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                await File.WriteAllBytesAsync(Path.Combine(output, "invite-159.bin"), invite);
                await File.WriteAllBytesAsync(Path.Combine(output, "decline-160.bin"), declined);
                await File.WriteAllBytesAsync(Path.Combine(output, "accepted-160.bin"), ownerResult);
                await File.WriteAllBytesAsync(Path.Combine(output, "join-58.bin"), joined);
                await File.WriteAllBytesAsync(Path.Combine(output, "join-rank-139.bin"), localRank);
                await File.WriteAllBytesAsync(Path.Combine(output, "join-info-70.bin"), info);
                await File.WriteAllBytesAsync(Path.Combine(output, "cmd162-ok.bin"), sent);
                await File.WriteAllBytesAsync(Path.Combine(output, "cmd163-ok.bin"), accepted);
            }
        }
        var reloaded = NewStore();
        await reloaded.InitializeAsync();
        var persisted = await reloaded.GetCharacterAsync(guestAccount, guest.Id);
        check(persisted?.GuildId == guild.Id && persisted.GuildName == guild.Name && persisted.GuildRank == 0
            && (await reloaded.GetGuildAsync(guild.Id)).Members.Length == 2
            && (await reloaded.GetCharacterAsync(awayAccount, away.Id)) is { GuildId: 0 },
            "restart recovers the committed newcomer rank and no membership for the failed write");

        JsonGameStore NewStore(AtomicSnapshotReplacement? writer = null) => writer is null
            ? new(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items)
            : new(options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: items);

        async Task<CharacterRecord> AddCharacterAsync(string account, string name)
        {
            await store.CreateAccountAsync(new(account, "password", OicqCode: "123456"));
            return (await store.CreateCharacterAsync(account, new(name))).Character!;
        }

        async Task<Connection> Connect(ServiceProvider services, string account, CharacterRecord character)
        {
            var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
            var connection = await Connection.CreateAsync(entrance, options, 47001);
            await connection.LoginAsync(account, character.Slot);
            await connection.EnableChapterTwoAsync();
            return connection;
        }
    }

    private static byte[] Field(string value)
    {
        var bytes = GuildRules.Encode(value);
        var field = new byte[4 + bytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(field, bytes.Length);
        bytes.CopyTo(field, 4);
        return field;
    }

    private static async Task SendAsync(Connection connection, byte command, byte[] body, ushort sequence)
    {
        var padded = new byte[2 + (body.Length + 15) / 16 * 16];
        BinaryPrimitives.WriteUInt16LittleEndian(padded, sequence);
        body.CopyTo(padded, 2);
        await connection.SendAsync(ChapterTwoItemUseSmokeTests.EncryptChapterTwoCommand(command, Convert.ToHexString(padded)));
    }
}
