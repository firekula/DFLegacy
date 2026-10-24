using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class EntranceSessionReadWaitSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        // 回归 stableverify/server.log:8692-8698：队友掉线后只剩一名组队实例成员；
        // 他死亡后停止发 TCP，扫描已投回城邮件，却一直等到 41 秒后的下一包才处理。
        await using var harness = await PartyHarness.CreateAsync();
        var member = Guid.NewGuid();
        var registry = new DungeonInstanceRegistry();
        var instance = fixture.CreatePartyInstance(1720);
        instance.AddMember(fixture.Member(member, 0, "留下的队员"));
        registry.BindCharacter(member, instance);
        var now = new DateTimeOffset(2026, 10, 23, 15, 48, 35, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, clock, NullLogger<DungeonPartyService>.Instance);
        instance.TrySetPartyMemberAlive(member, false, now);
        var pollMailbox = EntranceSessionReadWait.ShouldPollPartyMailbox(
            isChannelPort: true,
            hasCharacter: true,
            isPartyDungeon: instance.IsParty,
            hasPendingMail: registry.HasPendingMail(member),
            partyMemberCount: 1);
        check(pollMailbox && !registry.HasPendingMail(member),
            "the last member of a party dungeon polls BEFORE the future wipe mail has been posted");

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token);
        using var receiver = await listener.AcceptTcpClientAsync(deadline.Token);
        var receiverStream = receiver.GetStream();
        var peerStream = peer.GetStream();
        var buffer = new byte[1];
        var pendingRead = receiverStream.ReadAsync(buffer, deadline.Token).AsTask();

        // 长会员/选图/卡牌期限也不能挡住取信。等待已经开始后，才由真实扫描投邮件；
        // 对端整个过程中不发一字节，测试不靠伪造 TCP 请求把会话唤醒。
        var wait = EntranceSessionReadWait.WaitAsync(
            pendingRead, TimeSpan.FromHours(1), pollMailbox, deadline.Token);
        clock.Advance(TimeSpan.FromSeconds(10));
        await service.SweepInstanceTimersAsync(deadline.Token);
        check(await wait == EntranceReadWakeReason.PartyMailTick
                && !pendingRead.IsCompleted
                && registry.TryTakeSettlementReturn(member, out var reason)
                && reason == DungeonPartyService.PartyWipeReturnReason
                && instance.TryBeginPartyWipeReturn(member, clock.GetUtcNow()),
            "a queued wipe wakes an idle TCP session despite a long timer, with no client packet required");

        // tick 复用同一次挂起读取，不取消它；随后真正到达的包仍只读一次。
        check(await EntranceSessionReadWait.WaitAsync(
                    pendingRead, Timeout.InfiniteTimeSpan, pollMailbox, deadline.Token)
                == EntranceReadWakeReason.PartyMailTick
                && !pendingRead.IsCompleted,
            "repeated mailbox ticks leave the original socket read pending");
        await peerStream.WriteAsync(new byte[] { 0x5A }, deadline.Token);
        check(await EntranceSessionReadWait.WaitAsync(
                    pendingRead, Timeout.InfiniteTimeSpan, pollMailbox, deadline.Token)
                == EntranceReadWakeReason.Packet
                && await pendingRead == 1 && buffer[0] == 0x5A,
            "a packet arriving after mailbox ticks is consumed by that same read exactly once");

        var soloPolling = EntranceSessionReadWait.ShouldPollPartyMailbox(
            isChannelPort: true, hasCharacter: true, isPartyDungeon: false,
            hasPendingMail: false, partyMemberCount: 1);
        pendingRead = receiverStream.ReadAsync(buffer, deadline.Token).AsTask();
        var soloWait = EntranceSessionReadWait.WaitAsync(
            pendingRead, Timeout.InfiniteTimeSpan, soloPolling, deadline.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), deadline.Token);
        check(!soloPolling && !soloWait.IsCompleted,
            "a solo session retains its unbounded packet wait instead of adding periodic mailbox ticks");
        await peerStream.WriteAsync(new byte[] { 0x7B }, deadline.Token);
        check(await soloWait == EntranceReadWakeReason.Packet && await pendingRead == 1 && buffer[0] == 0x7B,
            "the unchanged solo wait ends normally when the client actually sends data");

        pendingRead = receiverStream.ReadAsync(buffer, deadline.Token).AsTask();
        check(await EntranceSessionReadWait.WaitAsync(
                    pendingRead, TimeSpan.FromMilliseconds(20), pollPartyMailbox: true, deadline.Token)
                == EntranceReadWakeReason.Deadline && !pendingRead.IsCompleted,
            "a scene deadline earlier than the mailbox tick still expires first without losing the pending read");
        await peerStream.WriteAsync(new byte[] { 0x3C }, deadline.Token);
        check(await pendingRead == 1 && buffer[0] == 0x3C,
            "timer expiry does not cancel or duplicate the next TCP read");
        check(EntranceSessionReadWait.ShouldPollPartyMailbox(true, true, false, false, 2)
                && !EntranceSessionReadWait.ShouldPollPartyMailbox(false, true, true, true, 2)
                && !EntranceSessionReadWait.ShouldPollPartyMailbox(true, false, true, true, 2),
            "town parties still poll while non-channel and characterless connections do not");

        await CheckPartyMembershipWakeAsync(check);
    }

    private static async Task CheckPartyMembershipWakeAsync(Action<bool, string> check)
    {
        // 真机 2026-10-24：申请者无队伍时已挂起无限读；队长接受后发出的 NOTI 9
        // 没有使读等待重新评估，选图邮件只能等下一条 CMD 161，实测延迟 22 秒。
        await using var harness = await PartyHarness.CreateAsync();
        var membershipWake = new EntranceSessionWakeSignal();
        var characterId = harness.IdOf("阿尔法");
        var leaderId = harness.IdOf("布拉沃");
        await harness.Service.AttachChannelAsync(
            "alice", "阿尔法", characterId, harness.UidOf("阿尔法"), harness.SessionIdOf("阿尔法"),
            packet =>
            {
                if (packet.Type == GameProtocolEngine.NotificationPacketType
                    && packet.ProtocolId == GameProtocolEngine.PartyInfoNotification)
                {
                    membershipWake.Pulse();
                }
                return ValueTask.CompletedTask;
            });

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token);
        using var receiver = await listener.AcceptTcpClientAsync(deadline.Token);
        var receiverStream = receiver.GetStream();
        var peerStream = peer.GetStream();
        var buffer = new byte[1];
        var pendingRead = receiverStream.ReadAsync(buffer, deadline.Token).AsTask();
        var membershipChanged = membershipWake.Capture();
        var soloPolling = EntranceSessionReadWait.ShouldPollPartyMailbox(
            isChannelPort: true, hasCharacter: true, isPartyDungeon: false, hasPendingMail: false,
            partyMemberCount: harness.Service.GetPartySlots(characterId)?.Length ?? 0);
        var soloWait = EntranceSessionReadWait.WaitAsync(
            pendingRead, Timeout.InfiniteTimeSpan, soloPolling, deadline.Token, membershipChanged);
        await Task.Delay(TimeSpan.FromMilliseconds(300), deadline.Token);
        check(!soloPolling && !soloWait.IsCompleted && !pendingRead.IsCompleted,
            "an unaffiliated session with a membership signal still has no periodic mailbox wakeups");

        // 使用真实 PartyService 建队，并由它实际发出的 NOTI 9 唤醒申请者；
        // TCP 对端没有发送任何字节，也不靠人造心跳让邮箱得以消费。
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        check(await soloWait == EntranceReadWakeReason.PartyMailTick && !pendingRead.IsCompleted,
            "a NOTI 9 from the real party-acceptance flow wakes an already blocked solo session without TCP input");

        var registry = new DungeonInstanceRegistry();
        membershipChanged = membershipWake.Capture();
        var partyPolling = EntranceSessionReadWait.ShouldPollPartyMailbox(
            isChannelPort: true, hasCharacter: true, isPartyDungeon: false, hasPendingMail: false,
            partyMemberCount: harness.Service.GetPartySlots(characterId)?.Length ?? 0);
        var partyWait = EntranceSessionReadWait.WaitAsync(
            pendingRead, Timeout.InfiniteTimeSpan, partyPolling, deadline.Token, membershipChanged);
        registry.PostSelectionRequest(characterId, leaderId);
        check(partyPolling && await partyWait == EntranceReadWakeReason.PartyMailTick
                && !pendingRead.IsCompleted
                && registry.TryTakeSelectionRequest(characterId, out var selectedLeader)
                && selectedLeader == leaderId,
            "after membership wakes the session, a future selection request is consumed before any client heartbeat");

        // capture 必须早于状态读取：即使 pulse 落在读取状态与开始 WhenAny 之间，
        // 捕获的老版本已经完成，不能因新版本被换入而把变化吞掉。
        membershipChanged = membershipWake.Capture();
        membershipWake.Pulse();
        check(await EntranceSessionReadWait.WaitAsync(
                    pendingRead, Timeout.InfiniteTimeSpan, pollPartyMailbox: false,
                    deadline.Token, membershipChanged) == EntranceReadWakeReason.PartyMailTick
                && !pendingRead.IsCompleted && !membershipWake.Capture().IsCompleted,
            "a membership pulse between state capture and wait registration is not lost and the next version is fresh");

        // 网络包与状态变化同时就绪时仍优先处理包；使用的始终是最初那一次读取。
        await peerStream.WriteAsync(new byte[] { 0x4D }, deadline.Token);
        await pendingRead;
        check(await EntranceSessionReadWait.WaitAsync(
                    pendingRead, Timeout.InfiniteTimeSpan, pollPartyMailbox: false,
                    deadline.Token, membershipChanged) == EntranceReadWakeReason.Packet
                && pendingRead.Result == 1 && buffer[0] == 0x4D,
            "a real TCP packet wins over a completed membership pulse and the original read receives it exactly once");

        pendingRead = receiverStream.ReadAsync(buffer, deadline.Token).AsTask();
        membershipChanged = membershipWake.Capture();
        check(await EntranceSessionReadWait.WaitAsync(
                    pendingRead, TimeSpan.FromMilliseconds(20), pollPartyMailbox: false,
                    deadline.Token, membershipChanged) == EntranceReadWakeReason.Deadline
                && !pendingRead.IsCompleted && !membershipChanged.IsCompleted,
            "an unpulsed membership signal does not prevent a scene deadline or cancel its pending TCP read");
        await peerStream.WriteAsync(new byte[] { 0x6E }, deadline.Token);
        check(await pendingRead == 1 && buffer[0] == 0x6E,
            "the socket remains usable after membership and deadline wakes with no duplicate read");
    }
}
