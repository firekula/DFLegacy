using System.Buffers.Binary;
using System.Net;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TownSyncSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        CheckAreaUserIdPool(check);
        CheckOptionsAndPackets(check);
        await CheckPrivateAreasAsync(check);
        await CheckAllSeriaRoomsAsync(check);
        CheckGhostAppearance(check);
        await CheckGhostBroadcastAsync(check);
        await CheckDirectoryAsync(check);
        await CheckAreaPeerTableAsync(check);
        await CheckPeerTableIntroductionOrderAsync(check);
        await CheckSymmetricIntroductionAsync(check);
        await CheckPositionRateLimitAsync(check);
        await CheckDirectoryMigrationAsync(check);
        await CheckDirectoryConcurrencyAsync(check);
    }

    private static void CheckOptionsAndPackets(Action<bool, string> check)
    {
        var defaults = new ServerOptions();
        check(defaults.TownSync.Enabled, "town sync is enabled by default");
        check(defaults.TownSync.ForeignAppearanceBeforeRoster,
            "foreign appearances precede the roster by default so every roster uid already has a USERINFO record");
        check(defaults.TownSync.DepartureSignal == TownDepartureSignal.UserAreaInactive,
            "the default departure signal is the USER_AREA region-mismatch destroy signal");
        check(defaults.TownSync.MaxPositionForwardsPerSecond == 10,
            "position forwarding is capped per member and second");
        check(defaults.TownSync.PrivateAreas.Length == 0,
            "the additional private-area list is empty; all five Seria rooms are always isolated");
        check(defaults.TownSync.SendAreaPeerTable,
            "the area UDP peer table is sent on membership changes by default");
        check(!defaults.Debug.TownGhost.Enabled
            && defaults.Debug.TownGhost.TownId == 1
            && defaults.Debug.TownGhost.AreaId == 0,
            "the debug town ghost defaults to the shared town square 1/0, never the private Seria room");

        var townUser = new GameTownUser(7, 474, 234, 5);
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser).Payload[9] == 1,
            "USER_AREA marks a joining actor as active");
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser, active: false).Payload[9] == 0,
            "the bare builder's active=0 tail is only the hide flag; it is not the departure signal");
        // 真正的销毁信号：同区 + active=0 只写 creature 的激活字节（隐藏），客户端只有在
        // (town, area) 与自己当前 scene 不一致时才 RemoveCreature；所以离场包必须带哨兵。
        var departure = GameProtocolEngine.CreateUserAreaDeparture(townUser);
        check(departure.ProtocolId == GameProtocolEngine.UserAreaNotification
                && departure.Payload.Length == 10
                && departure.Payload[0] == 7
                && departure.Payload[2] == 0xFF
                && departure.Payload[3] == 0xFF
                && departure.Payload[9] == 0,
            "the departure signal is USER_AREA with 0xFF/0xFF (region mismatch) so the client destroys the actor");

        var channelSized = new AreaUserIdPool(new ServerOptions().Channel.MaximumUsers);
        check(channelSized.Capacity == 100,
            "the pool capacity follows the channel user limit so ids stay inside the client's range");
    }

    /// <summary>
    /// 缺陷回归（双客户端验收）：赛利亚房间（城镇 1/区域 1）在原作里是单人实例，服务端此前
    /// 按 (城镇, 区域) 分桶，两名玩家各自站在房间里也能互相看见。这里钉住私密区域的完整边界：
    /// 名单/外观/位置/聊天都只及于成员自己，进入快照与离开（0xFF/0xFF 销毁信号）照常，
    /// 管理面按区域聚合，且同一份配置下的共享区域行为完全不变。
    /// </summary>
    private static async Task CheckPrivateAreasAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        // Seria's room privacy is declared by server.json, not by the code
        // default (ConfigurationBinder appends arrays to non-empty defaults,
        // which would make an explicit empty list impossible).
        options.TownSync.PrivateAreas = ["1:1"];
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        var alicePeer = CreatePeer(1, "10.0.0.1", "203.0.113.1", 40001, accountId: 7, natType: 1, mtu: 1400);
        var bobPeer = CreatePeer(2, "10.0.0.2", "203.0.113.2", 40002, accountId: 8, natType: 2, mtu: 1472);

        // Alice 先进入并广播外观：若 1/1 是共享区域，后来者必然能拿到这份已存档的外观。
        var alice = new FakeChannel();
        var aliceTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 1, "Alice", alice, alicePeer),
            CancellationToken.None);
        await directory.RefreshAppearanceAsync(
            aliceTicket,
            CreateAppearance(1, "Alice"),
            CancellationToken.None);
        var aliceSoloRoster = ReadRoster(alice.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(aliceSoloRoster.Length == 1 && aliceSoloRoster[0].UserId == 1,
            "a private-area member still gets its unconditional entry roster, containing only itself");

        // 打开「新人补收已有成员 USER_AREA」开关：默认关闭时共享区域的新人也不会收到外来
        // USER_AREA，下面的隔离断言就会空转（删掉实例隔离也照样绿）。开关打开后，共享区域的
        // 新人必收一条（见后面的对照断言），私密区域的新人仍必须一条都没有。
        options.TownSync.SendUserAreaForExistingMembers = true;

        var bob = new FakeChannel();
        var bobTicket = await directory.EnterAsync(
            CreateEntry(1, 1, 2, "Bob", bob, bobPeer),
            CancellationToken.None);
        check(bobTicket is not null
            && directory.GetMemberCount(1, 1) == 2
            && directory.Snapshot(1, 1).Select(member => member.AreaUserId).OrderBy(id => id)
                .SequenceEqual(new ushort[] { 1, 2 }),
            "both members enter the private area and the admin-facing count/snapshot aggregate both instances");
        var bobRoster = ReadRoster(bob.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(bob.Of(GameProtocolEngine.AreaUsersNotification).Count == 1
            && bobRoster.Length == 1
            && bobRoster[0].UserId == 2,
            "a private-area newcomer's roster contains only itself");
        check(bob.Of(GameProtocolEngine.UserInfoNotification).Count == 0,
            "a private area never delivers the other member's stored appearance");
        check(bob.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "a private area never delivers a foreign USER_AREA even with the USER_AREA switch on");

        // Bob 广播自己的外观：私人房间里的另一个人既收不到外观，也收不到加入增量。
        await directory.RefreshAppearanceAsync(
            bobTicket,
            CreateAppearance(2, "Bob"),
            CancellationToken.None);
        check(alice.Of(GameProtocolEngine.UserInfoNotification).Count == 0
            && alice.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "a private-area appearance and its join increment never reach the other member");

        // 位置：私密区域里没有别的收件人（也不回发移动者本人），一个包都不发。
        await directory.UpdatePositionAsync(bobTicket, 500, 300, 7, 120, CancellationToken.None);
        check(alice.Of(GameProtocolEngine.UserPositionNotification).Count == 0
            && bob.Of(GameProtocolEngine.UserPositionNotification).Count == 0,
            "a private-area position update is delivered to nobody else");

        // 聊天：投递计数只算自己一个。
        var chat = GameProtocolEngine.CreateMessageNotification(
            GameMessageType.Type03White,
            2,
            Encoding.ASCII.GetBytes("hello"));
        check(await directory.BroadcastToAreaAsync(bobTicket, chat, includeSelf: true, CancellationToken.None) == 1,
            "a private-area broadcast reaches exactly one recipient: the sender itself");
        check(bob.Of(GameProtocolEngine.MessageNotification).Count == 1
            && alice.Of(GameProtocolEngine.MessageNotification).Count == 0,
            "the other private-area member never receives the broadcast");

        // 对端表照常工作，但只列自己（客户端仍需要自己的端点）。
        var bobPeers = ReadPeerTable(bob.Of(GameProtocolEngine.UdpPeerInfoNotification)[^1]);
        var alicePeers = ReadPeerTable(alice.Of(GameProtocolEngine.UdpPeerInfoNotification)[^1]);
        check(bobPeers.Length == 1 && bobPeers[0].UserId == 2
            && alicePeers.Length == 1 && alicePeers[0].UserId == 1,
            "a private-area peer table keeps working per member and lists only its owner");

        // 离开信号：Alice 离开后自己的实例被回收，Bob 看不到这条私人房间里的信号。
        var bobPacketsBeforeAliceLeft = bob.Packets.Count;
        await directory.LeaveAsync(aliceTicket, CancellationToken.None);
        check(directory.GetMemberCount(1, 1) == 1
            && !directory.TryGetSnapshot(aliceTicket!.CharacterId, out _),
            "a private-area member's departure still clears its own registration");
        check(bob.Packets.Count == bobPacketsBeforeAliceLeft
            && bob.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "the surviving private-area member sees nothing of the other member's departure signal");

        await directory.LeaveAsync(bobTicket, CancellationToken.None);
        check(directory.GetMemberCount(1, 1) == 0,
            "the private area is released once its last instance empties");

        // 同一份默认配置（PrivateAreas=["1:1"]）下的共享区域 1/2：两人行为与改造前一致。
        var sharedAlice = new FakeChannel();
        var sharedAliceTicket = await directory.EnterAsync(
            CreateEntry(1, 2, 3, "Alice", sharedAlice, alicePeer),
            CancellationToken.None);
        await directory.RefreshAppearanceAsync(
            sharedAliceTicket,
            CreateAppearance(3, "Alice"),
            CancellationToken.None);

        var sharedBob = new FakeChannel();
        var sharedBobTicket = await directory.EnterAsync(
            CreateEntry(1, 2, 4, "Bob", sharedBob, bobPeer),
            CancellationToken.None);
        var sharedRoster = ReadRoster(sharedBob.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(sharedRoster.Length == 2 && sharedRoster[0].UserId == 4 && sharedRoster[1].UserId == 3,
            "under the same private-area config a shared area still delivers a two-member roster");
        var sharedAppearances = sharedBob.Of(GameProtocolEngine.UserInfoNotification);
        check(sharedAppearances.Count == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(sharedAppearances[0].Payload.AsSpan(3, 2)) == 3,
            "a shared area still delivers the existing member's appearance to the newcomer");
        // 与私密阶段的「零外来 USER_AREA」成对：同一个开关在共享区域必发，证明隔离断言可证伪。
        var sharedForeignAreas = sharedBob.Of(GameProtocolEngine.UserAreaNotification);
        check(sharedForeignAreas.Count == 1
            && sharedForeignAreas[0].Payload[0] == 3
            && sharedForeignAreas[0].Payload[9] == 1,
            "the same USER_AREA switch delivers the existing member in a shared area, so the private-area zero-USER_AREA assertion is falsifiable");

        await directory.RefreshAppearanceAsync(
            sharedBobTicket,
            CreateAppearance(4, "Bob"),
            CancellationToken.None);
        var sharedJoins = sharedAlice.Of(GameProtocolEngine.UserAreaNotification);
        check(sharedJoins.Count == 1
            && sharedJoins[0].Payload[0] == 4
            && sharedJoins[0].Payload[9] == 1,
            "a shared area still announces the newcomer's join to the existing member");

        check(await directory.BroadcastToAreaAsync(sharedBobTicket, chat, includeSelf: true, CancellationToken.None) == 2,
            "a shared-area broadcast still reaches both members");
        check(await directory.BroadcastToAreaAsync(sharedBobTicket, chat, includeSelf: false, CancellationToken.None) == 1,
            "a shared-area broadcast without self still reaches only the other member");

        // 共享区域的销毁信号（USER_AREA 0xFF/0xFF 哨兵）同样不受私密区域配置影响。
        await directory.LeaveAsync(sharedBobTicket, CancellationToken.None);
        var sharedDepartures = sharedAlice.Of(GameProtocolEngine.UserAreaNotification);
        check(sharedDepartures.Count == 2
            && sharedDepartures[1].Payload[0] == 4
            && sharedDepartures[1].Payload[2] == 0xFF
            && sharedDepartures[1].Payload[3] == 0xFF
            && sharedDepartures[1].Payload[9] == 0
            && directory.GetMemberCount(1, 2) == 1,
            "the shared-area departure still fires the region-mismatch signal under the private-area config");

        // ---- 配置解析：合法条目生效，非法条目告警跳过，绝不抛异常 ----
        var parsedOptions = new ServerOptions();
        parsedOptions.TownSync.PrivateAreas = ["2:7", "0:1", "1:abc", "abc", "256:1", "1:256", ""];
        var parseLogger = new RecordingLogger<TownSessionDirectory>();
        TownSessionDirectory parsed;
        try
        {
            parsed = new TownSessionDirectory(parsedOptions, TimeProvider.System, parseLogger);
            check(true, "invalid private-area entries are skipped without throwing");
        }
        catch (Exception exception)
        {
            check(false, $"invalid private-area entries are skipped without throwing ({exception.GetType().Name})");
            return;
        }

        check(parseLogger.Entries.Count(entry => entry.Level == LogLevel.Warning) == 6,
            "each invalid private-area entry is logged once and skipped");

        var privateOne = new FakeChannel();
        await parsed.EnterAsync(CreateEntry(2, 7, 1, "P1", privateOne), CancellationToken.None);
        var privateTwo = new FakeChannel();
        await parsed.EnterAsync(CreateEntry(2, 7, 2, "P2", privateTwo), CancellationToken.None);
        check(parsed.GetMemberCount(2, 7) == 2
            && ReadRoster(privateTwo.Of(GameProtocolEngine.AreaUsersNotification)[0]).Length == 1,
            "a valid private-area entry is honoured");

        // 被跳过的非法条目不得把普通区域变成私密。
        var sharedOne = new FakeChannel();
        await parsed.EnterAsync(CreateEntry(2, 1, 1, "S1", sharedOne), CancellationToken.None);
        var sharedTwo = new FakeChannel();
        await parsed.EnterAsync(CreateEntry(2, 1, 2, "S2", sharedTwo), CancellationToken.None);
        check(ReadRoster(sharedTwo.Of(GameProtocolEngine.AreaUsersNotification)[0]).Length == 2,
            "invalid entries never make the shared HendonMyre street private");
        var zeroAreaOne = new FakeChannel();
        await parsed.EnterAsync(CreateEntry(0, 1, 1, "Z1", zeroAreaOne), CancellationToken.None);
        var zeroAreaTwo = new FakeChannel();
        await parsed.EnterAsync(CreateEntry(0, 1, 2, "Z2", zeroAreaTwo), CancellationToken.None);
        check(ReadRoster(zeroAreaTwo.Of(GameProtocolEngine.AreaUsersNotification)[0]).Length == 2,
            "the invalid 0:1 entry never makes an area private");
    }

    private static async Task CheckAllSeriaRoomsAsync(Action<bool, string> check)
    {
        var directory = new TownSessionDirectory(new ServerOptions(), TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);
        foreach (var (town, area) in new (byte, byte)[] { (1, 1), (2, 5), (3, 2), (4, 1), (5, 1) })
        {
            var alice = new FakeChannel();
            var bob = new FakeChannel();
            var first = await directory.EnterAsync(CreateEntry(town, area, 1, "Alice", alice), CancellationToken.None);
            await directory.RefreshAppearanceAsync(first, CreateAppearance(1, "Alice"), CancellationToken.None);
            var second = await directory.EnterAsync(CreateEntry(town, area, 2, "Bob", bob), CancellationToken.None);
            await directory.RefreshAppearanceAsync(second, CreateAppearance(2, "Bob"), CancellationToken.None);
            await directory.UpdatePositionAsync(second, 500, 300, 7, 120, CancellationToken.None);
            check(ReadRoster(bob.Of(GameProtocolEngine.AreaUsersNotification)[0]).Select(u => u.UserId)
                    .SequenceEqual(new ushort[] { 2 })
                && alice.Of(GameProtocolEngine.UserInfoNotification).Count == 0
                && bob.Of(GameProtocolEngine.UserInfoNotification).Count == 0
                && alice.Of(GameProtocolEngine.UserPositionNotification).Count == 0,
                $"Seria room {town}/{area} isolates roster, appearance and movement with no configuration");
            await directory.LeaveAsync(first, CancellationToken.None);
            await directory.LeaveAsync(second, CancellationToken.None);
        }
        foreach (byte town in new byte[] { 2, 3 })
        {
            var first = await directory.EnterAsync(CreateEntry(town, 1, 1, "Alice", new FakeChannel()), CancellationToken.None);
            var bob = new FakeChannel();
            var second = await directory.EnterAsync(CreateEntry(town, 1, 2, "Bob", bob), CancellationToken.None);
            check(ReadRoster(bob.Of(GameProtocolEngine.AreaUsersNotification)[0]).Length == 2,
                $"ordinary street {town}/1 remains shared");
            await directory.LeaveAsync(first, CancellationToken.None);
            await directory.LeaveAsync(second, CancellationToken.None);
        }
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
    /// 幽灵与观察者都走**默认区域**（城镇 1/区域 0 广场，共享区）：默认值若落回已私密的
    /// 1/1，幽灵会与玩家各自成区、观察者收不到外观，这条测试随之变红。
    /// </summary>
    private static async Task CheckGhostBroadcastAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        options.Debug.TownGhost.Enabled = true;
        options.Debug.TownGhost.Name = "TEST";
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
            CreateEntry(options.Debug.TownGhost.TownId, options.Debug.TownGhost.AreaId, 1, "Witness", witness),
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
            var appearance = await WaitForGhostIntroductionAsync(witness, expectedGhostId);
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

    private static async Task<GameServerPacket?> WaitForGhostIntroductionAsync(
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
            // The peer table follows USERINFO. Observing only the first packet
            // can race the ghost's asynchronous introduction and falsely fail
            // the endpoint assertions immediately after this wait.
            if (match is not null
                && channel.Of(GameProtocolEngine.UdpPeerInfoNotification)
                    .Any(packet => ReadPeerTable(packet)
                        .Any(peer => peer.UserId == expectedUserId)))
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
        // 共享区域的既有行为：显式清空私密区域（默认的 1/1 私密性由 CheckPrivateAreasAsync 覆盖）。
        options.TownSync.PrivateAreas = [];
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        var alice = new FakeChannel();
        var aliceTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 1, "Alice", alice),
            CancellationToken.None);
        check(aliceTicket is not null, "the first member enters the town area");
        check(directory.GetMemberCount(1, 0) == 1, "the area reports one member");
        check(alice.Of(GameProtocolEngine.AreaUsersNotification).Count == 1,
            "the newcomer receives exactly one AREA_USERS roster");
        var soloRoster = ReadRoster(alice.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(soloRoster.Length == 1 && soloRoster[0].UserId == 1,
            "a solo roster contains only the newcomer itself, exactly like before");
        check(alice.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "a solo newcomer receives no foreign USER_AREA packet");

        var bob = new FakeChannel();
        var bobTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 2, "Bob", bob),
            CancellationToken.None);
        check(bobTicket is not null && directory.GetMemberCount(1, 0) == 2,
            "the second member joins the same area");
        var sharedRoster = ReadRoster(bob.Of(GameProtocolEngine.AreaUsersNotification)[0]);
        check(sharedRoster.Length == 2 && sharedRoster[0].UserId == 2 && sharedRoster[1].UserId == 1,
            "the newcomer roster carries itself first and the existing member second");
        // 加入增量不在进入时发：此刻客户端还没有 Bob 的 USERINFO 记录，这条 USER_AREA 会被丢掉。
        check(alice.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "the existing member gets no join signal before the newcomer's appearance exists");

        var appearance = CreateAppearance(2, "Bob");
        await directory.RefreshAppearanceAsync(bobTicket, appearance, CancellationToken.None);
        // 老人一侧的完整顺序：自己的名单 → 新人的外观 → 新人的加入增量。外观必须先到，
        // 客户端才有 Bob 的 USERINFO 记录来承接后面的 USER_AREA。
        check(alice.Packets.Select(packet => packet.ProtocolId).SequenceEqual(new byte[]
            {
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserInfoNotification,
                GameProtocolEngine.UserAreaNotification
            }),
            "the existing member receives the newcomer's appearance before its join USER_AREA");
        var joinPacket = alice.Of(GameProtocolEngine.UserAreaNotification);
        check(joinPacket.Count == 1
            && joinPacket[0].Payload[0] == 2
            && joinPacket[0].Payload[9] == 1,
            "the join USER_AREA(active=1) arrives exactly once, right after the appearance");
        check(bob.Of(GameProtocolEngine.UserInfoNotification).Count == 0,
            "an appearance refresh never echoes back to its owner");

        // 换装/宠物变化反复刷新外观：外观重播，加入增量不再重复。
        await directory.RefreshAppearanceAsync(bobTicket, CreateAppearance(2, "Bob"), CancellationToken.None);
        await directory.RefreshAppearanceAsync(bobTicket, CreateAppearance(2, "Bob"), CancellationToken.None);
        check(alice.Of(GameProtocolEngine.UserInfoNotification).Count == 3
            && alice.Of(GameProtocolEngine.UserAreaNotification).Count == 1,
            "later appearance refreshes replay the appearance but never repeat the join signal");

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
        check(directory.GetMemberCount(1, 0) == 1, "a departing member leaves the area");
        // Alice 的 USER_AREA 列表此时是 [Bob 加入, Bob 离开]，离开信号是第二条。
        var departure = alice.Of(GameProtocolEngine.UserAreaNotification);
        check(departure.Count == 2
            && departure[1].Payload[0] == 2
            && departure[1].Payload[2] == 0xFF
            && departure[1].Payload[3] == 0xFF
            && departure[1].Payload[9] == 0,
            "leaving is signalled with USER_AREA(town=0xFF, area=0xFF) so the client destroys the actor");
        check(!directory.TryGetSnapshot(bobTicket!.CharacterId, out _),
            "a departed member is no longer reported by character id");

        await directory.LeaveAsync(aliceTicket, CancellationToken.None);
        check(directory.GetMemberCount(1, 0) == 0, "an empty area is released");

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
        // 备选信号只重发名单：AREA_USERS 逐条只做加/更新，缺席 uid 不会被处理，
        // 所以这条信号**不能销毁**角色——这里同时钉住“它不发 USER_AREA”。
        check(resent.Count == 2 && ReadRoster(resent[1]).Length == 1,
            "the roster-resend signal sends a roster without the departed member");
        check(dave.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "the roster-resend signal emits no USER_AREA and therefore cannot remove the actor (client rosters only add/update)");

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
                CreateEntry(1, 0, 1, "Zoe", ignored),
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
        // 共享区域的对端表行为：显式清空私密区域（默认的 1/1 私密性由 CheckPrivateAreasAsync 覆盖）。
        options.TownSync.PrivateAreas = [];
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        var alicePeer = CreatePeer(1, "10.0.0.1", "203.0.113.1", 40001, accountId: 7, natType: 1, mtu: 1400);
        var bobPeer = CreatePeer(2, "10.0.0.2", "203.0.113.2", 40002, accountId: 8, natType: 2, mtu: 1472);

        // Alice 独自进区：表里只有她自己（这是本任务指定的新增包）。
        var alice = new FakeChannel();
        var aliceTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 1, "Alice", alice, alicePeer),
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
        await directory.RefreshAppearanceAsync(
            aliceTicket, CreateAppearance(1, "Alice"), CancellationToken.None);

        // Bob 进入：新人拿到「自己第一、老人随后」的表；老人的表要等 Bob 外观介绍之后。
        var bob = new FakeChannel();
        var bobTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 2, "Bob", bob, bobPeer),
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
        check(aliceTables.Count == 1, "the resident's peer refresh waits for the newcomer's appearance");
        await directory.RefreshAppearanceAsync(
            bobTicket, CreateAppearance(2, "Bob"), CancellationToken.None);
        aliceTables = alice.Of(GameProtocolEngine.UdpPeerInfoNotification);
        check(aliceTables.Count == 2, "the existing member receives one refreshed table");
        check(ReadPeerTable(aliceTables[1]).Length == 2
            && ReadPeerTable(aliceTables[1])[0].UserId == 1
            && ReadPeerTable(aliceTables[1])[1].UserId == 2,
            "the refreshed table lists the existing member first and now includes the newcomer");

        // Carol 没有对端信息（例如没上报 NAT）：必须被跳过，而不是带着空端点进表。
        var carol = new FakeChannel();
        var carolTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 3, "Carol", carol),
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
        // Alice 的表依次来自：自己进入、Bob 外观介绍、Bob 离开；无端点 Carol 不触发重播。
        check(aliceTables.Count == 3
            && aliceAfterDeparture.Length == 1
            && aliceAfterDeparture[0].UserId == 1,
            "a departure refreshes the remaining member's table without the departed member");
        var carolAfterDeparture = ReadPeerTable(
            carol.Of(GameProtocolEngine.UdpPeerInfoNotification)[^1]);
        check(carolAfterDeparture.Length == 1 && carolAfterDeparture[0].UserId == 1,
            "every remaining member sees the departure in its refreshed table");
        check(bob.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 1,
            "the departed member receives no peer table after leaving");

        // 强制移除路径：Carol 的通道在下一次投递时抛异常，她会被目录摘除；
        // 剩下的人的表里必须也删掉她（嵌套刷新用的是摘除后的现算名单）。
        var davePeer = CreatePeer(4, "10.0.0.4", "203.0.113.4", 40004, accountId: 9, natType: 1, mtu: 1400);
        var dave = new FakeChannel();
        carol.FailNextSend = true;
        var daveTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 4, "Dave", dave, davePeer),
            CancellationToken.None);
        await directory.RefreshAppearanceAsync(
            daveTicket, CreateAppearance(4, "Dave"), CancellationToken.None);
        check(directory.GetMemberCount(1, 0) == 2,
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
        var secondTicket = await disabledDirectory.EnterAsync(
            CreateEntry(2, 1, 2, "Second", second, bobPeer),
            CancellationToken.None);
        // 加入增量跟着 Second 的外观广播走：开关不打开时也不在进入时抢先发。
        check(first.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "disabling the peer table does not bring the join signal forward to entry time");
        await disabledDirectory.RefreshAppearanceAsync(
            secondTicket,
            CreateAppearance(2, "Second"),
            CancellationToken.None);
        await disabledDirectory.LeaveAsync(firstTicket, CancellationToken.None);
        check(first.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 0
            && second.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 0,
            "disabling SendAreaPeerTable sends no peer table on entry or departure");
        check(first.Of(GameProtocolEngine.AreaUsersNotification).Count == 1
            && second.Of(GameProtocolEngine.AreaUsersNotification).Count == 1,
            "disabling SendAreaPeerTable keeps the per-member AREA_USERS roster untouched");
        check(first.Packets.Select(packet => packet.ProtocolId).SequenceEqual(new byte[]
            {
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserInfoNotification,
                GameProtocolEngine.UserAreaNotification
            }),
            "disabling the peer table keeps the appearance → join order for the existing member");
        var firstAreas = first.Of(GameProtocolEngine.UserAreaNotification);
        check(firstAreas.Count == 1
            && firstAreas[0].Payload[0] == 2
            && firstAreas[0].Payload[9] == 1,
            "disabling the peer table keeps the existing member's join signal");
        var secondAreas = second.Of(GameProtocolEngine.UserAreaNotification);
        check(secondAreas.Count == 1
            && secondAreas[0].Payload[0] == 1
            && secondAreas[0].Payload[2] == 0xFF
            && secondAreas[0].Payload[3] == 0xFF
            && secondAreas[0].Payload[9] == 0,
            "disabling the peer table keeps the region-mismatch USER_AREA departure signal");
    }

    private static async Task CheckPeerTableIntroductionOrderAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        options.TownSync.PrivateAreas = [];
        var directory = new TownSessionDirectory(
            options, TimeProvider.System, NullLogger<TownSessionDirectory>.Instance);
        var alicePeer = CreatePeer(1, "10.0.0.1", "203.0.113.1", 40001, 7, 1, 1400);
        var bobPeer = CreatePeer(2, "10.0.0.2", "203.0.113.2", 40002, 8, 2, 1472);
        var alice = new FakeChannel();
        var aliceTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 1, "Alice", alice, alicePeer), CancellationToken.None);
        await directory.RefreshAppearanceAsync(
            aliceTicket, CreateAppearance(1, "Alice"), CancellationToken.None);
        var bob = new FakeChannel();
        var bobTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 2, "Bob", bob, bobPeer), CancellationToken.None);

        check(alice.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 1,
            "the resident does not receive the newcomer's peer entry before its USERINFO exists");
        var bobClient = ReplayClientPeers(bob, ownUserId: 2);
        check(bobClient.Ignored == 0 && bobClient.Mtus.GetValueOrDefault((ushort)1) == 1400,
            "the newcomer accepts the resident's MTU after receiving its USERINFO");

        await directory.RefreshAppearanceAsync(
            bobTicket, CreateAppearance(2, "Bob"), CancellationToken.None);
        var aliceClient = ReplayClientPeers(alice, ownUserId: 1);
        check(aliceClient.Ignored == 0 && aliceClient.Mtus.GetValueOrDefault((ushort)2) == 1472,
            "the resident accepts the newcomer's real MTU instead of silently retaining zero");
        check(alice.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 2
            && bob.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 1,
            "the first introduction sends exactly one deferred peer table to the resident");
        await directory.RefreshAppearanceAsync(
            bobTicket, CreateAppearance(2, "Bob"), CancellationToken.None);
        check(alice.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 2
            && bob.Of(GameProtocolEngine.UdpPeerInfoNotification).Count == 1,
            "equipment appearance refreshes do not replay unchanged peer tables");

        // Two entries can overlap before either caller publishes its own appearance.
        // A live directory entry alone must not make that uid eligible for a peer table.
        var carolPeer = CreatePeer(3, "10.0.0.3", "203.0.113.3", 40003, 9, 1, 1280);
        var davePeer = CreatePeer(4, "10.0.0.4", "203.0.113.4", 40004, 10, 1, 1024);
        var carol = new FakeChannel();
        var carolTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 3, "Carol", carol, carolPeer), CancellationToken.None);
        var dave = new FakeChannel();
        var daveTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 4, "Dave", dave, davePeer), CancellationToken.None);
        check(ReadPeerTable(dave.Of(GameProtocolEngine.UdpPeerInfoNotification)[0])
                .All(peer => peer.UserId != 3),
            "an overlapping newcomer does not receive a peer entry for a still-unannounced resident");
        await directory.RefreshAppearanceAsync(
            carolTicket, CreateAppearance(3, "Carol"), CancellationToken.None);
        check(ReadPeerTable(alice.Of(GameProtocolEngine.UdpPeerInfoNotification)[^1])
                .All(peer => peer.UserId != 4),
            "one introduction cannot publish another pending newcomer's peer entry early");
        await directory.RefreshAppearanceAsync(
            daveTicket, CreateAppearance(4, "Dave"), CancellationToken.None);
        foreach (var (channel, ownId) in new[] { (alice, (ushort)1), (bob, (ushort)2), (carol, (ushort)3), (dave, (ushort)4) })
        {
            var client = ReplayClientPeers(channel, ownId);
            check(client.Ignored == 0 && client.Mtus.Count == 4
                && client.Mtus[1] == 1400 && client.Mtus[2] == 1472
                && client.Mtus[3] == 1280 && client.Mtus[4] == 1024,
                $"recipient {ownId} accepts every peer MTU across overlapping introductions");
        }

        await directory.LeaveAsync(bobTicket, CancellationToken.None);
        check(!aliceTicket!.Member.KnownAppearances.ContainsKey(2)
            && !carolTicket!.Member.KnownAppearances.ContainsKey(2)
            && bobTicket!.Member.KnownAppearances.IsEmpty,
            "departure releases introduced-member references in survivors and the departing member");
        var reusedPeer = CreatePeer(2, "10.0.0.8", "203.0.113.8", 40008, 18, 2, 1280);
        var reused = new FakeChannel();
        var reusedTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 2, "New Bob", reused, reusedPeer), CancellationToken.None);
        check(ReadPeerTable(alice.Of(GameProtocolEngine.UdpPeerInfoNotification)[^1])
                .All(peer => peer.UserId != 2),
            "reusing a departed uid does not reuse the old member's appearance knowledge");
        await directory.RefreshAppearanceAsync(
            reusedTicket, CreateAppearance(2, "New Bob"), CancellationToken.None);
        await directory.LeaveAsync(bobTicket, CancellationToken.None);
        check(aliceTicket.Member.KnownAppearances.TryGetValue(2, out var knownReplacement)
            && ReferenceEquals(knownReplacement, reusedTicket!.Member)
            && ReplayClientPeers(alice, 1).Mtus.GetValueOrDefault((ushort)2) == 1280,
            "a stale departure cannot remove the replacement uid's new identity or MTU");

        dave.FailNextSend = true;
        await directory.RefreshAppearanceAsync(
            reusedTicket, CreateAppearance(2, "New Bob"), CancellationToken.None);
        check(!aliceTicket.Member.KnownAppearances.ContainsKey(4)
            && daveTicket!.Member.KnownAppearances.IsEmpty,
            "a broken-channel eviction releases the same appearance references as an ordinary departure");
    }

    // DNF.exe NOTI 11 handler 0x41C8D9/0x41C8EB drops entries whose CNUser
    // does not exist; it does not cache them for a later USERINFO. The CNUser
    // constructor initializes MTU to zero (0x4FD2D0), so emission alone is not success.
    private static (Dictionary<ushort, uint> Mtus, int Ignored) ReplayClientPeers(
        FakeChannel channel, ushort ownUserId)
    {
        var users = new HashSet<ushort> { ownUserId };
        var mtus = new Dictionary<ushort, uint>();
        var ignored = 0;
        foreach (var packet in channel.Packets)
        {
            if (packet.ProtocolId == GameProtocolEngine.UserInfoNotification)
            {
                users.Add(BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3, 2)));
            }
            else if (packet.ProtocolId == GameProtocolEngine.UdpPeerInfoNotification)
            {
                foreach (var peer in ReadPeerTable(packet))
                {
                    if (users.Contains(peer.UserId)) mtus[peer.UserId] = peer.Mtu;
                    else ignored++;
                }
            }
        }

        return (mtus, ignored);
    }

    /// <summary>
    /// 对称性开关（Task 13）：客户端只为已有 USERINFO 记录的 uid 建立同镇角色，所以顺序
    /// 必须是「外观 → 名单」与「外观 → 加入增量」。这里钉住默认顺序（逐字节）、两个开关
    /// 各自的报文与顺序，以及单人成员一个新增包都不收。
    /// </summary>
    private static async Task CheckSymmetricIntroductionAsync(Action<bool, string> check)
    {
        // ---- 默认配置：外观先于名单，加入增量跟在外观之后（逐个报文逐字节钉死） ----
        var defaultOptions = new ServerOptions();
        // 本测试钉共享区域的报文序列：使用共享广场（私人房间由 CheckAllSeriaRoomsAsync 覆盖）。
        defaultOptions.TownSync.PrivateAreas = [];
        check(!defaultOptions.TownSync.SendUserAreaForExistingMembers
            && !defaultOptions.TownSync.RefreshRosterOnMembershipChange,
            "both symmetric introduction switches are off by default");

        var defaultDirectory = new TownSessionDirectory(
            defaultOptions,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);
        var alicePeer = CreatePeer(1, "10.0.0.1", "203.0.113.1", 40001, accountId: 7, natType: 1, mtu: 1400);
        var bobPeer = CreatePeer(2, "10.0.0.2", "203.0.113.2", 40002, accountId: 8, natType: 2, mtu: 1472);
        var aliceTownUser = new GameTownUser(1, 474, 234, 5);
        var bobTownUser = new GameTownUser(2, 474, 234, 5);

        var alice = new FakeChannel();
        var aliceTicket = await defaultDirectory.EnterAsync(
            CreateEntry(1, 0, 1, "Alice", alice, alicePeer),
            CancellationToken.None);
        // Alice 先广播外观：新人进区时拿到的就是这份已存档的外观快照。
        await defaultDirectory.RefreshAppearanceAsync(
            aliceTicket,
            CreateAppearance(1, "Alice"),
            CancellationToken.None);

        var bob = new FakeChannel();
        var bobTicket = await defaultDirectory.EnterAsync(
            CreateEntry(1, 0, 2, "Bob", bob, bobPeer),
            CancellationToken.None);
        // 新人默认顺序：老人外观（先建 USERINFO 记录）→ 名单 → 对端表。
        // 客户端对名单里没有记录的 uid 会静默跳过，所以外观必须排在名单前面。
        check(PacketsMatch(bob,
            [
                CreateAppearance(1, "Alice"),
                GameProtocolEngine.CreateAreaUsers(1, 0, [bobTownUser, aliceTownUser]),
                GameProtocolEngine.CreateUdpPeerInfo([bobPeer, alicePeer])
            ]),
            "by default the newcomer receives the existing member's appearance before its roster");

        await defaultDirectory.RefreshAppearanceAsync(
            bobTicket,
            CreateAppearance(2, "Bob"),
            CancellationToken.None);
        // 换装/宠物变化再刷两次：外观重播，加入增量只发一次。
        await defaultDirectory.RefreshAppearanceAsync(
            bobTicket,
            CreateAppearance(2, "Bob"),
            CancellationToken.None);
        await defaultDirectory.RefreshAppearanceAsync(
            bobTicket,
            CreateAppearance(2, "Bob"),
            CancellationToken.None);
        await defaultDirectory.LeaveAsync(bobTicket, CancellationToken.None);

        // Alice 的默认序列：单人名单 → 单人对端表 → Bob 的外观 →
        // 一次加入 USER_AREA → 含 Bob 的对端表 → 两次重播外观（没有第二次加入）→ Bob 离开的 USER_AREA
        // → 只剩自己的对端表。
        check(PacketsMatch(alice,
            [
                GameProtocolEngine.CreateAreaUsers(1, 0, [aliceTownUser]),
                GameProtocolEngine.CreateUdpPeerInfo([alicePeer]),
                CreateAppearance(2, "Bob"),
                GameProtocolEngine.CreateUserArea(1, 0, bobTownUser),
                GameProtocolEngine.CreateUdpPeerInfo([alicePeer, bobPeer]),
                CreateAppearance(2, "Bob"),
                CreateAppearance(2, "Bob"),
                GameProtocolEngine.CreateUserAreaDeparture(bobTownUser),
                GameProtocolEngine.CreateUdpPeerInfo([alicePeer])
            ]),
            "the existing member gets appearance → join once, later appearances without a second join, then the departure signal");
        check(alice.Of(GameProtocolEngine.AreaUsersNotification).Count == 1
            && bob.Of(GameProtocolEngine.AreaUsersNotification).Count == 1,
            "with both switches off no roster refresh is sent");
        check(bob.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "with both switches off the newcomer receives no foreign USER_AREA packet");
        check(alice.Of(GameProtocolEngine.UserInfoNotification).Count == 3
            && alice.Of(GameProtocolEngine.UserAreaNotification).Count == 2,
            "repeated appearance refreshes replay only the appearance and leave the join signal at one");

        // ---- SendUserAreaForExistingMembers：新人补收每个老人的 USER_AREA ----
        var areaOptions = new ServerOptions();
        areaOptions.TownSync.SendUserAreaForExistingMembers = true;
        var areaDirectory = new TownSessionDirectory(
            areaOptions,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        // 单人成员没有「已有成员」可补，开关打开也不得多收包。
        var areaSolo = new FakeChannel();
        await areaDirectory.EnterAsync(
            CreateEntry(2, 1, 1, "Solo", areaSolo),
            CancellationToken.None);
        check(areaSolo.Packets.Count == 1
            && areaSolo.Packets[0].ProtocolId == GameProtocolEngine.AreaUsersNotification,
            "a solo member receives no extra packets even with the USER_AREA switch on");

        var areaAlice = new FakeChannel();
        var areaAliceTicket = await areaDirectory.EnterAsync(
            CreateEntry(2, 2, 1, "Alice", areaAlice),
            CancellationToken.None);
        // 先给 Alice 存一份外观：新人要先拿到它，补发的 USER_AREA 追加在名单之后。
        await areaDirectory.RefreshAppearanceAsync(
            areaAliceTicket,
            CreateAppearance(1, "Alice"),
            CancellationToken.None);

        var areaBob = new FakeChannel();
        var areaBobTicket = await areaDirectory.EnterAsync(
            CreateEntry(2, 2, 2, "Bob", areaBob),
            CancellationToken.None);
        var bobOrder = areaBob.Packets.Select(packet => packet.ProtocolId).ToArray();
        check(bobOrder.SequenceEqual(new byte[]
            {
                GameProtocolEngine.UserInfoNotification,
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserAreaNotification
            }),
            "with the USER_AREA switch on the newcomer sees the appearance, then the roster, then each foreign USER_AREA");

        var foreignAreas = areaBob.Of(GameProtocolEngine.UserAreaNotification);
        check(foreignAreas.Count == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(foreignAreas[0].Payload.AsSpan(0, 2)) == 1
            && foreignAreas[0].Payload[2] == 2
            && foreignAreas[0].Payload[3] == 2
            && BinaryPrimitives.ReadInt16LittleEndian(foreignAreas[0].Payload.AsSpan(4, 2)) == 474
            && BinaryPrimitives.ReadInt16LittleEndian(foreignAreas[0].Payload.AsSpan(6, 2)) == 234
            && foreignAreas[0].Payload[8] == 5
            && foreignAreas[0].Payload[9] == 1,
            "the newcomer gets one active USER_AREA for the existing member with its uid, area, coordinates and direction");
        check(foreignAreas.All(packet =>
                BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(0, 2)) != 2),
            "the newcomer never receives a USER_AREA for itself");
        // 老人一侧不加包：加入增量仍只在 Bob 自己广播外观之后出现一次（该开关只补新人那侧）。
        await areaDirectory.RefreshAppearanceAsync(
            areaBobTicket,
            CreateAppearance(2, "Bob"),
            CancellationToken.None);
        check(areaAlice.Packets.Select(packet => packet.ProtocolId).SequenceEqual(new byte[]
            {
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserInfoNotification,
                GameProtocolEngine.UserAreaNotification
            }),
            "the USER_AREA switch leaves the existing member with appearance → join only");
        var areaAliceJoins = areaAlice.Of(GameProtocolEngine.UserAreaNotification);
        check(areaAliceJoins.Count == 1
            && areaAliceJoins[0].Payload[0] == 2
            && areaAliceJoins[0].Payload[9] == 1,
            "the USER_AREA switch leaves the existing member's join signal untouched");

        // ---- RefreshRosterOnMembershipChange：老人收到成员变化后的刷新名单 ----
        var rosterOptions = new ServerOptions();
        rosterOptions.TownSync.RefreshRosterOnMembershipChange = true;
        var rosterDirectory = new TownSessionDirectory(
            rosterOptions,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        var rosterAlice = new FakeChannel();
        await rosterDirectory.EnterAsync(
            CreateEntry(3, 1, 1, "Alice", rosterAlice),
            CancellationToken.None);
        var soloRosters = rosterAlice.Of(GameProtocolEngine.AreaUsersNotification);
        check(soloRosters.Count == 1 && ReadRoster(soloRosters[0]).Length == 1,
            "a solo newcomer's initial roster keeps its old shape when the refresh switch is on");
        check(rosterAlice.Of(GameProtocolEngine.UserAreaNotification).Count == 0,
            "the roster-refresh switch alone adds no foreign USER_AREA packets");

        var rosterBob = new FakeChannel();
        var rosterBobTicket = await rosterDirectory.EnterAsync(
            CreateEntry(3, 1, 2, "Bob", rosterBob),
            CancellationToken.None);
        // 加入增量跟着 Bob 的外观广播走：进区时先到刷新名单，随后才是「外观 → 加入」。
        await rosterDirectory.RefreshAppearanceAsync(
            rosterBobTicket,
            CreateAppearance(2, "Bob"),
            CancellationToken.None);
        check(rosterAlice.Packets.Select(packet => packet.ProtocolId).SequenceEqual(new byte[]
            {
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserInfoNotification,
                GameProtocolEngine.UserAreaNotification
            }),
            "with the refresh switch on the refreshed roster arrives at entry and the join follows the newcomer's appearance");

        var aliceRosters = rosterAlice.Of(GameProtocolEngine.AreaUsersNotification);
        check(aliceRosters.Count == 2,
            "the existing member receives exactly one refreshed roster when a member joins");
        var afterBobJoined = ReadRoster(aliceRosters[1]);
        check(afterBobJoined.Length == 2
            && afterBobJoined[0].UserId == 1
            && afterBobJoined[1].UserId == 2,
            "the refreshed roster contains both members with the recipient first and the newcomer second");
        var bobRosters = rosterBob.Of(GameProtocolEngine.AreaUsersNotification);
        check(bobRosters.Count == 1
            && ReadRoster(bobRosters[0]).Length == 2
            && ReadRoster(bobRosters[0])[0].UserId == 2
            && ReadRoster(bobRosters[0])[1].UserId == 1,
            "the newcomer's own initial roster keeps its old shape and self-first order");

        // 第三个成员：刷新名单必须按接收者各自「自己排第一」，而不是共享一份新人第一的快照。
        var rosterCarol = new FakeChannel();
        var rosterCarolTicket = await rosterDirectory.EnterAsync(
            CreateEntry(3, 1, 3, "Carol", rosterCarol),
            CancellationToken.None);
        aliceRosters = rosterAlice.Of(GameProtocolEngine.AreaUsersNotification);
        check(aliceRosters.Count == 3
            && ReadRoster(aliceRosters[^1]).Select(user => user.UserId).SequenceEqual(new ushort[] { 1, 2, 3 }),
            "the first member's refresh after a third join starts with itself, then the others in join order");
        bobRosters = rosterBob.Of(GameProtocolEngine.AreaUsersNotification);
        check(bobRosters.Count == 2
            && ReadRoster(bobRosters[^1]).Select(user => user.UserId).SequenceEqual(new ushort[] { 2, 1, 3 }),
            "the second member's refresh starts with itself, proving each roster is rebuilt per recipient");
        var carolRosters = rosterCarol.Of(GameProtocolEngine.AreaUsersNotification);
        check(carolRosters.Count == 1
            && ReadRoster(carolRosters[0]).Select(user => user.UserId).SequenceEqual(new ushort[] { 3, 1, 2 }),
            "the third member's initial roster also starts with itself, then the existing members");

        await rosterDirectory.LeaveAsync(rosterCarolTicket, CancellationToken.None);
        aliceRosters = rosterAlice.Of(GameProtocolEngine.AreaUsersNotification);
        bobRosters = rosterBob.Of(GameProtocolEngine.AreaUsersNotification);
        check(aliceRosters.Count == 4
            && ReadRoster(aliceRosters[^1]).Select(user => user.UserId).SequenceEqual(new ushort[] { 1, 2 }),
            "a departure refreshes the survivor's roster with itself first and without the departed member");
        check(bobRosters.Count == 3
            && ReadRoster(bobRosters[^1]).Select(user => user.UserId).SequenceEqual(new ushort[] { 2, 1 }),
            "every survivor sees itself first in the departure refresh");
        check(rosterCarol.Of(GameProtocolEngine.AreaUsersNotification).Count == 1,
            "the departed member receives no roster refresh");

        await rosterDirectory.LeaveAsync(rosterBobTicket, CancellationToken.None);
        aliceRosters = rosterAlice.Of(GameProtocolEngine.AreaUsersNotification);
        check(aliceRosters.Count == 5
            && ReadRoster(aliceRosters[^1]).Length == 1
            && ReadRoster(aliceRosters[^1])[0].UserId == 1
            && ReadRoster(aliceRosters[^1])[0].Active == 1,
            "the last survivor's refresh keeps a single active self entry");
        check(rosterAlice.Packets.Select(packet => packet.ProtocolId).SequenceEqual(new byte[]
            {
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserInfoNotification,
                GameProtocolEngine.UserAreaNotification,
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserAreaNotification,
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserAreaNotification,
                GameProtocolEngine.AreaUsersNotification
            }),
            "the join still follows the newcomer's appearance, and every departure signal still precedes its refreshed roster");

        // ---- 迁移：加入增量随新成员对象重来，旧区仍拿到离开信号 ----
        var migrationOptions = new ServerOptions();
        migrationOptions.TownSync.RefreshRosterOnMembershipChange = true;
        var migrationDirectory = new TownSessionDirectory(
            migrationOptions,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        var witnessOld = new FakeChannel();
        await migrationDirectory.EnterAsync(
            CreateEntry(4, 0, 9, "WitnessOld", witnessOld),
            CancellationToken.None);
        var witnessNew = new FakeChannel();
        await migrationDirectory.EnterAsync(
            CreateEntry(4, 2, 8, "WitnessNew", witnessNew),
            CancellationToken.None);
        var moverId = Guid.NewGuid();
        var oldMoverTicket = await migrationDirectory.EnterAsync(
            CreateEntry(4, 0, 1, "Mover", new FakeChannel(), moverId),
            CancellationToken.None);
        // 迁移前已在旧区广播过外观：旧成员对象上的 JoinAnnounced 已经是 true。
        await migrationDirectory.RefreshAppearanceAsync(
            oldMoverTicket,
            CreateAppearance(1, "Mover"),
            CancellationToken.None);
        var witnessOldAreas = witnessOld.Of(GameProtocolEngine.UserAreaNotification);
        check(witnessOldAreas.Count == 1
            && witnessOldAreas[0].Payload[0] == 1
            && witnessOldAreas[0].Payload[9] == 1,
            "the mover announces its join to the old area after its appearance");

        var newMoverTicket = await migrationDirectory.EnterAsync(
            CreateEntry(4, 2, 5, "Mover", new FakeChannel(), moverId),
            CancellationToken.None);
        await migrationDirectory.RefreshAppearanceAsync(
            newMoverTicket,
            CreateAppearance(5, "Mover"),
            CancellationToken.None);

        // 迁移后的成员对象是新的：JoinAnnounced 自动归零，新区观众同样拿到「外观 → 加入」。
        check(witnessNew.Packets.Select(packet => packet.ProtocolId).SequenceEqual(new byte[]
            {
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.AreaUsersNotification,
                GameProtocolEngine.UserInfoNotification,
                GameProtocolEngine.UserAreaNotification
            }),
            "the new area's witness receives the migrated member's appearance before its join");
        var witnessNewAreas = witnessNew.Of(GameProtocolEngine.UserAreaNotification);
        check(witnessNewAreas.Count == 1
            && witnessNewAreas[0].Payload[0] == 5
            && witnessNewAreas[0].Payload[9] == 1,
            "the migration resets JoinAnnounced so the new area gets exactly one join increment");
        check(witnessOld.Of(GameProtocolEngine.UserInfoNotification).Count == 1
            && witnessOld.Of(GameProtocolEngine.UserAreaNotification).Count == 2,
            "the old area keeps its appearance → join and then the departure signal");

        var witnessRosters = witnessOld.Of(GameProtocolEngine.AreaUsersNotification);
        check(witnessRosters.Count == 3,
            "a migration refreshes the old area's roster when the switch is on");
        var migratedRoster = ReadRoster(witnessRosters[^1]);
        check(migratedRoster.Length == 1 && migratedRoster[0].UserId == 9,
            "the migrated member is dropped from the old area's refreshed roster");
        check(witnessOld.Packets[^2].ProtocolId == GameProtocolEngine.UserAreaNotification
            && witnessOld.Packets[^1].ProtocolId == GameProtocolEngine.AreaUsersNotification
            && witnessOld.Packets[^2].Payload[9] == 0,
            "the migration departure signal is still sent before the refreshed roster");

        // ---- 两个开关同时打开：单人成员仍然只收一条旧名单 ----
        var bothOptions = new ServerOptions();
        bothOptions.TownSync.SendUserAreaForExistingMembers = true;
        bothOptions.TownSync.RefreshRosterOnMembershipChange = true;
        var bothSolo = new FakeChannel();
        await new TownSessionDirectory(
                bothOptions,
                TimeProvider.System,
                NullLogger<TownSessionDirectory>.Instance)
            .EnterAsync(CreateEntry(5, 1, 1, "Solo", bothSolo), CancellationToken.None);
        check(bothSolo.Packets.Count == 1
            && bothSolo.Packets[0].ProtocolId == GameProtocolEngine.AreaUsersNotification,
            "a solo member with both switches on receives exactly the single old roster packet");
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
        var options = new ServerOptions();
        // 迁移测试用 1/0 广场作为共享区域（默认私密性由 CheckPrivateAreasAsync 覆盖）。
        options.TownSync.PrivateAreas = [];
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        // 旧区里先放一个旁观者：迁移时它应该收到离开信号。
        var witness = new FakeChannel();
        var witnessTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 9, "Witness", witness),
            CancellationToken.None);

        var moverId = Guid.NewGuid();
        var oldChannel = new FakeChannel();
        var oldTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 1, "Mover", oldChannel, moverId),
            CancellationToken.None);
        check(oldTicket is not null
            && oldTicket.CharacterId == moverId
            && oldTicket.AreaUserId == 1
            && oldTicket.AreaId == 0
            && directory.GetMemberCount(1, 0) == 2,
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
        check(directory.GetMemberCount(1, 0) == 1 && directory.GetMemberCount(1, 2) == 1,
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

        // 迁移者从未广播外观，因此这里只有离开信号、没有加入增量（加入增量跟在外观之后）。
        var witnessAreas = witness.Of(GameProtocolEngine.UserAreaNotification);
        check(witnessAreas.Count == 1
            && witnessAreas[0].Payload[0] == 1
            && witnessAreas[0].Payload[2] == 0xFF
            && witnessAreas[0].Payload[3] == 0xFF
            && witnessAreas[0].Payload[9] == 0,
            "the old area is told the migrated member left with the 0xFF/0xFF region-mismatch USER_AREA");
        // 尾沿：没有外观广播就没有加入增量。加入增量只可能从 RefreshAppearanceAsync 发出，
        // 一个从不广播外观的成员对同区其他人只留下名单/对端表，事后也不会补一条 USER_AREA。
        check(witnessAreas.All(packet => packet.Payload[9] == 0),
            "a member that never broadcasts an appearance is never announced with a join increment");
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
        check(directory.GetMemberCount(1, 0) == 0,
            "the old area is released once its last member leaves");
        check(directory.GetMemberCount(1, 2) == 1,
            "releasing the old area leaves the migrated member in the new area");

        var reuser = new FakeChannel();
        var reuserTicket = await directory.EnterAsync(
            CreateEntry(1, 0, 7, "Reuser", reuser),
            CancellationToken.None);
        check(reuserTicket is not null
            && reuserTicket.AreaId == 0
            && directory.GetMemberCount(1, 0) == 1,
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

    /// <summary>
    /// 逐字节对比整条通道的报文序列（类型 + 协议号 + payload），用于钉住「开关关闭时与
    /// 改动前完全相同」。GameServerPacket 虽是 record，但 payload 是数组、按引用比较，
    /// 这里必须自己比字节。
    /// </summary>
    private static bool PacketsMatch(FakeChannel channel, IReadOnlyList<GameServerPacket> expected)
    {
        var actual = channel.Packets;
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (actual[index].Type != expected[index].Type
                || actual[index].ProtocolId != expected[index].ProtocolId
                || !actual[index].Payload.SequenceEqual(expected[index].Payload))
            {
                return false;
            }
        }

        return true;
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
                // The port is network order; the MTU is a raw little-endian u32
                // in the client's NOTI 11 handler (0x41C8C3 / 0x417A90).
                BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset + 10, 2)),
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

    /// <summary>只收集日志级别与文本的假 logger：用于断言非法私密区域条目告警且被跳过。</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
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
