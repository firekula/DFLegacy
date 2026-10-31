using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;

internal static class GameplayDatagramSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var previousRelay = Environment.GetEnvironmentVariable("DFLEGACY_UDP_RELAY");
        var previousSink = Environment.GetEnvironmentVariable("DFLEGACY_UDP_PEER_SINK");
        Environment.SetEnvironmentVariable("DFLEGACY_UDP_RELAY", "1");
        Environment.SetEnvironmentVariable("DFLEGACY_UDP_PEER_SINK", null);
        try
        {
            await CheckSessionEndpointsAsync(check);
            await CheckRelayContextsAsync(check);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFLEGACY_UDP_RELAY", previousRelay);
            Environment.SetEnvironmentVariable("DFLEGACY_UDP_PEER_SINK", previousSink);
        }
    }

    private static async Task CheckSessionEndpointsAsync(Action<bool, string> check)
    {
        using var alice = BindClient();
        using var bob = BindClient();
        using var stranger = BindClient();
        var options = new ServerOptions { EnablePacketTracing = true, HexDumpLimit = 256 };
        // Reserve both ports together so the OS cannot return the same free port twice.
        using (var primaryReservation = BindClient())
        using (var secondaryReservation = BindClient())
        {
            options.GameplayDatagram.PrimaryPort = LocalEndpoint(primaryReservation).Port;
            options.GameplayDatagram.SecondaryPort = LocalEndpoint(secondaryReservation).Port;
        }

        var primary = new IPEndPoint(IPAddress.Loopback, options.GameplayDatagram.PrimaryPort);
        var secondary = new IPEndPoint(IPAddress.Loopback, options.GameplayDatagram.SecondaryPort);
        var runtime = new RuntimeState();
        var logger = new ReadyLogger();
        using var service = new GameplayDatagramService(options, runtime, logger);
        await service.StartAsync(CancellationToken.None);
        try
        {
            // BackgroundService.StartAsync on .NET 10 does not wait for ExecuteAsync to bind.
            await logger.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var aliceSession = runtime.Add("channel:test", "127.0.0.1:41001");
            var bobSession = runtime.Add("channel:test", "127.0.0.1:41002");
            service.RegisterChannelSession(aliceSession, IPAddress.Loopback);
            service.RegisterChannelSession(bobSession, IPAddress.Loopback);

            await ProbeAsync(alice, primary, 1, check);
            await ProbeAsync(bob, secondary, 5, check);
            check(!await SendHpAsync(service, aliceSession.Id, 100, 1000)
                    && !await SendHpAsync(service, bobSession.Id, 200, 2000),
                "anonymous same-IP NAT probes cannot bind either channel session before its own NAT report");

            service.SetNatReport(aliceSession.Id, NatReport(alice));
            service.SetNatReport(bobSession.Id, NatReport(bob));
            service.SetRelayContext(aliceSession.Id, 1);
            service.SetRelayContext(bobSession.Id, 1);
            await ExpectHpAsync(service, aliceSession.Id, alice, 100, 1000, 0, check);
            await ExpectHpAsync(service, bobSession.Id, bob, 200, 2000, 0, check);

            // ACK blocks use type zero too. A 16-bit selective ACK is ten bytes,
            // so a first-byte/length-only NAT check silently drops reliable ACKs.
            byte[] selectiveAck = [0, 1, 2, 0, 0, 0, 16, 0, 0xff, 0xff];
            check(GameDatagramProtocol.TryReadBlock(selectiveAck, out var ackFrame, out var ackBytes)
                    && ackFrame is null && ackBytes == selectiveAck.Length,
                "a ten-byte selective ACK is a complete MT UDP control block");
            await bob.SendAsync(selectiveAck, primary);
            await ExpectDatagramAsync(alice, selectiveAck, primary, check,
                "a ten-byte reliable ACK reaches the peer instead of being swallowed as NAT keepalive");
            await alice.SendAsync(selectiveAck, secondary);
            await ExpectDatagramAsync(bob, selectiveAck, secondary, check,
                "the secondary listener preserves ten-byte reliable ACKs that are not NAT keepalives");

            // The two wire formats can even overlap byte-for-byte. Only the
            // secondary reflector owns NAT keepalives; primary traffic is MT UDP.
            byte[] overlappingAck = [0, 2, 1, 0, 0, 0, 16, 0, 0, 0];
            check(GameDatagramProtocol.TryReadBlock(overlappingAck, out _, out var overlapBytes)
                    && overlapBytes == overlappingAck.Length,
                "a selective ACK can share the secondary NAT keepalive signature");
            await bob.SendAsync(overlappingAck, primary);
            await ExpectDatagramAsync(alice, overlappingAck, primary, check,
                "the primary gameplay listener relays even an ACK whose bytes resemble a secondary NAT keepalive");

            // Exercise both reflector loops and both clients in alternating order. Each probe reply
            // also serves as a FIFO barrier for the preceding keepalive on that listener.
            await ProbeAsync(bob, primary, 1, check);
            await KeepAliveThenProbeAsync(alice, secondary, 5, check);
            await KeepAliveThenProbeAsync(bob, primary, 1, check);
            await KeepAliveThenProbeAsync(alice, primary, 1, check);
            await KeepAliveThenProbeAsync(bob, secondary, 5, check);
            await KeepAliveThenProbeAsync(stranger, primary, 1, check);
            await KeepAliveThenProbeAsync(stranger, secondary, 5, check);
            await ExpectHpAsync(service, aliceSession.Id, alice, 101, 1001, 1, check);
            await ExpectHpAsync(service, bobSession.Id, bob, 201, 2001, 1, check);

            var unknownGameplay = Gameplay(301, 7, 0x91);
            await stranger.SendAsync(unknownGameplay, primary);
            await ProbeAsync(stranger, primary, 1, check);
            var aliceGameplay = Gameplay(302, 0, 0xA1);
            await alice.SendAsync(aliceGameplay, primary);
            await ExpectDatagramAsync(bob, aliceGameplay, primary, check,
                "same-IP Alice gameplay reaches Bob byte-for-byte, without any preceding unknown-source broadcast");
            var bobGameplay = Gameplay(303, 1, 0xB1);
            await bob.SendAsync(bobGameplay, secondary);
            await ExpectDatagramAsync(alice, bobGameplay, secondary, check,
                "same-IP Bob gameplay reaches Alice byte-for-byte through the secondary listener");
            // A self-echo or duplicate would be ahead of these markers in the receiver queue.
            await ExpectHpAsync(service, aliceSession.Id, alice, 102, 1002, 2, check);
            await ExpectHpAsync(service, bobSession.Id, bob, 202, 2002, 2, check);
            CheckTrace(runtime, aliceSession.Id, aliceGameplay, check,
                "Alice gameplay is attributed to her complete reported IP:port");
            CheckTrace(runtime, bobSession.Id, bobGameplay, check,
                "Bob gameplay is attributed to his complete reported IP:port");
            check(!HasReceiveTrace(runtime, unknownGameplay),
                "an unregistered source is neither broadcast nor attributed to either same-IP session");

            // A third recipient makes rejected ambiguous/stale traffic observable without a sleep:
            // the next known packet/HP marker must be first in its queue.
            var observerSession = runtime.Add("channel:test", "127.0.0.1:41003");
            service.RegisterChannelSession(observerSession, IPAddress.Loopback);
            service.SetNatReport(observerSession.Id, NatReport(stranger));
            service.SetRelayContext(observerSession.Id, 1);
            service.SetNatReport(aliceSession.Id, NatReport(bob));
            var ambiguousGameplay = Gameplay(304, 1, 0xC1);
            await bob.SendAsync(ambiguousGameplay, primary);
            await ProbeAsync(bob, primary, 1, check);
            var staleGameplay = Gameplay(305, 0, 0xD1);
            await alice.SendAsync(staleGameplay, primary);
            await ProbeAsync(alice, primary, 1, check);
            await ExpectHpAsync(service, observerSession.Id, stranger, 300, 3000, 0, check);
            check(!HasReceiveTrace(runtime, ambiguousGameplay)
                    && !HasReceiveTrace(runtime, staleGameplay),
                "a colliding endpoint is ambiguous and the old endpoint becomes unknown after a session report changes");

            var observerGameplay = Gameplay(306, 2, 0xE1);
            await stranger.SendAsync(observerGameplay, primary);
            await ExpectDatagramAsync(bob, observerGameplay, primary, check,
                "two reports sharing a destination receive one relay copy at that endpoint");
            await ExpectHpAsync(service, bobSession.Id, bob, 203, 2003, 3, check);
            await ExpectHpAsync(service, observerSession.Id, stranger, 301, 3001, 1, check);

            // Moving the former owner away must leave Bob's endpoint usable and attributable.
            service.SetNatReport(aliceSession.Id, NatReport(alice));
            var afterUpdate = Gameplay(307, 1, 0xF1);
            await bob.SendAsync(afterUpdate, primary);
            await ExpectDatagramAsync(alice, afterUpdate, primary, check,
                "updating a colliding session preserves the remaining owner's relay route");
            await ExpectDatagramAsync(stranger, afterUpdate, primary, check,
                "the remaining owner can still relay to every other registered endpoint after an update");
            CheckTrace(runtime, bobSession.Id, afterUpdate, check,
                "updating a former owner does not erase the remaining owner's receive attribution");

            service.SetNatReport(aliceSession.Id, NatReport(bob));
            service.UnregisterChannelSession(aliceSession.Id);
            var afterUnregister = Gameplay(308, 1, 0xF2);
            await bob.SendAsync(afterUnregister, primary);
            await ExpectDatagramAsync(stranger, afterUnregister, primary, check,
                "unregistering a colliding session preserves the remaining owner's relay route");
            CheckTrace(runtime, bobSession.Id, afterUnregister, check,
                "unregistering a former owner does not erase the remaining owner's receive attribution");
            check(!await SendHpAsync(service, aliceSession.Id, 103, 1003),
                "an unregistered channel session no longer has a direct UDP send route");
            await ExpectHpAsync(service, bobSession.Id, bob, 204, 2004, 4, check);

            service.SetNatReport(bobSession.Id, NatReport(bob) with { PublicPort = 0 });
            check(!await SendHpAsync(service, bobSession.Id, 205, 2005),
                "a NAT report with no public port clears the old route instead of falling back to anonymous probes");
            service.SetNatReport(bobSession.Id, NatReport(bob));
            await ExpectHpAsync(service, bobSession.Id, bob, 206, 2006, 5, check);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await service.StopAsync(stopTimeout.Token);
        }
    }

    private static async Task CheckRelayContextsAsync(Action<bool, string> check)
    {
        using var alice = BindClient();
        using var bob = BindClient();
        using var outsider = BindClient();
        var options = new ServerOptions();
        using (var primaryReservation = BindClient())
        using (var secondaryReservation = BindClient())
        {
            options.GameplayDatagram.PrimaryPort = LocalEndpoint(primaryReservation).Port;
            options.GameplayDatagram.SecondaryPort = LocalEndpoint(secondaryReservation).Port;
        }

        var primary = new IPEndPoint(IPAddress.Loopback, options.GameplayDatagram.PrimaryPort);
        var secondary = new IPEndPoint(IPAddress.Loopback, options.GameplayDatagram.SecondaryPort);
        var runtime = new RuntimeState();
        var logger = new ReadyLogger();
        using var service = new GameplayDatagramService(options, runtime, logger);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await logger.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var aliceSession = runtime.Add("channel:test", "127.0.0.1:42001");
            var bobSession = runtime.Add("channel:test", "127.0.0.1:42002");
            var outsiderSession = runtime.Add("channel:test", "127.0.0.1:42003");
            foreach (var (session, client) in new[]
            {
                (aliceSession, alice), (bobSession, bob), (outsiderSession, outsider)
            })
            {
                service.RegisterChannelSession(session, IPAddress.Loopback);
                service.SetNatReport(session.Id, NatReport(client));
            }

            var sequences = new Dictionary<Guid, uint>();
            uint gameplaySequence = 400;
            async Task<byte[]> NoRelayAsync(
                UdpClient source, UdpClient destination, Guid destinationId,
                IPEndPoint listener, string message)
            {
                var gameplay = Gameplay(gameplaySequence++, 0, 0xC7);
                await source.SendAsync(gameplay, listener);
                // A reflected probe on the same listener is a FIFO processing barrier. A direct
                // HP marker must then be first at the rejected recipient: no timeout-based absence.
                await ProbeAsync(source, listener, listener.Equals(primary) ? (byte)1 : (byte)5, check);
                var next = sequences.GetValueOrDefault(destinationId);
                await ExpectHpAsync(service, destinationId, destination, 700, 7000, next, check, message);
                sequences[destinationId] = next + 1;
                return gameplay;
            }

            async Task RelayAsync(
                UdpClient source, UdpClient destination, IPEndPoint listener, string message)
            {
                var gameplay = Gameplay(gameplaySequence++, 0, 0xD8);
                await source.SendAsync(gameplay, listener);
                await ExpectDatagramAsync(destination, gameplay, listener, check, message);
            }

            await NoRelayAsync(alice, bob, bobSession.Id, primary,
                "NAT endpoints alone never enable a gameplay route without an explicit party context");
            service.SetRelayParty(aliceSession.Id, 1);
            service.SetRelayParty(bobSession.Id, 1);
            service.SetRelayParty(outsiderSession.Id, 2);
            await RelayAsync(alice, bob, primary,
                "same-party town members can exchange handshake/gameplay datagrams through the primary listener");
            await RelayAsync(bob, alice, secondary,
                "same-party town handshake remains bidirectional through the secondary listener");
            var otherPartyProbe = await NoRelayAsync(alice, outsider, outsiderSession.Id, primary,
                "a town member of another party cannot receive this party's gameplay");
            // The accepted copy from that isolation check still belongs in Bob's queue.
            await ExpectDatagramAsync(bob, otherPartyProbe, primary, check,
                "rejecting a different town party preserves delivery to the intended same-party recipient");

            var dungeon = new object();
            service.SetRelayDungeonInstance(aliceSession.Id, dungeon);
            await NoRelayAsync(alice, bob, bobSession.Id, primary,
                "a member entering a dungeon cannot relay gameplay to a party member still in town");
            await NoRelayAsync(bob, alice, aliceSession.Id, secondary,
                "a town member cannot relay gameplay to a party member already in a dungeon");

            service.SetRelayDungeonInstance(bobSession.Id, dungeon);
            await RelayAsync(alice, bob, primary,
                "party members in the same dungeon instance receive the original complete datagram");
            await RelayAsync(bob, alice, secondary,
                "same-instance dungeon forwarding remains bidirectional");

            service.SetRelayContext(outsiderSession.Id, 1, new object());
            var otherInstanceProbe = await NoRelayAsync(alice, outsider, outsiderSession.Id, primary,
                "matching party ids cannot bridge distinct dungeon instance objects");
            await ExpectDatagramAsync(bob, otherInstanceProbe, primary, check,
                "rejecting another instance preserves delivery to the actual shared-instance recipient");

            // This is the death-return regression: party membership is retained, while scene
            // ownership changes immediately before the returning client's town-state packet.
            service.SetRelayDungeonInstance(aliceSession.Id, null);
            await NoRelayAsync(bob, alice, aliceSession.Id, primary,
                "after one member returns to town, remaining dungeon traffic cannot reach that client");
            await NoRelayAsync(alice, bob, bobSession.Id, secondary,
                "the returning member's stale gameplay cannot reach the remaining dungeon client");
            service.SetRelayParty(bobSession.Id, 1);
            await NoRelayAsync(bob, alice, aliceSession.Id, secondary,
                "a roster replay preserves the existing dungeon instance and cannot reopen the town route");

            service.SetRelayDungeonInstance(aliceSession.Id, dungeon);
            await RelayAsync(alice, bob, primary,
                "reentry to the actual shared dungeon restores the gameplay route");
            service.SetRelayParty(bobSession.Id, null);
            await NoRelayAsync(alice, bob, bobSession.Id, primary,
                "leaving a party disables incoming gameplay even while its dungeon object remains bound");
            await NoRelayAsync(bob, alice, aliceSession.Id, secondary,
                "leaving a party also disables outgoing gameplay");
            service.SetRelayParty(bobSession.Id, 1);
            await RelayAsync(bob, alice, primary,
                "a party-only update preserves the instance reference used by the active dungeon");

            service.SetRelayContext(aliceSession.Id, 1);
            service.SetRelayContext(bobSession.Id, 1);
            await RelayAsync(alice, bob, primary,
                "after both members return, the same party can establish its town handshake again");
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await service.StopAsync(stopTimeout.Token);
        }
    }

    private static UdpClient BindClient() => new(new IPEndPoint(IPAddress.Loopback, 0));

    private static IPEndPoint LocalEndpoint(UdpClient client) =>
        (IPEndPoint)client.Client.LocalEndPoint!;

    private static GameNatReport NatReport(UdpClient client) => new(
        1, IPAddress.Loopback, IPAddress.Loopback, checked((ushort)LocalEndpoint(client).Port), 1400);

    private static byte[] Gameplay(uint sequence, byte partyIndex, byte marker)
    {
        // Two valid blocks ensure the relay preserves the entire original datagram.
        var first = GameDatagramProtocol.CreateUnreliableFrame(sequence, partyIndex, 0,
            GameDatagramProtocol.CreateApplication(1, 3, new byte[] { marker, 2, 3, 4 }));
        var second = GameDatagramProtocol.CreateUnreliableFrame(sequence + 1000, partyIndex, 1,
            GameDatagramProtocol.CreateApplication(1, 4, new byte[] { marker, 5, 6 }));
        return [.. first, .. second];
    }

    private static async Task ProbeAsync(
        UdpClient client, IPEndPoint reflector, byte type, Action<bool, string> check)
    {
        await client.SendAsync(new byte[] { type }, reflector);
        var received = await ReceiveAsync(client);
        check(received.RemoteEndPoint.Equals(reflector)
                && received.Buffer.Length == 7
                && received.Buffer[0] == GameDatagramProtocol.NatResponseType
                && BinaryPrimitives.ReadUInt16LittleEndian(received.Buffer.AsSpan(5))
                    == LocalEndpoint(client).Port,
            $"NAT probe {type} returns the sender's own reflected port from the requested listener");
    }

    private static async Task KeepAliveThenProbeAsync(
        UdpClient client, IPEndPoint reflector, byte type, Action<bool, string> check)
    {
        if (type == 5)
        {
            // Captured secondary-reflector format: zero, the NAT response,
            // followed by two reserved bytes. It is not ten zero bytes.
            var endpoint = LocalEndpoint(client);
            GameDatagramProtocol.TryCreateNatProbeResponse(new byte[] { 1 }, endpoint, out var response);
            byte[] keepalive = [0, .. response, 0, 0];
            await client.SendAsync(keepalive, reflector);
        }
        await ProbeAsync(client, reflector, type, check);
    }

    private static async Task<bool> SendHpAsync(
        GameplayDatagramService service, Guid sessionId, ushort objectId, uint hp)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return await service.SendSetActiveObjectHpAsync(
            sessionId, 0x0211, objectId, hp, 19, 3, timeout.Token);
    }

    private static async Task ExpectHpAsync(
        GameplayDatagramService service, Guid sessionId, UdpClient client,
        ushort objectId, uint hp, uint sequence, Action<bool, string> check, string? context = null)
    {
        check(await SendHpAsync(service, sessionId, objectId, hp),
            $"session {objectId} has its own direct HP datagram route");
        var received = await ReceiveAsync(client);
        check(GameDatagramProtocol.TryReadBlock(received.Buffer, out var frame, out var consumed)
                && consumed == received.Buffer.Length && frame is not null
                && frame.Sequence == sequence && frame.SenderPartyIndex == 3
                && GameDatagramProtocol.TryParseApplication(frame.Application, out var application)
                && application.ProtocolId == GameDatagramProtocol.SetActiveObjectHpProtocol
                && application.Payload.Length == 12
                && BinaryPrimitives.ReadUInt16LittleEndian(application.Payload) == 0x0211
                && BinaryPrimitives.ReadUInt16LittleEndian(application.Payload.AsSpan(2)) == objectId
                && BinaryPrimitives.ReadUInt32LittleEndian(application.Payload.AsSpan(4)) == hp
                && BinaryPrimitives.ReadUInt32LittleEndian(application.Payload.AsSpan(8)) == 19,
            context ?? $"session {objectId} receives only its own HP update with its independent sequence {sequence}; no gameplay echo or duplicate precedes it");
    }

    private static async Task ExpectDatagramAsync(
        UdpClient client, byte[] expected, IPEndPoint source, Action<bool, string> check, string message)
    {
        var received = await ReceiveAsync(client);
        check(received.RemoteEndPoint.Equals(source) && received.Buffer.SequenceEqual(expected), message);
    }

    private static async Task<UdpReceiveResult> ReceiveAsync(UdpClient client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return await client.ReceiveAsync(timeout.Token);
    }

    private static void CheckTrace(
        RuntimeState runtime, Guid sessionId, byte[] datagram, Action<bool, string> check, string message) =>
        check(runtime.RecentPackets().Any(trace => trace.SessionId == sessionId
            && trace.Direction == "RX-UDP" && trace.BodyHex == Convert.ToHexString(datagram)), message);

    private static bool HasReceiveTrace(RuntimeState runtime, byte[] datagram) =>
        runtime.RecentPackets().Any(trace => trace.Direction == "RX-UDP"
            && trace.BodyHex == Convert.ToHexString(datagram));

    private sealed class ReadyLogger : ILogger<GameplayDatagramService>
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).StartsWith("Gameplay UDP primary/secondary listeners ready", StringComparison.Ordinal))
            {
                Ready.TrySetResult();
            }
        }
    }
}
