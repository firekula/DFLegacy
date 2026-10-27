using System.Text;
using System.Buffers.Binary;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MentorSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var name = Encoding.GetEncoding(936).GetBytes("师父");
        var packet = GameProtocolEngine.CreateMentorInvitation(45, name);
        check(packet.Type == 0 && packet.ProtocolId == 73
                && packet.Payload.SequenceEqual(new byte[] { 45, 0, 4, 0, 0, 0, 0xCA, 0xA6, 0xB8, 0xB8 }),
            "ACT1 mentor invitation is u16 level plus a CP936 length-prefixed name");
        CheckProtocolAndRules(check);
        await CheckStoreAsync(check);
        await CheckWriteFailuresAsync(check);
        await CheckServiceAsync(check);
        await CheckConcurrentAcceptAsync(check);
        await CheckOrderedDeliveryAsync(check);
        await CheckMutationRecoveryAsync(check);
        await CheckCapacityCycleAndGoldAsync(check);
    }

    private static void CheckProtocolAndRules(Action<bool, string> check)
    {
        void Vector(GameServerPacket packet, byte type, byte id, string hex) => check(
            packet.Type == type && packet.ProtocolId == id && packet.Payload.SequenceEqual(Convert.FromHexString(hex)),
            $"ACT1 mentorship packet {type}/{id} matches the native byte contract");
        Vector(GameProtocolEngine.CreateMentorRequestReply([65]), 1, 79, "010100000041");
        Vector(GameProtocolEngine.CreateMentorRequestReply([65], 44), 1, 79, "0001000000412C");
        Vector(GameProtocolEngine.CreateMentorEstablished(1, 1, 45, [65]), 0, 74, "01012D0100000041");
        Vector(GameProtocolEngine.CreateMentorReply(2, 6, [65]), 1, 80, "0102060100000041");
        Vector(GameProtocolEngine.CreateMentorRemoved([65]), 1, 81, "010100000041");
        Vector(GameProtocolEngine.CreateMentorRemovalNotice([65]), 0, 77, "0100000041");
        Vector(GameProtocolEngine.CreateMentorPresence(false, 255, 45, [65]), 0, 72, "00FF2D0100000041");
        Vector(GameProtocolEngine.CreateMentorReset(), 0, 75, "");
        Vector(GameProtocolEngine.CreateMentorListReply(new(1, 45, [65]), [new(255, 6, [66], 1)]), 1, 82,
            "01012D01000000410001000000000300000001FF06010000004201010000000003000000");
        check(GameProtocolEngine.CreateMentorListReply(null, []).Payload.SequenceEqual(
                GameProtocolEngine.CreatePrivateStorePollReply().Payload)
                && GameProtocolEngine.CreateMentorListReply(null, []).Payload.Length == 18,
            "empty mentorship lists preserve the previous compatible 18-byte ACT1 reply");
        check(GameProtocolEngine.TryReadMentorName([1, 0, 1, 0, 0, 0, 65], out var name) && name.SequenceEqual(new byte[] { 65 })
                && !GameProtocolEngine.TryReadMentorName([1, 0, 2, 0, 0, 0, 65], out _)
                && !GameProtocolEngine.TryReadMentorName([1, 0, 1, 0, 0, 0, 0], out _)
                && !GameProtocolEngine.TryReadMentorName([1, 0, 1, 0, 0, 0, 65, 66], out _),
            "mentor names enforce exact payload length and reject NUL/truncation/trailing bytes");
        check(GameProtocolEngine.TryReadMentorReply([1, 0, 1], out var reply) && reply == 1
                && !GameProtocolEngine.TryReadMentorReply([1, 0, 3], out _)
                && !GameProtocolEngine.TryReadMentorReply([1, 0, 1, 0], out _),
            "the ACT1 response parser permits only the exact accept/reject byte");
        check(new[] { 17, 18, 19, 20, 29, 30, 60 }.Select(MentorRules.Capacity)
                .SequenceEqual(new[] { 0, 5, 5, 8, 8, 10, 10 }),
            "mentor capacity follows the verified official minimum and ten-level bands");
        var reset = new DateTimeOffset(2026, 10, 27, 6, 0, 0, TimeSpan.FromHours(8));
        var atReset = new MentorRegistrationState(reset.ToUnixTimeSeconds());
        check(!MentorRules.CanRegister(atReset, reset) && MentorRules.CanRegister(atReset, reset.AddDays(1)),
            "the daily gate is strict at exactly the registration reset boundary");
        var separation = new MentorRegistrationState(0, reset.AddSeconds(1).ToUnixTimeSeconds());
        check(!MentorRules.CanRegister(separation, reset.AddDays(2))
                && MentorRules.CanRegister(separation, reset.AddDays(3)),
            "separation eligibility is the third 06:00 boundary rather than a fixed 72-hour duration");
    }

    private static async Task CheckConcurrentAcceptAsync(Action<bool, string> check)
    {
        await using var fixture = await PartyHarness.CreateAsync();
        var store = fixture.Store;
        var alice = (await store.GetCharactersAsync("alice"))[0];
        var bob = (await store.GetCharactersAsync("bobby"))[0];
        var carol = (await store.GetCharactersAsync("carol"))[0];
        await store.SaveCharacterProgressionAsync("alice", alice.Id, 45, 0, 0);
        await store.SaveCharacterProgressionAsync("carol", carol.Id, 45, 0, 0);
        await store.SaveCharacterProgressionAsync("bobby", bob.Id, 6, 0, 0);
        check(await store.CheckMentorPairAsync(alice.Id, carol.Id, DateTimeOffset.Now) == 44
                && await store.CheckMentorPairAsync(alice.Id, alice.Id, DateTimeOffset.Now) == 49,
            "same-level and self relationships fail without mutation");
        await store.AddBlacklistAsync("bobby", alice.Name);
        check(await store.CheckMentorPairAsync(alice.Id, bob.Id, DateTimeOffset.Now) == 54,
            "the target's existing account blacklist blocks the invitation");
        await store.RemoveBlacklistAsync("bobby", alice.Name);
        var results = await Task.WhenAll(store.EstablishMentorAsync(alice.Id, bob.Id, DateTimeOffset.Now),
            store.EstablishMentorAsync(carol.Id, bob.Id, DateTimeOffset.Now));
        check(results.Count(r => r.Status == DungeonRewardLedgerStatus.Committed) == 1
                && (await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is { } winner
                && (winner == alice.Id || winner == carol.Id),
            $"simultaneous acceptances install exactly one canonical mentor edge ({string.Join(',', results.Select(r => r.Status + "/" + r.Error))}; apprentice level={(await store.GetCharacterByNameAsync(bob.Name))?.Level})");
    }

    private static async Task CheckStoreAsync(Action<bool, string> check)
    {
        await using var fixture = await PartyHarness.CreateAsync();
        var state = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Options.DataPath))!;
        var levels = new Dictionary<string, int> { ["alice"] = 45, ["bobby"] = 6, ["carol"] = 44, ["dave"] = 4, ["erin"] = 18 };
        foreach (var account in state["Accounts"]!.AsArray())
            foreach (var character in account!["Characters"]!.AsArray())
                if (levels.TryGetValue(account["UserName"]!.GetValue<string>(), out var level)) character!["Level"] = level;
        await File.WriteAllTextAsync(fixture.Options.DataPath, state.ToJsonString());
        var store = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        var alice = (await store.GetCharactersAsync("alice"))[0];
        var bob = (await store.GetCharactersAsync("bobby"))[0];
        var carol = (await store.GetCharactersAsync("carol"))[0];
        var dave = (await store.GetCharactersAsync("dave"))[0];
        var erin = (await store.GetCharactersAsync("erin"))[0];
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(8));
        var operations = store;
        var linked = await operations.EstablishMentorAsync(alice.Id, bob.Id, now, CancellationToken.None);
        check(linked.Status == DungeonRewardLedgerStatus.Committed
                && (await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId == alice.Id,
            "the higher-level character becomes the persisted mentor regardless of requester direction");
        var saved = await store.SaveCharacterInventoryAsync("bobby", bob.Id, bob.Gold + 1, bob.Inventory ?? [], bob.Equipment ?? []);
        check(saved?.MentorCharacterId == alice.Id, "an old inventory snapshot cannot overwrite the newly established parent edge");
        var daily = await operations.EstablishMentorAsync(alice.Id, carol.Id, now.AddHours(1), CancellationToken.None);
        check(daily.Error == 55, "the official daily registration gate blocks another relation before the next 06:00");
        var nextDay = now.Date.AddDays(1).AddHours(6);
        var boundary = new DateTimeOffset(nextDay, now.Offset);
        var second = await operations.EstablishMentorAsync(alice.Id, carol.Id, boundary, CancellationToken.None);
        check(second.Status == DungeonRewardLedgerStatus.Committed, "a previous-day registration becomes eligible at 06:00");
        var removed = await operations.RemoveMentorAsync(alice.Id, bob.Name, boundary.AddHours(1), CancellationToken.None);
        check(removed.Status == DungeonRewardLedgerStatus.Committed
                && (await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null,
            "a mentor can remove an offline apprentice atomically");
        var restricted = await operations.EstablishMentorAsync(alice.Id, dave.Id, boundary.AddDays(2), CancellationToken.None);
        check(restricted.Error == 55, "voluntary separation with other relations retained waits for the third 06:00 reset");
        var unblocked = await operations.EstablishMentorAsync(alice.Id, dave.Id, boundary.AddDays(3), CancellationToken.None);
        check(unblocked.Status == DungeonRewardLedgerStatus.Committed, "the third qualifying 06:00 reset permits registration again");
        var freeBob = await operations.EstablishMentorAsync(erin.Id, bob.Id, boundary.AddHours(2), CancellationToken.None);
        check(freeBob.Status == DungeonRewardLedgerStatus.Committed,
            "the removed apprentice's empty record is cleared without a voluntary-separation penalty");
        var reloaded = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance);
        await reloaded.InitializeAsync();
        check((await reloaded.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId == erin.Id,
            "the canonical mentor edge survives server restart");
        var deleted = await reloaded.DeleteCharacterAsync("erin", 0);
        check(deleted.Deleted && (await reloaded.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null,
            "deleting a mentor cleans the surviving apprentice edge in the same transaction");
        var orphan = bob with { MentorCharacterId = alice.Id };
        var replaced = await reloaded.ReplaceAllCharactersAsync("bobby", new[] { orphan });
        check(replaced.Replaced && (await reloaded.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null,
            "a roster replacement cannot import a parent edge to a character it removed");
    }

    private static async Task CheckWriteFailuresAsync(Action<bool, string> check)
    {
        foreach (var afterReplace in new[] { false, true })
        {
            await using var fixture = await PartyHarness.CreateAsync();
            var state = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Options.DataPath))!;
            foreach (var account in state["Accounts"]!.AsArray())
                foreach (var c in account!["Characters"]!.AsArray())
                    c!["Level"] = account["UserName"]!.GetValue<string>() == "bobby" ? 6 : 45;
            await File.WriteAllTextAsync(fixture.Options.DataPath, state.ToJsonString());
            var writer = new MentorFailingWriter(afterReplace);
            var store = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance, writer);
            await store.InitializeAsync();
            var alice = (await store.GetCharactersAsync("alice"))[0];
            var bob = (await store.GetCharactersAsync("bobby"))[0];
            var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(8));
            await store.EstablishMentorAsync(alice.Id, bob.Id, now);
            writer.Armed = true;
            var result = await store.DeleteCharacterAsync("alice", 0);
            check(!result.Deleted && !store.TryTakeMentorRemovalNotice(out _),
                "uncertain character deletion publishes neither success nor relationship notices");
            var reload = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance);
            await reload.InitializeAsync();
            var savedAlice = await reload.GetCharacterByNameAsync(alice.Name);
            var savedBob = await reload.GetCharacterByNameAsync(bob.Name);
            check(afterReplace ? savedAlice is null && savedBob?.MentorCharacterId is null
                    : savedAlice is not null && savedBob?.MentorCharacterId == alice.Id,
                "deletion snapshot failure recovers either both relationship changes or neither");
        }
    }

    private sealed class MentorFailingWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        internal int Calls;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken ct)
        {
            Calls++;
            if (Armed && !afterReplace) throw new IOException("before mentorship replace");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, ct);
            if (Armed) throw new IOException("after mentorship replace");
        }
    }

    private static async Task CheckMutationRecoveryAsync(Action<bool, string> check)
    {
        foreach (var operation in new[] { "establish", "remove", "replace" })
        foreach (var afterReplace in new[] { false, true })
        {
            await using var fixture = await PartyHarness.CreateAsync();
            var writer = new MentorFailingWriter(afterReplace);
            var store = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance, writer);
            await store.InitializeAsync();
            var a = (await store.GetCharactersAsync("alice"))[0];
            var b = (await store.GetCharactersAsync("bobby"))[0];
            await store.SaveCharacterProgressionAsync("alice", a.Id, 45, 0, 0);
            await store.SaveCharacterProgressionAsync("bobby", b.Id, 6, 0, 0);
            var now = DateTimeOffset.Now;
            if (operation != "establish") await store.EstablishMentorAsync(a.Id, b.Id, now);
            writer.Armed = true;
            var reportedSuccess = operation switch
            {
                "establish" => (await store.EstablishMentorAsync(a.Id, b.Id, now)).Status == DungeonRewardLedgerStatus.Committed,
                "remove" => (await store.RemoveMentorAsync(b.Id, a.Name, now)).Status == DungeonRewardLedgerStatus.Committed,
                _ => (await store.ReplaceAllCharactersAsync("bobby", [b with { MentorCharacterId = a.Id }])).Replaced
            };
            var calls = writer.Calls;
            var retry = await store.EstablishMentorAsync(a.Id, b.Id, now);
            check(!reportedSuccess && retry.Status == DungeonRewardLedgerStatus.Unknown && writer.Calls == calls,
                $"uncertain mentorship {operation} never reports success or blindly retries a write");
            var reload = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance);
            await reload.InitializeAsync();
            var savedA = await reload.GetCharacterByNameAsync(a.Name);
            var savedB = await reload.GetCharacterByNameAsync(b.Name);
            var linked = savedB?.MentorCharacterId == a.Id;
            check(operation == "establish" ? linked == afterReplace
                    : operation == "remove" ? linked != afterReplace
                    : afterReplace ? savedA is null && savedB?.MentorCharacterId is null : savedA is not null && linked,
                $"restart recovers the actual atomic {operation} snapshot on either side of replacement");
        }
    }

    private static async Task CheckCapacityCycleAndGoldAsync(Action<bool, string> check)
    {
        await using var fixture = await PartyHarness.CreateAsync();
        var store = fixture.Store;
        var a = (await store.GetCharactersAsync("alice"))[0];
        var b = (await store.GetCharactersAsync("bobby"))[0];
        var c = (await store.GetCharactersAsync("carol"))[0];
        await store.SaveCharacterProgressionAsync("alice", a.Id, 50, 0, 0);
        await store.SaveCharacterProgressionAsync("bobby", b.Id, 40, 0, 0);
        await store.SaveCharacterProgressionAsync("carol", c.Id, 30, 0, 0);
        var now = DateTimeOffset.Now;
        await store.EstablishMentorAsync(a.Id, b.Id, now);
        await store.EstablishMentorAsync(b.Id, c.Id, now.AddDays(1));
        await store.SaveCharacterProgressionAsync("carol", c.Id, 60, 0, 0);
        var cycle = await store.EstablishMentorAsync(c.Id, a.Id, now.AddDays(2));
        check(cycle.Status == DungeonRewardLedgerStatus.Rejected
                && (await store.GetCharacterByNameAsync(a.Name))?.MentorCharacterId is null,
            "level changes cannot close a multi-generation mentor cycle");
        using var dungeon = new DungeonFixture();
        await store.CommitDungeonGoldDistributionAsync(Guid.NewGuid(),
            [dungeon.Member(a.Id, 0, a.Name), dungeon.Member(b.Id, 1, b.Name)], 0, 19);
        await store.RemoveMentorAsync(b.Id, a.Name, now.AddDays(3));
        check((await store.ConsumeDungeonGoldCreditsAsync(a.Id))?.Gold == a.Gold + 10
                && (await store.ConsumeDungeonGoldCreditsAsync(b.Id))?.Gold == b.Gold + 9,
            "mentorship snapshots preserve pending party gold and its durable receipt");
        await store.DeleteCharacterAsync("carol", 0);
        await store.DeleteCharacterAsync("bobby", 0);
        for (var i = 0; i < 10; i++)
        {
            var account = "student" + i;
            await store.CreateAccountAsync(new CreateAccountRequest(account, "password", OicqCode: "123456"));
            var child = (await store.CreateCharacterAsync(account, new CreateCharacterRequest("学员" + i))).Character!;
            await store.SaveCharacterProgressionAsync(account, child.Id, 1, 0, 0);
            var accepted = await store.EstablishMentorAsync(a.Id, child.Id, now.AddDays(4 + i));
            check(accepted.Status == DungeonRewardLedgerStatus.Committed, "each permitted apprentice occupies one canonical capacity slot");
        }
        var extra = (await store.GetCharactersAsync("erin"))[0];
        await store.SaveCharacterProgressionAsync("erin", extra.Id, 6, 0, 0);
        check(await store.CheckMentorPairAsync(a.Id, extra.Id, now.AddDays(15)) == 52
                && await store.CheckMentorPairAsync(extra.Id, a.Id, now.AddDays(15)) == 42,
            "actual capacity exhaustion uses the official error for each requester direction");
    }

    private static async Task CheckServiceAsync(Action<bool, string> check)
    {
        await using var fixture = await PartyHarness.CreateAsync();
        var state = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Options.DataPath))!;
        foreach (var account in state["Accounts"]!.AsArray())
            foreach (var c in account!["Characters"]!.AsArray())
                c!["Level"] = account["UserName"]!.GetValue<string>() == "bobby" ? 6 : 45;
        await File.WriteAllTextAsync(fixture.Options.DataPath, state.ToJsonString());
        var store = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        var alice = (await store.GetCharactersAsync("alice"))[0];
        var bob = (await store.GetCharactersAsync("bobby"))[0];
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var received = new ConcurrentDictionary<Guid, ConcurrentQueue<GameServerPacket>>();
        Func<GameServerPacket, ValueTask> Send(Guid id) => packet =>
        {
            received.GetOrAdd(id, _ => new()).Enqueue(packet);
            return ValueTask.CompletedTask;
        };
        GameServerPacket[] Of(Guid id) => received.TryGetValue(id, out var queue) ? queue.ToArray() : [];
        using var service = new MentorService(store, TimeProvider.System, NullLogger<MentorService>.Instance);
        await service.AttachChannelAsync("alice", alice.Id, (ushort)1, a, Send(alice.Id), CancellationToken.None);
        await service.AttachChannelAsync("bobby", bob.Id, (ushort)2, b, Send(bob.Id), CancellationToken.None);
        received.Clear();
        await service.RequestAsync(bob.Id, b, alice.Name, CancellationToken.None);
        check(Of(alice.Id).Any(p => p.ProtocolId == 73 && p.Type == 0)
                && Of(bob.Id).Any(p => p.ProtocolId == 79 && p.Payload[0] == 1),
            "an apprentice request produces native acknowledgement and the mentor's confirmation window");
        await service.ReplyAsync(alice.Id, a, (byte)2, CancellationToken.None);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null
                && Of(bob.Id).Any(p => p.ProtocolId == 74 && p.Payload[0] == 2),
            "rejecting the native invitation notifies the requester without creating a relationship");
        received.Clear();
        await service.RequestAsync(bob.Id, b, alice.Name, CancellationToken.None);
        await store.SaveCharacterProgressionAsync("alice", alice.Id, 6, 0, 0);
        await service.ReplyAsync(alice.Id, a, (byte)1, CancellationToken.None);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null
                && Of(alice.Id).Any(p => p.ProtocolId == 79 && p.Payload[^1] == 44),
            "acceptance rechecks current levels rather than the invitation snapshot");
        await store.SaveCharacterProgressionAsync("alice", alice.Id, 45, 0, 0);
        received.Clear();
        await service.RequestAsync(bob.Id, b, alice.Name, CancellationToken.None);
        await service.ReplyAsync(alice.Id, a, (byte)1, CancellationToken.None);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId == alice.Id
                && Of(bob.Id).Any(p => p.ProtocolId == 74 && p.Payload.Take(3).SequenceEqual(new byte[] { 1, 1, 45 }))
                && Of(alice.Id).Any(p => p.ProtocolId == 80 && p.Payload.Take(3).SequenceEqual(new byte[] { 1, 2, 6 })),
            "the requester and responder receive their own ACT1 role and peer level after atomic acceptance");
        received.Clear();
        await service.SendListAsync(bob.Id, b, CancellationToken.None);
        var list = Of(bob.Id).Single(p => p.ProtocolId == 82);
        check(list.Payload.Take(3).SequenceEqual(new byte[] { 1, 1, 45 })
                && list.Payload[^1] == 0 && list.Payload[^10] == 1,
            "a live mentor list includes the actual channel/level and initial relationship level one");
        received.Clear();
        await service.DetachChannelAsync(alice.Id, a, CancellationToken.None);
        check(Of(bob.Id).Any(p => p.ProtocolId == 72 && p.Payload.Take(3).SequenceEqual(new byte[] { 0, 255, 45 })),
            "mentor disconnection updates the surviving apprentice's native presence");
        await service.RemoveAsync(bob.Id, b, alice.Name, CancellationToken.None);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null
                && Of(bob.Id).Any(p => p.ProtocolId == 81 && p.Payload[0] == 1)
                && Of(bob.Id).Any(p => p.ProtocolId == 75),
            "offline separation succeeds and clears the final native membership record");
        var newer = Guid.NewGuid();
        await service.AttachChannelAsync("alice", alice.Id, (ushort)1, newer, Send(alice.Id), CancellationToken.None);
        await service.RequestAsync(bob.Id, b, alice.Name, CancellationToken.None);
        await service.ReplyAsync(alice.Id, a, (byte)1, CancellationToken.None);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null,
            "an old connection cannot answer a new connection's invitation");
        await service.DetachChannelAsync(alice.Id, a, CancellationToken.None);
        await service.ReplyAsync(alice.Id, newer, (byte)1, CancellationToken.None);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId == alice.Id,
            "a stale detach cannot remove the replacement connection or its pending invitation");
        await service.RemoveAsync(bob.Id, b, alice.Name, CancellationToken.None);
        var erin = (await store.GetCharactersAsync("erin"))[0];
        await store.SaveCharacterProgressionAsync("erin", erin.Id, 18, 0, 0);
        var e = Guid.NewGuid();
        await service.AttachChannelAsync("erin", erin.Id, (ushort)3, e, Send(erin.Id), CancellationToken.None);
        received.Clear();
        await service.RequestAsync(bob.Id, b, alice.Name, CancellationToken.None);
        await service.RequestAsync(erin.Id, e, alice.Name, CancellationToken.None);
        check(Of(erin.Id).Any(p => p.ProtocolId == 79 && p.Payload[0] == 0 && p.Payload[^1] == 41),
            "a second requester cannot replace the target's pending native confirmation");
        await service.ReplyAsync(alice.Id, newer, (byte)1, CancellationToken.None);
        await service.AttachChannelAsync("bobby", bob.Id, (ushort)2, b,
            (Func<GameServerPacket, ValueTask>)(_ => ValueTask.FromException(new IOException("broken recipient"))), CancellationToken.None);
        received.Clear();
        await service.RemoveAsync(alice.Id, newer, bob.Name, CancellationToken.None);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null
                && Of(alice.Id).Any(p => p.ProtocolId == 81 && p.Payload[0] == 1),
            "a broken peer channel cannot undo a committed separation or suppress its requester reply");
        await service.AttachChannelAsync("bobby", bob.Id, (ushort)2, b, Send(bob.Id), CancellationToken.None);
        await service.RequestAsync(bob.Id, b, alice.Name, CancellationToken.None);
        await service.ReplyAsync(alice.Id, newer, (byte)1, CancellationToken.None);
        received.Clear();
        await store.DeleteCharacterAsync("alice", 0);
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null,
            "character deletion commits the surviving edge cleanup before delivery");
        await service.RequestAsync(bob.Id, b, erin.Name, CancellationToken.None);
        await service.ReplyAsync(erin.Id, e, (byte)1, CancellationToken.None);
        received.Clear();
        await service.SweepChangesAsync();
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId == erin.Id
                && !Of(bob.Id).Any(p => p.ProtocolId == 75)
                && Of(bob.Id).LastOrDefault(p => p.ProtocolId == 82)?.Payload.Take(3)
                    .SequenceEqual(new byte[] { 1, 1, 18 }) == true,
            "an old deletion notice reconciles current membership instead of resetting a newly established mentor");
        received.Clear();
        await store.DeleteCharacterAsync("erin", 0);
        await service.SweepChangesAsync();
        check((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is null
                && Of(bob.Id).Any(p => p.ProtocolId == 75)
                && Of(bob.Id).Any(p => p.ProtocolId == 77),
            "committed character deletion clears the survivor and publishes native relationship removal");
    }

    private static async Task CheckOrderedDeliveryAsync(Action<bool, string> check)
    {
        await using var fixture = await PartyHarness.CreateAsync();
        var store = fixture.Store;
        var alice = (await store.GetCharactersAsync("alice"))[0];
        var bob = (await store.GetCharactersAsync("bobby"))[0];
        await store.SaveCharacterProgressionAsync("alice", alice.Id, 45, 0, 0);
        await store.SaveCharacterProgressionAsync("bobby", bob.Id, 6, 0, 0);
        using var service = new MentorService(store, TimeProvider.System, NullLogger<MentorService>.Instance);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var received = new ConcurrentQueue<GameServerPacket>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var switched = false;
        var staleSends = 0;
        await service.AttachChannelAsync("alice", alice.Id, 1, a, async packet =>
        {
            if (packet.ProtocolId == 80)
            {
                entered.TrySetResult();
                await release.Task;
                if (switched) Interlocked.Increment(ref staleSends);
            }
            received.Enqueue(packet);
        });
        await service.AttachChannelAsync("bobby", bob.Id, 2, b, _ => ValueTask.CompletedTask);
        received.Clear();
        await service.RequestAsync(bob.Id, b, alice.Name);
        var accept = service.ReplyAsync(alice.Id, a, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var separate = service.RemoveAsync(bob.Id, b, alice.Name);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((await store.GetCharacterByNameAsync(bob.Name))?.MentorCharacterId is not null && DateTime.UtcNow < deadline)
            await Task.Delay(1);
        release.TrySetResult();
        await Task.WhenAll(accept, separate);
        var packets = received.ToArray();
        check(Array.FindIndex(packets, p => p.ProtocolId == 80) < Array.FindIndex(packets, p => p.ProtocolId == 77),
            "a slow acceptance recipient receives acceptance before a later committed separation");

        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        received.Clear();
        await service.RequestAsync(bob.Id, b, alice.Name);
        accept = service.ReplyAsync(alice.Id, a, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var detach = service.DetachChannelAsync(alice.Id, a);
        check(!detach.IsCompleted, "character detachment drains an already running delivery before allowing reselect");
        switched = detach.IsCompleted;
        release.TrySetResult();
        await Task.WhenAll(accept, detach);
        switched = true;
        check(staleSends == 0, "no old attachment delivery completes after character detachment returned");
    }
}
