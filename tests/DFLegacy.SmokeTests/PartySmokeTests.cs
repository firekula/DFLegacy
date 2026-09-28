using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// 组队协议与服务的冒烟测试。包布局的权威说明见
/// .superpowers/sdd/party-protocol-recon.md（合并卷 §2）与设计稿 §2。
/// </summary>
internal static class PartySmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        CheckPartyInfoNotificationLayout(check);
        CheckPartyDisbandNotificationLayout(check);
        CheckWalkoutNotificationLayout(check);
        CheckRequestPartyNotificationLayout(check);
        CheckPartyCommandErrorLayout(check);
        CheckPartyRequestParsers(check);
        CheckPartyAcceptedNotificationLayout(check);
        CheckPartyRoster(check);
        await CheckPartyServiceAsync(check);
    }

    /// <summary>NOTI 8（组队）：u16 接受者 uid + u8 peerType=0 + u32 未用 → 7 字节（A 卷 §5）。</summary>
    private static void CheckPartyAcceptedNotificationLayout(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreatePartyAcceptedNotification(accepterUid: 88);
        check(packet.Type == GameProtocolEngine.NotificationPacketType
                && packet.ProtocolId == GameProtocolEngine.ResponsePeerNotification
                && packet.Payload.Length == 7
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(0, 2)) == 88
                && packet.Payload[2] == 0,
            "party NOTI 8 is 7 bytes with peerType 0 so the client prints its party chat line");
    }

    /// <summary>
    /// NOTI 9 body = u16 count, u16 partyId, u8 kind, u8 nameSel, u8 maxMembers,
    /// {u16 slotUid; u8 memberByte}×4（交错）, u8 leaderSlot —— 共 20 字节（合并卷 §2）。
    /// </summary>
    private static void CheckPartyInfoNotificationLayout(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreatePartyInfoNotification(
            partyId: 1000,
            nameSel: 3,
            maxMembers: 4,
            slotUids: [101, 102, 0xFFFF, 0],
            leaderSlot: 2);

        check(packet.Type == GameProtocolEngine.NotificationPacketType
                && packet.ProtocolId == GameProtocolEngine.PartyInfoNotification,
            "party NOTI 9 uses the notification channel and id 9");
        check(packet.Payload.Length == 20, "party NOTI 9 body is 20 bytes");
        check(BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(0, 2)) == 1,
            "party NOTI 9 carries exactly one record");
        check(BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(2, 2)) == 1000,
            "party NOTI 9 record uid is the stable party id");
        check(packet.Payload[4] == 0 && packet.Payload[5] == 3 && packet.Payload[6] == 4,
            "party NOTI 9 kind/nameSel/maxMembers occupy offsets 4..6");
        // 交错布局：slotUid 与 memberByte 交替，不是两个数组（合并卷 §2.1）。
        check(BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(7, 2)) == 101
                && packet.Payload[9] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(10, 2)) == 102
                && packet.Payload[12] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(13, 2)) == 0xFFFF
                && packet.Payload[15] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(16, 2)) == 0
                && packet.Payload[18] == 0,
            "party NOTI 9 interleaves slotUid/memberByte (0xFFFF empty, 0 absent)");
        check(packet.Payload[19] == 2, "party NOTI 9 leaderSlot is the last byte");
    }

    /// <summary>解散 = 1 条 kind=3 记录，不带名册：u16 count, u16 partyId, u8 kind → 5 字节。</summary>
    private static void CheckPartyDisbandNotificationLayout(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreatePartyDisbandNotification(partyId: 4242);
        check(packet.Type == GameProtocolEngine.NotificationPacketType
                && packet.ProtocolId == GameProtocolEngine.PartyInfoNotification
                && packet.Payload.Length == 5
                && packet.Payload[4] == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(2, 2)) == 4242,
            "party disband is a single kind=3 record of 5 bytes");
    }

    /// <summary>NOTI 10 = u8 槽位, u8 kind（0=被踢, 1=掉线）。</summary>
    private static void CheckWalkoutNotificationLayout(Action<bool, string> check)
    {
        var kicked = GameProtocolEngine.CreateWalkoutNotification(slot: 2, disconnected: false);
        var dropped = GameProtocolEngine.CreateWalkoutNotification(slot: 3, disconnected: true);
        check(kicked.ProtocolId == GameProtocolEngine.WalkoutNotification
                && kicked.Payload.Length == 2
                && kicked.Payload[0] == 2
                && kicked.Payload[1] == 0,
            "party NOTI 10 kick body is [slot, 0]");
        check(dropped.Payload[0] == 3 && dropped.Payload[1] == 1,
            "party NOTI 10 disconnect body is [slot, 1]");
    }

    /// <summary>NOTI 7（组队）比交易多两个 u16：u16 uid, u8 0, u32 复活币, u16 疲劳, u16 虚弱% → 11 字节。</summary>
    private static void CheckRequestPartyNotificationLayout(Action<bool, string> check)
    {
        var packet = BuildPartyRequestNotification();
        check(packet.Type == GameProtocolEngine.NotificationPacketType
                && packet.ProtocolId == GameProtocolEngine.RequestPeerNotification
                && packet.Payload.Length == 11
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(0, 2)) == 77
                && packet.Payload[2] == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(3, 4)) == 12
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(7, 2)) == 34
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(9, 2)) == 56,
            "party NOTI 7 is 11 bytes: uid, peerType 0, revive coins, fatigue, weakness%");
    }

    private static GameServerPacket BuildPartyRequestNotification() =>
        GameProtocolEngine.CreateRequestPartyNotification(
            requesterUid: 77,
            reviveCoins: 12,
            fatigue: 34,
            weaknessPercent: 56);

    /// <summary>命令通道失败回包 = [0, errorCode]（B 卷 §6.4）。</summary>
    private static void CheckPartyCommandErrorLayout(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreatePartyCommandError(
            GameProtocolEngine.WalkoutPartyMemberCommand,
            errorCode: 4);
        check(packet.Type == GameProtocolEngine.CommandPacketType
                && packet.ProtocolId == GameProtocolEngine.WalkoutPartyMemberCommand
                && packet.Payload.Length == 2
                && packet.Payload[0] == 0
                && packet.Payload[1] == 4,
            "party command failure is a two-byte [0, code] receipt");
    }

    private static void CheckPartyRequestParsers(Action<bool, string> check)
    {
        // CMD 10 body = [u16 seq][u16 uid][u8 peerType][u32 X]（与交易同前缀，靠 peerType 分流）。
        var party = new byte[] { 0x11, 0x22, 0xE8, 0x03, 0x00, 0x2A, 0x00, 0x00, 0x00 };
        check(GameProtocolEngine.TryReadPartyRequestPeer(party, out var targetUid, out var coins)
                && targetUid == 1000
                && coins == 42,
            "party CMD 10 parser reads uid and revive coins when peerType==0");

        var trade = new byte[] { 0x11, 0x22, 0xE8, 0x03, 0x01, 0x2A, 0x00, 0x00, 0x00 };
        check(!GameProtocolEngine.TryReadPartyRequestPeer(trade, out _, out _),
            "party CMD 10 parser rejects peerType==1 so the trade path keeps its own packet");

        check(!GameProtocolEngine.TryReadPartyRequestPeer(new byte[8], out _, out _),
            "party CMD 10 parser rejects a truncated body");

        var response = new byte[] { 0x11, 0x22, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00 };
        check(GameProtocolEngine.TryReadPartyResponsePeer(response, out var requesterUid)
                && requesterUid == 1000,
            "party CMD 11 parser reads the requester uid when peerType==0");

        // CMD 12 body = [u16 seq][u8 sel] + (sel==0 ? u32 长度+名字) + [u8 上限]。
        var withName = new byte[] { 0x11, 0x22, 0x00, 0x03, 0x00, 0x00, 0x00, 0x41, 0x42, 0x43, 0x04 };
        check(GameProtocolEngine.TryReadSetPartyInfo(
                withName,
                out var nameSel,
                out var nameBytes,
                out var maxMembers)
                && nameSel == 0
                && maxMembers == 4
                && nameBytes.Length == 3,
            "party CMD 12 parser reads a custom name only when sel==0");

        var preset = new byte[] { 0x11, 0x22, 0x05, 0x04 };
        check(GameProtocolEngine.TryReadSetPartyInfo(preset, out var presetSel, out var emptyName, out var presetMax)
                && presetSel == 5
                && presetMax == 4
                && emptyName.Length == 0,
            "party CMD 12 parser treats sel!=0 as a preset name with no trailing string");

        var badName = new byte[] { 0x11, 0x22, 0x00, 0x09, 0x00, 0x00, 0x00, 0x41, 0x42, 0x04 };
        check(!GameProtocolEngine.TryReadSetPartyInfo(badName, out _, out _, out _),
            "party CMD 12 parser rejects a name length that overruns the body");
    }

    /// <summary>
    /// 队伍模型不变量（设计稿 §4）：上限 4、槽位紧凑、**队长恒在 0 号槽**（换队长 = 新队长移到 0 号）、
    /// 槽位 uid 按**收包者所在区域**替换（同区域填真实 uid / 异区域填 0 / 空槽 0xFFFF）。
    /// </summary>
    private static void CheckPartyRoster(Action<bool, string> check)
    {
        var leader = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "队长",
            AreaUserId = 11,
            TownId = 1,
            AreaId = 0
        };
        var roster = new PartyRoster(partyId: 11, leader, maxMembers: 4, nameSelection: 0);
        check(roster.Count == 1 && roster.Leader == leader && PartyRoster.LeaderSlot == 0,
            "a fresh party has exactly the leader in slot 0");

        var sameArea = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "同区",
            AreaUserId = 12,
            TownId = 1,
            AreaId = 0
        };
        var otherArea = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "异区",
            AreaUserId = 13,
            TownId = 1,
            AreaId = 1
        };
        check(roster.TryAdd(sameArea) && roster.TryAdd(otherArea) && roster.Count == 3,
            "members join in order while the party has room");

        var slotsForHome = roster.SlotUids(member => member.IsIn(1, 0) ? member.AreaUserId : null);
        var slotsForElsewhere = roster.SlotUids(member => member.IsIn(1, 1) ? member.AreaUserId : null);
        check(slotsForHome.SequenceEqual(new ushort[] { 11, 12, 0, 0xFFFF }),
            "slots carry real uids for members the receiver can resolve");
        check(slotsForElsewhere.SequenceEqual(new ushort[] { 0, 0, 13, 0xFFFF }),
            "slots keep the global join order and substitute 0 for members the receiver cannot resolve");
        check(roster.SlotUids(_ => null).SequenceEqual(new ushort[] { 0, 0, 0, 0xFFFF }),
            "a receiver who cannot resolve anybody still gets four slots");

        var fourth = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "第四",
            AreaUserId = 14,
            TownId = 1,
            AreaId = 0
        };
        var fifth = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "第五",
            AreaUserId = 15,
            TownId = 1,
            AreaId = 0
        };
        check(roster.TryAdd(fourth) && roster.IsFull && !roster.TryAdd(fifth),
            "the fourth member fits and the fifth is refused (hard cap 4)");

        check(roster.Remove(leader.CharacterId) && roster.Leader == sameArea && roster.Count == 3,
            "removing the leader promotes the earliest remaining member into slot 0");
        check(roster.SlotUids(member => member.IsIn(1, 0) ? member.AreaUserId : null)
                .SequenceEqual(new ushort[] { 12, 0, 14, 0xFFFF }),
            "after the leader leaves the roster stays compact");

        check(!roster.Remove(Guid.NewGuid()), "removing a character that is not in the party changes nothing");

        roster.SetInfo(nameSelection: 6, maxMembers: 2);
        check(roster.NameSelection == 6 && roster.MaxMembers == 3 && roster.IsFull,
            "CMD 12 updates the preset name index, and the cap never drops below the current headcount");

        check(roster.UpdateLocation(sameArea.CharacterId, townId: 2, areaId: 3, areaUserId: 99),
            "a member's area change updates its stored location");
        check(roster.SlotUids(member => member.IsIn(2, 3) ? member.AreaUserId : null)
                .SequenceEqual(new ushort[] { 99, 0, 0, 0xFFFF }),
            "the moved member is resolvable only in its new area");

        check(PartyRoster.TryAllocatePartyId([11, 12], preferred: 12, out var allocated)
                && allocated != 0
                && allocated != 11
                && allocated != 12,
            "party id allocation skips a preferred id that is already taken (and any other taken id)");
        check(PartyRoster.TryAllocatePartyId([1, 2, 3], preferred: 0, out var scanned)
                && scanned is not 0 and not 1 and not 2 and not 3,
            "party id allocation scans for a free value when the preferred one is unusable");
    }

    private static async Task CheckPartyServiceAsync(Action<bool, string> check)
    {
        await CheckInviteFlowAsync(check);
        await CheckApplyToJoinFlowAsync(check);
        await CheckRequestRefusalsAsync(check);
        await CheckResponseRefusalShapeAsync(check);
        await CheckDuplicateAcceptIsInertAsync(check);
        await CheckTimeoutAndBlacklistAsync(check);
        await CheckLeaveAndKickAsync(check);
        await CheckSetPartyInfoAsync(check);
    }

    /// <summary>
    /// 接受方也会失败（双方在等待期间各自建了队）——失败包的形状必须与请求方**完全一致**：
    /// `CMD 11 [0,100,0]`。CMD 11 的客户端分支按 `body[1]` 当 peerType 分流，填别的 errorCode
    /// （如 19）是未知 peerType → 接受方什么都看不到（设计稿 §2.1 的"三条必须遵守的形状"）。
    /// </summary>
    private static async Task CheckResponseRefusalShapeAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();

        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        // 等待期间双方各自用 CMD 12 建了 1 人队 → 接受时"双方都有队伍" → 接受方收失败。
        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("阿尔法"), nameSelection: 5, nameBytes: [], maxMembers: 4, CancellationToken.None);
        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("布拉沃"), nameSelection: 5, nameBytes: [], maxMembers: 4, CancellationToken.None);
        harness.ClearAll();

        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        check(harness.Of("布拉沃").Count == 1
                && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("布拉沃")[0].Payload.SequenceEqual(new byte[] { 0, 100, 0 })
                && harness.Of("阿尔法").Count == 0,
            "the accepter's refusal keeps the CMD 11 [0,100,0] shape instead of an unknown peerType that the client drops");
    }

    /// <summary>
    /// 重复接受必须幂等：同一份待决请求被接受两次（第二次与第一次并发）只产出**一套**包 ——
    /// 每个成员一条名册 + 请求方一条 NOTI 8，绝不多发一条失败回执（评审修复）。
    /// 说明：`_pending.TryRemove` 的"读到了 / 还没摘掉"那个窗口里没有 await，冒烟用例无法在不加生产钩子的
    /// 前提下把两次接受真的压进窗口里；这里断言的是**可观测契约**（第二次接受不产出任何包），
    /// 守卫本身（检查 TryRemove 的返回值）靠代码可读性 + 评审保证。
    /// </summary>
    private static async Task CheckDuplicateAcceptIsInertAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        harness.ClearAll();

        // 不 await 第一发就发第二发：harness 的发包会 await Task.Yield()，所以第一次接受会停在
        // "给第一个成员发名册"处，第二次接受是**真的与它重叠**（而不是被同步跑完）。
        var first = harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);
        var second = harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);
        await Task.WhenAll(first, second);

        check(harness.Of("布拉沃").Count(packet =>
                        packet.ProtocolId == GameProtocolEngine.PartyInfoNotification) == 1
                && harness.Of("阿尔法").Count(packet =>
                        packet.ProtocolId == GameProtocolEngine.PartyInfoNotification) == 1
                && harness.Of("阿尔法").Count(packet =>
                        packet.ProtocolId == GameProtocolEngine.ResponsePeerNotification) == 1
                && harness.Of("布拉沃").All(packet =>
                        packet.ProtocolId != GameProtocolEngine.ResponsePeerCommand)
                && harness.Of("阿尔法").All(packet =>
                        packet.ProtocolId != GameProtocolEngine.ResponsePeerCommand),
            "a second (concurrent) accept is inert: one roster each plus one NOTI 8, never a failure receipt");
    }

    /// <summary>邀请：A 邀请 B（都无队伍）→ B 收 NOTI 7 → B 接受 → 双方各收一条 NOTI 9，A 另收 NOTI 8。</summary>
    private static async Task CheckInviteFlowAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();

        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"),
            harness.UidOf("布拉沃"),
            PartyHarness.DefaultInfo,
            CancellationToken.None);

        var invitation = harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification);
        check(invitation is { Payload.Length: 11 }
                && invitation.Payload[2] == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(invitation.Payload.AsSpan(3, 4)) == 7
                && harness.Of("阿尔法").Count == 0,
            "an invite reaches the target as an 11-byte NOTI 7 and the requester sees nothing yet");

        harness.ClearAll();
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        var alphaRoster = harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification);
        var bravoRoster = harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification);
        check(alphaRoster is { Payload.Length: 20 } && bravoRoster is { Payload.Length: 20 },
            "both sides receive a 20-byte NOTI 9 roster");
        check(BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster!.Payload.AsSpan(2, 2)) == harness.UidOf("阿尔法"),
            "the roster uid is the stable party id (the requester's uid), identical for every receiver");
        check(BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃")
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(13, 2)) == 0xFFFF
                && bravoRoster.Payload[19] == 0,
            "the invited member sees leader then itself in slots 0..1 and slot 0 is the leader");
        check(harness.Of("阿尔法").Any(packet =>
                packet.ProtocolId == GameProtocolEngine.ResponsePeerNotification && packet.Payload.Length == 7),
            "the requester also gets the 7-byte NOTI 8 chat line");
        check(harness.Of("布拉沃").All(packet =>
                packet.ProtocolId != GameProtocolEngine.ResponsePeerNotification),
            "the accepter does not get a NOTI 8 for its own acceptance");
    }

    /// <summary>"申请加入"：目标（队长）已有队伍、请求者无队伍 → 接受后**请求者并入目标队伍**（设计稿 §5.1）。</summary>
    private static async Task CheckApplyToJoinFlowAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("布拉沃"), harness.UidOf("查理"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("查理"), harness.UidOf("布拉沃"), CancellationToken.None);
        harness.ClearAll();

        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is not null,
            "applying to a leader is the same CMD 10/NOTI 7 pair as an invite");

        harness.ClearAll();
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        var roster = harness.FirstOf("查理", GameProtocolEngine.PartyInfoNotification);
        check(roster is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(2, 2)) == harness.UidOf("布拉沃"),
            "the party keeps its id and its leader after the applicant joins");
        check(BinaryPrimitives.ReadUInt16LittleEndian(roster!.Payload.AsSpan(7, 2)) == harness.UidOf("布拉沃")
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(10, 2)) == harness.UidOf("查理")
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(13, 2)) == harness.UidOf("阿尔法"),
            "the applicant is appended in join order behind the existing members");
    }

    /// <summary>拒绝路径：都只回请求方，不打扰目标。</summary>
    private static async Task CheckRequestRefusalsAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();

        // ① 指向自己：客户端发不出（uid 是本区域的别人），服务端按"目标找不到"处理。
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("阿尔法"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload.SequenceEqual(new byte[] { 0, 100, 0 })
                && harness.Of("布拉沃").Count == 0,
            "a request aimed at yourself fails with CMD 11 [0,100,0] and never reaches anybody");

        // ② 限流：3 秒内的第二次请求被静默丢弃。
        harness.ClearAll();
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 1,
            "a second invite inside the 3 second window is dropped by the rate limiter");

        // ③ 目标已有待决请求：给新来者回失败，不给他再弹一个窗。
        // 只清申请人的收件箱：布拉沃那条窗口（②留下的待决请求）正是"没有第二个窗"的判据。
        harness.Clear("查理");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("查理"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("查理"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.Of("查理").Count == 1
                && harness.Of("查理")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("布拉沃").Count == 1,
            "a target that already has a pending request refuses the newcomer without a second window");
    }

    /// <summary>踢人 / 退队 / 掉线：NOTI 10 必须早于 NOTI 9；最后一人退队走 kind=3。</summary>
    private static async Task CheckLeaveAndKickAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);
        harness.ClearAll();

        await harness.Service.HandleKickAsync(harness.IdOf("阿尔法"), slot: 1, CancellationToken.None);
        var bravoPackets = harness.Of("布拉沃");
        var walkoutIndex = bravoPackets.FindIndex(packet =>
            packet.ProtocolId == GameProtocolEngine.WalkoutNotification);
        var rosterIndex = bravoPackets.FindIndex(packet =>
            packet.ProtocolId == GameProtocolEngine.PartyInfoNotification);
        check(walkoutIndex >= 0
                && rosterIndex > walkoutIndex
                && bravoPackets[walkoutIndex].Payload.SequenceEqual(new byte[] { 1, 0 }),
            "NOTI 10 [slot, 0] precedes the roster refresh that clears that slot");
        check(bravoPackets[rosterIndex].Payload[13] == 0xFF && bravoPackets[rosterIndex].Payload[14] == 0xFF,
            "the kicked member's slot becomes 0xFFFF on the roster it receives");

        harness.ClearAll();
        await harness.Service.HandleLeaveAsync(harness.IdOf("阿尔法"), CancellationToken.None);
        var disbanded = harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification);
        check(disbanded is { Payload.Length: 5 } && disbanded.Payload[4] == 3,
            "the last member leaving dissolves the party with a 5-byte kind=3 record");

        harness.ClearAll();
        // 限流是真实时钟的 3 秒窗口，而测试是瞬时的：重发前先清掉本测试早先那次邀请的时间戳。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);
        harness.ClearAll();
        await harness.Service.DetachChannelAsync(
            harness.IdOf("布拉沃"), harness.SessionIdOf("布拉沃"), CancellationToken.None);
        check(harness.Of("阿尔法").Any(packet =>
                packet.ProtocolId == GameProtocolEngine.WalkoutNotification
                && packet.Payload.SequenceEqual(new byte[] { 1, 1 })),
            "a disconnect tells the remaining member with NOTI 10 [slot, 1]");
        check(harness.Of("布拉沃").Count == 0,
            "the disconnected session receives nothing");
    }

    /// <summary>
    /// 超时与拉黑（设计稿 §5.1/§5.2）：客户端"拒绝"不发包 → 超时是唯一能告诉请求方"对方没答应"的途径；
    /// 被拉黑时目标完全无感知，但发起方仍会拿到**同形**的失败提示（否则"立刻能重发"就成了拉黑的旁证）。
    /// </summary>
    private static async Task CheckTimeoutAndBlacklistAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        harness.ClearAll();

        harness.Service.ExpirePendingForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload.SequenceEqual(new byte[] { 0, 100, 0 }),
            "an unanswered party request ends with CMD 11 [0,100,0] for the requester when the window expires");
        check(harness.Of("布拉沃").Count == 0,
            "the timeout tells nobody else");

        // 拉黑：bobby（布拉沃的账号）把 阿尔法 拉黑 → 目标零感知、发起方仍收到同形失败。
        harness.ClearAll();
        check(await harness.Store.AddBlacklistAsync("bobby", "阿尔法", CancellationToken.None)
                == BlacklistUpdateFailure.None,
            "the harness can blacklist the requester on the target's account");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 0,
            "a blacklisted requester's invitation never reaches the target (no NOTI 7)");
        harness.Service.ExpirePendingForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].Payload.SequenceEqual(new byte[] { 0, 100, 0 }),
            "the blacklisted requester sees exactly the same failure as a plain unanswered request");
    }

    private static async Task CheckSetPartyInfoAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();

        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("阿尔法"), nameSelection: 5, nameBytes: [], maxMembers: 4, CancellationToken.None);
        var roster = harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification);
        check(roster is not null
                && roster.Payload[5] == 5
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(10, 2)) == 0xFFFF,
            "CMD 12 with no party creates a one-member party led by the requester");

        harness.ClearAll();
        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("阿尔法"), nameSelection: 0, nameBytes: [0x41, 0x42], maxMembers: 2,
            CancellationToken.None);
        var resized = harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification);
        check(resized is not null && resized.Payload[5] == 0 && resized.Payload[6] == 2,
            "CMD 12 updates the preset name index and the member cap");

        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), CancellationToken.None);
        harness.ClearAll();
        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("布拉沃"), nameSelection: 3, nameBytes: [], maxMembers: 4, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 0 && harness.Of("布拉沃").Count == 0,
            "a non-leader CMD 12 is ignored");
    }
}

