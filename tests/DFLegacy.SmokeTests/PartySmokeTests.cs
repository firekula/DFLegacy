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
    /// 队伍模型不变量（设计稿 §4）：上限 4、**槽位下标稳定（成员从不搬动）**、队长由 `LeaderSlot`
    /// 表达（建队时 0，队长离开后就地继任给最小占用槽）、槽位 uid 按**收包者所在区域**替换
    /// （同区域填真实 uid / 异区域填 0 / 空槽 0xFFFF）。
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
        check(roster.Count == 1 && roster.Leader == leader && roster.LeaderSlot == 0,
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

        // ★ 回归（"幽灵队伍"根因）：队长退队**不得搬动**任何成员 —— 只清空他自己的槽、就地继任。
        // 客户端逐槽 clear-then-set：搬动会让被搬者的 user+0xFC 链接在"他原先那一格被写成 0xFFFF"
        // 时被清掉，客户端误判"我离队了"（设计稿 §11）。这里断言槽下标与槽位 uid 都不动。
        check(roster.Remove(leader.CharacterId) && roster.Count == 3
                && roster.LeaderSlot == 1 && roster.Leader == sameArea
                && !roster.TryGetSlot(leader.CharacterId, out _)
                && roster.TryGetSlot(sameArea.CharacterId, out var sameAreaSlot) && sameAreaSlot == 1
                && roster.TryGetSlot(otherArea.CharacterId, out var otherAreaSlot) && otherAreaSlot == 2
                && roster.TryGetSlot(fourth.CharacterId, out var fourthSlot) && fourthSlot == 3
                && roster.TryGetSlot(slot: 1, out var atSlotOne) && atSlotOne == sameArea,
            "removing the leader keeps every remaining member in his own slot (no compaction) and promotes the lowest occupied slot");
        check(roster.SlotUids(member => member.IsIn(1, 0) ? member.AreaUserId : null)
                .SequenceEqual(new ushort[] { 0xFFFF, 12, 0, 14 }),
            "after the leader leaves only his slot becomes 0xFFFF; the others keep their slot uids");

        check(!roster.Remove(Guid.NewGuid()), "removing a character that is not in the party changes nothing");

        roster.SetInfo(nameSelection: 6, maxMembers: 2);
        check(roster.NameSelection == 6 && roster.MaxMembers == 3 && roster.IsFull,
            "CMD 12 updates the preset name index, and the cap never drops below the current headcount");

        check(roster.UpdateLocation(sameArea.CharacterId, townId: 2, areaId: 3, areaUserId: 99),
            "a member's area change updates its stored location");
        check(roster.SlotUids(member => member.IsIn(2, 3) ? member.AreaUserId : null)
                .SequenceEqual(new ushort[] { 0xFFFF, 99, 0, 0 }),
            "the moved member is resolvable only in its new area (and stays in his own slot)");

        // ★ 回归（真机"幽灵队伍"的最小复现形状）：三人队 [甲@0, 乙@1, 丙@2] 移除队长甲 ——
        // 乙、丙的槽下标原地不动（不紧凑、不搬到槽 0），空出的是槽 0（0xFFFF），队长槽 = 1。
        var trioLeader = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "甲",
            AreaUserId = 21,
            TownId = 1,
            AreaId = 0
        };
        var trioSecond = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "乙",
            AreaUserId = 22,
            TownId = 1,
            AreaId = 0
        };
        var trioThird = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "丙",
            AreaUserId = 23,
            TownId = 1,
            AreaId = 0
        };
        var trio = new PartyRoster(partyId: 12, trioLeader, maxMembers: 4, nameSelection: 0);
        check(trio.TryAdd(trioSecond) && trio.TryAdd(trioThird),
            "the three-member regression party fills slots 0..2 in join order");
        check(trio.Remove(trioLeader.CharacterId) && trio.Count == 2
                && trio.LeaderSlot == 1 && trio.Leader == trioSecond
                && !trio.TryGetSlot(trioLeader.CharacterId, out _)
                && trio.TryGetSlot(trioSecond.CharacterId, out var trioSecondSlot) && trioSecondSlot == 1
                && trio.TryGetSlot(trioThird.CharacterId, out var trioThirdSlot) && trioThirdSlot == 2
                && trio.SlotUids(member => member.AreaUserId).SequenceEqual(new ushort[] { 0xFFFF, 22, 23, 0xFFFF }),
            "regression (ghost party): losing the leader blanks only slot 0; the other two keep their slot indexes and uids");
        var trioFourth = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "丁",
            AreaUserId = 24,
            TownId = 1,
            AreaId = 0
        };
        check(trio.TryAdd(trioFourth)
                && trio.TryGetSlot(trioFourth.CharacterId, out var trioFourthSlot) && trioFourthSlot == 0
                && trio.TryGetSlot(trioSecond.CharacterId, out var trioSecondAgain) && trioSecondAgain == 1
                && trio.TryGetSlot(trioThird.CharacterId, out var trioThirdAgain) && trioThirdAgain == 2
                && trio.LeaderSlot == 1 && trio.Leader == trioSecond,
            "a newcomer takes the lowest free slot without moving anybody (and the leader stays put)");

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
        await CheckAccepterJoinsRequesterPartyAsync(check);
        await CheckRequestRefusalsAsync(check);
        await CheckPartyFullRefusalAsync(check);
        await CheckResponseRefusalShapeAsync(check);
        await CheckDuplicateAcceptIsInertAsync(check);
        await CheckTimeoutAndBlacklistAsync(check);
        await CheckLeaveAndKickAsync(check);
        await CheckLeaderLeaveKeepsMemberSlotsAsync(check);
        await CheckKickFromNonZeroLeaderSlotAsync(check);
        await CheckSetPartyInfoAsync(check);
        await CheckOutboundNameSelectionAsync(check);
        await CheckAreaBroadcastAsync(check);
        await CheckRosterResettleAsync(check);
        await CheckAreaChangeResettleAsync(check);
    }

    /// <summary>某个角色收件箱里的 NOTI 9 列表（断言"补播了一份逐字节相同的名册"用）。</summary>
    private static List<GameServerPacket> RostersOf(PartyHarness harness, string characterName) =>
        harness.Of(characterName)
            .Where(packet => packet.ProtocolId == GameProtocolEngine.PartyInfoNotification)
            .ToList();

    /// <summary>
    /// 「队伍信息」列表与「推荐伙伴」的过滤都靠"附近玩家身上挂着队伍"这条链接（C 卷 §2.2），
    /// 所以名册必须广播给**同区域的所有客户端**（含不在这支队伍里的旁观者），并在进区时补发。
    /// </summary>
    private static async Task CheckAreaBroadcastAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        var bystander = harness.FirstOf("德尔塔", GameProtocolEngine.PartyInfoNotification);
        check(bystander is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(bystander.Payload.AsSpan(2, 2)) == harness.UidOf("阿尔法"),
            "a bystander in the same area learns the new party (same party id as the members)");
        check(harness.Of("艾琳").Count == 0,
            "a client in another area of the same town gets no party roster");

        // 换区：旧区域版本里离场成员变 0，新区域版本里他是真实 uid（设计稿 §2.2.2）。
        harness.ClearAll();
        await harness.MoveToAsync("布拉沃", townId: 1, areaId: 1, uid: 21);
        var oldAreaRoster = harness.FirstOf("德尔塔", GameProtocolEngine.PartyInfoNotification);
        check(oldAreaRoster is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(oldAreaRoster.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(oldAreaRoster.Payload.AsSpan(10, 2)) == 0,
            "the roster sent to the area the member left substitutes 0 for that member");
        var newAreaRoster = harness.FirstOf("艾琳", GameProtocolEngine.PartyInfoNotification);
        check(newAreaRoster is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(newAreaRoster.Payload.AsSpan(7, 2)) == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(newAreaRoster.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃"),
            "the roster sent to the area the member entered carries its real uid there");

        // 进区补发：队伍先建好，旁观者后进这个区域，也要看得见。
        harness.ClearAll();
        await harness.MoveToAsync("查理", townId: 1, areaId: 0, uid: 31);
        check(harness.FirstOf("查理", GameProtocolEngine.PartyInfoNotification) is not null,
            "a client entering the area is back-filled with the rosters of the parties living there");

        // 离区（进副本）：**不退队**（设计稿 §4.4），只重播名册——目录里已经没有他，
        // 于是"曾通知过的区域"收到的那份里他那一格变成 0（旁观者不会把 uid 解析到别人身上）。
        harness.ClearAll();
        await harness.LeaveTownAsync("布拉沃");
        var afterLeaveArea = harness.FirstOf("德尔塔", GameProtocolEngine.PartyInfoNotification);
        check(afterLeaveArea is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(afterLeaveArea.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(afterLeaveArea.Payload.AsSpan(10, 2)) == 0,
            "leaving town for a dungeon blanks the member's slot in the area he left");
        // 他自己那份走的是另一条"填 0"分支：收包者不在任何城镇区域 → 除自己外全部 0
        // （Task 3 评审留下的覆盖缺口：异区/不可解析的成员必须填 0，不能填真实 uid，设计稿 §2.2.2）。
        var offTownCopy = harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification);
        check(offTownCopy is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(offTownCopy.Payload.AsSpan(7, 2)) == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(offTownCopy.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃"),
            "a member outside every town area still gets his own copy: only himself is resolvable");
        // 先断言两个收件箱**非空**再 All：空列表上的 All 恒真，等于没测（评审修复）。
        var alphaAfterLeave = harness.Of("阿尔法");
        var bravoAfterLeave = harness.Of("布拉沃");
        check(alphaAfterLeave.Count > 0
                && bravoAfterLeave.Count > 0
                && alphaAfterLeave.All(packet =>
                    packet.ProtocolId != GameProtocolEngine.WalkoutNotification)
                && bravoAfterLeave.All(packet =>
                    packet.ProtocolId != GameProtocolEngine.WalkoutNotification),
            "leaving town does not drop the member from the party (both members got the roster refresh, nobody a NOTI 10)");

        // 解散：旁观者也要收到 kind=3，否则列表里会留一支永远点不开的队伍。
        harness.ClearAll();
        await harness.Service.HandleLeaveAsync(harness.IdOf("阿尔法"), CancellationToken.None);
        await harness.Service.HandleLeaveAsync(harness.IdOf("布拉沃"), CancellationToken.None);
        check(harness.Of("德尔塔").Any(packet =>
                packet.ProtocolId == GameProtocolEngine.PartyInfoNotification
                && packet.Payload.Length == 5
                && packet.Payload[4] == 3),
            "a disband reaches the bystanders (every area the party ever announced itself in) as a kind=3 record");
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
    /// 每个成员各一份名册（Task 4 起同区成员是两份**相同**的：一份定向、一份区域广播）+ 请求方一条 NOTI 8，
    /// 绝不多发一条失败回执（评审修复）。
    /// 说明：`_pending.TryRemove` 的"读到了 / 还没摘掉"那个窗口里没有 await，冒烟用例无法在不加生产钩子的
    /// 前提下把两次接受真的压进窗口里；这里断言的是**可观测契约**（第二次接受不产出任何包），
    /// 守卫本身（检查 TryRemove 的返回值）靠代码可读性 + 评审保证。第二发若没被守住，这里会变成四份名册
    /// 或冒出一条失败回执。
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

        var alphaRosters = harness.Of("阿尔法")
            .Where(packet => packet.ProtocolId == GameProtocolEngine.PartyInfoNotification)
            .ToList();
        var bravoRosters = harness.Of("布拉沃")
            .Where(packet => packet.ProtocolId == GameProtocolEngine.PartyInfoNotification)
            .ToList();
        check(alphaRosters.Count == 2
                && bravoRosters.Count == 2
                && alphaRosters.All(packet => packet.Payload.SequenceEqual(alphaRosters[0].Payload))
                && bravoRosters.All(packet => packet.Payload.SequenceEqual(bravoRosters[0].Payload))
                && harness.Of("阿尔法").Count(packet =>
                        packet.ProtocolId == GameProtocolEngine.ResponsePeerNotification) == 1
                && harness.Of("布拉沃").All(packet =>
                        packet.ProtocolId != GameProtocolEngine.ResponsePeerCommand)
                && harness.Of("阿尔法").All(packet =>
                        packet.ProtocolId != GameProtocolEngine.ResponsePeerCommand),
            "a second (concurrent) accept is inert: one roster set each plus one NOTI 8, never a failure receipt");
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

    /// <summary>
    /// 请求者有队伍、目标无队伍 → 接受后**目标并入请求者的队伍**（设计稿 §5.1 的第三种情形；
    /// 已有用例 `CheckApplyToJoinFlowAsync` 覆盖的是反过来的"请求者并入目标队伍"）。
    /// </summary>
    private static async Task CheckAccepterJoinsRequesterPartyAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("阿尔法"), nameSelection: 5, nameBytes: [], maxMembers: 4, CancellationToken.None);
        harness.ClearAll();

        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is not null,
            "a party member who invites an unaffiliated player produces the standard NOTI 7 window");

        harness.ClearAll();
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        var roster = harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification);
        check(roster is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(2, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃")
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(13, 2)) == 0xFFFF
                && roster.Payload[19] == 0,
            "the accepter joins the requester's party: party id and leader slot stay with the requester");
        check(harness.Of("阿尔法").Any(packet =>
                packet.ProtocolId == GameProtocolEngine.ResponsePeerNotification && packet.Payload.Length == 7),
            "the requester gets the NOTI 8 chat line when the accepter joins his party");
    }

    /// <summary>
    /// 队满：目标队伍已达上限（这里用 `CMD 12` 把上限改成 3，凑满 3 人）时再被邀请 →
    /// 请求方立刻收 `CMD 11 [0,100,0]`，目标**不**收 `NOTI 7`（设计稿 §5.1 的"其余校验"：
    /// 不能给目标弹一个接受后必定失败的窗）、也不收任何包。
    /// </summary>
    private static async Task CheckPartyFullRefusalAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        // 阿尔法 建 3 人上限的队，再让 布拉沃/查理 申请加入 → 满员。
        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("阿尔法"), nameSelection: 5, nameBytes: [], maxMembers: 3, CancellationToken.None);
        foreach (var applicant in new[] { "布拉沃", "查理" })
        {
            await harness.Service.HandleRequestAsync(
                harness.IdOf(applicant),
                harness.UidOf("阿尔法"),
                PartyHarness.DefaultInfo,
                CancellationToken.None);
            await harness.Service.HandleResponseAsync(
                harness.IdOf("阿尔法"),
                harness.UidOf(applicant),
                CancellationToken.None);
        }

        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("德尔塔"), harness.UidOf("阿尔法"), PartyHarness.DefaultInfo, CancellationToken.None);

        check(harness.Of("德尔塔").Count == 1
                && harness.Of("德尔塔")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("德尔塔")[0].Payload.SequenceEqual(new byte[] { 0, 100, 0 }),
            "inviting a full party fails the requester immediately with CMD 11 [0,100,0]");
        check(harness.Of("阿尔法").Count == 0,
            "the full party's leader gets no NOTI 7 window (and no packet at all) for a request that cannot succeed");
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
        // 断线是**整个会话**结束：城镇目录里也不再有人。不先退目录的话，区域广播还会经目录通道投给他，
        // 而真机上那条通道已经死了（目录会把坏通道按掉线剔除），这里就测不到"拿不到任何包"。
        await harness.LeaveTownAsync("布拉沃");
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
    /// ★ 回归（真机"幽灵队伍"）：三人队 [阿尔法@0, 布拉沃@1, 查理@2]，**队长退队后**发给剩余成员的
    /// NOTI 9 里，布拉沃的 uid 必须**仍在槽 1**（槽 0 变 0xFFFF、leaderSlot=1）——他**不能**被搬到槽 0。
    /// 客户端 `SetMember`（`0x4FE690`）逐槽 clear-then-set：一旦被搬动，处理他原先那一格（写成 0xFFFF）
    /// 时会把他刚挂上的 `user+0xFC` 清成 0，客户端误判"我离队了"→ HUD 消失但服务端仍认为他在队里。
    /// 本用例在实现回退成"紧凑排列"时会变红（断言"uid 没被搬到槽 0"）。
    /// </summary>
    private static async Task CheckLeaderLeaveKeepsMemberSlotsAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        // 三人队：阿尔法（队长）先邀布拉沃、再邀查理（都在 1/0 同区域，接受即入队）。
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);
        // 限流是真实时钟的 3 秒窗口，而测试是瞬时的：同一个人连邀两次前先清掉上次的时间戳。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("查理"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("查理"), harness.UidOf("阿尔法"), CancellationToken.None);
        harness.ClearAll();

        await harness.Service.HandleLeaveAsync(harness.IdOf("阿尔法"), CancellationToken.None);

        var bravoRoster = harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification);
        check(bravoRoster is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(2, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(7, 2)) == 0xFFFF
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃")
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(13, 2)) == harness.UidOf("查理")
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoRoster.Payload.AsSpan(16, 2)) == 0xFFFF
                && bravoRoster.Payload[19] == 1,
            "regression: after the leader leaves, the remaining member's uid stays in his own slot (never compacted into slot 0) and leaderSlot points at slot 1");

        // 查理那份同理：只看槽位（uid 与布拉沃相同，因为同区域）。
        var charlieRoster = harness.FirstOf("查理", GameProtocolEngine.PartyInfoNotification);
        check(charlieRoster is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(charlieRoster.Payload.AsSpan(7, 2)) == 0xFFFF
                && BinaryPrimitives.ReadUInt16LittleEndian(charlieRoster.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃")
                && BinaryPrimitives.ReadUInt16LittleEndian(charlieRoster.Payload.AsSpan(13, 2)) == harness.UidOf("查理")
                && charlieRoster.Payload[19] == 1,
            "every remaining member receives the same stable slot layout with leaderSlot=1");

        // 名册里没有任何一格指向离队者 → 他自己的客户端仍能正确自我清理（这条路径不受修复影响，作对照）。
        var alphaRoster = harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification);
        check(alphaRoster is { Payload.Length: 20 }
                && Enumerable.Range(0, 4).All(slot =>
                    BinaryPrimitives.ReadUInt16LittleEndian(alphaRoster.Payload.AsSpan(7 + slot * 3, 2))
                    != harness.UidOf("阿尔法")),
            "the leaver's own copy has no slot pointing at him");
    }

    /// <summary>
    /// 名册补播（真机阻塞项：B 接受邀请后**双方都看不到队伍 UI**）。客户端对**同一份**名册不重试：
    /// NOTI 9 的收尾有两道门（`0x41BDA8`：`[myUser+0xFC] != party` → 整个 UI 收尾跳过；`0x41BEA5`：
    /// 能解析到的成员数 &lt; 2 → 跳过 HUD 激活），逐槽填充又用 `0x4FDDB0(userMgr, slotUid)` 解析、
    /// 取不到 user 就静默跳过该行——那一刻没渲染出来就永久丢（勘察见
    /// party-protocol-recon-e-window-open.md §7、-f-partyui.md §门控）。真机证据：接受时的 NOTI 9
    /// 双方都看不见，1 分多钟后由 CMD 12 触发的、内容几乎相同的一份一到，队伍立刻显示 ⇒ 包没错，
    /// 客户端只是不自己重试。服务端的兜底是"名册下发之后排两次补播"（发布路径 + 换区/进区重播，
    /// 后者见 `CheckAreaChangeResettleAsync`）。
    /// 本用例把补播时刻直接推到过期，断言每个收包者（含同区域的旁观者）又收到一份**逐字节相同**的名册；
    /// 再断言补播**不再排期**（不自激）与已解散的队伍不会被补播。**把 `PublishRosterToAsync` 里的补播
    /// 登记去掉（或让扫描不处理待补播项）时，"an expired refresh re-publishes ..." 那条断言会变红；
    /// 把补播路径的 `registerResettle: false` 误改回 true 时，"a resettle never schedules another one ..."
    /// 那条断言会变红**（前者已实测）。
    /// </summary>
    private static async Task CheckRosterResettleAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        // 基线：一次发布 = 每个同区域收包者两份相同名册（一份定向 + 一份区域广播），20 字节。
        var alphaBaseline = RostersOf(harness, "阿尔法");
        var bravoBaseline = RostersOf(harness, "布拉沃");
        check(alphaBaseline.Count == 2
                && bravoBaseline.Count == 2
                && alphaBaseline.All(packet =>
                    packet.Payload.Length == 20 && packet.Payload.SequenceEqual(alphaBaseline[0].Payload))
                && bravoBaseline.All(packet => packet.Payload.SequenceEqual(bravoBaseline[0].Payload)),
            "baseline: one publish gives every member two identical 20-byte rosters (direct + area broadcast)");

        harness.ClearAll();
        harness.Service.ExpireResettleForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);

        var alphaResettled = RostersOf(harness, "阿尔法");
        var bravoResettled = RostersOf(harness, "布拉沃");
        var bystanderResettled = RostersOf(harness, "德尔塔");
        // 成员各两份（定向 + 区域广播）、旁观者一份（区域广播）：补播走的是**完整发布**，两条路都重发。
        check(alphaResettled.Count == 2
                && bravoResettled.Count == 2
                && bystanderResettled.Count == 1
                && alphaResettled.All(packet => packet.Payload.SequenceEqual(alphaBaseline[0].Payload))
                && bravoResettled.All(packet => packet.Payload.SequenceEqual(bravoBaseline[0].Payload))
                && bystanderResettled.All(packet => packet.Payload.Length == 20),
            "an expired refresh re-publishes the byte-identical roster to both members and to the area (full publish)");
        check(harness.Of("阿尔法").All(packet =>
                packet.ProtocolId == GameProtocolEngine.PartyInfoNotification)
                && harness.Of("布拉沃").All(packet =>
                    packet.ProtocolId == GameProtocolEngine.PartyInfoNotification),
            "a refresh produces rosters only (no NOTI 10, no failure receipt, nobody is dropped by it)");

        // 补播**不自激**：补播路径不再排期，所以同一支队在一轮补播之后再推过期 + 扫描必须零包
        // （否则每一次补播都会续出下一次，扫描每个周期都在无限重发）。有人把补播那一路的
        // `registerResettle: false` 误改回 true 时，这条断言会变红（已实测）。
        harness.ClearAll();
        harness.Service.ExpireResettleForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);
        check(harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0
                && harness.Of("德尔塔").Count == 0,
            "a resettle never schedules another one: a second expiry sweep produces no packets at all");

        // 解散：待补播项随队伍清掉，扫描再没有任何包可发（含旁观者）——绝不能把已不存在的队伍的
        // 名册再发一遍（客户端收到会把它重新加回本地队伍列表）。
        await harness.Service.HandleLeaveAsync(harness.IdOf("阿尔法"), CancellationToken.None);
        await harness.Service.HandleLeaveAsync(harness.IdOf("布拉沃"), CancellationToken.None);
        harness.ClearAll();
        harness.Service.ExpireResettleForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);
        check(harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0
                && harness.Of("德尔塔").Count == 0,
            "a dissolved party is never resettled: its pending refreshes die with it");
    }

    /// <summary>
    /// 换区/进区重播也要进补播表（评审留的**同类缺口**）：`OnAreaChangedAsync` 自己按区域 Build 包
    /// （不走 <c>PublishRosterToAsync</c>），所以补播要在它内部单独排。客户端在同一个门控上静默丢帧
    /// 且不重试，换区重播的故障面与发布完全相同（真机 L7："回到同区域后队友行恢复显示"）。
    /// **把 `OnAreaChangedAsync` 里的 `ScheduleResettle` 调用删掉时，补播那条断言会变红**（已实测）。
    /// </summary>
    private static async Task CheckAreaChangeResettleAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        // 先把"接受那一次发布"排的补播放掉：否则下面那次过期 + 扫描打的是它的排期，
        // 换区到底排没排补播就测不出来了（初版用例正是被这个残留排期掩盖的）。
        harness.Service.ExpireResettleForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);

        // 换区：布拉沃去 1/1（同城镇另一个区域）——这一步的名册重播绕开了发布路径。
        harness.ClearAll();
        await harness.MoveToAsync("布拉沃", townId: 1, areaId: 1, uid: 21);
        var alphaMoved = RostersOf(harness, "阿尔法").FirstOrDefault();
        var bravoMoved = RostersOf(harness, "布拉沃").FirstOrDefault();
        check(alphaMoved is { Payload.Length: 20 } && bravoMoved is { Payload.Length: 20 },
            "baseline: the area change replays the roster to both members");

        harness.ClearAll();
        harness.Service.ExpireResettleForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);

        var alphaResettled = RostersOf(harness, "阿尔法");
        var bravoResettled = RostersOf(harness, "布拉沃");
        check(alphaResettled.Count > 0
                && bravoResettled.Count > 0
                && alphaResettled.All(packet => packet.Payload.SequenceEqual(alphaMoved!.Payload))
                && bravoResettled.All(packet => packet.Payload.SequenceEqual(bravoMoved!.Payload)),
            "an area change schedules the resettle too: the replayed roster is re-sent byte-identically");
    }

    /// <summary>
    /// 稳定槽位下的踢人（终局评审留的 Important 覆盖缺口）：队长允许坐在**非 0 槽**，所以判定必须按
    /// 槽位下标比较（`slot != roster.LeaderSlot`），既不能假设"队长在槽 0"，也不能假设"比队长低的槽位
    /// 一定不属于别人"（槽位不搬动，队长退队会留下空洞，后来的成员会填进那个空洞）。
    /// 构造 [阿尔法@0, 布拉沃@1（队长）, 查理@2]：三人队建好后让槽 0 的原队长退队（布拉沃就地继任 →
    /// 队长身份落到槽 1），再让阿尔法重新入队——他填进空出来的槽 0。
    /// **有人日后把 `slot != roster.LeaderSlot` 删掉（或写成 `slot != 0`）时，②③ 两条断言会变红**
    /// （已实测）：踢"自己那一格"会真的把队长踢出队伍（而不是回 `CMD 14 [0,8]`），队伍随之换队长，
    /// ③ 紧接着也会跟着崩。
    /// </summary>
    private static async Task CheckKickFromNonZeroLeaderSlotAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        // ① 三人队 [阿尔法@0, 布拉沃@1, 查理@2]。
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);
        // 限流是真实时钟的 3 秒窗口，而测试是瞬时的：同一个人连邀两次前先清掉上次的时间戳。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("查理"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("查理"), harness.UidOf("阿尔法"), CancellationToken.None);
        // ② 槽 0 的队长退队 → 布拉沃（槽 1）就地继任。
        await harness.Service.HandleLeaveAsync(harness.IdOf("阿尔法"), CancellationToken.None);
        harness.ClearAll();
        // ③ 阿尔法重新申请入队 → TryAdd 填进最小的空槽 0，于是队长在槽 1、槽 0 是他的队员。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        var setup = RostersOf(harness, "查理").FirstOrDefault();
        check(setup is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(setup.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(setup.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃")
                && BinaryPrimitives.ReadUInt16LittleEndian(setup.Payload.AsSpan(13, 2)) == harness.UidOf("查理")
                && setup.Payload[19] == 1,
            "setup: the party is [阿尔法@0, 布拉沃@1 (leader), 查理@2] with a non-zero leader slot");

        // ① 队长（槽 1）踢槽 0 的队员：必须成功——被踢者收 NOTI 10 [0,0] 与一份不含他的新名册。
        harness.ClearAll();
        await harness.Service.HandleKickAsync(harness.IdOf("布拉沃"), slot: 0, CancellationToken.None);
        var kicked = harness.Of("阿尔法");
        var walkoutIndex = kicked.FindIndex(packet =>
            packet.ProtocolId == GameProtocolEngine.WalkoutNotification);
        var roster = kicked.FirstOrDefault(packet =>
            packet.ProtocolId == GameProtocolEngine.PartyInfoNotification);
        check(walkoutIndex >= 0
                && kicked[walkoutIndex].Payload.SequenceEqual(new byte[] { 0, 0 })
                && roster is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(7, 2)) == 0xFFFF
                && roster.Payload[19] == 1
                && Enumerable.Range(0, 4).All(slot =>
                    BinaryPrimitives.ReadUInt16LittleEndian(roster.Payload.AsSpan(7 + slot * 3, 2))
                    != harness.UidOf("阿尔法")),
            "a leader sitting in slot 1 can kick the member in slot 0 (NOTI 10 [0,0] plus a roster without him)");

        // ② 踢自己那一格（slot == LeaderSlot = 1）：槽里"没有人"→ CMD 14 回 [0,8]。
        harness.ClearAll();
        await harness.Service.HandleKickAsync(harness.IdOf("布拉沃"), slot: 1, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 1
                && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.WalkoutPartyMemberCommand
                && harness.Of("布拉沃")[0].Payload.SequenceEqual(new byte[] { 0, 8 })
                && harness.Of("查理").Count == 0,
            "kicking the leader's own slot (leaderSlot = 1) is refused with CMD 14 [0,8] and changes nothing");

        // ③ 空槽（槽 3）：同样 CMD 14 回 [0,8]。
        harness.ClearAll();
        await harness.Service.HandleKickAsync(harness.IdOf("布拉沃"), slot: 3, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 1
                && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.WalkoutPartyMemberCommand
                && harness.Of("布拉沃")[0].Payload.SequenceEqual(new byte[] { 0, 8 })
                && harness.Of("查理").Count == 0,
            "kicking an empty slot is refused with CMD 14 [0,8] and changes nothing");
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
        // nameSel：客户端给的是 0（自定义名），出站被规范化成预置名第 1 项（见 DefaultPartyNameSelection）——
        // NOTI 9 本来就带不了自定义名，而且真机上 nameSel=0 的名册完全无效（见 CheckOutboundNameSelectionAsync）。
        check(resized is not null && resized.Payload[5] == 1 && resized.Payload[6] == 2,
            "CMD 12 updates the member cap and ships the degraded preset name index (custom sel=0 -> 1)");

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

    /// <summary>
    /// ★ 回归（真机证据：接受邀请发出的 nameSel=0 名册**完全无效**）：出站名册的 nameSel 永远不是 0。
    /// 客户端只在 `nameSel == 0` 时走"取 dnf.str `0x272`（'无队伍名'）→ snprintf → 写队伍名"那条路径
    /// （`0x41B933`-`0x41B95A`），`nameSel != 0` 时整段跳过。真机上接受邀请时发出的三份（0s/+3s/+7s，
    /// 逐字节相同）nameSel=0 名册双方都看不见：零链接、无聊天行、推荐伙伴照旧把队友当无队伍；
    /// 1 分多钟后由 CMD 12 触发的一份**只有 nameSel 0→1 不同**的名册一到，队伍立刻出现。服务端改不了
    /// 客户端，于是把出站值钉在合法的预置名下标上：自定义名（sel=0）降级为预置名第 1 项
    /// （`DefaultPartyNameSelection`）——与"自定义队名不传播"的既有结论一致。
    /// **把 `NormalizeNameSelection` 从两个 Build 点摘掉时，本用例的两条断言都会变红。**
    /// </summary>
    private static async Task CheckOutboundNameSelectionAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();

        // ① 接受邀请新建的队伍：服务端建队时存的是 nameSel 0（= 客户端"自定义名"的初值），
        //    出站必须非 0——成员定向（BuildRosterPacket）与区域广播（BuildRosterPacketForArea，
        //    收件人是旁观者 德尔塔）两条路都算在内，共 2+2+1 份。
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), CancellationToken.None);

        var rosters = RostersOf(harness, "阿尔法")
            .Concat(RostersOf(harness, "布拉沃"))
            .Concat(RostersOf(harness, "德尔塔"))
            .ToList();
        check(rosters.Count == 5
                && rosters.All(packet => packet.Payload.Length == 20)
                && rosters.All(packet => packet.Payload[5] != 0),
            "a party created by an accept stores nameSel 0 but ships a non-zero nameSel to both members and the bystander");

        // ② 队长用 CMD 12 起一个自定义名（sel=0 + 名字正文）：出站仍是预置名第 1 项，队伍 id 不变。
        harness.ClearAll();
        await harness.Service.HandleSetInfoAsync(
            harness.IdOf("阿尔法"),
            nameSelection: 0,
            nameBytes: [0x41, 0x42],
            maxMembers: 4,
            CancellationToken.None);
        var renamed = RostersOf(harness, "阿尔法");
        check(renamed.Count > 0
                && renamed.All(packet => packet.Payload[5] == 1
                    && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(2, 2))
                        == harness.UidOf("阿尔法")),
            "CMD 12 with a custom name (sel=0) still ships nameSel 1 for the same party");
    }
}

