using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;
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
        CheckPartyRequestInfoValues(check);
        CheckPartyRequestParsers(check);
        CheckPartyAcceptedNotificationLayout(check);
        CheckPartyRoster(check);
        await CheckPartyServiceAsync(check);
        await CheckHostSlotIsSharedByEveryMemberAsync(check);
        CheckEnterSelectDungeonLayout(check);
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

    /// <summary>
    /// 应答窗正文（NOTI 7）两个服务端取值口径的真机回归：疲劳必须是**剩余疲劳**（旧实现填的是已耗值
    /// `UsedFatigue`），虚弱% 必须由服务端耐力状态换算（旧实现固定 0）：
    /// 疲劳 = 上限（含黑钻加成，`GameProtocolEngine.DefaultMaximumFatigue`/`BlackDiamondBonusFatigue`）− 已耗，
    /// 下限 0；虚弱% = `DungeonWeaknessPolicy.FullStamina(100) − 当前耐力值`，钳 0..100。
    /// **变异验证**：把 `RemainingFatigue` 换成回传 `usedFatigue`、或把 `WeaknessPercent` 换成恒 0，本用例变红。
    /// </summary>
    private static void CheckPartyRequestInfoValues(Action<bool, string> check)
    {
        check(EntranceService.RemainingFatigue(maximumFatigue: 156, usedFatigue: 100) == 56
                && EntranceService.RemainingFatigue(maximumFatigue: 188, usedFatigue: 0) == 188
                && EntranceService.RemainingFatigue(maximumFatigue: 156, usedFatigue: 156) == 0
                && EntranceService.RemainingFatigue(maximumFatigue: 156, usedFatigue: 200) == 0,
            "the party window's fatigue value is remaining fatigue (maximum incl. black diamond - used), floored at 0");
        check(EntranceService.WeaknessPercent(DungeonWeaknessPolicy.FullStamina) == 0
                && EntranceService.WeaknessPercent(100) == 0
                && EntranceService.WeaknessPercent(30) == 70
                && EntranceService.WeaknessPercent(DungeonWeaknessPolicy.MinimumRecovery) == 90,
            "the party window's weakness% is FullStamina - current stamina (0 when healthy, 90 at the floor of 10)");
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

        // ★ CMD 11 组队：9 字节 = 【是】（接受）；同前缀再追加 u16 0x0064（字节 `64 00`）= 11 字节 = 小窗【否】
        //（拒绝）——G 卷 §2.1/§2.2 的两条发送路径（0x4FEBC0 / 0x821FBA）+ 真机日志 `...0000 00000000 6400`。
        var accept = new byte[] { 0x11, 0x22, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00 };
        check(GameProtocolEngine.TryReadPartyResponsePeer(accept, out var requesterUid, out var accepted)
                && requesterUid == 1000
                && accepted,
            "party CMD 11 parser reads the requester uid and treats a 9-byte body as accept");

        var refuse = new byte[] { 0x11, 0x22, 0xE8, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00 };
        check(GameProtocolEngine.TryReadPartyResponsePeer(refuse, out var refusedUid, out var refusedAccepted)
                && refusedUid == 1000
                && !refusedAccepted,
            "party CMD 11 parser treats an 11-byte body ending in 64 00 as the 【否】 button (reject)");

        var tradeResponse = new byte[] { 0x11, 0x22, 0xE8, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00 };
        check(!GameProtocolEngine.TryReadPartyResponsePeer(tradeResponse, out _, out _),
            "party CMD 11 parser rejects peerType==1 so the trade path keeps its own packet");

        check(!GameProtocolEngine.TryReadPartyResponsePeer(new byte[8], out _, out _),
            "party CMD 11 parser rejects a truncated body");

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
    /// （同区域填真实 uid / 解析不到填**该成员自己的 uid** / 空槽 0xFFFF——**绝不填 0**：
    /// 真机证明 0 会被客户端当成"该成员已离开"，跨区域换区后两端同时弹"队伍中只剩下[X]"并散队）。
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
        check(slotsForHome.SequenceEqual(new ushort[] { 11, 12, 13, 0xFFFF }),
            "slots carry real uids for members the receiver can resolve");
        check(slotsForElsewhere.SequenceEqual(new ushort[] { 11, 12, 13, 0xFFFF }),
            "slots keep the global slot order and substitute the member's own uid for members the receiver cannot resolve");

        // ★ 回归（真机"跨区域散队"）：解析不到（跨区域 / 目录里查不到）的成员，槽位必须是**他自己的 uid**，
        // 既不能是 0 也不能是 0xFFFF——真机上 0 被客户端当成"这个槽没有人 / 该成员已离开"：换区后两端名册里
        // 对方那一格都是 0 → **两端同时**弹"队伍中只剩下[X]。要继续保留队伍吗？"并各自摘掉对方，队伍退化成
        // 两个单人队（设计稿 §2.2.2/§11）。填一个本地解析不到的 uid 只是那一行显示空白，成员仍在队伍里。
        // **变异验证**：把 `PartyRoster.SlotUids` 的回落值改回 0 → 本条断言变红。
        var unresolvableSlots = roster.SlotUids(_ => null);
        check(unresolvableSlots.SequenceEqual(new ushort[] { 11, 12, 13, 0xFFFF })
                && unresolvableSlots.Take(3).All(uid => uid is not 0 and not 0xFFFF)
                && unresolvableSlots[3] == 0xFFFF,
            "regression (cross-area party break): a member the receiver cannot resolve keeps his own uid (0 would read as 'he left the party'; 0xFFFF would read as 'empty slot')");

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
                .SequenceEqual(new ushort[] { 0xFFFF, 12, 13, 14 }),
            "after the leader leaves only his slot becomes 0xFFFF; the others keep their slot uids (an unresolvable member keeps his own uid)");

        check(!roster.Remove(Guid.NewGuid()), "removing a character that is not in the party changes nothing");

        roster.SetInfo(nameSelection: 6, maxMembers: 2);
        check(roster.NameSelection == 6 && roster.MaxMembers == 3 && roster.IsFull,
            "CMD 12 updates the preset name index, and the cap never drops below the current headcount");

        check(roster.UpdateLocation(sameArea.CharacterId, townId: 2, areaId: 3, areaUserId: 99),
            "a member's area change updates its stored location");
        check(roster.SlotUids(member => member.IsIn(2, 3) ? member.AreaUserId : null)
                .SequenceEqual(new ushort[] { 0xFFFF, 99, 13, 14 }),
            "the moved member is resolvable only in its new area (and stays in his own slot; the others fall back to their own uids)");

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

        // ★ CMD 131 模型层：就地转交队长 —— 只改 LeaderSlot，**不搬动任何成员**（槽位下标与槽位 uid 都不变）。
        var hostLeader = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "队长甲",
            AreaUserId = 31,
            TownId = 1,
            AreaId = 0
        };
        var hostSecond = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "队员乙",
            AreaUserId = 32,
            TownId = 1,
            AreaId = 0
        };
        var hostThird = new PartyMember
        {
            CharacterId = Guid.NewGuid(),
            CharacterName = "队员丙",
            AreaUserId = 33,
            TownId = 1,
            AreaId = 0
        };
        var hostParty = new PartyRoster(partyId: 13, hostLeader, maxMembers: 4, nameSelection: 0);
        check(hostParty.TryAdd(hostSecond) && hostParty.TryAdd(hostThird),
            "the CMD 131 party fills slots 0..2 in join order");
        check(hostParty.TryPromoteLeader(slot: 2)
                && hostParty.LeaderSlot == 2
                && hostParty.Leader == hostThird
                && hostParty.TryGetSlot(hostLeader.CharacterId, out var hostLeaderSlot) && hostLeaderSlot == 0
                && hostParty.TryGetSlot(hostSecond.CharacterId, out var hostSecondSlot) && hostSecondSlot == 1
                && hostParty.TryGetSlot(hostThird.CharacterId, out var hostThirdSlot) && hostThirdSlot == 2
                && hostParty.SlotUids(member => member.AreaUserId).SequenceEqual(new ushort[] { 31, 32, 33, 0xFFFF }),
            "CMD 131 promotes slot 2 in place: leaderSlot moves and every member keeps his slot index and slot uid");
        check(!hostParty.TryPromoteLeader(slot: 3)
                && !hostParty.TryPromoteLeader(slot: 4)
                && !hostParty.TryPromoteLeader(slot: 2)
                && hostParty.LeaderSlot == 2
                && hostParty.Leader == hostThird,
            "promoting an empty slot (or the current leader slot) is refused and leaves leaderSlot unchanged");

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
        await CheckRejectFlowAsync(check);
        await CheckApplyToJoinFlowAsync(check);
        await CheckAccepterJoinsRequesterPartyAsync(check);
        await CheckRequestRefusalsAsync(check);
        await CheckPartyFullRefusalAsync(check);
        await CheckResponseRefusalShapeAsync(check);
        await CheckDuplicateAcceptIsInertAsync(check);
        await CheckTimeoutAndBlacklistAsync(check);
        await CheckExpiredPendingIsTakenOverAsync(check);
        await CheckInviteTimeoutIsLoggedAsync(check);
        CheckPendingWindowMatchesClientDialog(check);
        await CheckLeaveAndKickAsync(check);
        await CheckLeaderLeaveKeepsMemberSlotsAsync(check);
        await CheckKickFromNonZeroLeaderSlotAsync(check);
        await CheckChangeHostAsync(check);
        await CheckSetPartyInfoAsync(check);
        await CheckOutboundNameSelectionAsync(check);
        await CheckAreaBroadcastAsync(check);
        await CheckRosterResettleAsync(check);
        await CheckAreaChangeResettleAsync(check);
        await CheckCrossAreaRosterKeepsBothMembersAsync(check);
    }

    /// <summary>某个角色收件箱里的 NOTI 9 列表（断言"补播了一份逐字节相同的名册"用）。</summary>
    private static List<GameServerPacket> RostersOf(PartyHarness harness, string characterName) =>
        harness.Of(characterName)
            .Where(packet => packet.ProtocolId == GameProtocolEngine.PartyInfoNotification)
            .ToList();

    /// <summary>NOTI 9 的 4 个槽位 uid（交错布局：body 偏移 7 起每 3 字节一个 u16）。</summary>
    private static ushort[] SlotUidsOf(GameServerPacket packet) =>
        Enumerable.Range(0, 4)
            .Select(slot => BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(7 + slot * 3, 2)))
            .ToArray();

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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

        var bystander = harness.FirstOf("德尔塔", GameProtocolEngine.PartyInfoNotification);
        check(bystander is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(bystander.Payload.AsSpan(2, 2)) == harness.UidOf("阿尔法"),
            "a bystander in the same area learns the new party (same party id as the members)");
        check(harness.Of("艾琳").Count == 0,
            "a client in another area of the same town gets no party roster");

        // 换区：旧区域版本里离场成员**回落成他自己的 uid**（不是 0——真机证明 0 会被客户端当成
        // "该成员已离开"，跨区域散队根因，设计稿 §2.2.2）；新区域版本里他是真实 uid。
        harness.ClearAll();
        await harness.MoveToAsync("布拉沃", townId: 1, areaId: 1, uid: 21);
        var oldAreaRoster = harness.FirstOf("德尔塔", GameProtocolEngine.PartyInfoNotification);
        check(oldAreaRoster is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(oldAreaRoster.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(oldAreaRoster.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃"),
            "the roster sent to the area the member left keeps his own uid (never 0, which the client reads as 'he left the party')");
        var newAreaRoster = harness.FirstOf("艾琳", GameProtocolEngine.PartyInfoNotification);
        check(newAreaRoster is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(newAreaRoster.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(newAreaRoster.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃"),
            "the roster sent to the area the member entered carries his real uid there (and the far-away member's own uid, never 0)");

        // 进区补发：队伍先建好，旁观者后进这个区域，也要看得见。
        harness.ClearAll();
        await harness.MoveToAsync("查理", townId: 1, areaId: 0, uid: 31);
        check(harness.FirstOf("查理", GameProtocolEngine.PartyInfoNotification) is not null,
            "a client entering the area is back-filled with the rosters of the parties living there");

        // 离区（进副本）：**不退队**（设计稿 §4.4），只重播名册——目录里已经没有他，
        // 于是"曾通知过的区域"收到的那份里他那一格回落到他自己的 uid（不是 0——0 会被当成"他已离队"）。
        harness.ClearAll();
        await harness.LeaveTownAsync("布拉沃");
        var afterLeaveArea = harness.FirstOf("德尔塔", GameProtocolEngine.PartyInfoNotification);
        check(afterLeaveArea is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(afterLeaveArea.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(afterLeaveArea.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃"),
            "leaving town for a dungeon keeps the member's own uid in his slot for the area he left (never a 0 that reads as 'he left the party')");
        // 他自己那份：收包者不在任何城镇区域 → 除自己外全部解析不到 → 其余成员也填他们**自己的 uid**，
        // 绝不填 0（真机证明 0 会让客户端以为对方已离队 → 跨区域/进副本后散队，设计稿 §2.2.2）。
        var offTownCopy = harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification);
        check(offTownCopy is { Payload.Length: 20 }
                && BinaryPrimitives.ReadUInt16LittleEndian(offTownCopy.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(offTownCopy.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃"),
            "a member outside every town area still gets a full roster: nobody's slot is 0, only himself is locally resolvable");
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
        var second = harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
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

    /// <summary>
    /// ★ 回归（真机「点【否】也等于接受」）：小窗【否】发的 11 字节拒绝包（与【是】同前缀、尾部多
    /// `64 00`，G 卷 §2.2）经协议层判成 `accepted: false` 之后，服务端必须**只**给请求方回一条与
    /// 超时/拉黑同形的 `CMD 11 [0,100,0]`、接受方零包，且**不建队、不加人**；待决项已被原子摘除，
    /// 超时收尾不会再补一条失败，随后再邀请一次也必须能成功（"先到者优先"不会误挡）。
    /// **变异验证**：把 `PartyService.HandleResponseAsync` 里的 `if (!accepted)` 分支摘掉（或让
    /// `IsPeerResponseAccepted` 恒为 true），"no NOTI 9" 与 "zero packet for the refuser" 两条断言变红
    ///（会建出队伍并向双方+旁观者广播名册、给接受方回 NOTI 8）。
    /// </summary>
    private static async Task CheckRejectFlowAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is not null,
            "the reject flow starts from a normal NOTI 7 window");
        harness.ClearAll();

        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: false, CancellationToken.None);

        check(harness.Of("阿尔法").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.PartyInfoNotification)
                && harness.Of("布拉沃").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.PartyInfoNotification)
                && harness.Of("德尔塔").Count == 0,
            "a refusal builds no party: nobody gets a NOTI 9 roster (neither side, nor the same-area bystander)");
        check(harness.Of("阿尔法").All(packet =>
                packet.ProtocolId != GameProtocolEngine.ResponsePeerNotification),
            "the requester gets no NOTI 8 chat line when the target refuses");
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload.SequenceEqual(new byte[] { 0, 100, 0 }),
            "the requester gets exactly the same CMD 11 [0,100,0] as an unanswered (timed out) request");
        check(harness.Of("布拉沃").Count == 0,
            "the refuser gets no packet at all (the client closes its own window on 【否】)");

        // 待决项必须在拒绝时就被摘掉：否则超时收尾会再补一条失败，而且第二次邀请会被"先到者优先"回绝。
        await harness.Service.SweepExpiredAsync(CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1 && harness.Of("布拉沃").Count == 0,
            "the refused request is already gone from the pending table: the 30-second sweep adds nothing");

        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is not null
                && harness.Of("阿尔法").Count == 0,
            "the pending slot was released: an immediate re-invite reaches the target instead of the late-arrival refusal");
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
        check(harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification) is not null
                && harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification) is not null,
            "the re-invite can then be accepted: the party forms normally");
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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
            harness.IdOf("查理"), harness.UidOf("布拉沃"), accepted: true, CancellationToken.None);
        harness.ClearAll();

        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is not null,
            "applying to a leader is the same CMD 10/NOTI 7 pair as an invite");

        harness.ClearAll();
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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
                accepted: true,
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
        // 限流是真实时钟的 3 秒窗口，而测试是瞬时的：同一个人连邀两次前先清掉上次的时间戳。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("查理"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("查理"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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
    /// ★ 回归（真机"跨区域散队"）：两人在 1/0 组队成功后，其中一人走到同城镇的另一个区域（1/1）——
    /// 发给**两端**的名册里，对方那一格都必须是他的真实 uid（离场者是"回落成他自己的 uid"那一支），
    /// **绝不能是 0**：真机上客户端把 0 当成"这个槽没有人 / 该成员已离开"，于是两端同时弹
    /// "队伍中只剩下[X]。要继续保留队伍吗？"并各自把对方摘掉，队伍退化成两个单人队。
    /// 同时名册的"人数"不能变：4 个槽里非 0xFFFF 的仍是 2（客户端只把 0xFFFF 当空，0 仍算成员）。
    /// **变异验证**：把 `PartyRoster.SlotUids` 的回落值改回 0 → 本用例的两条断言变红。
    /// </summary>
    private static async Task CheckCrossAreaRosterKeepsBothMembersAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

        // 布拉沃换到同城镇的另一个区域（1/1，uid 21）：阿尔法是"留下来那位"。
        harness.ClearAll();
        await harness.MoveToAsync("布拉沃", townId: 1, areaId: 1, uid: 21);

        var alphaRoster = harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification);
        var bravoRoster = harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification);
        check(alphaRoster is { Payload.Length: 20 } && bravoRoster is { Payload.Length: 20 },
            "an area change still delivers a 20-byte roster to both members");

        var alphaSlots = SlotUidsOf(alphaRoster!);
        var bravoSlots = SlotUidsOf(bravoRoster!);
        // 人数不变：非 0xFFFF 的槽位仍是 2（0xFFFF 才是"空槽"，0 从来不是合法值）。
        check(alphaSlots.Count(uid => uid != 0xFFFF) == 2
                && bravoSlots.Count(uid => uid != 0xFFFF) == 2,
            "the cross-area roster keeps both members: exactly two non-0xFFFF slots on each side");
        // 留下来那位（阿尔法）收到的名册：离场者（布拉沃）那一格是他的真实 uid（21），不是 0。
        check(alphaSlots.SequenceEqual(new[] { harness.UidOf("阿尔法"), harness.UidOf("布拉沃"), (ushort)0xFFFF, (ushort)0xFFFF })
                && alphaSlots.All(uid => uid != 0),
            "the member left behind still sees the moved member's real uid (a 0 would make the client drop him from the party)");
        // 离场者自己收到的名册：留下者那一格同样不是 0（跨区域双方都无法在本地解析对方，但都不填 0）。
        check(bravoSlots.SequenceEqual(new[] { harness.UidOf("阿尔法"), harness.UidOf("布拉沃"), (ushort)0xFFFF, (ushort)0xFFFF })
                && bravoSlots.All(uid => uid != 0),
            "the moved member's own copy keeps the other member's uid too (neither side may read a 0 as 'he left the party')");
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
        // 限流是真实时钟的 3 秒窗口，而测试是瞬时的：同一个人连邀两次前先清掉上次的时间戳。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("查理"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("查理"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
        // ② 槽 0 的队长退队 → 布拉沃（槽 1）就地继任。
        await harness.Service.HandleLeaveAsync(harness.IdOf("阿尔法"), CancellationToken.None);
        harness.ClearAll();
        // ③ 阿尔法重新申请入队 → TryAdd 填进最小的空槽 0，于是队长在槽 1、槽 0 是他的队员。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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
    /// CMD 131 转交队长（B 卷 §4.3）：载荷 = u8 槽位，形状与 CMD 14 一致。客户端收不到 CMD 131 的**任何**
    /// 回包（分发表把它映射到默认空分支），所以结果只能经 `NOTI 9` 的 `leaderSlot` 广播；失败必须静默。
    /// 构造三人队 [阿尔法@0（队长）, 布拉沃@1, 查理@2]，队长把队长身份转交槽 1。
    /// **变异验证（已实测）**：把 `PartyRoster.TryPromoteLeader` 改成搬动成员（把新队长换到槽 0）→
    /// "槽位布局逐字节不变"断言变红；删掉 `HandleChangeHostAsync` 的队长校验 → 非队长那条零包断言变红。
    /// </summary>
    private static async Task CheckChangeHostAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        // ① 三人队 [阿尔法@0（队长）, 布拉沃@1, 查理@2]。
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
        // 限流是真实时钟的 3 秒窗口，而测试是瞬时的：同一个人连邀两次前先清掉上次的时间戳。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("查理"), PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(
            harness.IdOf("查理"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

        // 查理同时是"区域旁观者"：第一次接受（2 人队）也会广播给他，所以要取**最后**一份名册（3 人队）。
        var before = RostersOf(harness, "查理").LastOrDefault();
        check(before is { Payload.Length: 20 }
                && before.Payload[19] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(before.Payload.AsSpan(7, 2)) == harness.UidOf("阿尔法")
                && BinaryPrimitives.ReadUInt16LittleEndian(before.Payload.AsSpan(10, 2)) == harness.UidOf("布拉沃")
                && BinaryPrimitives.ReadUInt16LittleEndian(before.Payload.AsSpan(13, 2)) == harness.UidOf("查理"),
            "setup: 阿尔法 leads the three-member party from slot 0");

        // ② 队长转交通知槽 1 的布拉沃：三名成员**每人**都收到带 leaderSlot=1 的新名册，
        //    而 slots/nameSel/maxMembers/partyId 与转交前逐字节相同（就地提升，不搬动槽位）。
        harness.ClearAll();
        await harness.Service.HandleChangeHostAsync(harness.IdOf("阿尔法"), slot: 1, CancellationToken.None);
        foreach (var member in new[] { "阿尔法", "布拉沃", "查理" })
        {
            var updated = RostersOf(harness, member);
            check(updated.Count > 0
                    && updated.All(packet => packet is { Payload.Length: 20 }
                        && packet.Payload[19] == 1
                        && before!.Payload.AsSpan(0, 19).SequenceEqual(packet.Payload.AsSpan(0, 19))),
                $"CMD 131 notifies {member} with a roster whose only change is leaderSlot = 1");
        }

        // ③ 非队长（仍在槽 0 的阿尔法）再发 CMD 131：零包——客户端没有失败回包可收，静默是设计。
        harness.ClearAll();
        await harness.Service.HandleChangeHostAsync(harness.IdOf("阿尔法"), slot: 2, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0
                && harness.Of("查理").Count == 0,
            "a non-leader's CMD 131 is silently ignored (no failure packet, no roster)");

        // ④ 新队长转交空槽（槽 3）与自己的槽（槽 1）：同样零包，leaderSlot 不变。
        harness.ClearAll();
        await harness.Service.HandleChangeHostAsync(harness.IdOf("布拉沃"), slot: 3, CancellationToken.None);
        await harness.Service.HandleChangeHostAsync(harness.IdOf("布拉沃"), slot: 1, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 0 && harness.Of("查理").Count == 0,
            "CMD 131 to an empty slot or to the leader's own slot is silently ignored");
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

    /// <summary>
    /// ★ 回归（真机 2026-10-23 20:01 现场，`.superpowers/sdd/2026-10-22-client-auto-input/work/server-demo.log`
    /// 522/529/530/536）：被邀请者客户端的应答窗超时后**一个包都不发**（那轮全会话零条 `RX … protocol=11`），
    /// 而服务端把待决项留满 `RequestWindow`（当时 30 秒；真机实测客户端窗口只有 ~10 秒）。于是 20:01:30 的第二次邀请撞上一条
    /// **已经过期、只是还没被 1 Hz 扫描摘掉**的待决项：`_pending.TryAdd` 失败 →
    /// 请求方立刻收 `CMD 11 [0,100,0]`、被邀请者一包都收不到（业主原话"后续再尝试要求就直接返回拒绝了"）。
    /// 契约：**只有还活着的待决项有权拒绝后来者**（"先到者优先"保护的是目标**此刻正在看**的那扇窗）；
    /// 已过期的待决项必须被新请求顶掉，顶掉后照常发 NOTI 7，且随后的接受仍然成队。
    /// 变异验证：把接管退回"`_pending.TryAdd` 失败即拒绝"，②④ 两条立刻变红（请求方收到 006400、目标收不到 NOTI 7）。
    /// </summary>
    private static async Task CheckExpiredPendingIsTakenOverAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        harness.ClearAll();

        // ① 活着（未到期）的待决项照旧拒绝后来者：这是设计稿 §5.1 的"先到者优先"，本轮**不许**修掉。
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is not null,
            "setup: the first invitation reaches 布拉沃 as a NOTI 7");
        harness.ClearAll();
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].Payload.SequenceEqual(new byte[] { 0, 100, 0 })
                && harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is null,
            "a still-live pending keeps first-come-first-served: the late invitation is refused at once and no second window opens");

        // ② 同一个人重邀：待决项已过期（真机上"应答窗已消失"就是这个状态）→ 新请求必须顶掉它，
        //    而不是像 20:01:30 那轮一样立刻回一条 006400。
        harness.ClearAll();
        harness.Service.ExpirePendingForTests();
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 0
                && harness.FirstOf("布拉沃", GameProtocolEngine.RequestPeerNotification) is { Payload.Length: 11 },
            "an expired pending is taken over: the re-invite reaches 布拉沃 and the requester hears nothing (no instant 'refused')");

        // ③ 顶掉之后一切照旧：被邀请者这次点【是】，队伍正常成形（接管不是把请求"吃掉"）。
        harness.ClearAll();
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);
        check(harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification) is not null
                && harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification) is not null,
            "the taken-over request can then be accepted normally: both sides get the roster");

        // ④ 顶掉与"谁发的"无关：目标身上那条过期待决项来自别人（查理），阿尔法的邀请同样能顶掉它。
        harness.ClearAll();
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("查理"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("查理"), harness.UidOf("德尔塔"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.FirstOf("德尔塔", GameProtocolEngine.RequestPeerNotification) is not null,
            "setup: 查理's invitation reaches 德尔塔");
        harness.ClearAll();
        harness.Service.ExpirePendingForTests();
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("德尔塔"), PartyHarness.DefaultInfo, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 0
                && harness.FirstOf("德尔塔", GameProtocolEngine.RequestPeerNotification) is not null,
            "a stale pending left by someone else never locks the target: the new requester reaches 德尔塔 directly");
    }

    /// <summary>
    /// 业主原话"服务端好像没有处理"有一半是**看不见**：`SweepExpiredAsync` 的超时收尾原来一条日志都不打，
    /// 日志里只有一条 `TX protocol=11 payload=006400`，没有任何业务行说明这是"邀请过期"。
    /// 契约：超时收尾必须留一条带**双方名字**的 info 行（排查时能一眼看出谁向谁发的邀请过期了）。
    /// </summary>
    private static async Task CheckInviteTimeoutIsLoggedAsync(Action<bool, string> check)
    {
        var log = new RecordingLogger<PartyService>();
        await using var harness = await PartyHarness.CreateAsync(log);
        harness.ClearAll();
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), PartyHarness.DefaultInfo, CancellationToken.None);
        harness.ClearAll();
        harness.Service.ExpirePendingForTests();
        await harness.Service.SweepExpiredAsync(CancellationToken.None);
        check(log.Entries.Any(entry => entry.Level == LogLevel.Information
                && entry.Message.Contains("timed out", StringComparison.Ordinal)
                && entry.Message.Contains("阿尔法", StringComparison.Ordinal)
                && entry.Message.Contains("布拉沃", StringComparison.Ordinal)),
            "the timeout sweep logs one business line naming both the requester and the target");
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand,
            "the logged timeout still sends the requester its CMD 11 [0,100,0] (logging must not replace the packet)");
    }

    /// <summary>
    /// ⑤ 窗口长度本身是契约：真机实测客户端应答窗 ≈10 s（+8 s 点【是】成队、+12 s 点【是】零反应；
    /// 1 Hz 序列 +10.0 s 有窗、+11.0 s 无窗，见 `measure-invite-window/`）。服务端窗口**不得**比它长——
    /// 长出来的每一秒都是"替一扇已经不存在的窗占位"，正是业主 20:01:30 被直接拒绝的原因。
    /// </summary>
    private static void CheckPendingWindowMatchesClientDialog(Action<bool, string> check) =>
        check(PartyService.RequestWindow <= TimeSpan.FromSeconds(10),
            "the pending window never outlives the client's accept dialog (measured ~10 s on 2026-10-23)");

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
            harness.IdOf("阿尔法"), harness.UidOf("布拉沃"), accepted: true, CancellationToken.None);
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
            harness.IdOf("布拉沃"), harness.UidOf("阿尔法"), accepted: true, CancellationToken.None);

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

    /// <summary>
    /// S1 不变量（官服对齐）：<c>TryGetLeaderSlot(任意成员)</c> 必须返回**同一个值 = 队长的槽位**。
    /// 它是三处 NOTI 26 的**唯一输入**：进选图（`EntranceService.cs:2779`）、队员入场（`:2884`）、
    /// live-join（`:13001`）；官服 `CParty::send_host_info`（@0x0859cdd2）给全队发同一个主机槽位，
    /// 客户端把它当 0..7 的槽位索引用（`DNF.exe 0x78D2B1`：`mov esi,[eax+28h]; cmp esi,8; jge`）。
    /// 发成"收件人自己的槽位"会让**每个**客户端都以为自己是主机（各模拟各的怪物）。
    /// **判别力（为什么既有用例抓不到）**：`DungeonPartySmokeTests.CheckGiveUpLeavesPartyAsync` 只问
    /// **队长/继任者**的 host 槽位 —— 那时"自己的槽位"与"队长的槽位"重合，把实现改成"返回自己的槽位"
    /// **变异存活**。本用例问的是**非队长的队员**（自己的槽位 1 ≠ 队长的槽位 0），变异必红。
    /// **变异验证**（实测变红后已回滚）：把 `PartyService.TryGetLeaderSlot` 改成
    /// `roster.TryGetSlot(characterId, out var slot) ? slot : null`（返回调用者自己的槽位）
    /// → 第一条断言 `every member resolves the SAME host slot` 变红。
    /// </summary>
    private static async Task CheckHostSlotIsSharedByEveryMemberAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leader = harness.IdOf("阿尔法");
        var member = harness.IdOf("布拉沃");

        // 前置：建队时队长占槽 0、被邀请者占槽 1（TryAdd 取最小空闲槽下标）。
        // 这一步本身也是断言——若槽位分配变了，下面"自己槽位 ≠ 主机槽位"的判别力就没了。
        var leaderOwnSlot = harness.Service.GetPartySlots(leader)!
            .Single(slot => slot.CharacterId == leader).SlotIndex;
        var memberOwnSlot = harness.Service.GetPartySlots(member)!
            .Single(slot => slot.CharacterId == member).SlotIndex;
        check(leaderOwnSlot == 0 && memberOwnSlot == 1,
            "the fixture really puts the leader in slot 0 and the member in slot 1");

        // ① 关键断言：两人查到**同一个** host 槽位，且等于队长的（不是队员自己的）。
        check(harness.Service.TryGetLeaderSlot(leader) == 0
                && harness.Service.TryGetLeaderSlot(member) == 0,
            "every member resolves the SAME host slot (the leader's), never his own");
        check(harness.Service.TryGetLeaderSlot(member) != memberOwnSlot,
            "a member's host slot is deliberately NOT his own slot (that mistake makes both clients claim to host)");

        // ② 不在任何队伍 → null（调用点据此回退 0，即单人语义：主机就是自己）。
        check(harness.Service.TryGetLeaderSlot(Guid.NewGuid()) is null,
            "a character outside every party has no host slot (call sites fall back to 0 for solo play)");

        // ③ 队长退队 → 队员**就地**继任（槽位不搬动）：host 槽位随之变成 1；已退队者查不到。
        await harness.Service.HandleLeaveAsync(leader, CancellationToken.None);
        check(harness.Service.TryGetLeaderSlot(member) == 1,
            "after the leader leaves, the successor's own slot becomes the host slot (no compaction to slot 0)");
        check(harness.Service.TryGetLeaderSlot(leader) is null,
            "the departed leader no longer resolves any host slot");
    }

    /// <summary>
    /// NOTI 27（ENTER_SELECT_DUNGEON）的载荷必须保持**本客户端要的三字段**：
    /// `bool 地狱任务是否满足` + `byte 缺失物品的槽位个数` + u16 **槽位下标**数组。
    /// 客户端处理器 `0x41F16C` 的读取序列 = `byte, byte, short`（`client_surface.py noti-fields 27`；
    /// 三个读取点在 `0x41F1CF`/`0x41F27A`/`0x41F2BE`）。
    /// **禁止照抄官服** `CParty::game_start`（`0x0859dd4f`）里那份：它是 `put_int` 开头的**更晚客户端**布局
    /// （`put_int + put_byte + put_short + put_byte×2 + put_byte(1) + put_byte(0)`）——
    /// 照抄会让本客户端从 int 里读出垃圾。同样的警告也写在 `GameProtocolEngine.cs:3307-3309`。
    /// **既有覆盖**：`Program.cs` 里已有 4 条 NOTI 27 布局用例（"blocks hell quests with string 5431"、
    /// "names missing-item party slot zero with string 5430"、"carries independent quest and item restrictions"、
    /// "dungeon-gate entry uses the exact DFLegacy quest/item-party-slot layout"）——**它们才是主护栏**。
    /// 本用例**补的是**：① 显式断言"绝不是以 int 开头的官服形态"（把警告变成可执行约束）；
    /// ② 缺失物品槽位的 `[2,3]` 组合（既有用例只覆盖了槽位 0）。
    /// **变异验证**（实测）：把 `CreateEnterSelectDungeon` 改成先 `writer.Write((int)0)`（官服形态）→
    /// 全量冒烟里**连本用例共 7 条**断言变红（本用例 3 条 + 既有 4 条）⇒ 布局确实被钉住。
    /// **真机交叉**（run #43）：队员会话收到 `protocol=27 length=8` ⇒ body 2 字节 = `[1, 0]`（合格且无缺失物品）✓。
    /// </summary>
    private static void CheckEnterSelectDungeonLayout(Action<bool, string> check)
    {
        var eligible = GameProtocolEngine.CreateEnterSelectDungeon(
            hellQuestsCompleted: true, missingHellItemPartyIndices: []);
        check(eligible.ProtocolId == GameProtocolEngine.EnterSelectDungeonNotification
                && eligible.Payload.SequenceEqual(new byte[] { 1, 0 }),
            "an eligible NOTI 27 body is exactly [hellQuestsCompleted=1, count=0] (client reads byte,byte,short)");

        var missing = GameProtocolEngine.CreateEnterSelectDungeon(
            hellQuestsCompleted: false, missingHellItemPartyIndices: [2, 3]);
        check(missing.Payload.SequenceEqual(new byte[] { 0, 2, 2, 0, 3, 0 }),
            "missing hell items ship as count + u16 PARTY SLOTS (2 and 3), never as an int-prefixed layout");
        check(missing.Payload.Length == 6,
            "the official later-client NOTI 27 layout starts with a 4-byte int — emitting it would break this client");
    }
}