/// <summary>
/// 组队服务测试台：真 store（临时目录）+ 真 TownSessionDirectory + 真 PartyService，
/// 只把发包换成写进 List（沿用 TradeHarness 的做法）；三个角色都在 1/0 号城镇区域。
/// </summary>
internal sealed class PartyHarness : IAsyncDisposable
{
    /// <summary>
    /// CMD 10 里客户端自报的三个显示值。复活币取 7 而不是 0：这个值会原样进 NOTI 7 的正文，
    /// 非零才能验证"服务端把它透传给了被邀请者"（0 的断言在任何实现下都成立，等于没测）。
    /// </summary>
    public static readonly PartyRequestInfo DefaultInfo = new(ReviveCoins: 7, Fatigue: 0, WeaknessPercent: 0);

    private readonly Dictionary<string, List<GameServerPacket>> _packets = new();
    private readonly Dictionary<string, (Guid Id, string Account)> _characters = new();
    private readonly Dictionary<string, Guid> _sessionIds = new();
    private readonly Dictionary<string, ushort> _uids = new();

    private PartyHarness(string root, ServerOptions options, JsonGameStore store, TownSessionDirectory directory)
    {
        Root = root;
        Options = options;
        Store = store;
        Directory = directory;
    }

    public string Root { get; }
    public ServerOptions Options { get; }
    public JsonGameStore Store { get; }
    public TownSessionDirectory Directory { get; }
    public PartyService Service { get; private set; } = null!;