/// <summary>
/// 组队服务测试台：真 store（临时目录）+ 真 TownSessionDirectory + 真 PartyService，
/// 只把发包换成写进 List（沿用 TradeHarness 的做法）；阿尔法/布拉沃/查理/德尔塔在 1/0 号共享区域，
/// 艾琳在 1/1（赛利亚房间，私密区域，每个成员一个实例）——用来验证区域广播不跨区、进区要补发。
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

    /// <summary>进入区域拿到的凭据：换区/离区必须带它退旧区（沿用 TownSessionDirectory 的凭据语义）。</summary>
    private readonly Dictionary<string, TownMemberTicket> _tickets = new();

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

        var fixtures = new (string Account, string Name, byte AreaId)[]
        {
            ("alice", "阿尔法", 0),
            ("bobby", "布拉沃", 0),
            ("carol", "查理", 0),
            ("dave", "德尔塔", 0),
            ("erin", "艾琳", 1)
        };
        foreach (var (account, name, _) in fixtures)
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
        foreach (var (account, name, areaId) in fixtures)
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
            // 四个角色在 1/0：组队请求要求请求者与目标同城镇同区域（设计稿 §5.1）。
            var ticket = await directory.EnterAsync(
                new TownEntryRequest(
                    TownId: 1,
                    AreaId: areaId,
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
            if (ticket is not null)
            {
                harness._tickets[name] = ticket;
            }
        }

        return harness;
    }

    /// <summary>把角色搬到另一个城镇区域：先退旧区、再进新区，并通知 PartyService（模拟 CMD 38 钩子）。</summary>
    public async Task MoveToAsync(string characterName, byte townId, byte areaId, ushort uid)
    {
        if (_tickets.TryGetValue(characterName, out var ticket))
        {
            await Directory.LeaveAsync(ticket, CancellationToken.None);
        }

        _uids[characterName] = uid;
        var characterId = _characters[characterName].Id;
        var entered = await Directory.EnterAsync(
            new TownEntryRequest(
                townId, areaId, uid, characterId, 100u + uid,
                Encoding.ASCII.GetBytes(characterName), characterName,
                Peer: null, X: 10, Y: 10, Direction: 0,
                SendAsync: (packet, _) => Record(characterName)(packet)),
            CancellationToken.None);
        if (entered is not null)
        {
            _tickets[characterName] = entered;
        }

        await Service.OnAreaChangedAsync(characterId, townId, areaId, uid, CancellationToken.None);
    }

    /// <summary>离开城镇区域（进副本等）：只退目录，不动队伍（设计稿 §4.4/§5.6）。</summary>
    public async Task LeaveTownAsync(string characterName)
    {
        if (_tickets.TryGetValue(characterName, out var ticket))
        {
            await Directory.LeaveAsync(ticket, CancellationToken.None);
            _tickets.Remove(characterName);
        }

        await Service.OnLeftAreaAsync(_characters[characterName].Id, CancellationToken.None);
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
