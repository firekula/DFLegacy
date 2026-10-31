using System.Net;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class FallbackRelaySmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        AdmissionAndContext(check);
        FallbackRelayPeerIdentitySmokeTests.Run(check);
        FallbackRelaySocketSmokeTests.Options(check);
        await FallbackRelaySocketSmokeTests.RunAsync(check);
        await FallbackRelayLoginSmokeTests.RunAsync(check);
        await FallbackRelayLifecycleSmokeTests.RunAsync(check);
    }

    private static void AdmissionAndContext(Action<bool, string> check)
    {
        var options = new ServerOptions();
        var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(options, runtime,
            NullLogger<GameplayDatagramService>.Instance);
        var alice = runtime.Add("channel:test", "127.0.0.1:41001");
        var bob = runtime.Add("channel:test", "127.0.0.1:41002");
        contexts.RegisterChannelSession(alice, IPAddress.Parse("::ffff:127.0.0.1"));
        contexts.RegisterChannelSession(bob, IPAddress.Loopback);
        check(contexts.FindRelayAdmission(0x11223344, IPAddress.Loopback) is null,
            "ordinary channel registration alone grants no fallback account identity");
        check(contexts.AuthenticateRelayAccount(Guid.NewGuid(), 0x11223344) is null,
            "unknown game session cannot grant relay ownership");
        check(contexts.AuthenticateRelayAccount(alice.Id, 0) is null,
            "zero account cannot be admitted to the native relay");
        var a = contexts.AuthenticateRelayAccount(alice.Id, 0x11223344);
        var b = contexts.AuthenticateRelayAccount(bob.Id, 0x55667788);
        check(a is not null && b is not null, "existing verified game sessions obtain distinct relay admissions");
        if (a is null || b is null) return;
        check(ReferenceEquals(contexts.FindRelayAdmission(0x11223344, IPAddress.Loopback), a),
            "IPv4-mapped game peer matches its real IPv4 relay connection");
        check(contexts.FindRelayAdmission(0x11223344, IPAddress.Parse("127.0.0.2")) is null,
            "another remote address cannot register this account");
        check(!contexts.IsCurrentRelayAdmission(a with { }),
            "reconstructing identical admission fields does not mint canonical authority");
        check(!contexts.TryCaptureRelayRoute(a, 0x55667788, out _),
            "two authenticated but ungrouped players cannot relay globally");
        check(!contexts.TryCaptureRelayRoute(a, 0x11223344, out _),
            "native relay does not create a self echo loop");
        contexts.SetRelayContext(alice.Id, 7);
        contexts.SetRelayContext(bob.Id, 7);
        check(contexts.TryCaptureRelayRoute(a, 0x55667788, out var town)
                && contexts.IsCurrentRelayRoute(town!),
            "same party may perform native handshake in town's null dungeon context");
        contexts.SetRelayParty(bob.Id, 8);
        check(!contexts.IsCurrentRelayRoute(town!)
                && !contexts.TryCaptureRelayRoute(a, 0x55667788, out _),
            "party change revokes a captured output ticket and excludes foreign party");
        contexts.SetRelayParty(bob.Id, 7);
        check(!contexts.IsCurrentRelayRoute(town!)
                && contexts.TryCaptureRelayRoute(a, 0x55667788, out _),
            "rejoining the same numeric party does not revive its old queued ticket");
        var dungeon = new object();
        contexts.SetRelayDungeonInstance(alice.Id, dungeon);
        check(!contexts.TryCaptureRelayRoute(a, 0x55667788, out _),
            "town cannot receive another member's dungeon transport");
        contexts.SetRelayDungeonInstance(bob.Id, new object());
        check(!contexts.TryCaptureRelayRoute(a, 0x55667788, out _),
            "equal party ids do not equate two different dungeon instances");
        contexts.SetRelayDungeonInstance(bob.Id, dungeon);
        check(contexts.TryCaptureRelayRoute(a, 0x55667788, out var inDungeon)
                && contexts.IsCurrentRelayRoute(inDungeon!),
            "same exact dungeon instance and party can exchange opaque native bytes");
        contexts.SetRelayDungeonInstance(bob.Id, null);
        check(!contexts.IsCurrentRelayRoute(inDungeon!),
            "returning to town retires previously queued dungeon traffic");

        var room = Guid.NewGuid();
        var attachmentA = Guid.NewGuid();
        var attachmentB = Guid.NewGuid();
        check(contexts.SetPvpContext(alice.Id, attachmentA, room, null)
                && contexts.SetPvpContext(bob.Id, attachmentB, room, null),
            "existing PvP attachment semantics can provide a relay context");
        check(contexts.TryCaptureRelayRoute(a, 0x55667788, out var lobby)
                && contexts.IsCurrentRelayRoute(lobby!),
            "two seats in the same PvP room share the lobby transport");
        check(!contexts.SetPvpContext(alice.Id, Guid.NewGuid(), room, null)
                && contexts.IsCurrentRelayRoute(lobby!),
            "stale attachment cannot replace live PvP context or revoke its queue");
        var match = Guid.NewGuid();
        contexts.SetPvpContext(alice.Id, attachmentA, room, match);
        check(!contexts.IsCurrentRelayRoute(lobby!)
                && !contexts.TryCaptureRelayRoute(a, 0x55667788, out _),
            "room equality alone cannot mix lobby and active match traffic");
        contexts.SetPvpContext(bob.Id, attachmentB, room, match);
        check(contexts.TryCaptureRelayRoute(a, 0x55667788, out var playing),
            "matching PvP room and match can route");
        contexts.ClearPvpContext(alice.Id, Guid.NewGuid(), room);
        check(contexts.IsCurrentRelayRoute(playing!),
            "old PvP clear callback cannot retire another attachment's traffic");
        contexts.ClearPvpContext(alice.Id, attachmentA, room);
        check(!contexts.IsCurrentRelayRoute(playing!),
            "real PvP departure invalidates its pending delivery ticket");

        var replacementSession = runtime.Add("channel:new", "127.0.0.1:41003");
        contexts.RegisterChannelSession(replacementSession, IPAddress.Loopback);
        var newer = contexts.AuthenticateRelayAccount(replacementSession.Id, 0x11223344);
        check(newer is not null && !contexts.IsCurrentRelayAdmission(a)
                && contexts.IsCurrentRelayAdmission(newer),
            "new authenticated game incarnation retires prior account ownership");
        if (newer is null) return;
        contexts.RevokeRelayAccount(alice.Id);
        contexts.UnregisterChannelSession(alice.Id);
        check(contexts.IsCurrentRelayAdmission(newer),
            "old game teardown cannot remove the newly authenticated account owner");
        var reauthenticated = contexts.AuthenticateRelayAccount(replacementSession.Id, 0x11223344);
        check(reauthenticated is not null && !contexts.IsCurrentRelayAdmission(newer)
                && contexts.IsCurrentRelayAdmission(reauthenticated),
            "fresh password verification rotates even the same channel's local admission identity");
        contexts.UnregisterChannelSession(replacementSession.Id);
        check(reauthenticated is not null && !contexts.IsCurrentRelayAdmission(reauthenticated)
                && contexts.FindRelayAdmission(0x11223344, IPAddress.Loopback) is null,
            "retired game owner cannot leave an authenticated relay behind");
    }
}
