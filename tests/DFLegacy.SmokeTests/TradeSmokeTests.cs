using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TradeSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        CheckTradePackets(check);
        await Task.CompletedTask;
    }

    // 逐字段依据 .superpowers/sdd/trade-protocol-recon.md §2/§3：
    // NOTI 7/8 与 CMD 11 的尾部 u32 是客户端请求时的时间差回填位（T1 真机确认），
    // CMD 19 结果必须 10 字节且首字节为 resultFlag=1，否则操作者交易栏永远空。
    private static void CheckTradePackets(Action<bool, string> check)
    {
        check(GameProtocolEngine.RequestPeerCommand == 10
                && GameProtocolEngine.ResponsePeerCommand == 11
                && GameProtocolEngine.RequestPeerNotification == 7
                && GameProtocolEngine.ResponsePeerNotification == 8
                && GameProtocolEngine.ChangeItemTradeItemNotification == 15
                && GameProtocolEngine.CancelItemTradeNotification == 16
                && GameProtocolEngine.StateItemTradeNotification == 17
                && GameProtocolEngine.FinishItemTradeNotification == 18,
            "the trade ids match the client's tables: NOTI 7/8/15/16/17/18 and CMD 10/11");

        var request = GameProtocolEngine.CreateRequestPeerNotification(0x1234, 0x1234);
        check(request.Type == 0 && request.ProtocolId == 7
                && request.Payload.Length == 2 + 1 + 4
                && BinaryPrimitives.ReadUInt16LittleEndian(request.Payload.AsSpan(0, 2)) == 0x1234
                && request.Payload[2] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(3, 4)) == 0x1234,
            "NOTI 7 is [u16 requesterUid][u8 type 1][u32 reserved]");

        var accepted = GameProtocolEngine.CreateResponsePeerNotification(0x0102, 0, 0);
        check(accepted.Type == 0 && accepted.ProtocolId == 8
                && accepted.Payload.Length == 2 + 1 + 4 + 1
                && BinaryPrimitives.ReadUInt16LittleEndian(accepted.Payload.AsSpan(0, 2)) == 0x0102
                && accepted.Payload[2] == 1
                && accepted.Payload[^1] == 0,
            "NOTI 8 is [u16 accepterUid][u8 type 1][u32 reserved][u8 mode]");

        var openWindow = GameProtocolEngine.CreateResponsePeerReply(0x2222, 0);
        check(openWindow.Type == 1 && openWindow.ProtocolId == 11
                && openWindow.Payload.Length == 1 + 2 + 1 + 1
                && openWindow.Payload[0] == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(openWindow.Payload.AsSpan(1, 2)) == 0x2222
                && openWindow.Payload[3] == 1
                && openWindow.Payload[4] == 0,
            "a CMD 11 success is [1][u16 requesterUid][u8 1][u8 mode] and opens the accepter's trade window");

        var rejected = GameProtocolEngine.CreateResponsePeerError(GameProtocolEngine.ItemTradePeerType, 1);
        check(rejected.Type == 1 && rejected.ProtocolId == 11
                && rejected.Payload.Length == 3
                && rejected.Payload[0] == 0
                && rejected.Payload[1] == 100
                && rejected.Payload[2] == 1,
            "a CMD 11 failure is [0][u8 type 100][u8 code 1]: the client prints 对方不在线，或拒绝了您的交易请求");

        var panel = GameProtocolEngine.CreateChangeItemTradeItem(
            new GameItemTradeEntry(4, 3001, 7, 3, 21));
        check(panel.Type == 0 && panel.ProtocolId == 15
                && panel.Payload.Length == 2 + 2 + 4 + 1 + 2
                && BinaryPrimitives.ReadUInt16LittleEndian(panel.Payload.AsSpan(0, 2)) == 4
                && BinaryPrimitives.ReadUInt16LittleEndian(panel.Payload.AsSpan(2, 2)) == 3001
                && BinaryPrimitives.ReadUInt32LittleEndian(panel.Payload.AsSpan(4, 4)) == 7
                && panel.Payload[8] == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(panel.Payload.AsSpan(9, 2)) == 21,
            "NOTI 15 repeats the inventory record shape: [u16 slot][u16 itemId][u32 count][u8 state][u16 durability]");

        var cleared = GameProtocolEngine.CreateChangeItemTradeItem(
            new GameItemTradeEntry(4, GameProtocolEngine.ClearItemTradeSlot, 0, 0, 0));
        check(BinaryPrimitives.ReadUInt16LittleEndian(cleared.Payload.AsSpan(2, 2)) == 0xFFFF
                && BinaryPrimitives.ReadUInt32LittleEndian(cleared.Payload.AsSpan(4, 4)) == 0,
            "itemId 0xFFFF with count 0 clears that panel slot on the peer");

        var cancel = GameProtocolEngine.CreateCancelItemTrade(
        [
            new GameItemTradeCancelEntry(3, 0, 5),
            new GameItemTradeCancelEntry(0, 0, 0)
        ]);
        check(cancel.Type == 0 && cancel.ProtocolId == 16
                && cancel.Payload.Length == 2 + 2 * (2 + 1 + 2)
                && BinaryPrimitives.ReadUInt16LittleEndian(cancel.Payload.AsSpan(0, 2)) == 2
                && BinaryPrimitives.ReadUInt16LittleEndian(cancel.Payload.AsSpan(2, 2)) == 3
                && cancel.Payload[4] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(cancel.Payload.AsSpan(5, 2)) == 5
                && BinaryPrimitives.ReadUInt16LittleEndian(cancel.Payload.AsSpan(7, 2)) == 0,
            "NOTI 16 is [u16 count] + count x [u16 myPanelSlot][u8 destListType][u16 destSlot]");

        var locked = GameProtocolEngine.CreateStateItemTrade(0x0102, locked: true);
        check(locked.Type == 0 && locked.ProtocolId == 17
                && locked.Payload.Length == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(locked.Payload.AsSpan(0, 2)) == 0x0102
                && locked.Payload[2] == 1,
            "NOTI 17 is [u16 uid][u8 lock]: the receiver decides my/other by comparing uid");

        var finish = GameProtocolEngine.CreateFinishItemTrade(
        [
            new GameItemTradeDeliveryEntry(3, 7),
            new GameItemTradeDeliveryEntry(4, 9)
        ]);
        check(finish.Type == 0 && finish.ProtocolId == 18
                && finish.Payload.Length == 2 + 2 * (2 + 2)
                && BinaryPrimitives.ReadUInt16LittleEndian(finish.Payload.AsSpan(0, 2)) == 2
                && BinaryPrimitives.ReadUInt16LittleEndian(finish.Payload.AsSpan(2, 2)) == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(finish.Payload.AsSpan(4, 2)) == 7
                && BinaryPrimitives.ReadUInt16LittleEndian(finish.Payload.AsSpan(6, 2)) == 4
                && BinaryPrimitives.ReadUInt16LittleEndian(finish.Payload.AsSpan(8, 2)) == 9,
            "NOTI 18 is [u16 count] + count x [u16 otherPanelSlot][u16 myInventorySlot]");

        // 解析：载荷从 Body+2 起算，前 2 字节是序列号。
        var requestBody = new byte[2 + 2 + 1 + 4];
        BinaryPrimitives.WriteUInt16LittleEndian(requestBody.AsSpan(2, 2), 0x4321);
        requestBody[4] = 1;
        check(GameProtocolEngine.TryReadRequestPeer(requestBody, out var targetUid, out var peerType)
                && targetUid == 0x4321 && peerType == 1,
            "CMD 10 parses [u16 targetUid][u8 type 1]");
        var wrongTypeBody = (byte[])requestBody.Clone();
        wrongTypeBody[4] = 2;
        check(!GameProtocolEngine.TryReadRequestPeer(wrongTypeBody, out _, out _),
            "a CMD 10 for team/duel (type != 1) is not a trade request");
        check(!GameProtocolEngine.TryReadRequestPeer(requestBody[..4], out _, out _),
            "a truncated CMD 10 is rejected");

        check(GameProtocolEngine.TryReadResponsePeer(requestBody, out var requesterUid, out var responseType)
                && requesterUid == 0x4321 && responseType == 1,
            "CMD 11 shares the [u16 uid][u8 type 1] prefix");

        var stateBody = new byte[3];
        stateBody[2] = 3;
        check(GameProtocolEngine.TryReadItemTradeStateRequest(stateBody, out var state) && state == 3,
            "CMD 26 parses its single state byte (3 = final confirm)");
        stateBody[2] = 4;
        check(GameProtocolEngine.TryReadItemTradeStateRequest(stateBody, out var unknownState)
                && unknownState == 4,
            "an unknown CMD 26 state (4) still parses: the service decides how to log it");
        check(!GameProtocolEngine.TryReadItemTradeStateRequest([0, 0], out _),
            "a truncated CMD 26 is rejected");
    }
}