/// <summary>
/// 组队服务测试台：真 store（临时目录）+ 真 TownSessionDirectory + 真 PartyService，
/// 只把发包换成写进 List（沿用 TradeHarness 的做法）；阿尔法/布拉沃/查理/德尔塔在 1/0 号共享区域，
/// 艾琳在 1/1（赛利亚房间，私密区域，每个成员一个实例）——用来验证区域广播不跨区、进区要补发。
/// </summary>
/// <remarks>
/// 只收集日志级别与文本的假 logger：用来断言"邀请超时收尾必须留一条业务日志"
/// （业主 2026-10-23 的现场里，超时路径一条日志都没有，看起来像"服务端没有处理"）。
/// </remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
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

    public Task SetTownAppearanceAsync(string characterName, GameServerPacket basic,
        GameServerPacket detail) => Directory.RefreshAppearanceAsync(
            _tickets[characterName], basic, CancellationToken.None, detail);

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

    public static async Task<PartyHarness> CreateAsync(ILogger<PartyService>? logger = null, string? tempParent = null)
    {
        var root = Path.Combine(tempParent ?? Path.GetTempPath(), $"dflegacy-party-harness-{Guid.NewGuid():N}");
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
            // Party/drop/PvP fixtures exercise funded economic transactions.
            created = (await store.SaveCharacterInventoryAsync(account, created.Id, 50_000, [], []))!;
            await store.GrantCeraToAccountAsync(account, 50_000);
            harness._packets[name] = [];
            harness._characters[name] = (created.Id, account);
            harness._sessionIds[name] = Guid.NewGuid();
        }

        harness.Service = new PartyService(
            store,
            directory,
            TimeProvider.System,
            logger ?? NullLogger<PartyService>.Instance);
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

    /// <summary>测试用：让两名已建角色的成员组队成功（走真实的接受流程，保证槽位/名册状态真实）。</summary>
    public async Task InviteAndAcceptAsync(string inviter, string target)
    {
        await Service.HandleRequestAsync(
            _characters[inviter].Id, _uids[target], DefaultInfo, CancellationToken.None);
        await Service.HandleResponseAsync(
            _characters[target].Id, _uids[inviter], accepted: true, CancellationToken.None);
    }

    /// <summary>测试用：把成员挪到别的区域（目录 + 名册都刷新）。</summary>
    public async Task MoveToAreaAsync(string characterName, byte townId, byte areaId)
    {
        await Directory.LeaveAsync(_tickets[characterName], CancellationToken.None);
        var entered = await Directory.EnterAsync(
            new TownEntryRequest(
                townId, areaId, _uids[characterName], _characters[characterName].Id, CharacterNo: 0,
                CharacterNameBytes: Encoding.UTF8.GetBytes(characterName), DisplayName: characterName,
                Peer: null, X: 474, Y: 234, Direction: 5,
                SendAsync: (packet, _) => Record(characterName)(packet)),
            CancellationToken.None);
        if (entered is not null)
        {
            _tickets[characterName] = entered;
        }

        await Service.OnAreaChangedAsync(
            _characters[characterName].Id, townId, areaId, _uids[characterName], CancellationToken.None);
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
