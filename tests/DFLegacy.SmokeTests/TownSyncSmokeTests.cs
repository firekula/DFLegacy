using System.Buffers.Binary;
using System.Net;
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
        CheckGhostAppearance(check);
        await CheckGhostBroadcastAsync(check);
        await CheckDirectoryAsync(check);
        await CheckAreaPeerTableAsync(check);
        await CheckPositionRateLimitAsync(check);
        await CheckDirectoryMigrationAsync(check);
        await CheckDirectoryConcurrencyAsync(check);
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
        check(defaults.TownSync.SendAreaPeerTable,
            "the area UDP peer table is sent on membership changes by default");
        check(!defaults.Debug.TownGhost.Enabled, "the debug town ghost is off by default");

        var townUser = new GameTownUser(7, 474, 234, 5);
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser).Payload[9] == 1,
            "USER_AREA marks a joining actor as active");
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser, active: false).Payload[9] == 0,
            "USER_AREA marks a departing actor as inactive");

        var channelSized = new AreaUserIdPool(new ServerOptions().Channel.MaximumUsers);
        check(channelSized.Capacity == 100,
            "the pool capacity follows the channel user limit so ids stay inside the client's range");
    }

    private static void CheckGhostAppearance(Action<bool, string> check)
    {
        var nameBytes = TownGhostService.EncodeGhostName("TEST", out var valid);
        check(valid && nameBytes.Length == 4,
            "a ghost name within 1..30 encoded bytes is accepted");
        check(!TownGhostService.EncodeGhostName(new string('x', 40), out var tooLong).Any(),
            "an over-long ghost name is rejected instead of building a broken appearance");
        var appearance = TownGhostService.CreateGhostAppearance(7, nameBytes);
        check(appearance.ProtocolId == GameProtocolEngine.UserInfoNotification
            && appearance.Payload[0] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(appearance.Payload.AsSpan(3, 2)) == 7,
            "the ghost appearance is a mode-zero USERINFO record carrying the ghost uid");
    }

    /// <summary>
    /// 缺陷回归：幽灵曾经只进入、走动、离开，从不广播外观，同区玩家只有坐标没有长相与名字。
    /// 这里启动真实的后台服务，让已在区域的观察者收到幽灵的外观包，锁住这条广播。
    /// </summary>
    private static async Task CheckGhostBroadcastAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        options.Debug.TownGhost.Enabled = true;
        options.Debug.TownGhost.Name = "TEST";
        options.Debug.TownGhost.TownId = 1;
        options.Debug.TownGhost.AreaId = 1;
        options.Debug.TownGhost.StepIntervalMs = 100;
        // 一直可见：本测试只等第一条外观，不需要出现/消失循环。
        options.Debug.TownGhost.VisibleSeconds = 0;
        options.Debug.TownGhost.HiddenSeconds = 0;

        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);
        var witness = new FakeChannel();
        await directory.EnterAsync(
            CreateEntry(1, 1, 1, "Witness", witness),
            CancellationToken.None);

        var pool = new AreaUserIdPool(options.Channel.MaximumUsers);
        var service = new TownGhostService(
            options,
            pool,
            directory,
            NullLogger<TownGhostService>.Instance);
        var expectedGhostId = (ushort)options.Channel.MaximumUsers;

        using var stopping = new CancellationTokenSource();
        await service.StartAsync(stopping.Token);
        try
        {
            var appearance = await WaitForAppearanceAsync(witness, expectedGhostId);
            check(appearance is not null,
                "a running ghost publishes its appearance to the area it joins");
            check(appearance is not null
                && appearance.ProtocolId == GameProtocolEngine.UserInfoNotification
                && appearance.Payload[0] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(appearance.Payload.AsSpan(3, 2))
                    == expectedGhostId,
                "the published appearance is a mode-zero USERINFO record carrying the ghost uid");
            check(appearance is not null
                && BinaryPrimitives.ReadUInt32LittleEndian(appearance.Payload.AsSpan(5, 4)) == 4
                && Encoding.ASCII.GetString(appearance.Payload, 9, 4) == "TEST",
                "the published appearance carries the configured ghost name");

            // 幽灵没有真实 NAT 上报：合成对端是它出现在同区地图上的前提。
            var ghostTables = witness.Of(GameProtocolEngine.UdpPeerInfoNotification);
            (ushort UserId, byte[] Local, byte[] Public, ushort PublicPort, uint AccountId, byte NatType, uint Mtu)[] ghostPeers =
                ghostTables.Count > 0 ? ReadPeerTable(ghostTables[^1]) : [];
            check(ghostPeers.Length == 1
                && ghostPeers[0].UserId == expectedGhostId
                && ghostPeers[0].Local.SequenceEqual(new byte[] { 127, 0, 0, 1 })
                && ghostPeers[0].Public.SequenceEqual(new byte[] { 127, 0, 0, 1 })
                && ghostPeers[0].PublicPort == 0
                && ghostPeers[0].AccountId == 0
                && ghostPeers[0].NatType == 2
                && ghostPeers[0].Mtu == 1472,
                "the ghost publishes a synthetic loopback peer so the client can render it");
        }
        finally
        {
            stopping.Cancel();
            await service.StopAsync(CancellationToken.None);
        }

        check(pool.InUseCount == 0,
            "stopping the ghost releases the area user id it allocated");
    }

    private static async Task<GameServerPacket?> WaitForAppearanceAsync(
        FakeChannel channel,
        ushort expectedUserId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var match = channel.Of(GameProtocolEngine.UserInfoNotification)
                .FirstOrDefault(packet =>
                    packet.Payload.Length > 4
                    && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3, 2))
                        == expectedUserId);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        return null;
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
        // 先释放低段（1）与高段（3）各留一个空位：正向扫描会拿到 1，只有从尾部扫描
        // 才会拿到容量值 3，这句断言才真正钉住「从尾部找」而不是碰巧相等。
        check(pool.Release(1) && pool.Release(3)
            && pool.TryAllocateReverse(out var ghost) && ghost == 3,
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

    /// <summary>
    /// 缺陷回归：客户端只为 UDP 对端表（NOTI 11）里的 uid 建立同镇角色，只有名单/外观/位置
    /// 时第二个角色不会出现在地图上。这里锁住「成员变化 → 刷新对端表」的每条路径：
    /// 进入、离开、通道坏掉的强制移除，以及开关关闭时一个包都不发。
    /// </summary>
    private static async Task CheckAreaPeerTableAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        var alicePeer = CreatePeer(1, "10.0.0.1", "203.0.113.1", 40001, accountId: 7, natType: 1, mtu: 1400);
        var bobPeer = CreatePeer(2, "10.0.0.2", "203.0.113.2", 40002, accountId: 8, natType: 2, mtu: 1472);

        // Alice 独自进区：表里只有她自己（这是本任务指定的新增包）。
        var alice = new FakeChannel();
        var aliceTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 1, "Alice", alice, alicePeer),
            CancellationToken.None);
        check(aliceTicket is not null, "the peer-table member enters the area");
        var aliceTables = alice.Of(GameProtocolEngine.UdpPeerInfoNotification);
        check(aliceTables.Count == 1,
            "a solo member with peer info still receives exactly one peer table");
        var soloTable = ReadPeerTable(aliceTables[0]);
        check(soloTable.Length == 1 && soloTable[0].UserId == 1,
            "the solo table lists exactly the local member itself");
        // 每条目 21 字节（uid u16 + local 4 + public 4 + port u16 + account u32 +
        // natType u8 + mtu u32），前面 1 字节是条数：长度必须严丝合缝。
        check(aliceTables[0].Payload.Length == 1 + 21,
            "the solo table has exactly the count byte plus one 21-byte entry");
        check(soloTable[0].Local.SequenceEqual(new byte[] { 10, 0, 0, 1 })
            && soloTable[0].Public.SequenceEqual(new byte[] { 203, 0, 113, 1 })
            && soloTable[0].PublicPort == 40001
            && soloTable[0].AccountId == 7
            && soloTable[0].NatType == 1
            && soloTable[0].Mtu == 1400,
            "the entry carries uid, local/public address, port, account, nat type and mtu in the writer's layout");

        // Bob 进入：新人拿到「自己第一、老人随后」的表，老人拿到含新人的刷新表。
        var bob = new FakeChannel();
        var bobTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 2, "Bob", bob, bobPeer),
            CancellationToken.None);
        check(bobTicket is not null, "the second peer-table member enters the area");
        var bobTables = bob.Of(GameProtocolEngine.UdpPeerInfoNotification);
        check(bobTables.Count == 1, "the newcomer receives exactly one peer table");
        var bobTable = ReadPeerTable(bobTables[0]);
        check(bobTable.Length == 2 && bobTable[0].UserId == 2 && bobTable[1].UserId == 1,
            "the newcomer's table lists itself first, then the existing member");
        check(bobTables[0].Payload.Length == 1 + (2 * 21)
            && bobTable[0].PublicPort == 40002
            && bobTable[0].AccountId == 8
            && bobTable[0].NatType == 2
            && bobTable[0].Mtu == 1472
            && bobTable[1].Local.SequenceEqual(new byte[] { 10, 0, 0, 1 })
            && bobTable[1].Public.SequenceEqual(new byte[] { 203, 0, 113, 1 })
            && bobTable[1].PublicPort == 40001
            && bobTable[1].AccountId == 7
            && bobTable[1].NatType == 1
            && bobTable[1].Mtu == 1400,
            "both entries decode field-for-field in the CreateUdpPeerInfo writer layout");
        aliceTables = alice.Of(GameProtocolEngine.UdpPeerInfoNotification);
        check(aliceTables.Count == 2, "the existing member receives one refreshed table");
        check(ReadPeerTable(aliceTables[1]).Length == 2
            && ReadPeerTable(aliceTables[1])[0].UserId == 1
            && ReadPeerTable(aliceTables[1])[1].UserId == 2,
            "the refreshed table lists the existing member first and now includes the newcomer");

        // Carol 没有对端信息（例如没上报 NAT）：必须被跳过，而不是带着空端点进表。
        var carol = new FakeChannel();
        var carolTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 3, "Carol", carol),
            CancellationToken.None);
        var carolTables = carol.Of(GameProtocolEngine.UdpPeerInfoNotification);
        check(carolTables.Count == 1, "a peer-less newcomer still gets the table of endpoints around it");
        var carolTable = ReadPeerTable(carolTables[0]);
        check(carolTable.Length == 2
            && carolTable.All(peer => peer.UserId != 3)
            && carolTable.Any(peer => peer.UserId == 1)
            && carolTable.Any(peer => peer.UserId == 2),
            "the peer-less member is skipped instead of being published with empty endpoints");
        check(carolTables[0].Payload.Length == 1 + (2 * 21),
            "skipping the peer-less member shrinks the table to exactly two entries");

        // 离开：剩下的人拿到去掉离开者的刷新表，离开者自己不再收到任何表。
        await directory.LeaveAsync(bobTicket, CancellationToken.None);
        aliceTables = alice.Of(GameProtocolEngine.UdpPeerInfoNotification);
        var aliceAfterDeparture = ReadPeerTable(aliceTables[^1]);
        // Alice 的表依次来自：自己进入、Bob 进入、Carol 进入、Bob 离开。
        check(aliceTables.Count == 4
            && aliceAfterDeparture.Length == 1
            && aliceAfterDeparture[0].UserId == 1,
            "a departure refreshes the remaining member's table without the departed member");
        var carolAfterDeparture = ReadPeerTable(
            carol.Of(GameProtocolEngine.UdpPeerInfoNotification)[^1]);
        check(carolAfterDeparture.Length == 1 && carolAfterDeparture[0].UserId == 1,
            "every remaining member sees the departure in its refreshed table");
        check(bob.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 2,
            "the departed member receives no peer table after leaving");

        // 强制移除路径：Carol 的通道在下一次投递时抛异常，她会被目录摘除；
        // 剩下的人的表里必须也删掉她（嵌套刷新用的是摘除后的现算名单）。
        var davePeer = CreatePeer(4, "10.0.0.4", "203.0.113.4", 40004, accountId: 9, natType: 1, mtu: 1400);
        var dave = new FakeChannel();
        carol.FailNextSend = true;
        await directory.EnterAsync(
            CreateEntry(1, 1, 4, "Dave", dave, davePeer),
            CancellationToken.None);
        check(directory.GetMemberCount(1, 1) == 2,
            "a peer-table send to a broken channel removes that member");
        var aliceAfterForcedRemoval = ReadPeerTable(
            alice.Of(GameProtocolEngine.UdpPeerInfoNotification)[^1]);
        check(aliceAfterForcedRemoval.Length == 2
            && aliceAfterForcedRemoval[0].UserId == 1
            && aliceAfterForcedRemoval[1].UserId == 4
            && aliceAfterForcedRemoval.All(peer => peer.UserId != 3),
            "the forced removal also refreshes the survivors' table without the broken member");

        // 开关关闭：一个对端表包都不发，名单/走进度保持原样。
        var disabledOptions = new ServerOptions();
        disabledOptions.TownSync.SendAreaPeerTable = false;
        var disabledDirectory = new TownSessionDirectory(
            disabledOptions,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);
        var first = new FakeChannel();
        var firstTicket = await disabledDirectory.EnterAsync(
            CreateEntry(2, 1, 1, "First", first, alicePeer),
            CancellationToken.None);
        var second = new FakeChannel();
        await disabledDirectory.EnterAsync(
            CreateEntry(2, 1, 2, "Second", second, bobPeer),
            CancellationToken.None);
        await disabledDirectory.LeaveAsync(firstTicket, CancellationToken.None);
        check(first.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 0
            && second.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 0,
            "disabling SendAreaPeerTable sends no peer table on entry or departure");
        check(first.Of(GameProtocolEngine.AreaUsersNotification).Count == 1
            && second.Of(GameProtocolEngine.AreaUsersNotification).Count == 1,
            "disabling SendAreaPeerTable keeps the per-member AREA_USERS roster untouched");
        var firstAreas = first.Of(GameProtocolEngine.UserAreaNotification);
        check(firstAreas.Count == 1
            && firstAreas[0].Payload[0] == 2
            && firstAreas[0].Payload[9] == 1,
            "disabling the peer table keeps the existing member's join signal");
        var secondAreas = second.Of(GameProtocolEngine.UserAreaNotification);
        check(secondAreas.Count == 1
            && secondAreas[0].Payload[0] == 1
            && secondAreas[0].Payload[9] == 0,
            "disabling the peer table keeps the USER_AREA(active=0) departure signal");
    }

    /// <summary>
    /// 位置限流（Task 8）的行为此前没有测试覆盖：默认每秒 10 条，&lt;=0 表示不限制。
    /// 用可控时钟把「同一秒内超限被丢」与「跨秒后预算重置」钉死。
    /// </summary>
    private static async Task CheckPositionRateLimitAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        options.TownSync.MaxPositionForwardsPerSecond = 2;
        var timeProvider = new FakeTimeProvider();
        var directory = new TownSessionDirectory(
            options,
            timeProvider,
            NullLogger<TownSessionDirectory>.Instance);

        var witness = new FakeChannel();
        await directory.EnterAsync(
            CreateEntry(6, 1, 1, "Witness", witness),
            CancellationToken.None);
        var mover = new FakeChannel();
        var moverTicket = await directory.EnterAsync(
            CreateEntry(6, 1, 2, "Mover", mover),
            CancellationToken.None);

        for (var step = 0; step < 3; step++)
        {
            await directory.UpdatePositionAsync(
                moverTicket,
                (short)(500 + step),
                300,
                7,
                120,
                CancellationToken.None);
        }

        check(witness.Of(GameProtocolEngine.UserPositionNotification).Count == 2
            && mover.Of(GameProtocolEngine.UserPositionNotification).Count == 0,
            "a per-second position budget forwards only two of three updates and still never echoes to the mover");

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await directory.UpdatePositionAsync(
            moverTicket,
            600,
            300,
            7,
            120,
            CancellationToken.None);
        check(witness.Of(GameProtocolEngine.UserPositionNotification).Count == 3,
            "the position budget resets after one second");

        options.TownSync.MaxPositionForwardsPerSecond = 0;
        for (var step = 0; step < 3; step++)
        {
            await directory.UpdatePositionAsync(
                moverTicket,
                (short)(700 + step),
                300,
                7,
                120,
                CancellationToken.None);
        }

        check(witness.Of(GameProtocolEngine.UserPositionNotification).Count == 6,
            "a zero position budget forwards every update without a limit");
    }

    private static async Task CheckDirectoryMigrationAsync(Action<bool, string> check)
    {
        var directory = new TownSessionDirectory(
            new ServerOptions(),
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        // 旧区里先放一个旁观者：迁移时它应该收到离开信号。
        var witness = new FakeChannel();
        var witnessTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 9, "Witness", witness),
            CancellationToken.None);

        var moverId = Guid.NewGuid();
        var oldChannel = new FakeChannel();
        var oldTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 1, "Mover", oldChannel, moverId),
            CancellationToken.None);
        check(oldTicket is not null
            && oldTicket.CharacterId == moverId
            && oldTicket.AreaUserId == 1
            && oldTicket.AreaId == 1
            && directory.GetMemberCount(1, 1) == 2,
            "the migrating member first joins the old area");

        var newChannel = new FakeChannel();
        var newTicket = await directory.EnterAsync(
            CreateEntry(1, 2, 5, "Mover", newChannel, moverId),
            CancellationToken.None);
        check(newTicket is not null
            && newTicket.CharacterId == moverId
            && newTicket.AreaUserId == 5
            && newTicket.AreaId == 2,
            "re-entering with the same character id migrates the member to the new area");
        check(directory.GetMemberCount(1, 1) == 1 && directory.GetMemberCount(1, 2) == 1,
            "a migrated member is counted only in its new area");
        check(directory.TryGetSnapshot(moverId, out var moverSnapshot)
            && moverSnapshot.AreaId == 2
            && moverSnapshot.AreaUserId == 5
            && moverSnapshot.DisplayName == "Mover"
            && moverSnapshot.X == 474
            && moverSnapshot.Y == 234,
            "the migrated member is reported with the new area, uid, name and position");
        check(newTicket!.AreaUserId == 5 && oldTicket!.AreaUserId == 1,
            "the old and new tickets keep their own area user ids");

        var witnessAreas = witness.Of(GameProtocolEngine.UserAreaNotification);
        check(witnessAreas.Count == 2
            && witnessAreas[1].Payload[0] == 1
            && witnessAreas[1].Payload[9] == 0,
            "the old area is told the migrated member left with USER_AREA(active=0)");
        var newRoster = ReadRoster(newChannel.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(newRoster.Length == 1 && newRoster[0].UserId == 5,
            "the new area roster contains only the migrated member");

        // 旧 ticket 已失效：不得发包、不得改状态。
        await directory.UpdatePositionAsync(oldTicket, 111, 222, 3, 50, CancellationToken.None);
        check(witness.Of(GameProtocolEngine.UserPositionNotification).Count == 0
            && newChannel.Of(GameProtocolEngine.UserPositionNotification).Count == 0,
            "a stale ticket cannot forward a position packet to any area");
        check(directory.TryGetSnapshot(moverId, out var afterStaleMove)
            && afterStaleMove.X == 474
            && afterStaleMove.Y == 234
            && afterStaleMove.Direction == 5,
            "a stale ticket cannot change the registered member state");

        await directory.RefreshAppearanceAsync(
            oldTicket,
            CreateAppearance(1, "Mover"),
            CancellationToken.None);
        check(witness.Of(GameProtocolEngine.UserInfoNotification).Count == 0
            && newChannel.Of(GameProtocolEngine.UserInfoNotification).Count == 0,
            "a stale ticket cannot broadcast an appearance to any area");

        var staleChat = GameProtocolEngine.CreateMessageNotification(
            GameMessageType.Type03White,
            1,
            Encoding.ASCII.GetBytes("ghost"));
        check(await directory.BroadcastToAreaAsync(
                oldTicket,
                staleChat,
                includeSelf: true,
                CancellationToken.None) == 0
            && witness.Of(GameProtocolEngine.MessageNotification).Count == 0,
            "a stale ticket cannot broadcast to the old area");

        // 旧区清空后会被回收，同一 (城镇, 区域) 可以重新进入且名单只含新成员。
        await directory.LeaveAsync(witnessTicket, CancellationToken.None);
        check(directory.GetMemberCount(1, 1) == 0,
            "the old area is released once its last member leaves");
        check(directory.GetMemberCount(1, 2) == 1,
            "releasing the old area leaves the migrated member in the new area");

        var reuser = new FakeChannel();
        var reuserTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 7, "Reuser", reuser),
            CancellationToken.None);
        check(reuserTicket is not null
            && reuserTicket.AreaId == 1
            && directory.GetMemberCount(1, 1) == 1,
            "a released area can be entered again");
        var reusedRoster = ReadRoster(reuser.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(reusedRoster.Length == 1 && reusedRoster[0].UserId == 7,
            "the rebuilt roster contains only the newcomer of the reused area");
        check(reuser.Of(GameProtocolEngine.UserAreaNotification).Count == 0
            && reuser.Of(GameProtocolEngine.UserInfoNotification).Count == 0,
            "the rebuilt area starts with no foreign packets");
    }

    private static async Task CheckDirectoryConcurrencyAsync(Action<bool, string> check)
    {
        var directory = new TownSessionDirectory(
            new ServerOptions(),
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        // 同一角色在两个区域之间反复并发迁移：结构锁必须保证它任何时刻只登记在一个区域，
        // 且登记它的区域一定真的看得见它（空区回收不能留下孤儿成员）。
        var characterId = Guid.NewGuid();
        var channels = new[] { new FakeChannel(), new FakeChannel() };
        var enters = new List<Task>();
        for (var round = 0; round < 16; round++)
        {
            var index = round % 2;
            var areaId = (byte)(index + 1);
            var areaUserId = (ushort)(index + 1);
            enters.Add(Task.Run(() => directory.EnterAsync(
                CreateEntry(4, areaId, areaUserId, "Racer", channels[index], characterId),
                CancellationToken.None)));
        }

        await Task.WhenAll(enters);

        check(directory.TryGetSnapshot(characterId, out var racer)
            && directory.GetMemberCount(racer.TownId, racer.AreaId) == 1,
            "a concurrently entered character stays reachable through its registered area");
        check(directory.GetMemberCount(4, 1) + directory.GetMemberCount(4, 2) == 1,
            "a character racing between two areas is only ever counted in one of them");
        foreach (var areaId in new byte[] { 1, 2 })
        {
            foreach (var member in directory.Snapshot(4, areaId))
            {
                check(directory.TryGetSnapshot(member.CharacterId, out var live)
                    && live.AreaUserId == member.AreaUserId
                    && live.AreaId == areaId,
                    "every roster member is reachable by character id");
            }
        }
    }

    private static TownEntryRequest CreateEntry(
        byte townId,
        byte areaId,
        ushort areaUserId,
        string name,
        FakeChannel channel,
        GamePeerInfo? peer = null) =>
        CreateEntry(townId, areaId, areaUserId, name, channel, Guid.NewGuid(), peer);

    private static TownEntryRequest CreateEntry(
        byte townId,
        byte areaId,
        ushort areaUserId,
        string name,
        FakeChannel channel,
        Guid characterId,
        GamePeerInfo? peer = null)
    {
        return new TownEntryRequest(
            townId,
            areaId,
            areaUserId,
            characterId,
            areaUserId,
            Encoding.ASCII.GetBytes(name),
            name,
            peer,
            474,
            234,
            5,
            channel.SendAsync);
    }

    private static GamePeerInfo CreatePeer(
        ushort userId,
        string localAddress,
        string publicAddress,
        ushort publicPort,
        uint accountId,
        byte natType,
        uint mtu) => new(
        userId,
        IPAddress.Parse(localAddress),
        IPAddress.Parse(publicAddress),
        publicPort,
        accountId,
        natType,
        mtu);

    /// <summary>
    /// 按 CreateUdpPeerInfo 的写入顺序解码对端表（条数 u8，然后每条：uid u16、local 4B、
    /// public 4B、publicPort u16、accountId u32、natType u8、mtu u32），用于逐字段钉住布局。
    /// </summary>
    private static (ushort UserId, byte[] Local, byte[] Public, ushort PublicPort, uint AccountId, byte NatType, uint Mtu)[]
        ReadPeerTable(GameServerPacket packet)
    {
        var payload = packet.Payload;
        var count = payload[0];
        var peers = new (ushort, byte[], byte[], ushort, uint, byte, uint)[count];
        var offset = 1;
        for (var index = 0; index < count; index++)
        {
            peers[index] = (
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset, 2)),
                payload.AsSpan(offset + 2, 4).ToArray(),
                payload.AsSpan(offset + 6, 4).ToArray(),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 10, 2)),
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 12, 4)),
                payload[offset + 16],
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 17, 4)));
            offset += 21;
        }

        return peers;
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

    /// <summary>只覆盖 GetUtcNow 的可控时钟：目录只用它来切分每秒位置预算的窗口。</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
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
