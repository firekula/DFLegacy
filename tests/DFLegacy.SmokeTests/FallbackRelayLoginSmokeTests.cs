using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class FallbackRelayLoginSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-relay-login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), ScriptPvfPath = "" };
            var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
            options.FallbackRelay.TcpPort = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            using (var udpPort = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                options.FallbackRelay.UdpPort = ((IPEndPoint)udpPort.Client.LocalEndPoint!).Port;
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.CreateAccountAsync(new CreateAccountRequest("relayalice", "password123", OicqCode: "123456"));
            await store.CreateAccountAsync(new CreateAccountRequest("relaybobby", "password456", OicqCode: "123456"));
            var alice = (await store.FindAccountAsync("relayalice"))!;
            var bob = (await store.FindAccountAsync("relaybobby"))!;
            var runtime = new RuntimeState();
            using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
            var first = runtime.Add("channel:test", "127.0.0.1:41000");
            var second = runtime.Add("channel:test", "127.0.0.1:41001");
            contexts.RegisterChannelSession(first, IPAddress.Loopback); contexts.RegisterChannelSession(second, IPAddress.Loopback);
            using var relay = new FallbackRelayService(options, contexts, NullLogger<FallbackRelayService>.Instance);
            await relay.StartAsync(CancellationToken.None);
            try
            {
                var wrong = await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, first.Id, "relayalice", "wrong");
                check(wrong is null && contexts.FindRelayAdmission(alice.AccountUid, IPAddress.Loopback) is null,
                    "actual password verification rejects wrong credentials without a relay owner");
                var verified = (await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, first.Id, "relayalice", "password123"))!;
                check(verified.Admission is not null && contexts.IsCurrentRelayAdmission(verified.Admission),
                    "only successful store password authentication grants canonical relay ownership");
                var normal = ChannelLoginAuthentication.CreateReply(options, contexts, verified, false, relay).Payload;
                byte[] expected = LoginTail(0, alice.AccountUid, options.FallbackRelay.TcpPort, options.FallbackRelay.UdpPort);
                check(normal.SequenceEqual(expected), "real authenticated normal login advertises actual bound ports and account id");
                var pvp = ChannelLoginAuthentication.CreateReply(options, contexts, verified, true, relay).Payload;
                expected[3] = 4;
                check(pvp.SequenceEqual(expected), "PvP uses the same live fallback while preserving native mode4");
                options.Pvp.RelayHost = "127.0.0.2"; options.Pvp.RelayTcpPort = 8101; options.Pvp.RelayUdpPort = 8201;
                var external = ChannelLoginAuthentication.CreateReply(options, contexts, verified, true, relay).Payload;
                check(external.AsSpan(13, 9).SequenceEqual("127.0.0.2"u8)
                    && BinaryPrimitives.ReadUInt32LittleEndian(external.AsSpan(22)) == 8101,
                    "explicit external PvP relay remains selectable for an authenticated session");
                foreach (var scope in new[] { "party", "dungeon", "pvp", "character" })
                {
                    if (scope == "party") contexts.SetRelayContext(first.Id, 7);
                    if (scope == "dungeon") contexts.SetRelayDungeonInstance(first.Id, new object());
                    var attachment = Guid.NewGuid(); var room = Guid.NewGuid();
                    if (scope == "pvp") contexts.SetPvpContext(first.Id, attachment, room, null);
                    var attempt = await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, first.Id,
                        "relaybobby", "password456", characterAttached: scope == "character");
                    check(attempt is null && verified.Admission is not null && contexts.IsCurrentRelayAdmission(verified.Admission)
                        && contexts.FindRelayAdmission(bob.AccountUid, IPAddress.Loopback) is null,
                        "attached " + scope + " prevents another account inheriting the previous world scope");
                    if (scope == "pvp") contexts.ClearPvpContext(first.Id, attachment, room);
                    contexts.SetRelayContext(first.Id, null);
                }
                var lookup = (await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, second.Id, "relayalice", null))!;
                var unavailable = ChannelLoginAuthentication.CreateReply(options, contexts, lookup, false, relay).Payload;
                check(lookup.Admission is null && unavailable.Length == 23 && unavailable.AsSpan(9, 12).IndexOfAnyExcept((byte)0) < 0
                    && ReferenceEquals(contexts.FindRelayAdmission(alice.AccountUid, IPAddress.Loopback), verified.Admission),
                    "username-only compatibility cannot replace another authenticated owner or advertise a relay");
                await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, first.Id, "relaybobby", "wrong");
                check(verified.Admission is not null && contexts.IsCurrentRelayAdmission(verified.Admission),
                    "failed identity switch does not revoke the current verified owner");
                var switched = (await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, first.Id, "relaybobby", "password456"))!;
                check(contexts.FindRelayAdmission(alice.AccountUid, IPAddress.Loopback) is null && switched.Admission is not null
                    && switched.Admission.AccountUid == bob.AccountUid,
                    "successful same-session identity switch retires only its old account admission");
                var replacement = (await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, second.Id, "relaybobby", "password456"))!;
                contexts.UnregisterChannelSession(first.Id);
                check(replacement.Admission is not null && contexts.IsCurrentRelayAdmission(replacement.Admission)
                    && (switched.Admission is null || !contexts.IsCurrentRelayAdmission(switched.Admission)),
                    "old channel teardown cannot revoke the newly password-authenticated game owner");
                var downgraded = (await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, second.Id, "relaybobby", null))!;
                check(downgraded.Admission is null && contexts.FindRelayAdmission(bob.AccountUid, IPAddress.Loopback) is null,
                    "a successful username-only identity switch cannot retain an earlier relay privilege");
                await relay.StopAsync(CancellationToken.None);
                var stopped = ChannelLoginAuthentication.CreateReply(options, contexts, replacement, false, relay).Payload;
                check(stopped.Length == 23 && stopped.AsSpan(9, 12).IndexOfAnyExcept((byte)0) < 0,
                    "unavailable transport always produces the complete ACT1 login tail with zero endpoint");
            }
            finally { await relay.StopAsync(CancellationToken.None); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] LoginTail(byte mode, uint uid, int tcp, int udp)
    {
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        writer.Write(new byte[] { 1, 0, 0, mode, 0 }); writer.Write(uid);
        writer.Write(9u); writer.Write("127.0.0.1"u8); writer.Write((uint)tcp); writer.Write((uint)udp);
        writer.Write(new byte[] { 1, 0 }); return buffer.ToArray();
    }
}
