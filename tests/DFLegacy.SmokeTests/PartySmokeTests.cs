using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;

/// <summary>
/// 组队协议与服务的冒烟测试。包布局的权威说明见
/// .superpowers/sdd/party-protocol-recon.md（合并卷 §2）与设计稿 §2。
/// </summary>
internal static class PartySmokeTests
{
    public static Task RunAsync(Action<bool, string> check)
    {
        CheckPartyInfoNotificationLayout(check);
        CheckPartyDisbandNotificationLayout(check);
        CheckWalkoutNotificationLayout(check);
        CheckRequestPartyNotificationLayout(check);
        CheckPartyCommandErrorLayout(check);
        CheckPartyRequestParsers(check);
        CheckPartyRoster(check);
        return Task.CompletedTask;
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
        // 期望值修正：异区域成员（异区 = 1/1）对 (1,0) 收包者不可解析 → 槽位填 0（设计稿 §2.2.2
        // "与 R 不同区域 → 0"，填真实 uid 会在对方区域解析成别人的名字）；brief 原文写 13 与同一次
        // 执行里的 slotsForElsewhere 断言（要求同区成员填 0）互相矛盾，任何实现都无法同时满足。
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
        // 期望值修正：槽位顺序仍是加入顺序（12 在 0 号、14 在 2 号），但异区成员（异区 = 1/1）的槽位是
        // 0（缺席哨兵）而不是它的真实 uid 13——0 仍算成员，只有超出成员数的槽才是空槽 0xFFFF（设计稿 §2.2.2）。
        check(roster.SlotUids(member => member.IsIn(1, 0) ? member.AreaUserId : null)
                .SequenceEqual(new ushort[] { 12, 0, 14, 0xFFFF }),
            "after the leader leaves the roster stays compact");

        check(!roster.Remove(Guid.NewGuid()), "removing a character that is not in the party changes nothing");

        roster.SetInfo(nameSelection: 6, maxMembers: 2);
        check(roster.NameSelection == 6 && roster.MaxMembers == 2 && roster.IsFull,
            "CMD 12 updates the preset name index and the member cap");

        check(roster.UpdateLocation(sameArea.CharacterId, townId: 2, areaId: 3, areaUserId: 99),
            "a member's area change updates its stored location");
        // 期望值修正：队长已移除，同区成员现在是 0 号槽，所以换区后的 uid 99 落在槽 0；brief 原文把 99
        // 写在槽 1（相当于队长仍在队里），与上面"移除队长后槽位重排"的断言不一致。
        check(roster.SlotUids(member => member.IsIn(2, 3) ? member.AreaUserId : null)
                .SequenceEqual(new ushort[] { 99, 0, 0, 0xFFFF }),
            "the moved member is resolvable only in its new area");

        // 期望值修正：brief 原文期望 allocated == 11，但 11 就在 takenIds 里——设计稿 §2.2.1 的硬约束是
        // "不与其它队伍撞号"，任何合法分配都不能返回已占用的 11（实现向上扫描的结果是空闲的 1）。
        // 这里改为断言测试名所述的性质：结果非 0、且不落在已占用的 id 上。
        check(PartyRoster.TryAllocatePartyId([11, 12], preferred: 12, out var allocated)
                && allocated != 0
                && allocated != 11
                && allocated != 12,
            "party id allocation skips a preferred id that is already taken");
        check(PartyRoster.TryAllocatePartyId([1, 2, 3], preferred: 0, out var scanned)
                && scanned is not 0 and not 1 and not 2 and not 3,
            "party id allocation scans for a free value when the preferred one is unusable");
    }
}
