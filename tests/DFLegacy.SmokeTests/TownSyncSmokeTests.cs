using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TownSyncSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        CheckAreaUserIdPool(check);
        CheckOptionsAndPackets(check);
        await CheckDirectoryAsync(check);
    }

    private static void CheckOptionsAndPackets(Action<bool, string> check)
    {
        var defaults = new ServerOptions();
        check(defaults.TownSync.Enabled, "town sync is enabled by default");
        check(!defaults.TownSync.ForeignAppearanceBeforeRoster,
            "the roster is sent before foreign appearances by default");
        check(defaults.TownSync.DepartureSignal == TownDepartureSignal.UserAreaInactive,
            "the default departure signal is USER_AREA with active zero");
        check(defaults.TownSync.MaxPositionForwardsPerSecond == 10,
            "position forwarding is capped per member and second");
        check(!defaults.Debug.TownGhost.Enabled, "the debug town ghost is off by default");

        var townUser = new GameTownUser(7, 474, 234, 5);
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser).Payload[9] == 1,
            "USER_AREA marks a joining actor as active");
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser, active: false).Payload[9] == 0,
            "USER_AREA marks a departing actor as inactive");
    }

    private static void CheckAreaUserIdPool(Action<bool, string> check)
    {
        var pool = new AreaUserIdPool(3);
        check(pool.Capacity == 3, "area user id pool capacity matches the channel limit");
        check(pool.TryAllocate(out var first) && first == 1,
            "the first area user id is still 1 so solo play stays byte-identical");
        check(pool.TryAllocate(out var second) && second == 2,
            "the second area user id is 2");
        check(pool.TryAllocate(out var third) && third == 3,
            "the third area user id is 3");
        check(!pool.TryAllocate(out _),
            "an exhausted pool refuses to hand out a duplicate id");
        check(pool.InUseCount == 3, "the pool reports three ids in use");
        check(pool.Release(2) && pool.TryAllocate(out var reused) && reused == 2,
            "a released area user id can be reused");
        check(!pool.Release(0) && !pool.Release(9),
            "releasing an out-of-range id is rejected");
        check(pool.Release(3) && pool.TryAllocateReverse(out var ghost) && ghost == 3,
            "the debug ghost allocates from the back of the pool");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new AreaUserIdPool(0),
            check,
            "a pool without capacity is rejected");
    }

    private static async Task CheckDirectoryAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        var alice = new FakeChannel();
        var aliceTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 1, "Alice", alice),
            CancellationToken.None);
        check(aliceTicket is not null, "the first member enters the town area");
        check(directory.GetMemberCount(1, 1) == 1, "the area reports one member");
        check(alice.Of(GameProtocolEngine.AreaUsersNotification).Count == 1,
            "the newcomer receives exactly one AREA_USERS roster");
        var soloRoster = ReadRoster(alice.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(soloRoster.Length == 1 && soloRoster[0].UserId == 1,
            "a solo roster contains only the newcomer itself, exactly like before");
        check(alice.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "a solo newcomer receives no foreign USER_AREA packet");

        var bob = new FakeChannel();
        var bobTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 2, "Bob", bob),
            CancellationToken.None);
        check(bobTicket is not null && directory.GetMemberCount(1, 1) == 2,
            "the second member joins the same area");
        var sharedRoster = ReadRoster(bob.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(sharedRoster.Length == 2 && sharedRoster[0].UserId == 2 && sharedRoster[1].UserId == 1,
            "the newcomer roster carries itself first and the existing member second");
        // Alice 只在 Bob 加入时收到一条 USER_AREA（她自己进入时不会收到自己的）。
        var joinPacket = alice.Of(GameProtocolEngine.UserAreaNotification);
        check(joinPacket.Count == 1
            && joinPacket[0].Payload[0] == 2
            && joinPacket[0].Payload[9] == 1,
            "existing members receive USER_AREA(active=1) for the newcomer");

        var appearance = CreateAppearance(2, "Bob");
        await directory.RefreshAppearanceAsync(bobTicket, appearance, CancellationToken.None);
        check(alice.Of(GameProtocolEngine.UserInfoNotification).Count == 1,
            "an appearance refresh reaches the other member");
        check(bob.Of(GameProtocolEngine.UserInfoNotification).Count == 0,
            "an appearance refresh never echoes back to its owner");

        await directory.UpdatePositionAsync(bobTicket, 500, 300, 7, 120, CancellationToken.None);
        var moved = alice.Of(GameProtocolEngine.UserPositionNotification);
        check(moved.Count == 1
            && moved[0].Payload[0] == 2
            && moved[0].Payload[6] == 7,
            "position updates go to the other member with the right uid and direction");
        check(bob.Of(GameProtocolEngine.UserPositionNotification).Count == 0,
            "position updates never echo back to the mover");

        var chat = GameProtocolEngine.CreateMessageNotification(
            GameMessageType.Type03White,
            2,
            Encoding.ASCII.GetBytes("hello"));
        check(await directory.BroadcastToAreaAsync(bobTicket, chat, includeSelf: true, CancellationToken.None) == 2,
            "a broadcast with includeSelf reaches both members");
        check(alice.Of(GameProtocolEngine.MessageNotification).Count == 1
            && alice.Of(GameProtocolEngine.MessageNotification)[0].Payload[1] == 2,
            "the receiver sees the sender area user id");
        check(await directory.BroadcastToAreaAsync(bobTicket, chat, includeSelf: false, CancellationToken.None) == 1,
            "a broadcast without includeSelf reaches only the other member");

        var carol = new FakeChannel();
        await directory.EnterAsync(CreateEntry(1, 2, 3, "Carol", carol), CancellationToken.None);
        var aliceChatCount = alice.Of(GameProtocolEngine.MessageNotification).Count;
        await directory.BroadcastToAreaAsync(bobTicket, chat, includeSelf: true, CancellationToken.None);
        check(carol.Of(GameProtocolEngine.MessageNotification).Count == 0
            && alice.Of(GameProtocolEngine.MessageNotification).Count == aliceChatCount + 1,
            "a different area never receives another area's broadcast");

        await directory.LeaveAsync(bobTicket, CancellationToken.None);
        check(directory.GetMemberCount(1, 1) == 1, "a departing member leaves the area");
        // Alice 的 USER_AREA 列表此时是 [Bob 加入, Bob 离开]，离开信号是第二条。
        var departure = alice.Of(GameProtocolEngine.UserAreaNotification);
        check(departure.Count == 2
            && departure[1].Payload[0] == 2
            && departure[1].Payload[9] == 0,
            "leaving is signalled with USER_AREA(active=0) by default");
        check(!directory.TryGetSnapshot(bobTicket!.CharacterId, out _),
            "a departed member is no longer reported by character id");

        await directory.LeaveAsync(aliceTicket, CancellationToken.None);
        check(directory.GetMemberCount(1, 1) == 0, "an empty area is released");

        var resendOptions = new ServerOptions();
        resendOptions.TownSync.DepartureSignal = TownDepartureSignal.RosterResend;
        var resendDirectory = new TownSessionDirectory(
            resendOptions,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);
        var dave = new FakeChannel();
        var daveTicket = await resendDirectory.EnterAsync(
            CreateEntry(3, 1, 1, "Dave", dave),
            CancellationToken.None);
        var erin = new FakeChannel();
        var erinTicket = await resendDirectory.EnterAsync(
            CreateEntry(3, 1, 2, "Erin", erin),
            CancellationToken.None);
        await resendDirectory.LeaveAsync(erinTicket, CancellationToken.None);
        var resent = dave.Of(GameProtocolEngine.AreaUsersNotification);
        check(resent.Count == 2 && ReadRoster(resent[1]).Length == 1,
            "the roster-resend signal sends a roster without the departed member");

        // Frank 的通道坏掉：Dave 刷新外观时投递给 Frank 会抛异常，Frank 被移除。
        var failing = new FakeChannel();
        await resendDirectory.EnterAsync(
            CreateEntry(3, 1, 3, "Frank", failing),
            CancellationToken.None);
        check(resendDirectory.GetMemberCount(3, 1) == 2, "the broken-channel member joins first");
        failing.FailNextSend = true;
        await resendDirectory.RefreshAppearanceAsync(
            daveTicket,
            CreateAppearance(1, "Dave"),
            CancellationToken.None);
        check(resendDirectory.GetMemberCount(3, 1) == 1,
            "a member whose channel throws is removed from the area");
        check(resendDirectory.TryGetSnapshot(daveTicket!.CharacterId, out _),
            "removing a broken member keeps the healthy ones");

        var disabledOptions = new ServerOptions();
        disabledOptions.TownSync.Enabled = false;
        var disabledDirectory = new TownSessionDirectory(
            disabledOptions,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);
        var ignored = new FakeChannel();
        check(await disabledDirectory.EnterAsync(
                CreateEntry(1, 1, 1, "Zoe", ignored),
                CancellationToken.None) is null
            && ignored.Packets.Count == 0,
            "a disabled town directory does nothing at all");
    }

    private static TownEntryRequest CreateEntry(
        byte townId,
        byte areaId,
        ushort areaUserId,
        string name,
        FakeChannel channel)
    {
        return new TownEntryRequest(
            townId,
            areaId,
            areaUserId,
            Guid.NewGuid(),
            areaUserId,
            Encoding.ASCII.GetBytes(name),
            name,
            474,
            234,
            5,
            channel.SendAsync);
    }

    private static GameServerPacket CreateAppearance(ushort userId, string name) =>
        GameProtocolEngine.CreateCurrentCharacterInfo(
            new GameCharacterSummary(0, Encoding.ASCII.GetBytes(name), 0, 0, 1),
            userId,
            equippedItems: [],
            equippedCreature: null,
            hasBlackDiamond: false);

    private static (ushort UserId, short X, short Y, byte Direction, byte Active)[] ReadRoster(
        GameServerPacket packet)
    {
        var payload = packet.Payload;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2));
        var users = new (ushort, short, short, byte, byte)[count];
        var offset = 4;
        for (var index = 0; index < count; index++)
        {
            users[index] = (
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset + 2, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset + 4, 2)),
                payload[offset + 6],
                payload[offset + 7]);
            offset += 8;
        }

        return users;
    }

    private sealed class FakeChannel
    {
        private readonly List<GameServerPacket> _packets = [];

        public bool FailNextSend { get; set; }

        public IReadOnlyList<GameServerPacket> Packets
        {
            get
            {
                lock (_packets)
                {
                    return [.. _packets];
                }
            }
        }

        public IReadOnlyList<GameServerPacket> Of(byte protocolId) =>
            [.. Packets.Where(packet => packet.ProtocolId == protocolId)];

        public ValueTask SendAsync(GameServerPacket packet, CancellationToken cancellationToken)
        {
            if (FailNextSend)
            {
                FailNextSend = false;
                throw new InvalidOperationException("channel is gone");
            }

            lock (_packets)
            {
                _packets.Add(packet);
            }

            return ValueTask.CompletedTask;
        }
    }

    private static void CheckThrows<TException>(
        Action action,
        Action<bool, string> check,
        string name)
        where TException : Exception
    {
        try
        {
            action();
            check(false, name);
        }
        catch (TException)
        {
            check(true, name);
        }
    }
}
