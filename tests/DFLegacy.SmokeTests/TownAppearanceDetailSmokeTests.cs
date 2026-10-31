using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TownAppearanceDetailSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await CheckRecoveryAndReplayAsync(check);
        await CheckRecoveryDuringTownRosterAsync(check);
        await CheckSceneAndTicketBoundariesAsync(check);
        await CheckPrivateAreaAsync(check);
        await CheckConcurrentReplayAsync(check);
    }

    private static async Task CheckRecoveryAndReplayAsync(Action<bool, string> check)
    {
        var directory = CreateDirectory();
        var alice = new Channel();
        var aliceTicket = await directory.EnterAsync(Entry(1, alice), CancellationToken.None);
        await directory.RefreshAppearanceAsync(aliceTicket, Basic(1), CancellationToken.None, Detail(1, 37));
        alice.Clear();

        var bob = new Channel();
        var bobTicket = await directory.EnterAsync(Entry(2, bob), CancellationToken.None);
        var initial = bob.Packets;
        check(initial.Length >= 3 && IsUserInfo(initial[0], 0, 1)
                && initial[1].ProtocolId == GameProtocolEngine.AreaUsersNotification
                && IsUserInfo(initial[2], 1, 1) && Recovery(initial[2]) == 37,
            "a returning town client receives basic appearance before its roster and initializes foreign recovery only after entering town");

        await directory.RefreshAppearanceAsync(bobTicket, Basic(2), CancellationToken.None, Detail(2, 28));
        var introductions = alice.Packets;
        check(introductions.Length >= 3 && IsUserInfo(introductions[0], 0, 2)
                && IsUserInfo(introductions[1], 1, 2) && Recovery(introductions[1]) == 28
                && introductions[2].ProtocolId == GameProtocolEngine.UserAreaNotification,
            "an existing town member receives the newcomer's real recovery after basic appearance and before the join signal");

        var otherArea = new Channel();
        await directory.EnterAsync(Entry(3, otherArea, areaId: 2), CancellationToken.None);
        alice.Clear();
        bob.Clear();
        otherArea.Clear();
        await directory.RefreshAppearanceRecoveryAsync(aliceTicket, 38, CancellationToken.None);
        check(bob.Packets.Length == 1 && IsUserInfo(bob.Packets[0], 1, 1)
                && Recovery(bob.Packets[0]) == 38 && alice.Packets.Length == 0 && otherArea.Packets.Length == 0,
            "a town recovery tick updates only known foreign actors in the owner's area, without a self reset or repeated join");
        var originalDetail = initial[2];
        var recoveredDetail = bob.Packets.Single();
        check(originalDetail.Payload.AsSpan(0, 93).SequenceEqual(recoveredDetail.Payload.AsSpan(0, 93))
                && originalDetail.Payload.AsSpan(94).SequenceEqual(recoveredDetail.Payload.AsSpan(94))
                && Recovery(originalDetail) == 37,
            "background recovery changes only the stamina byte and preserves the cached stats, equipment and prior immutable packet");
        check(recoveredDetail.Payload.AsSpan(recoveredDetail.Payload.Length - 4)
                .SequenceEqual(new byte[] { 1, 20, 10, 0 }),
            "real cached Town recovery replay retains the peer's skill20 level10 instead of clearing its skill table");

        var charlie = new Channel();
        await directory.EnterAsync(Entry(4, charlie), CancellationToken.None);
        check(charlie.Packets.Where(p => IsUserInfo(p, 1, 1)).Select(Recovery).SequenceEqual(new byte[] { 38 }),
            "a later town newcomer replays the updated recovery rather than the original weakened snapshot");

        // A notification before mode 0 must not create a foreign combat model.
        var unknown = new Channel();
        var unknownTicket = await directory.EnterAsync(Entry(5, unknown), CancellationToken.None);
        bob.Clear();
        await directory.RefreshAppearanceRecoveryAsync(unknownTicket, 45, CancellationToken.None);
        check(bob.Packets.Length == 0,
            "a recovery notification without a cached basic appearance never sends mode 1 ahead of USERINFO mode 0");
    }

    private static async Task CheckRecoveryDuringTownRosterAsync(Action<bool, string> check)
    {
        var directory = CreateDirectory();
        var aliceTicket = await directory.EnterAsync(Entry(1, new Channel(),
            peer: Peer(1)), CancellationToken.None);
        await directory.RefreshAppearanceAsync(aliceTicket, Basic(1), CancellationToken.None, Detail(1, 37));
        var bob = new Channel
        {
            BeforeSendAsync = async packet =>
            {
                if (packet.ProtocolId == GameProtocolEngine.AreaUsersNotification)
                {
                    // The server has registered B, but its town roster has not
                    // reached native scene dispatch yet. This tick must only
                    // update the cached detail, not initialize B's old actor.
                    await directory.RefreshAppearanceRecoveryAsync(aliceTicket, 38, CancellationToken.None);
                }
            }
        };
        await directory.EnterAsync(Entry(2, bob, peer: Peer(2)), CancellationToken.None);
        var packets = bob.Packets;
        var basic = Array.FindIndex(packets, p => IsUserInfo(p, 0, 1));
        var roster = Array.FindIndex(packets, p => p.ProtocolId == GameProtocolEngine.AreaUsersNotification);
        var detail = Array.FindIndex(packets, p => IsUserInfo(p, 1, 1));
        var peer = Array.FindIndex(packets, p => p.ProtocolId == GameProtocolEngine.UdpPeerInfoNotification);
        check(basic >= 0 && basic < roster && roster < detail && detail < peer
                && packets.Count(p => IsUserInfo(p, 1, 1)) == 1
                && Recovery(packets[detail]) == 38,
            "a recovery tick during town entry is cached, then sent once with the latest recovery after roster and before peer table");
    }

    private static async Task CheckSceneAndTicketBoundariesAsync(Action<bool, string> check)
    {
        var directory = CreateDirectory();
        var alice = new Channel();
        var bob = new Channel();
        var aliceInTown = true;
        var bobInTown = true;
        var aliceId = Guid.NewGuid();
        var aliceTicket = await directory.EnterAsync(Entry(1, alice, characterId: aliceId,
            isInTown: () => aliceInTown), CancellationToken.None);
        await directory.RefreshAppearanceAsync(aliceTicket, Basic(1), CancellationToken.None, Detail(1, 37));
        var bobTicket = await directory.EnterAsync(Entry(2, bob, isInTown: () => bobInTown), CancellationToken.None);
        bob.Clear();

        bobInTown = false;
        await directory.RefreshAppearanceRecoveryAsync(aliceTicket, 38, CancellationToken.None);
        check(bob.Packets.Length == 0,
            "a still-registered recipient that has entered a dungeon receives no town recovery stat reset");
        bobInTown = true;
        aliceInTown = false;
        await directory.RefreshAppearanceRecoveryAsync(aliceTicket, 39, CancellationToken.None);
        check(bob.Packets.Length == 0,
            "a still-registered owner that has entered a dungeon cannot broadcast a town recovery stat reset");

        aliceInTown = true;
        await directory.RefreshAppearanceRecoveryAsync(aliceTicket, 40, CancellationToken.None);
        check(bob.Packets.Length == 1 && Recovery(bob.Packets[0]) == 40,
            "normal town recovery resumes when both sessions are in town again");

        // Recheck the recipient after the asynchronous basic-appearance send.
        bob.Clear();
        bob.BeforeSendAsync = packet =>
        {
            if (IsUserInfo(packet, 0, 1)) bobInTown = false;
            return ValueTask.CompletedTask;
        };
        await directory.RefreshAppearanceAsync(aliceTicket, Basic(1), CancellationToken.None, Detail(1, 41));
        check(bob.Packets.Count(p => p.ProtocolId == GameProtocolEngine.UserInfoNotification) == 1
                && IsUserInfo(bob.Packets[0], 0, 1),
            "entering a dungeon while basic appearance is in flight suppresses the following combat detail");
        bob.BeforeSendAsync = null;
        bobInTown = true;

        // Re-entry invalidates the old ticket even when the character/uid match.
        await directory.EnterAsync(Entry(1, new Channel(), areaId: 2, characterId: aliceId), CancellationToken.None);
        bob.Clear();
        await directory.RefreshAppearanceRecoveryAsync(aliceTicket, 99, CancellationToken.None);
        await directory.RefreshAppearanceAsync(aliceTicket, Basic(1), CancellationToken.None, Detail(1, 99));
        check(bob.Packets.Length == 0 && !bobTicket!.Member.KnownCombatDetails.ContainsKey(1),
            "a migrated member's stale ticket cannot replay or refresh its old area's combat detail");
    }

    private static async Task CheckPrivateAreaAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        options.TownSync.PrivateAreas = ["1:1"];
        var directory = CreateDirectory(options);
        var alice = new Channel();
        var bob = new Channel();
        var ticket = await directory.EnterAsync(Entry(1, alice, areaId: 1), CancellationToken.None);
        await directory.RefreshAppearanceAsync(ticket, Basic(1), CancellationToken.None, Detail(1, 37));
        await directory.EnterAsync(Entry(2, bob, areaId: 1), CancellationToken.None);
        var replayed = bob.Packets.Any(p => p.ProtocolId == GameProtocolEngine.UserInfoNotification);
        bob.Clear();
        await directory.RefreshAppearanceRecoveryAsync(ticket, 38, CancellationToken.None);
        check(!replayed && bob.Packets.Length == 0,
            "combat detail replay and recovery updates preserve private town instance isolation");
    }

    private static async Task CheckConcurrentReplayAsync(Action<bool, string> check)
    {
        var directory = CreateDirectory();
        var aliceTicket = await directory.EnterAsync(Entry(1, new Channel()), CancellationToken.None);
        await directory.RefreshAppearanceAsync(aliceTicket, Basic(1), CancellationToken.None, Detail(1, 37));
        var basicStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBasic = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bob = new Channel
        {
            BeforeSendAsync = async packet =>
            {
                if (IsUserInfo(packet, 0, 1))
                {
                    basicStarted.TrySetResult();
                    await releaseBasic.Task;
                }
            }
        };
        var entry = directory.EnterAsync(Entry(2, bob), CancellationToken.None);
        await basicStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var recovery = directory.RefreshAppearanceRecoveryAsync(aliceTicket, 38, CancellationToken.None);
        releaseBasic.TrySetResult();
        await Task.WhenAll(entry, recovery).WaitAsync(TimeSpan.FromSeconds(5));
        var packets = bob.Packets;
        var details = packets.Where(p => IsUserInfo(p, 1, 1)).ToArray();
        var recoveryValues = details.Select(Recovery).ToArray();
        var roster = Array.FindIndex(packets, p => p.ProtocolId == GameProtocolEngine.AreaUsersNotification);
        check((recoveryValues.SequenceEqual(new byte[] { 38 })
                    || recoveryValues.SequenceEqual(new byte[] { 37, 38 }))
                && details.Distinct(ReferenceEqualityComparer.Instance).Count() == details.Length
                && Array.FindIndex(packets, p => IsUserInfo(p, 1, 1)) > roster,
            "an in-flight introduction and recovery tick initialize only after town roster, preserve the newest HP and never replay the same detail twice");
    }

    private static TownSessionDirectory CreateDirectory(ServerOptions? options = null) =>
        new(options ?? new ServerOptions(), TimeProvider.System, NullLogger<TownSessionDirectory>.Instance);

    private static TownEntryRequest Entry(ushort uid, Channel channel, byte areaId = 0,
        Guid? characterId = null, Func<bool>? isInTown = null, GamePeerInfo? peer = null) => new(
        1, areaId, uid, characterId ?? Guid.NewGuid(), uid, Encoding.ASCII.GetBytes($"User{uid}"),
        $"User{uid}", peer, 474, 234, 5, channel.SendAsync, isInTown);

    private static GamePeerInfo Peer(ushort uid) =>
        new(uid, IPAddress.Loopback, IPAddress.Loopback, (ushort)(40_000 + uid), 1, 2, 1400);

    private static GameServerPacket Basic(ushort uid) => GameProtocolEngine.CreateCurrentCharacterInfo(
        new GameCharacterSummary(0, Encoding.ASCII.GetBytes($"User{uid}"), 3, 1, 45), uid,
        equippedItems: [], equippedCreature: null, hasBlackDiamond: false);

    private static GameServerPacket Detail(ushort uid, byte recovery) => PartyMemberDetailProjection.CreatePacket(
        uid, new CharacterRecord(Guid.NewGuid(), $"User{uid}", 3, 1, 45, 10, 0, WeaknessRecovery: recovery),
        new GameCharacterCombatStats(14_400, 19_600, 110, 220, 330, 440,
                1, 2, 3, 4, 410_000, 5, 6, 8_500, 8_600, 7_000, 6_000, 4_300, 500_000), [], false,
        learnedSkills: [new(20, 10)]);

    private static bool IsUserInfo(GameServerPacket packet, byte mode, ushort uid) =>
        packet.ProtocolId == GameProtocolEngine.UserInfoNotification && packet.Payload.Length >= 5
        && packet.Payload[0] == mode && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3)) == uid;

    private static byte Recovery(GameServerPacket packet) => packet.Payload[13 + 80];

    private sealed class Channel
    {
        private readonly ConcurrentQueue<GameServerPacket> _packets = new();
        public Func<GameServerPacket, ValueTask>? BeforeSendAsync { get; set; }
        public GameServerPacket[] Packets => _packets.ToArray();
        public void Clear() => _packets.Clear();
        public async ValueTask SendAsync(GameServerPacket packet, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BeforeSendAsync is { } beforeSend) await beforeSend(packet);
            _packets.Enqueue(packet);
        }
    }
}