    public Guid IdOf(string characterName) => _characters[characterName].Id;
    public ushort UidOf(string characterName) => _uids[characterName];
    public Guid SessionIdOf(string characterName) => _sessionIds[characterName];

    /// <summary>返回 List（而不是 IReadOnlyList）以便用例用 FindIndex 断言包的先后顺序。</summary>
    public List<GameServerPacket> Of(string characterName)
    {
        lock (_packets[characterName])
        {
            return [.. _packets[characterName]];
        }
    }

    public GameServerPacket? FirstOf(string characterName, byte protocolId) =>
        Of(characterName).FirstOrDefault(packet => packet.ProtocolId == protocolId);

    public void Clear(string characterName)
    {
        lock (_packets[characterName])
        {
            _packets[characterName].Clear();
        }
    }

    public void ClearAll()
    {
        foreach (var name in _packets.Keys)
        {
            Clear(name);
        }
    }

    public static async Task<PartyHarness> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dflegacy-party-harness-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        var options = new ServerOptions
        {
            DataPath = Path.Combine(root, "state.json"),
            ScriptPvfPath = string.Empty,
            SkillScriptPath = root
        };
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);
        var harness = new PartyHarness(root, options, store, directory);

        var fixtures = new (string Account, string Name)[]
        {
            ("alice", "阿尔法"),
            ("bobby", "布拉沃"),
            ("carol", "查理")
        };
        foreach (var (account, name) in fixtures)
        {
            await store.CreateAccountAsync(
                new CreateAccountRequest(account, "password", OicqCode: "123456"),
                CancellationToken.None);
            var created = (await store.CreateCharacterAsync(
                account,
                new CreateCharacterRequest(name),
                CancellationToken.None)).Character!;
            harness._packets[name] = [];
            harness._characters[name] = (created.Id, account);
            harness._sessionIds[name] = Guid.NewGuid();
        }

        harness.Service = new PartyService(
            store,
            directory,
            TimeProvider.System,
            NullLogger<PartyService>.Instance);
        ushort uid = 1;
        foreach (var (account, name) in fixtures)
        {
            harness._uids[name] = uid++;
            await harness.Service.AttachChannelAsync(
                account,
                name,
                harness._characters[name].Id,
                harness._uids[name],
                harness._sessionIds[name],
                harness.Record(name),
                CancellationToken.None);
            // 三个角色都进 1/0：组队请求要求请求者与目标同城镇同区域（设计稿 §5.1）。
            await directory.EnterAsync(
                new TownEntryRequest(
                    TownId: 1,
                    AreaId: 0,
                    AreaUserId: harness._uids[name],
                    CharacterId: harness._characters[name].Id,
                    CharacterNo: 100u + harness._uids[name],
                    CharacterNameBytes: Encoding.ASCII.GetBytes(name),
                    DisplayName: name,
                    Peer: null,
                    X: 10,
                    Y: 10,
                    Direction: 0,
                    SendAsync: (packet, _) => harness.Record(name)(packet)),
                CancellationToken.None);
        }

        return harness;
    }

    /// <summary>
    /// 记录发包。刻意 <c>await Task.Yield()</c>（真机的 socket 写本来就是异步的）：只有让 handler 在
    /// 发包处真的让出线程，"重复/并发接受"这类竞态用例才会交错到第二个 handler，而不是被同步跑完。
    /// </summary>
    private Func<GameServerPacket, ValueTask> Record(string characterName) =>
        async packet =>
        {
            await Task.Yield();
            lock (_packets[characterName])
            {
                _packets[characterName].Add(packet);
            }
        };

    public async ValueTask DisposeAsync()
    {
        await Service.StopAsync(CancellationToken.None);
        if (System.IO.Directory.Exists(Root))
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
    }
}
