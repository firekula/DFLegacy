using System.Buffers.Binary;
using System.Net;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class FallbackRelayPeerIdentitySmokeTests
{
    internal static void Run(Action<bool, string> check)
    {
        var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(new(), runtime, NullLogger<GameplayDatagramService>.Instance);
        var alice = runtime.Add("channel:test", "127.0.0.1:41000");
        var bob = runtime.Add("channel:test", "127.0.0.1:41001");
        foreach (var session in new[] { alice, bob })
        {
            contexts.RegisterChannelSession(session, IPAddress.Loopback);
            contexts.SetNatReport(session.Id, new(2, IPAddress.Loopback, IPAddress.Loopback, 42000, 1472));
            contexts.SetRelayContext(session.Id, 7);
        }
        check(!contexts.TryGetChannelPeerInfo(alice.Id, 101, out _), "an unidentified game channel cannot invent a peer account id");
        var a = contexts.AuthenticateRelayAccount(alice.Id, 18000000)!;
        var b = contexts.AuthenticateRelayAccount(bob.Id, 1)!;
        var haveA = contexts.TryGetChannelPeerInfo(alice.Id, 101, out var peerA);
        var haveB = contexts.TryGetChannelPeerInfo(bob.Id, 202, out var peerB);
        check(haveA && haveB && peerA.AccountId == 18000000 && peerB.AccountId == 1,
            "native peer identities use actual account ids rather than area ids or a shared placeholder");
        if (!haveA || !haveB) return;
        var payload = GameProtocolEngine.CreateUdpPeerInfo([peerA, peerB]).Payload;
        check(payload[0] == 2 && BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(13)) == 18000000
                && BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(34)) == 1,
            "ACT1 NOTI11 carries both distinct account identities at its original record offsets");
        check(contexts.TryCaptureRelayRoute(b, peerA.AccountId, out var route) && contexts.IsCurrentRelayRoute(route!),
            "the native peer account field selects the other authenticated game account for TCP fallback");
        var legacy = runtime.Add("channel:legacy", "127.0.0.1:41002");
        contexts.RegisterChannelSession(legacy, IPAddress.Loopback);
        contexts.BindChannelAccountIdentity(legacy.Id, 42);
        check(!contexts.TryGetChannelPeerInfo(legacy.Id, 303, out _), "bound identity without a NAT report cannot fabricate an endpoint");
        contexts.SetNatReport(legacy.Id, new(2, IPAddress.Loopback, IPAddress.Loopback, 42001, 1472));
        check(contexts.TryGetChannelPeerInfo(legacy.Id, 303, out var legacyPeer) && legacyPeer.AccountId == 42
            && contexts.FindRelayAdmission(42, IPAddress.Loopback) is null,
            "ordinary UDP retains a looked-up account identity without gaining TCP relay admission");
    }
}
