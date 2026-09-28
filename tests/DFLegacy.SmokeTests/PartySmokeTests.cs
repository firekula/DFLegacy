using System.Buffers.Binary;
using DFLegacy.Protocol;

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
}
