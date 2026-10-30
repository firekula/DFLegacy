using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PvpPartyAdmissionSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await DelayedAcceptance(check, entrantIsRequester: true);
        await DelayedAcceptance(check, entrantIsRequester: false);
        await PausedAcceptancePublication(check, entrantIsRequester: true);
        await PausedAcceptancePublication(check, entrantIsRequester: false);
        await CancelledInvitationDoesNotSurviveReturn(check, entrantIsRequester: true);
        await CancelledInvitationDoesNotSurviveReturn(check, entrantIsRequester: false);
        await SingleMemberCreationCannotCrossPvp(check);
    }

    // A forgotten invitation must not restore PvE membership after the real PvP entry.
    private static async Task DelayedAcceptance(Action<bool, string> check, bool entrantIsRequester)
    {
        await using var f = await Fixture.Create();
        var requester = entrantIsRequester ? 0 : 1;
        var accepter = 1 - requester;
        await f.Harness.Service.HandleRequestAsync(f.Id(requester), f.Uid(accepter), PartyHarness.DefaultInfo);
        await f.EnterPvp();
        f.Clear();
        await f.Harness.Service.HandleResponseAsync(f.Id(accepter), f.Uid(requester), true);
        check(f.Service.IsBusy(f.Id(0)) && f.Harness.Service.GetPartySlots(f.Id(0)) is null
            && f.Harness.Service.GetPartySlots(f.Id(1)) is null && !f.PartyPackets[0].Any(IsAdmissionPacket),
            $"delayed party acceptance cannot re-add the PvP {(entrantIsRequester ? "requester" : "accepter")} or send its accepted/roster transition");
    }

    // Pause the real roster send after the response has consumed the pending invitation and
    // mutated party membership. PvP admission must not overtake this in-flight publication.
    private static async Task PausedAcceptancePublication(Action<bool, string> check, bool entrantIsRequester)
    {
        await using var f = await Fixture.Create();
        var requester = entrantIsRequester ? 0 : 1;
        var accepter = 1 - requester;
        await f.Harness.Service.HandleRequestAsync(f.Id(requester), f.Uid(accepter), PartyHarness.DefaultInfo);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        await f.AttachParty(0, async packet =>
        {
            if (packet.ProtocolId == GameProtocolEngine.PartyInfoNotification && Interlocked.Exchange(ref first, 1) == 0)
            {
                blocked.TrySetResult();
                await release.Task;
            }
            f.PartyPackets[0].Enqueue(packet);
        });
        var accepting = f.Harness.Service.HandleResponseAsync(f.Id(accepter), f.Uid(requester), true);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var entering = f.EnterPvp();
        try
        {
            var completed = await Task.WhenAny(entering, Task.Delay(TimeSpan.FromSeconds(1))) == entering;
            check(completed && !f.Service.IsBusy(f.Id(0)) && !f.PvpPackets.Any(p => p.ProtocolId == 41 && p.Payload.Length > 2),
                $"PvP cannot acquire the {(entrantIsRequester ? "requester" : "accepter")} while consumed party acceptance is still publishing a real roster");
        }
        finally { release.TrySetResult(); }
        await accepting.WaitAsync(TimeSpan.FromSeconds(5));
        await entering.WaitAsync(TimeSpan.FromSeconds(5));
        await f.EnterPvp();
        f.Clear();
        await f.Harness.Service.HandleResponseAsync(f.Id(accepter), f.Uid(requester), true);
        check(f.Service.IsBusy(f.Id(0)) && f.Harness.Service.GetPartySlots(f.Id(0)) is null
            && !f.PartyPackets[0].Any(IsAdmissionPacket),
            "after acceptance publication completes, actual PvP entry removes party membership and consumed response cannot replay into it");
    }

    private static async Task CancelledInvitationDoesNotSurviveReturn(Action<bool, string> check, bool entrantIsRequester)
    {
        await using var f = await Fixture.Create(cancelInvitations: true);
        var requester = entrantIsRequester ? 0 : 1;
        var accepter = 1 - requester;
        await f.Harness.Service.HandleRequestAsync(f.Id(requester), f.Uid(accepter), PartyHarness.DefaultInfo);
        await f.EnterPvp();
        await f.LeavePvp();
        await f.Harness.MoveToAsync("阿尔法", 1, 0, f.Uid(0));
        f.Clear();
        await f.Harness.Service.HandleResponseAsync(f.Id(accepter), f.Uid(requester), true);
        check(!f.Service.IsBusy(f.Id(0)) && f.Harness.Service.GetPartySlots(f.Id(0)) is null
            && !f.PartyPackets[0].Any(IsAdmissionPacket),
            $"PvP preparation permanently cancels the {(entrantIsRequester ? "outgoing" : "incoming")} invitation, even after returning to town before its deadline");
    }

    private static bool IsAdmissionPacket(GameServerPacket packet) => packet.Type == GameProtocolEngine.NotificationPacketType
        && packet.ProtocolId is GameProtocolEngine.PartyInfoNotification or GameProtocolEngine.ResponsePeerNotification;

    private static async Task SingleMemberCreationCannotCrossPvp(Action<bool, string> check)
    {
        await using var f = await Fixture.Create(cancelInvitations: true);
        await f.EnterPvp();
        f.Clear();
        await f.Harness.Service.HandleSetInfoAsync(f.Id(0), 1, [], 4);
        check(f.Harness.Service.GetPartySlots(f.Id(0)) is null && !f.PartyPackets[0].Any(IsAdmissionPacket),
            "party creation without an invitation also respects the same PvP admission ownership");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly string[] Names = ["阿尔法", "布拉沃"];
        private static readonly string[] Accounts = ["alice", "bobby"];
        internal PartyHarness Harness = null!;
        internal PvpService Service = null!;
        internal GameplayDatagramService Udp = null!;
        internal ConcurrentQueue<GameServerPacket>[] PartyPackets = [new(), new()];
        internal ConcurrentQueue<GameServerPacket> PvpPackets = new();
        private Guid _attachment;
        private ushort _sequence;
        internal Guid Id(int index) => Harness.IdOf(Names[index]);
        internal ushort Uid(int index) => Harness.UidOf(Names[index]);

        internal static async Task<Fixture> Create(bool cancelInvitations = false)
        {
            var f = new Fixture { Harness = await PartyHarness.CreateAsync() };
            Directory.CreateDirectory(Path.Combine(f.Harness.Root, "map"));
            File.WriteAllText(Path.Combine(f.Harness.Root, "map", "map.lst"), "50000 `random`\n50035 `pub.map`\n");
            File.WriteAllText(Path.Combine(f.Harness.Root, "map", "pub.map"), "[player number] 2 8\n");
            var scripts = new ScriptFileSystem(f.Harness.Options, NullLogger<ScriptFileSystem>.Instance);
            var runtime = new RuntimeState();
            f.Udp = new(f.Harness.Options, runtime, NullLogger<GameplayDatagramService>.Instance);
            f.Service = new(f.Harness.Store, f.Udp, new(scripts, NullLogger<PvpExperienceCatalog>.Instance),
                new(scripts), TimeProvider.System, NullLogger<PvpService>.Instance);
            var session = runtime.Add("pvp-party-admission", "alice");
            f.Udp.RegisterChannelSession(session, IPAddress.Loopback);
            f.Udp.SetNatReport(session.Id, new(0, IPAddress.Loopback, IPAddress.Loopback, 42601, 1200));
            var character = (await f.Harness.Store.GetCharactersAsync("alice"))[0];
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var appearance = GameProtocolEngine.CreateCurrentCharacterInfo(new(0, Encoding.GetEncoding(936).GetBytes(character.Name), 0, 0, 1), f.Uid(0));
            var detail = PartyMemberDetailProjection.CreatePacket(f.Uid(0), character,
                new(100, 100, 1, 1, 1, 1, 0, 0, 0, 0, 1000, 0, 0, 100, 100, 100, 0, 0, 0), [], false);
            f._attachment = Guid.NewGuid();
            await f.Service.AttachAsync(new(f._attachment, session.Id, "alice", character.Id, f.Uid(0), character.AccountUid,
                () => ValueTask.FromResult<PvpModelSnapshot?>(new(appearance, detail)),
                packet => { f.PvpPackets.Enqueue(packet); return ValueTask.CompletedTask; },
                () => f.Harness.Directory.TryGetSnapshot(character.Id, out _),
                async () =>
                {
                    if (cancelInvitations) await f.Harness.Service.PrepareForPvpAsync(character.Id);
                    else await f.Harness.Service.TryLeavePartySilentlyAsync(character.Id);
                    await f.Harness.LeaveTownAsync(Names[0]);
                }, () => { }));
            for (var i = 0; i < 2; i++)
            {
                var index = i;
                await f.AttachParty(i, packet => { f.PartyPackets[index].Enqueue(packet); return ValueTask.CompletedTask; });
            }
            f.Clear();
            return f;
        }

        internal Task AttachParty(int index, Func<GameServerPacket, ValueTask> send) => Harness.Service.AttachChannelAsync(
            Accounts[index], Names[index], Id(index), Uid(index), Harness.SessionIdOf(Names[index]), send);

        internal Task EnterPvp()
        {
            var body = new byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(body, ++_sequence);
            body[2] = 1; body[3] = 1;
            return Service.HandleAsync(Id(0), _attachment, 53, body);
        }

        internal Task LeavePvp()
        {
            var body = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(body, ++_sequence);
            body[3] = 254;
            return Service.HandleAsync(Id(0), _attachment, 55, body);
        }

        internal void Clear() { foreach (var packets in PartyPackets) packets.Clear(); PvpPackets.Clear(); }
        public async ValueTask DisposeAsync() { Service.Dispose(); Udp.Dispose(); await Harness.DisposeAsync(); }
    }
}
