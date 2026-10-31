using System.Buffers.Binary;
using System.Collections.Concurrent;
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
        CheckPlanner(check);
        await CheckStoreAsync(check);
        await CheckRequestFlowAsync(check);
        await CheckResponseRejectFlowAsync(check);
        await CheckMoveFlowAsync(check);
        await CheckPanelSlotAllocationAsync(check);
        await CheckMoveAfterCancelIsInertAsync(check);
        await CheckReattachCancelsStaleSessionAsync(check);
        await CheckSettlementFlowAsync(check);
        await CheckCancelFlowAsync(check);
        await CheckDetachClearsDeliveryMarkAsync(check);
        await CheckSettlementFailureAsync(check);
        await CheckSettlementGoldMissingAsync(check);
        await CheckSettlementSaveFailureAsync(check);
        await CheckConcurrentConfirmSettlesOnceAsync(check);
        await CheckConfirmAfterCancelIsInertAsync(check);
        await CheckConfirmAfterPeerDetachAsync(check);
        await CheckOfflinePeerAbortsSettlementAsync(check);
    }

    // 逐字段依据 .superpowers/sdd/trade-weight-check-recon.md §3.3/§5：
    // NOTI 7/8 与 CMD 10/11 尾部的 u32 是客户端自算的「当前负重 − 负重上限」（克，正常为负，
    // 二补码 u32），服务端必须原样透传（旧报告把它当"时间差/回传 uid"已作废）；
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

        var request = GameProtocolEngine.CreateRequestPeerNotification(0x1234, 0xFFFF8C1D);
        check(request.Type == 0 && request.ProtocolId == 7
                && request.Payload.Length == 2 + 1 + 4
                && BinaryPrimitives.ReadUInt16LittleEndian(request.Payload.AsSpan(0, 2)) == 0x1234
                && request.Payload[2] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(3, 4)) == 0xFFFF8C1D
                && request.Payload.AsSpan(3, 4).SequenceEqual(new byte[] { 0x1D, 0x8C, 0xFF, 0xFF }),
            "NOTI 7 is [u16 requesterUid][u8 type 1][u32 weightDelta], a negative delta kept as two's complement");

        var accepted = GameProtocolEngine.CreateResponsePeerNotification(0x0102, 0xFFFF8C55, 0);
        check(accepted.Type == 0 && accepted.ProtocolId == 8
                && accepted.Payload.Length == 2 + 1 + 4 + 1
                && BinaryPrimitives.ReadUInt16LittleEndian(accepted.Payload.AsSpan(0, 2)) == 0x0102
                && accepted.Payload[2] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(accepted.Payload.AsSpan(3, 4)) == 0xFFFF8C55
                && accepted.Payload[^1] == 0,
            "NOTI 8 is [u16 accepterUid][u8 type 1][u32 weightDelta][u8 mode]");

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

        // CMD 19 失败：真机反汇编（recon-2 §4.1，客户端 handler 0x4110FE）只读前 4 字节
        // [0, errorCode, srcType, destType]，errorCode 不在它的 switch 里就一个字都不弹。
        // 旧实现的 11 字节回显（[0, srcType, srcSlot, count, destType, destSlot]）会让客户端把
        // srcType 当 errorCode（=0）⇒ 静默失败，正是真机上"面板毫无变化"的直接原因。
        // 成功回包仍是 11 字节（Program.cs 的 CreateMoveItemSpaceReply 断言）。
        var moveFailure = GameProtocolEngine.CreateMoveItemSpaceFailure(
            GameProtocolEngine.MoveItemSpaceFailureDestinationOccupied,
            sourceListType: 0,
            destinationListType: 4);
        check(moveFailure.Type == 1
                && moveFailure.ProtocolId == GameProtocolEngine.MoveItemSpaceCommand
                && moveFailure.Payload.SequenceEqual(new byte[] { 0, 0x17, 0, 4 }),
            "a CMD 19 failure is exactly [0, errorCode, srcType, destType]: 4 bytes, no slot/count echo");

        // 解析：载荷从 Body+2 起算，前 2 字节是序列号；尾部 4 字节是「当前负重 − 负重上限」。
        var requestBody = new byte[2 + 2 + 1 + 4];
        BinaryPrimitives.WriteUInt16LittleEndian(requestBody.AsSpan(2, 2), 0x4321);
        requestBody[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(requestBody.AsSpan(5, 4), 0xFFFF8C1D);
        check(GameProtocolEngine.TryReadRequestPeer(
                    requestBody, out var targetUid, out var peerType, out var requestWeightDelta)
                && targetUid == 0x4321
                && peerType == 1
                && requestWeightDelta == 0xFFFF8C1D,
            "CMD 10 parses [u16 targetUid][u8 type 1][u32 weightDelta], negative delta included");
        var wrongTypeBody = (byte[])requestBody.Clone();
        wrongTypeBody[4] = 2;
        check(!GameProtocolEngine.TryReadRequestPeer(wrongTypeBody, out _, out _, out _),
            "a CMD 10 for team/duel (type != 1) is not a trade request");
        // 截断（< 9 字节）在旧实现里会被当成合法请求，现在必须拒绝并把三个 out 全部清掉：
        // 少 4 字节的负重差要么读越界要么是别的命令，绝不能拿默认值当负重回传。
        check(!GameProtocolEngine.TryReadRequestPeer(
                    requestBody[..8], out var shortUid, out _, out var shortDelta)
                && shortUid == 0
                && shortDelta == 0,
            "a truncated CMD 10 (missing the weight delta) is rejected and leaves the outs cleared");
        check(!GameProtocolEngine.TryReadRequestPeer(requestBody[..4], out _, out _, out _),
            "a truncated CMD 10 is rejected");
        var positiveDeltaBody = (byte[])requestBody.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(positiveDeltaBody.AsSpan(5, 4), 0x00004E20);
        check(GameProtocolEngine.TryReadRequestPeer(
                    positiveDeltaBody, out _, out _, out var overweightDelta)
                && overweightDelta == 0x00004E20,
            "a positive (already overweight) delta parses unsigned: the server passes it through as-is");

        check(GameProtocolEngine.TryReadResponsePeer(
                    requestBody, out var requesterUid, out var responseType, out var responseWeightDelta,
                    out var tradeAccepted)
                && requesterUid == 0x4321
                && responseType == 1
                && responseWeightDelta == 0xFFFF8C1D
                && tradeAccepted,
            "CMD 11 shares the [u16 uid][u8 type 1][u32 weightDelta] prefix and a 9-byte body means accept");
        // ★ 小窗【否】：与【是】同前缀，尾部多一个 u16 0x0064（`64 00`）→ 11 字节（G 卷 §2.3 的 0x821F77；
        // 真机日志 `0200 01 50730000 6400`）。旧实现只看前 7 字节，把点否当成接受。
        var refusedBody = new byte[11];
        Array.Copy(requestBody, refusedBody, requestBody.Length);
        refusedBody[9] = 0x64;
        refusedBody[10] = 0x00;
        check(GameProtocolEngine.TryReadResponsePeer(
                    refusedBody, out var refusedUid, out var refusedType, out _, out var refusedAccepted)
                && refusedUid == 0x4321
                && refusedType == 1
                && !refusedAccepted,
            "an 11-byte CMD 11 ending in 64 00 is the trade window's 【否】: still peerType 1, but not accepted");
        var wrongTypeBody2 = (byte[])requestBody.Clone();
        wrongTypeBody2[4] = 0;
        check(!GameProtocolEngine.TryReadResponsePeer(wrongTypeBody2, out _, out _, out _, out _),
            "a CMD 11 for team/duel (type != 1) is not a trade response");
        check(!GameProtocolEngine.TryReadResponsePeer(requestBody[..8], out _, out _, out _, out _),
            "a truncated CMD 11 is rejected");

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

    private static void CheckPlanner(Action<bool, string> check)
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"dflegacy-trade-planner-{Guid.NewGuid():N}");
        try
        {
            TradeFixtures.Create(fixtureRoot);
            var items = TradeFixtures.CreateItems(fixtureRoot);

            var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
            // 品质种子按仓库约定放 EquipmentQualitySeed（CountOrValue 仍记实例数量，负重按 1 件算）。
            var sword = new CharacterItemRecord(
                9, TradeFixtures.SwordItemId, 1, EquipmentQualitySeed: 0x1234, InstanceId: Guid.NewGuid());
            var banned = new CharacterItemRecord(10, TradeFixtures.BannedItemId, 1, InstanceId: Guid.NewGuid());
            var inventory = new Dictionary<ushort, CharacterItemRecord>
            {
                [5] = potion,
                [9] = sword,
                [10] = banned
            };
            var escrows = new List<ItemTradeEscrow>();

            var potionPlaced = ItemTradePlanner.TryPlaceEscrow(
                inventory, escrows, locked: false, sourceListType: 0, sourceSlot: 5,
                panelSlot: 3, count: 4, items, out var potionEscrow, out var failure);
            check(potionPlaced
                    && failure == ItemTradeFailure.None
                    && potionEscrow.Count == 4
                    && potionEscrow.InstanceId == potion.InstanceId
                    && potionEscrow.Snapshot == potion,
                "a stackable escrow keeps the server record and the requested partial count");
            if (potionPlaced)
            {
                // 放置失败时 escrow 为 null，入列表会让后续断言的失败变成 NRE 中断（而不是 FAIL 行）。
                escrows.Add(potionEscrow);
            }


            check(potionPlaced
                    && ItemTradePlanner.ProjectPanelEntry(potionEscrow, items)
                        is { Slot: 3, ItemId: TradeFixtures.PotionItemId, CountOrValue: 4 },
                "the peer sees the escrowed portion (4), not the whole stack (10)");

            check(ItemTradePlanner.TryPlaceEscrow(
                    inventory, escrows, locked: false, sourceListType: 0, sourceSlot: 9,
                    panelSlot: 4, count: 9, items, out var swordEscrow, out failure)
                    && swordEscrow.Count == 1,
                "a non-stackable escrow ignores the client count (9) and escrows the whole item");
            escrows.Add(swordEscrow);
            check(ItemTradePlanner.ProjectPanelEntry(swordEscrow, items).CountOrValue == 0x1234,
                "equipment keeps its quality seed as the wire count (T5)");

            check(!ItemTradePlanner.TryPlaceEscrow(
                        inventory, escrows, locked: false, sourceListType: 0, sourceSlot: 5,
                        panelSlot: 8, count: 4, items, out _, out failure)
                    && failure == ItemTradeFailure.SourceEscrowed,
                "the same inventory slot cannot be escrowed twice");
            check(!ItemTradePlanner.TryPlaceEscrow(
                        inventory, escrows, locked: false, sourceListType: 0, sourceSlot: 10,
                        panelSlot: 8, count: 1, items, out _, out failure)
                    && failure == ItemTradeFailure.NotTradeable,
                "an item the client itself calls untradeable is refused by CharacterItemSealing.CanTrade");
            check(!ItemTradePlanner.TryPlaceEscrow(
                        inventory, escrows, locked: false, sourceListType: 2, sourceSlot: 5,
                        panelSlot: 8, count: 4, items, out _, out failure)
                    && failure == ItemTradeFailure.InvalidSourceSpace,
                "this round only the main inventory may feed the trade panel");
            check(!ItemTradePlanner.TryPlaceEscrow(
                        inventory, escrows, locked: false, sourceListType: 0, sourceSlot: 5,
                        panelSlot: 4, count: 4, items, out _, out failure)
                    && failure == ItemTradeFailure.PanelSlotOccupied,
                "an occupied panel slot is refused instead of overwritten");
            check(!ItemTradePlanner.TryPlaceEscrow(
                        inventory, escrows, locked: false, sourceListType: 0, sourceSlot: 5,
                        panelSlot: 3, count: 11, items, out _, out failure)
                    && failure == ItemTradeFailure.InvalidCount,
                "a count above the server's stack is refused");
            check(!ItemTradePlanner.TryPlaceEscrow(
                        inventory, escrows, locked: false, sourceListType: 0, sourceSlot: 5,
                        panelSlot: 8, count: 0, items, out _, out failure)
                    && failure == ItemTradeFailure.InvalidCount,
                "a zero-count stack offer is refused");
            check(!ItemTradePlanner.TryPlaceEscrow(
                        inventory, escrows, locked: true, sourceListType: 0, sourceSlot: 5,
                        panelSlot: 8, count: 4, items, out _, out failure)
                    && failure == ItemTradeFailure.Locked,
                "a locked side cannot change its offer");

            check(ItemTradePlanner.TryReleaseEscrow(
                    escrows, locked: false, panelSlot: 3, out var releasedEscrow, out failure)
                    && failure == ItemTradeFailure.None
                    && releasedEscrow == potionEscrow,
                "a release returns the escrow sitting in that panel slot");
            check(!ItemTradePlanner.TryReleaseEscrow(
                        escrows, locked: false, panelSlot: 8, out _, out failure)
                    && failure == ItemTradeFailure.NotEscrowed,
                "a release for a panel slot that holds no escrow is refused");
            check(!ItemTradePlanner.TryReleaseEscrow(
                        escrows, locked: true, panelSlot: 3, out _, out failure)
                    && failure == ItemTradeFailure.Locked,
                "a locked side cannot take an offer back");

            var goldPlaced = ItemTradePlanner.TryPlaceGoldEscrow(
                escrows, locked: false, count: 1_000, currentGold: 5_000,
                out var goldEscrow, out failure);
            check(goldPlaced && goldEscrow.PanelSlot == 0 && goldEscrow.Count == 1_000,
                "gold is a panel slot 0 escrow with the offered amount");
            check(!ItemTradePlanner.TryPlaceGoldEscrow(
                        escrows, locked: false, count: 6_000, currentGold: 5_000,
                        out _, out failure)
                    && failure == ItemTradeFailure.InsufficientGold,
                "an offer above the character's gold is refused");

            // 真机放入的面板槽分配（recon-2 §4.2-1）：客户端 CMD 19 的 destSlot 恒为 0，
            // 服务端取最小空槽；客户端若送了一个 [3,26] 且空着的槽则采用它（兼容将来自带真实槽的请求）。
            check(ItemTradePlanner.TryAllocatePanelSlot([], requestedSlot: 0, out var firstAllocation)
                    && firstAllocation == 3,
                "the first panel slot allocated for a real-machine place (destSlot=0) is 3");
            check(ItemTradePlanner.TryAllocatePanelSlot(escrows, requestedSlot: 0, out var nextAllocation)
                    && nextAllocation == 5,
                "with 3 and 4 taken the next allocated slot is the smallest free one (5)");
            check(ItemTradePlanner.TryAllocatePanelSlot(escrows, requestedSlot: 4, out var occupiedRedirect)
                    && occupiedRedirect == 5,
                "a client slot that is already occupied falls back to the smallest free slot");
            check(ItemTradePlanner.TryAllocatePanelSlot(escrows, requestedSlot: 8, out var adoptedSlot)
                    && adoptedSlot == 8,
                "a plausible client slot (8) that is still free is adopted");
            var fullPanel = new List<ItemTradeEscrow>();
            for (var slot = (int)ItemTradePlanner.FirstItemPanelSlot;
                 slot <= ItemTradePlanner.LastItemPanelSlot;
                 slot++)
            {
                fullPanel.Add(new ItemTradeEscrow(
                    (ushort)slot, 0, 0, Guid.Empty, 0, new CharacterItemRecord(0, 0, 0)));
            }

            check(!ItemTradePlanner.TryAllocatePanelSlot(fullPanel, requestedSlot: 0, out _),
                "24 occupied panel slots leave no room: allocation fails instead of overwriting");

            // 失败码映射：客户端 handler 0x4110FE 的 switch（recon-2 §4.1）只认这几档，
            // 其它码会静默——所以"目标格被占 / 无权限 / 其它"必须分别落到 0x17 / 0x13 / 0x11。
            check(ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.PanelSlotOccupied) == 0x17
                    && ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.Locked) == 0x13
                    && ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.InvalidSourceSpace) == 0x13
                    && ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.NotTradeable) == 0x13
                    && ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.SourceMissing) == 0x11
                    && ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.InvalidCount) == 0x11
                    && ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.InsufficientGold) == 0x11
                    && ItemTradePlanner.ToMoveErrorCode(ItemTradeFailure.NotOpen) == 0x11,
                "move failures map onto the client's CMD 19 switch: 0x17 occupied, 0x13 forbidden, else 0x11");

            // 结算：A 给 4 瓶药 + 1000 金，B 给 1 把剑
            var characterA = TradeFixtures.CreateCharacter("阿尔法", gold: 5_000, inventory);
            var bravoSword = new CharacterItemRecord(
                7, TradeFixtures.SwordItemId, 0x9, InstanceId: Guid.NewGuid());
            var characterB = TradeFixtures.CreateCharacter(
                "布拉沃", gold: 7_000,
                new Dictionary<ushort, CharacterItemRecord> { [7] = bravoSword });
            var sideA = new ItemTradeSideState(characterA.Id, [potionEscrow, goldEscrow], 1_000);
            var sideB = new ItemTradeSideState(
                characterB.Id,
                [new ItemTradeEscrow(3, 0, 7, bravoSword.InstanceId, 1, bravoSword)],
                0);
            // 失败时 out plan 为 null：先断言成功，再解引用（否则失败会变成 NRE 中断而不是 FAIL 行）。
            var settlementPlanned = false;
            ItemTradeSettlementPlan plan = null!;
            if (potionPlaced && goldPlaced)
            {
                settlementPlanned = ItemTradePlanner.TryPlanSettlement(
                    new ItemTradeSettlementRequest(characterA, characterB, sideA, sideB),
                    items, TradeFixtures.CreateStats(fixtureRoot), out plan, out failure);
            }

            check(settlementPlanned,
                $"the settlement plan validates both sides (failure={failure})");
            // A 的背包 {5:药, 9:剑, 10:不可交易} 移除 4 瓶后仍占着 9/10，剑落进装备段第一个空格 11。
            check(settlementPlanned && plan.Initiator.Removals.Count == 1
                    && plan.Initiator.Removals[0] is { SourceSlot: 5, RemovedCount: 4, RemainingCount: 6 }
                    && plan.Initiator.GoldDelta == -1_000
                    && plan.Initiator.Deliveries.Count == 1
                    && plan.Initiator.Deliveries[0].Item.ItemId == TradeFixtures.SwordItemId
                    && plan.Initiator.Deliveries[0].DestinationSlot == 11
                    && plan.Initiator.Deliveries[0].Item.Slot == 11,
                "the receiver's plan removes only the escrowed portion and delivers a sword into the first free equipment slot");
            // B 的背包只有 slot 7 的剑；移除后材料段的第一个空格是 73。
            check(settlementPlanned && plan.Partner.Removals.Count == 1
                    && plan.Partner.Removals[0] is { SourceSlot: 7, RemovedCount: 1, RemainingCount: 0 }
                    && plan.Partner.GoldDelta == 1_000
                    && plan.Partner.Deliveries.Count == 1
                    && plan.Partner.Deliveries[0].Item.ItemId == TradeFixtures.PotionItemId
                    && plan.Partner.Deliveries[0].Item.CountOrValue == 4
                    && plan.Partner.Deliveries[0].DestinationSlot == 73,
                "the giver's plan removes the whole stack portion and receives the gold difference");

            // InstanceId 复核：托管物被别处动过 → 整笔失败
            var movedInventory = new Dictionary<ushort, CharacterItemRecord>(inventory)
            {
                [5] = potion with { InstanceId = Guid.NewGuid() }
            };
            check(potionPlaced
                    && !ItemTradePlanner.TryPlanSettlement(
                        new ItemTradeSettlementRequest(
                            TradeFixtures.CreateCharacter("阿尔法", 5_000, movedInventory),
                            characterB,
                            sideA,
                            sideB),
                        items, TradeFixtures.CreateStats(fixtureRoot), out _, out failure)
                    && failure == ItemTradeFailure.SettlementEscrowMissing,
                "a settlement whose escrowed instance vanished is refused before any write");

            // 原地改状态/耐久（强化、属性修改）不换 InstanceId 也不换数量：只看身份与数量的复核
            // 会把旧快照交付出去，因此这类改动同样必须整笔中止（仍报 SettlementEscrowMissing）。
            var restatedInventory = new Dictionary<ushort, CharacterItemRecord>(inventory)
            {
                [5] = potion with { State = 1 }
            };
            check(potionPlaced
                    && !ItemTradePlanner.TryPlanSettlement(
                        new ItemTradeSettlementRequest(
                            TradeFixtures.CreateCharacter("阿尔法", 5_000, restatedInventory),
                            characterB,
                            sideA,
                            sideB),
                        items, TradeFixtures.CreateStats(fixtureRoot), out _, out failure)
                    && failure == ItemTradeFailure.SettlementEscrowMissing,
                "a settlement whose escrowed item changed state in place is refused (state/durability recheck)");

            var rewearedInventory = new Dictionary<ushort, CharacterItemRecord>(inventory)
            {
                [5] = potion with { Durability = 7 }
            };
            check(potionPlaced
                    && !ItemTradePlanner.TryPlanSettlement(
                        new ItemTradeSettlementRequest(
                            TradeFixtures.CreateCharacter("阿尔法", 5_000, rewearedInventory),
                            characterB,
                            sideA,
                            sideB),
                        items, TradeFixtures.CreateStats(fixtureRoot), out _, out failure)
                    && failure == ItemTradeFailure.SettlementEscrowMissing,
                "a settlement whose escrowed item changed durability in place is refused (state/durability recheck)");

            // 结算失败码逐条：托管数量不足 / 本侧金币不足 / 收货方负重超限 / 收货方无空位 / 金币溢出。
            // 这些场景自建角色，不依赖上面的托管结果。
            var shortPotion = new CharacterItemRecord(
                5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
            var shortA = TradeFixtures.CreateCharacter(
                "阿尔法", gold: 5_000,
                new Dictionary<ushort, CharacterItemRecord> { [5] = shortPotion });
            check(!ItemTradePlanner.TryPlanSettlement(
                    new ItemTradeSettlementRequest(
                        shortA,
                        characterB,
                        new ItemTradeSideState(
                            shortA.Id,
                            [new ItemTradeEscrow(3, 0, 5, shortPotion.InstanceId, 11, shortPotion)],
                            0),
                        sideB),
                    items, TradeFixtures.CreateStats(fixtureRoot), out _, out failure)
                    && failure == ItemTradeFailure.SettlementEscrowMissing,
                "an escrow that asks for more than the stack still holds is refused");

            var poorPotion = new CharacterItemRecord(
                5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
            var poorA = TradeFixtures.CreateCharacter(
                "阿尔法", gold: 500,
                new Dictionary<ushort, CharacterItemRecord> { [5] = poorPotion });
            check(!ItemTradePlanner.TryPlanSettlement(
                    new ItemTradeSettlementRequest(
                        poorA,
                        characterB,
                        new ItemTradeSideState(
                            poorA.Id,
                            [new ItemTradeEscrow(3, 0, 5, poorPotion.InstanceId, 4, poorPotion)],
                            1_000),
                        sideB),
                    items, TradeFixtures.CreateStats(fixtureRoot), out _, out failure)
                    && failure == ItemTradeFailure.SettlementGoldMissing,
                "gold that vanished after the escrow (mailed away) aborts the settlement");

            var heavy = new CharacterItemRecord(
                9, TradeFixtures.HeavyItemId, 1, InstanceId: Guid.NewGuid());
            var heavyCarrier = TradeFixtures.CreateCharacter(
                "查理", gold: 1_000,
                new Dictionary<ushort, CharacterItemRecord> { [9] = heavy });
            var heavyReceiver = TradeFixtures.CreateCharacter(
                "德尔塔", gold: 1_000, new Dictionary<ushort, CharacterItemRecord>());
            var stats = TradeFixtures.CreateStats(fixtureRoot);
            var deliveredWeight = CharacterInventoryPlanner.CalculateCarriedWeight(
                [heavy with { Slot = 9 }], [], [], items);
            var receiverWeightLimit = CharacterInventoryPlanner.CalculateCarriedWeightLimit(
                stats.Get(heavyReceiver).InventoryLimit, [], items);
            check(deliveredWeight > receiverWeightLimit
                    && !ItemTradePlanner.TryPlanSettlement(
                        new ItemTradeSettlementRequest(
                            heavyReceiver,
                            heavyCarrier,
                            new ItemTradeSideState(heavyReceiver.Id, [], 0),
                            new ItemTradeSideState(
                                heavyCarrier.Id,
                                [new ItemTradeEscrow(3, 0, 9, heavy.InstanceId, 1, heavy)],
                                0)),
                        items, stats, out _, out failure)
                    && failure == ItemTradeFailure.SettlementWeightExceeded,
                $"a delivery past the receiver's weight limit is refused ({deliveredWeight} > {receiverWeightLimit})");

            var fullEquipment = new Dictionary<ushort, CharacterItemRecord>();
            for (var slot = (int)CharacterInventoryLayout.EquipmentSlotStart;
                 slot < CharacterInventoryLayout.EquipmentSlotEnd;
                 slot++)
            {
                fullEquipment[(ushort)slot] = new CharacterItemRecord(
                    (ushort)slot, TradeFixtures.SwordItemId, 1, InstanceId: Guid.NewGuid());
            }

            var crowded = TradeFixtures.CreateCharacter("回声", gold: 1_000, fullEquipment);
            check(!ItemTradePlanner.TryPlanSettlement(
                    new ItemTradeSettlementRequest(
                        crowded,
                        characterB,
                        new ItemTradeSideState(crowded.Id, [], 0),
                        sideB),
                    items, TradeFixtures.CreateStats(fixtureRoot), out _, out failure)
                    && failure == ItemTradeFailure.SettlementNoSpace,
                "a receiver whose equipment span is full refuses the sword delivery (no stacking, no quick-slot fallback)");

            var rich = TradeFixtures.CreateCharacter(
                "福克斯", gold: int.MaxValue - 100, new Dictionary<ushort, CharacterItemRecord>());
            check(!ItemTradePlanner.TryPlanSettlement(
                    new ItemTradeSettlementRequest(
                        rich,
                        characterB,
                        new ItemTradeSideState(rich.Id, [], 0),
                        sideB with { OfferedGold = 1_000 }),
                    items, TradeFixtures.CreateStats(fixtureRoot), out _, out failure)
                    && failure == ItemTradeFailure.SettlementGoldOverflow,
                "a gold result above int.MaxValue is refused instead of wrapping");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static async Task CheckStoreAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var sword = new CharacterItemRecord(
            9, TradeFixtures.SwordItemId, 1,
            EquipmentQualitySeed: 0x4321, InstanceId: Guid.NewGuid());
        check(await harness.SetInventoryAsync("阿尔法", gold: 5_000, [potion]) is not null
                && await harness.SetInventoryAsync("布拉沃", gold: 7_000, [sword]) is not null,
            "the two trade parties start with a persisted inventory");

        var commit = await harness.Store.CommitItemTradeAsync(
            new ItemTradeCommitRequest(
                harness.IdOf("阿尔法"), "alice",
                harness.IdOf("布拉沃"), "bobby",
                [
                    new ItemTradeEscrow(3, 0, 5, potion.InstanceId, 4, potion),
                    new ItemTradeEscrow(0, 0, 0, Guid.Empty, 1_000, new CharacterItemRecord(0, 0, 1_000))
                ],
                [new ItemTradeEscrow(3, 0, 9, sword.InstanceId, 1, sword)],
                InitiatorOfferedGold: 1_000,
                PartnerOfferedGold: 0));
        check(commit.Committed && commit.Failure == ItemTradeCommitFailure.None
                && commit.Initiator is not null && commit.Partner is not null,
            "a valid trade commits in one call");

        var aliceInventory = commit.Initiator!.Inventory!;
        var bravoInventory = commit.Partner!.Inventory!;
        check(aliceInventory.Single(item => item.Slot == 5).CountOrValue == 6
                && aliceInventory.Single(item => item.InstanceId == sword.InstanceId) is { Slot: 9 }
                && commit.Initiator.Gold == 4_000,
            "the giver keeps the unescrowed portion, receives the peer's item in a free slot and loses the offered gold");
        check(bravoInventory.All(item => item.ItemId != TradeFixtures.SwordItemId)
                && bravoInventory.Single(item => item.InstanceId == potion.InstanceId) is { CountOrValue: 4 }
                && commit.Partner.Gold == 8_000,
            "the receiver loses the escrowed item, receives exactly the escrowed portion and gains the gold");

        var reloaded = new JsonGameStore(
            harness.Options,
            NullLogger<JsonGameStore>.Instance,
            itemCatalog: harness.Items,
            statCatalog: harness.Stats);
        await reloaded.InitializeAsync(CancellationToken.None);
        var persisted = await reloaded.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        check(persisted is not null
                && persisted.Gold == 4_000
                && persisted.Inventory!.Single(item => item.Slot == 5).CountOrValue == 6,
            "the settlement is written to state.json, not only kept in memory");

        // 盲写守门：交付未确认前，被动方的任何盲写保存都必须被拒（否则复制）
        check(harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法"))
                && harness.Store.IsTradeDeliveryPending(harness.IdOf("布拉沃")),
            "both characters carry a pending trade delivery until their session installs it");
        check(await harness.Store.SaveCharacterItemSpacesAsync(
                    "alice", harness.IdOf("阿尔法"), 4_000,
                    [new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: potion.InstanceId)],
                    [], [], CancellationToken.None) is null,
            "a blind inventory save from a stale session is refused while the delivery is pending");
        harness.Store.AcknowledgeTradeDelivery(harness.IdOf("阿尔法"), commit.TradeId);
        check(!harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法"))
                && await harness.Store.SaveCharacterItemSpacesAsync(
                    "alice", harness.IdOf("阿尔法"), 4_000,
                    commit.Initiator.Inventory!,
                    [], [], CancellationToken.None) is not null,
            "after the session acknowledges, the same save path works again");

        // 失败路径：托管物已被动过 → 不写盘
        var before = File.ReadAllText(harness.Options.DataPath);
        var failed = await harness.Store.CommitItemTradeAsync(
            new ItemTradeCommitRequest(
                harness.IdOf("布拉沃"), "bobby",
                harness.IdOf("阿尔法"), "alice",
                [new ItemTradeEscrow(3, 0, 5, Guid.NewGuid(), 1, potion)],
                [],
                InitiatorOfferedGold: 0,
                PartnerOfferedGold: 0));
        check(!failed.Committed && failed.Failure == ItemTradeCommitFailure.EscrowMissing
                && File.ReadAllText(harness.Options.DataPath) == before,
            "a settlement whose escrowed instance vanished writes nothing at all (atomic all-or-nothing)");

        // 保存失败 = 结算没成功：异常上抛、内存回滚、两侧都不许留下未安装交付标记。
        var crashRoot = Path.Combine(Path.GetTempPath(), $"dflegacy-trade-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(crashRoot);
        try
        {
            TradeFixtures.Create(crashRoot);
            var crashOptions = new ServerOptions
            {
                DataPath = Path.Combine(crashRoot, "state.json"),
                ScriptPvfPath = string.Empty,
                SkillScriptPath = crashRoot
            };
            var crashStore = new JsonGameStore(
                crashOptions,
                NullLogger<JsonGameStore>.Instance,
                itemCatalog: TradeFixtures.CreateItems(crashRoot),
                statCatalog: TradeFixtures.CreateStats(crashRoot));
            await crashStore.CreateAccountAsync(
                new CreateAccountRequest("carol", "password", OicqCode: "123456"),
                CancellationToken.None);
            await crashStore.CreateAccountAsync(
                new CreateAccountRequest("dave", "password", OicqCode: "123456"),
                CancellationToken.None);
            var carol = (await crashStore.CreateCharacterAsync(
                "carol", new CreateCharacterRequest("查理", GrowType: 3), CancellationToken.None)).Character!;
            var dave = (await crashStore.CreateCharacterAsync(
                "dave", new CreateCharacterRequest("德尔塔", GrowType: 3), CancellationToken.None)).Character!;
            var crashPotion = new CharacterItemRecord(
                5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
            var crashSword = new CharacterItemRecord(
                9, TradeFixtures.SwordItemId, 1, InstanceId: Guid.NewGuid());
            await crashStore.SaveCharacterItemSpacesAsync(
                "carol", carol.Id, 5_000, [crashPotion], [], [], CancellationToken.None);
            await crashStore.SaveCharacterItemSpacesAsync(
                "dave", dave.Id, 7_000, [crashSword], [], [], CancellationToken.None);

            // 让下一次保存必然失败：把存档目录换成一个同名文件，Directory.CreateDirectory 会抛。
            Directory.Delete(crashRoot, recursive: true);
            File.WriteAllText(crashRoot, "blocked");

            Exception? saveFailure = null;
            try
            {
                await crashStore.CommitItemTradeAsync(
                    new ItemTradeCommitRequest(
                        carol.Id, "carol",
                        dave.Id, "dave",
                        [new ItemTradeEscrow(3, 0, 5, crashPotion.InstanceId, 4, crashPotion)],
                        [new ItemTradeEscrow(3, 0, 9, crashSword.InstanceId, 1, crashSword)],
                        InitiatorOfferedGold: 0,
                        PartnerOfferedGold: 0));
            }
            catch (Exception exception)
            {
                saveFailure = exception;
            }

            var rolledBackInitiator = await crashStore.GetCharacterAsync("carol", carol.Id, CancellationToken.None);
            var rolledBackPartner = await crashStore.GetCharacterAsync("dave", dave.Id, CancellationToken.None);
            check(saveFailure is IOException
                    && !crashStore.IsTradeDeliveryPending(carol.Id)
                    && !crashStore.IsTradeDeliveryPending(dave.Id)
                    && rolledBackInitiator is not null
                    && rolledBackInitiator.Gold == 5_000
                    && rolledBackInitiator.Inventory!.Single(item => item.Slot == 5).CountOrValue == 10
                    && rolledBackPartner is not null
                    && rolledBackPartner.Gold == 7_000
                    && rolledBackPartner.Inventory!.Single(item => item.Slot == 9).ItemId == TradeFixtures.SwordItemId,
                $"a settlement whose save throws rolls both characters back and leaves no pending marker (failure={saveFailure})");
        }
        finally
        {
            if (File.Exists(crashRoot))
            {
                File.Delete(crashRoot);
            }
            else if (Directory.Exists(crashRoot))
            {
                Directory.Delete(crashRoot, recursive: true);
            }
        }
    }

    private static async Task CheckRequestFlowAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();

        // TradeHarness.CreateAsync 只挂通道、不进镇：先验证"在线但不在任何城镇区域"的
        // 请求被拒（设计 §2.4：双方都在城镇区域是放行条件之一），且目标完全无感知。
        harness.Clear("阿尔法");
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 2, weightDelta: 0, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload[0] == 0
                && harness.Of("布拉沃").Count == 0,
            "a requester outside any town area is refused before the target ever sees a NOTI 7");

        await harness.EnterTownAsync("阿尔法", townId: 1, areaId: 2, areaUserId: 1);
        await harness.EnterTownAsync("布拉沃", townId: 1, areaId: 2, areaUserId: 2);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        // 服务端独立限流：同一角色 3 秒内的第二条 CMD 10 直接丢弃（客户端 CMD 10 的 u32 是
        // 负重差、不是防抖时间戳，服务端不能依赖它）。上面那条"未进镇"的请求也会打
        // 限流时间戳，所以这一相位前先重置。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 0x4321, weightDelta: 0, CancellationToken.None);
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 0x4321, weightDelta: 0, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1,
            "two back-to-back requests inside the throttle window answer exactly one failure");

        harness.Clear("阿尔法");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        // 真机负值（-29.667 kg）：CMD 10 的这个 u32 必须逐字节进入 NOTI 7——被请求方的锁定自检
        // 读的正是它，回传 uid 或 0 就是"负缺 0.001kg"那个真机 bug。
        const uint RequesterWeightDelta = 0xFFFF8C1D;
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), 2, RequesterWeightDelta, CancellationToken.None);
        var alicePackets = harness.Of("阿尔法");
        var bravoPackets = harness.Of("布拉沃");
        check(alicePackets.Count == 0,
            "the initiator gets no reply: the client prints 已发出交易请求 locally");
        check(bravoPackets.Count == 1
                && bravoPackets[0].Type == 0
                && bravoPackets[0].ProtocolId == GameProtocolEngine.RequestPeerNotification
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoPackets[0].Payload.AsSpan(0, 2)) == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(bravoPackets[0].Payload.AsSpan(3, 4))
                    == RequesterWeightDelta
                && bravoPackets[0].Payload.AsSpan(3, 4).SequenceEqual(new byte[] { 0x1D, 0x8C, 0xFF, 0xFF }),
            "the target receives exactly one NOTI 7: [requesterUid][1][the requester's weight delta bytes]");

        // 同一时间只能一笔：已在会话中的角色（含等待应答的 Requested）再发请求被静默丢弃——
        // 既不给自己回失败包，也不打扰对手。清空两侧收件箱后再断言，否则前一笔的 NOTI 7
        // 会掩盖后一笔多发出来的包。
        // 旧断言恒真：上一行刚清空阿尔法信箱，对空列表 All() 永远为 true。改成可失败的计数
        // 断言，并给布拉沃信箱一个非空前提：上一相位阿尔法发起的请求在它那里留下了一条 NOTI 7，
        // 保留它、只清阿尔法信箱；被静默丢弃的第二次请求（布拉沃 → 阿尔法）不得在任一侧新增包。
        harness.Clear("阿尔法");
        var bravoBeforeSecondRequest = harness.Of("布拉沃");
        await harness.Service.HandleRequestAsync(
            harness.IdOf("布拉沃"), 1, weightDelta: 0, CancellationToken.None);
        var bravoAfterSecondRequest = harness.Of("布拉沃");
        check(bravoBeforeSecondRequest.Count == 1
                && bravoBeforeSecondRequest[0].ProtocolId == GameProtocolEngine.RequestPeerNotification
                && bravoAfterSecondRequest.Count == 1
                && bravoAfterSecondRequest[0].ProtocolId == GameProtocolEngine.RequestPeerNotification
                && harness.Of("阿尔法").Count == 0,
            "a second request while one is pending is dropped silently: no new packet on either side");

        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        // 接受方 CMD 11 的 u32 同样要逐字节进入 NOTI 8：请求方靠它判断锁定后接受方会不会超重。
        const uint AccepterWeightDelta = 0xFFFF8C55;
        await harness.Service.HandleResponseAsync(
            harness.IdOf("布拉沃"), 1, accept: true, AccepterWeightDelta, CancellationToken.None);
        var alphaTail = harness.Of("阿尔法");
        var bravoTail = harness.Of("布拉沃");
        check(alphaTail.Count == 1
                && alphaTail[0].Type == 0
                && alphaTail[0].ProtocolId == GameProtocolEngine.ResponsePeerNotification
                && BinaryPrimitives.ReadUInt16LittleEndian(alphaTail[0].Payload.AsSpan(0, 2)) == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(alphaTail[0].Payload.AsSpan(3, 4))
                    == AccepterWeightDelta
                && alphaTail[0].Payload.AsSpan(3, 4).SequenceEqual(new byte[] { 0x55, 0x8C, 0xFF, 0xFF })
                && alphaTail[0].Payload[^1] == 0,
            "the requester opens the window with NOTI 8 [accepterUid][1][accepter's weight delta][mode 0]");
        check(bravoTail.Count == 1
                && bravoTail[0].Type == 1
                && bravoTail[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && bravoTail[0].Payload[0] == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoTail[0].Payload.AsSpan(1, 2)) == 1,
            "the accepter opens the window with CMD 11 [1, requesterUid, 1, mode]");

        // 上一笔已经 Open：先用离区取消把它收掉（Task 4 还没有 CMD 26 的接口），
        // 否则"同时只能一笔"会让下面的新请求直接被丢掉。
        await harness.Service.CancelForAreaChangeAsync(harness.IdOf("阿尔法"), CancellationToken.None);
        check(harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification)
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification),
            "leaving the area cancels the open trade and both sides are told (NOTI 16)");

        // 拒绝/超时：30 秒后请求方收到失败提示，双方可再次发起
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 2, weightDelta: 0, CancellationToken.None);
        harness.Clear("阿尔法");
        check(await harness.Service.SweepExpiredAsync(
                    DateTimeOffset.UtcNow + TimeSpan.FromSeconds(31),
                    CancellationToken.None) == 1,
            "the request window expires after 30 seconds");
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].Type == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload[0] == 0
                && harness.Of("阿尔法")[0].Payload[1] == GameProtocolEngine.ItemTradePeerType
                && harness.Of("阿尔法")[0].Payload[2] == 1,
            "an expired request answers CMD 11 [0, 100, 1]: 对方不在线，或拒绝了您的交易请求");
        check(harness.Of("布拉沃").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.CancelItemTradeNotification),
            "an expired request never sends NOTI 16: no panel was ever open");

        // 目标离线 / 不在同镇 / 自己
        harness.Clear("阿尔法");
        await harness.EnterTownAsync("布拉沃", townId: 3, areaId: 4, areaUserId: 2);
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 2, weightDelta: 0, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload[0] == 0,
            "a target in another town is refused with the same CMD 11 failure");

        // 自己：客户端本来就把菜单项拦掉了（0x139/0x13D），改造客户端发来时按"目标不可达"
        // 回同一条失败包——静默只留给被拉黑那一档（设计稿 §2.6）。
        // 布拉沃的收件箱里还留着上一条 30 秒超时请求的 NOTI 7，这里一并清掉再断言。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 1, weightDelta: 0, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload[0] == 0
                && harness.Of("布拉沃").Count == 0,
            "a request against your own uid answers the same CMD 11 failure and never reaches anyone else");

        harness.Clear("阿尔法");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 0x4321, weightDelta: 0, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].Payload[0] == 0,
            "an unknown area user id is refused");

        // 被拉黑（设计 §2.6 的两半）：目标侧完全无感知（没有 NOTI 7），发起方 30 秒后收到
        // 与"对方没理"完全同形的 CMD 11 [0,100,1]——否则"立刻就能重发"会成为拉黑的旁证。
        await harness.EnterTownAsync("布拉沃", townId: 1, areaId: 2, areaUserId: 2);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Store.AddBlacklistAsync("bobby", "阿尔法", CancellationToken.None)
                == BlacklistUpdateFailure.None,
            "the target puts the requester on its blacklist");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(harness.IdOf("阿尔法"), 2, weightDelta: 0, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 0,
            "a blacklisted target never learns about the request: not a single packet, not even NOTI 7");
        check(await harness.Service.SweepExpiredAsync(
                    DateTimeOffset.UtcNow + TimeSpan.FromSeconds(31),
                    CancellationToken.None) == 1,
            "the silently dropped request still holds the requester's own slot for 30 seconds");
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].Type == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload[0] == 0
                && harness.Of("阿尔法")[0].Payload[1] == GameProtocolEngine.ItemTradePeerType
                && harness.Of("阿尔法")[0].Payload[2] == 1
                && harness.Of("布拉沃").Count == 0,
            "the blacklisted request fails exactly like an ignored one (CMD 11 [0, 100, 1]) and the target stays silent");

        // 对方的名额没有被占：被拉黑期间，目标自己照样能发起交易（发起方的键才登记）。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("布拉沃"));
        await harness.Service.HandleRequestAsync(harness.IdOf("布拉沃"), 1, weightDelta: 0, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].Type == 0
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.RequestPeerNotification
                && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(0, 2)) == 2,
            "the blacklisted target can still start its own trade: the dropped request never held its slot");

        // 离区钩子（Task 6 的掉线/离区路径也走这里）：先取消这笔在途请求，再真的离区，
        // 票据被用掉后目录里不再有这名角色。
        await harness.LeaveTownAsync("布拉沃");
        check(harness.Of("布拉沃").Count == 1
                && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("布拉沃")[0].Payload[0] == 0
                && !harness.Directory.TryGetSnapshot(harness.IdOf("布拉沃"), out _),
            "LeaveTownAsync cancels the in-flight request window and really leaves the directory");
    }

    /// <summary>
    /// ★ 回归（真机「点【否】也成立交易」）：交易应答窗【否】发的是 11 字节 CMD 11——与【是】同前缀、
    /// 尾部多一个 `u16 0x0064`（`64 00`），由协议层判成 `accepted: false`（G 卷 §2.1/§2.3）。
    /// **承重点**是 EntranceService 的交易应答分支：必须把判出的 accepted 原样传给
    /// <see cref="ItemTradeService.HandleResponseAsync"/>（`accept: accepted`，曾硬编码 `true`）。
    /// 这个用例拿真机形状的字节走完整条链——判据（TryReadResponsePeer）→ 分派
    /// （EntranceService.TryHandleTradeResponseAsync）→ 服务语义（`!accept` → 取消流程）：
    /// 请求方恰好收一条 `CMD 11 [0, 100, 1]`、接受方零包（窗口从未打开，因此没有 NOTI 8/16），
    /// 会话就地摘除、双方立刻能重新发起；同一分派对 9 字节【是】必须照样开窗。
    /// **变异验证**：把 `TryHandleTradeResponseAsync` 里的 `accept: accepted` 改回硬编码 `true`，
    /// 前两条断言即变红（会走 Open 分支：请求方收 NOTI 8、接受方收 CMD 11 [1, ...]）。
    /// </summary>
    private static async Task CheckResponseRejectFlowAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", townId: 1, areaId: 2, areaUserId: 1);
        await harness.EnterTownAsync("布拉沃", townId: 1, areaId: 2, areaUserId: 2);

        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), 2, weightDelta: 0xFFFF8C1D, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 1
                && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.RequestPeerNotification,
            "the refusal test starts from a real pending request window (NOTI 7)");
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        // 真机拒绝包（G 卷 §2.3 的 0x821F77）：[u16 seq][u16 uid=1][u8 选择子 1=交易][u32 负重差] + u16 0x0064。
        // 第 5 字节（下沉到 Body 的 index 4）是**选择子** 1，不是失败包里的 ItemTradePeerType(100)。
        var acceptedResponse = new byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(acceptedResponse.AsSpan(2, 2), 1);
        acceptedResponse[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(acceptedResponse.AsSpan(5, 4), 0xFFFF8C55);
        var refusedResponse = new byte[11];
        Array.Copy(acceptedResponse, refusedResponse, acceptedResponse.Length);
        refusedResponse[9] = 0x64;
        refusedResponse[10] = 0x00;

        check(await EntranceService.TryHandleTradeResponseAsync(
                harness.Service,
                harness.IdOf("布拉沃"),
                refusedResponse,
                CancellationToken.None),
            "the 11-byte CMD 11 (peerType 1, tail 64 00) takes the trade-response branch");

        check(harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].Type == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("阿尔法")[0].Payload.SequenceEqual(
                    new byte[] { 0, GameProtocolEngine.ItemTradePeerType, 1 }),
            "【否】 answers the requester exactly one CMD 11 [0, 100, 1]: the trade never opens");
        check(harness.Of("布拉沃").Count == 0,
            "the refuser gets zero packets: no window was opened, so no NOTI 8 / CMD 11 [1, ...] / NOTI 16");

        // 会话已被摘除：重置限流后同一对角色立刻能再发起（对方再收一条 NOTI 7）。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.Service.HandleRequestAsync(
            harness.IdOf("阿尔法"), 2, weightDelta: 0, CancellationToken.None);
        check(harness.Of("布拉沃").Count == 1
                && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.RequestPeerNotification
                && harness.Of("阿尔法").Count == 1,
            "the refused session is really gone: an immediate re-request reaches the target");

        // 同一分派必须对【是】照样开窗（9 字节、无 `64 00` 尾）：防止"把判据写成一律拒绝"的过度修复。
        check(await EntranceService.TryHandleTradeResponseAsync(
                harness.Service,
                harness.IdOf("布拉沃"),
                acceptedResponse,
                CancellationToken.None),
            "the 9-byte CMD 11 (no tail) still takes the same trade-response branch");
        check(harness.Of("阿尔法").Count == 2
                && harness.Of("阿尔法")[1].Type == 0
                && harness.Of("阿尔法")[1].ProtocolId == GameProtocolEngine.ResponsePeerNotification
                && harness.Of("布拉沃").Count == 2
                && harness.Of("布拉沃")[1].ProtocolId == GameProtocolEngine.ResponsePeerCommand
                && harness.Of("布拉沃")[1].Payload[0] == 1,
            "the 9-byte 【是】 still opens the window: NOTI 8 for the requester, CMD 11 [1, ...] for the accepter");
    }

    private static async Task CheckMoveFlowAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        await harness.SetInventoryAsync("阿尔法", 5_000, []);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        // 6 号格另放一组药水：「同一来源格不能押两次」（规划器 SourceEscrowed / 设计 §2.4），
        // 「客户端自报 itemId 不可信」要在一格尚未被托管的来源上验证。
        var spare = new CharacterItemRecord(6, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var sword = new CharacterItemRecord(
            9, TradeFixtures.SwordItemId, 1,
            EquipmentQualitySeed: 0x1234, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion, spare, sword]);
        await harness.OpenTradeAsync();

        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 5, itemId: TradeFixtures.PotionItemId, count: 4,
                destinationListType: 4, destinationSlot: 3,
                CancellationToken.None),
            "escrowing four potions succeeds");
        var echo = harness.Of("阿尔法");
        var peer = harness.Of("布拉沃");
        // 操作者收到 CMD 19 结果 + 两条 NOTI 17（设计 §2.3.3：双方锁定清零、各发两条）。
        check(echo.Count == 3
                && echo[0].Type == 1
                && echo[0].ProtocolId == GameProtocolEngine.MoveItemSpaceCommand
                && echo[0].Payload.Length == 11
                && echo[0].Payload[0] == 1
                && echo[0].Payload[1] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(echo[0].Payload.AsSpan(2, 2)) == 5
                && BinaryPrimitives.ReadUInt32LittleEndian(echo[0].Payload.AsSpan(4, 4)) == 4
                && echo[0].Payload[8] == 4
                && BinaryPrimitives.ReadUInt16LittleEndian(echo[0].Payload.AsSpan(9, 2)) == 3,
            "the actor's own panel is driven only by the CMD 19 result [1, 0, 5, 4, 4, 3]");
        check(peer.Count == 3
                && peer[0].ProtocolId == GameProtocolEngine.ChangeItemTradeItemNotification
                && BinaryPrimitives.ReadUInt16LittleEndian(peer[0].Payload.AsSpan(0, 2)) == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(peer[0].Payload.AsSpan(2, 2)) == TradeFixtures.PotionItemId
                && BinaryPrimitives.ReadUInt32LittleEndian(peer[0].Payload.AsSpan(4, 4)) == 4
                && peer.Count(packet => packet.ProtocolId == GameProtocolEngine.StateItemTradeNotification) == 2
                && peer.Where(packet => packet.ProtocolId == GameProtocolEngine.StateItemTradeNotification)
                    .All(packet => packet.Payload[2] == 0),
            "the peer sees NOTI 15 with only the escrowed portion, then two NOTI 17 clearing both locks");
        check(echo.Count(packet => packet.ProtocolId == GameProtocolEngine.StateItemTradeNotification) == 2,
            "the actor also gets its two lock-cleared NOTI 17 (the client clears both locally as well)");

        // 客户端自报的 itemId 不可信：服务端按自己的 (0,6) 记录写入
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 6, itemId: TradeFixtures.SwordItemId, count: 2,
                destinationListType: 4, destinationSlot: 4,
                CancellationToken.None),
            "a mismatched itemId in the request does not break the move: the server trusts its own slot record");
        check(BinaryPrimitives.ReadUInt16LittleEndian(
                    harness.Of("布拉沃")[0].Payload.AsSpan(2, 2)) == TradeFixtures.PotionItemId,
            "the peer is told about the server's item, not the client's claim");

        // 装备（不可堆叠）：客户端报的 count 被忽略，整件押上
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 9, itemId: TradeFixtures.SwordItemId, count: 1,
                destinationListType: 4, destinationSlot: 5,
                CancellationToken.None)
            && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.ChangeItemTradeItemNotification
            && BinaryPrimitives.ReadUInt32LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(4, 4)) == 0x1234,
            "an equipment escrow keeps the whole item and the peer sees the quality seed as the wire count (T5)");
        harness.Clear("阿尔法");
        check(!await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 5, itemId: TradeFixtures.PotionItemId, count: 99,
                destinationListType: 4, destinationSlot: 6,
                CancellationToken.None)
            && harness.Of("阿尔法")[0].Payload.Length == 4
            && harness.Of("阿尔法")[0].Payload[0] == 0
            && harness.Of("阿尔法")[0].Payload[1] == GameProtocolEngine.MoveItemSpaceFailureNotFound,
            "a count above the server's stack is refused with a 4-byte [0, 0x11, ...] failure");

        // 金币
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 0, itemId: 0, count: 1_200,
                destinationListType: 4, destinationSlot: 0,
                CancellationToken.None),
            "offering 1200 gold succeeds");
        check(harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.ChangeItemTradeItemNotification
                && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(0, 2)) == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(2, 2)) == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(4, 4)) == 1_200,
            "gold is announced as panel slot 0 / itemId 0 / count = amount");
        harness.Clear("阿尔法");
        check(!await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 0, itemId: 0, count: 99_999,
                destinationListType: 4, destinationSlot: 0,
                CancellationToken.None)
            && harness.Of("阿尔法")[0].Payload.SequenceEqual(
                new byte[] { 0, GameProtocolEngine.MoveItemSpaceFailureNotFound, 0, 4 }),
            "an offer above the character's gold is refused as a 4-byte [0, 0x11, 0, 4] failure");

        // 取回
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 4, sourceSlot: 3, itemId: TradeFixtures.PotionItemId, count: 4,
                destinationListType: 0, destinationSlot: 5,
                CancellationToken.None)
            && harness.Of("阿尔法")[0].Payload[0] == 1
            && harness.Of("阿尔法")[0].Payload[1] == 4
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(2, 2))
                == GameProtocolEngine.ClearItemTradeSlot,
            "taking the potions back clears the peer's slot with itemId 0xFFFF");

        // 冻结查询
        check(harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 9)
                && harness.Service.IsEscrowedInstance(harness.IdOf("阿尔法"), sword.InstanceId)
                && !harness.Service.IsEscrowed(harness.IdOf("布拉沃"), 0, 9),
            "the frozen-slot query answers per character, not globally");
    }

    /// <summary>
    /// 真机勘察 recon-2 §3.2/§4.2-1：客户端拖物品进交易栏时 CMD 19 的 destinationSlot 恒为 0
    /// （空位搜索的结果根本不进包，分配权留给服务端）。这里逐条验证服务端分配：
    /// ① 连续放入分配 3、4，CMD 19 结果与对端 NOTI 15 都带该槽；客户端送了 3..26 的槽时空着就采用、
    /// 被占就退回最小空槽；② 用分配出的槽取回 → 对应托管项移除、对端收 NOTI 15 itemId=0xFFFF；
    /// ③ 金币仍走 0 号格（[1,0,0,金额,4,0]）；④ 失败回包是 4 字节 [0, errorCode, srcType, destType]，
    /// 数量超量 = 0x11、锁定 = 0x13，且失败时对端零包。
    /// </summary>
    private static async Task CheckPanelSlotAllocationAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var first = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var second = new CharacterItemRecord(6, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var third = new CharacterItemRecord(8, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var fourth = new CharacterItemRecord(10, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [first, second, third, fourth]);
        await harness.OpenTradeAsync();

        // ① 真机形态：destinationSlot = 0（客户端从不送 3..26）→ 服务端分配 3。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 5, itemId: TradeFixtures.PotionItemId, count: 4,
                destinationListType: 4, destinationSlot: 0,
                CancellationToken.None)
            && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.MoveItemSpaceCommand
            && harness.Of("阿尔法")[0].Payload.Length == 11
            && harness.Of("阿尔法")[0].Payload[0] == 1
            && harness.Of("阿尔法")[0].Payload[8] == 4
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(9, 2)) == 3,
            "a real-machine place (destinationSlot = 0) is accepted and the CMD 19 result carries the allocated slot 3");
        var firstPeerPackets = harness.Of("布拉沃");
        // 对端信箱 = NOTI 15 + 两条锁定清零的 NOTI 17（设计 §2.3.3）。
        check(firstPeerPackets.Count == 3
                && firstPeerPackets[0].ProtocolId == GameProtocolEngine.ChangeItemTradeItemNotification
                && BinaryPrimitives.ReadUInt16LittleEndian(firstPeerPackets[0].Payload.AsSpan(0, 2)) == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(firstPeerPackets[0].Payload.AsSpan(2, 2))
                    == TradeFixtures.PotionItemId
                && BinaryPrimitives.ReadUInt32LittleEndian(firstPeerPackets[0].Payload.AsSpan(4, 4)) == 4,
            "the peer's NOTI 15 uses the same allocated slot 3 (player-visible first cell)");

        // 第二件物品仍送 0 → 下一个最小空槽 4。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 6, itemId: TradeFixtures.PotionItemId, count: 10,
                destinationListType: 4, destinationSlot: 0,
                CancellationToken.None)
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(9, 2)) == 4
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(0, 2)) == 4,
            "a second place lands in the next free slot 4 (CMD 19 result and peer NOTI 15 agree)");

        // 客户端给了被占用的槽（3）→ 退回最小空槽 5；给了空着的槽（7）→ 采用 7。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 8, itemId: TradeFixtures.PotionItemId, count: 10,
                destinationListType: 4, destinationSlot: 3,
                CancellationToken.None)
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(9, 2)) == 5
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(0, 2)) == 5,
            "an occupied client slot falls back to the smallest free slot (5) so two items never overlap");
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 10, itemId: TradeFixtures.PotionItemId, count: 10,
                destinationListType: 4, destinationSlot: 7,
                CancellationToken.None)
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(9, 2)) == 7
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(0, 2)) == 7,
            "a free client-supplied slot (7) is adopted as-is");

        // ② 取回必须用服务端分配出的槽（3）：托管移除 + 对端清格 0xFFFF。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: GameProtocolEngine.ItemTradeListType, sourceSlot: 3,
                itemId: TradeFixtures.PotionItemId, count: 4,
                destinationListType: 0, destinationSlot: 5,
                CancellationToken.None)
            && harness.Of("阿尔法")[0].Payload[0] == 1
            && harness.Of("阿尔法")[0].Payload[1] == 4
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(2, 2)) == 3
            && harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.ChangeItemTradeItemNotification
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(0, 2)) == 3
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(2, 2))
                == GameProtocolEngine.ClearItemTradeSlot,
            "taking back the allocated slot 3 clears exactly that slot on the peer (itemId 0xFFFF)");
        check(!harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 5)
                && harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 6)
                && harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 10),
            "only the released escrow is gone: the others stay frozen");

        // ③ 金币保持原样：destSlot=0 是金币在面板列表里的真实槽位，不进服务端分配。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 0, itemId: 0, count: 1_200,
                destinationListType: 4, destinationSlot: 0,
                CancellationToken.None)
            && harness.Of("阿尔法")[0].Payload.Length == 11
            && harness.Of("阿尔法")[0].Payload[0] == 1
            && harness.Of("阿尔法")[0].Payload[1] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(2, 2)) == 0
            && BinaryPrimitives.ReadUInt32LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(4, 4)) == 1_200
            && harness.Of("阿尔法")[0].Payload[8] == 4
            && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("阿尔法")[0].Payload.AsSpan(9, 2)) == 0,
            "gold keeps the 11-byte success [1, 0, 0, 1200, 4, 0]: panel slot 0 is its real slot");
        check(harness.Of("布拉沃")[0].ProtocolId == GameProtocolEngine.ChangeItemTradeItemNotification
                && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(0, 2)) == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(2, 2)) == 0
                && BinaryPrimitives.ReadUInt32LittleEndian(harness.Of("布拉沃")[0].Payload.AsSpan(4, 4)) == 1_200,
            "the peer's gold NOTI 15 stays slot 0 / itemId 0 / count = amount");

        // ④ 失败回包：4 字节 [0, errorCode, srcType, destType]，对端零包。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(!await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 5, itemId: TradeFixtures.PotionItemId, count: 99,
                destinationListType: 4, destinationSlot: 0,
                CancellationToken.None)
            && harness.Of("阿尔法").Count == 1
            && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.MoveItemSpaceCommand
            && harness.Of("阿尔法")[0].Payload.SequenceEqual(
                new byte[] { 0, GameProtocolEngine.MoveItemSpaceFailureNotFound, 0, 4 })
            && harness.Of("布拉沃").Count == 0,
            "a count above the stack answers the real 4-byte failure [0, 0x11, 0, 4] and never notifies the peer");

        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(!await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"),
                sourceListType: 0, sourceSlot: 5, itemId: TradeFixtures.PotionItemId, count: 10,
                destinationListType: 4, destinationSlot: 0,
                CancellationToken.None)
            && harness.Of("阿尔法").Count == 1
            && harness.Of("阿尔法")[0].Payload.SequenceEqual(
                new byte[] { 0, GameProtocolEngine.MoveItemSpaceFailureNotAllowed, 0, 4 })
            && harness.Of("布拉沃").Count == 0,
            "a locked side answers [0, 0x13, 0, 4]: the client prints 您没有移动的权限 instead of staying silent");
    }

    /// <summary>
    /// 评审 Important 2：托管表的改动此前完全在 <c>session.Sync</c> 之外——<c>HandleMoveAsync</c>
    /// 在 <c>await store.GetCharacterAsync</c> 之前判会话状态、await 之后才 Add，对手的连接线程可以
    /// 在同一时刻取消会话，于是「服务端已取消、客户端却收到 CMD 19 成功 + NOTI 15」。
    /// 公开 API 无法把取消精确插进那个 await 窗口（需要测试专用的 store 钩子；本轮不改 store 接口），
    /// 因此这里锁住等价的顺序语义：取消之后到达的放入/取回必须回失败包、对端零包、托管表已清空。
    /// 覆盖强度如实标注：修复后由「锁内复核 + 锁内改动」保证任何交错都不会双写；本用例只确定性
    /// 覆盖「取消先完成 → 放入不得落地」这一半。
    /// </summary>
    private static async Task CheckMoveAfterCancelIsInertAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        check(await harness.Service.HandleMoveAsync(
                harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3,
                CancellationToken.None),
            "the potion is escrowed before the peer cancels (positive control)");

        // 对手关窗取消（CMD 26=2）：此刻阿尔法的客户端可能还有一条放入在途。
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 2, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        var lateMove = await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 4, 3, TradeFixtures.PotionItemId, 4, 0, 5,
            CancellationToken.None);
        check(!lateMove
                && harness.Of("阿尔法").Count == 1
                && harness.Of("阿尔法")[0].ProtocolId == GameProtocolEngine.MoveItemSpaceCommand
                && harness.Of("阿尔法")[0].Payload.SequenceEqual(
                    new byte[] { 0, GameProtocolEngine.MoveItemSpaceFailureNotFound, 4, 0 })
                && harness.Of("布拉沃").Count == 0
                && !harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 5),
            "a move arriving after the cancel answers only its own CMD 19 failure, never a peer NOTI 15, and keeps no escrow");

        var alphaAfter = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        check(alphaAfter is not null && alphaAfter.Inventory is not null
                && alphaAfter.Gold == 5_000
                && alphaAfter.Inventory.Single(item => item.Slot == 5).CountOrValue == 10,
            "the late move changed nothing in the store: the potion never left the owner");
    }

    /// <summary>
    /// 评审 Minor 3：顶号/重登时旧会话可能活到 5 分钟扫描——期间该角色「忙」、旧槽位被冻结。
    /// 新通道挂载时若发现该角色已有会话且会话 id 不同，必须先取消旧会话（通知存活方）。
    /// </summary>
    private static async Task CheckReattachCancelsStaleSessionAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        await harness.Service.AttachChannelAsync(
            "alice",
            "阿尔法",
            harness.IdOf("阿尔法"),
            areaUserId: 1,
            sessionId: Guid.NewGuid(),
            _ => ValueTask.CompletedTask,
            CancellationToken.None);
        check(harness.Of("布拉沃").Count(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification) == 1
                && !harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 5),
            "attaching a new channel for a character cancels the stale session and tells the survivor");

        // 新通道可用：旧会话已被清掉，重新发起一笔交易照常成立。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.OpenTradeAsync();
        check(harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.RequestPeerNotification)
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.ResponsePeerCommand),
            "the freshly attached channel can start a new trade (the character is not stuck busy)");
    }

    private static async Task CheckSettlementFlowAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var sword = new CharacterItemRecord(
            9, TradeFixtures.SwordItemId, 1,
            EquipmentQualitySeed: 0x1234, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, [sword]);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleMoveAsync(
            harness.IdOf("布拉沃"), 0, 9, TradeFixtures.SwordItemId, 1, 4, 4, CancellationToken.None);
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 0, 0, 1_000, 4, 0, CancellationToken.None);

        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        check(harness.Of("布拉沃").Count(packet =>
                    packet.ProtocolId == GameProtocolEngine.StateItemTradeNotification
                    && packet.Payload[2] == 1) == 1,
            "locking broadcasts one NOTI 17 about the locker to the peer");
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);

        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        var singleConfirm = await harness.Service.HandleStateAsync(
            harness.IdOf("阿尔法"), 3, CancellationToken.None);
        check(!singleConfirm.SettlementApplied
                && harness.Of("阿尔法").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.FinishItemTradeNotification)
                && harness.Of("布拉沃").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.FinishItemTradeNotification),
            "one side's final confirm does not settle anything by itself");

        var repeatedConfirm = await harness.Service.HandleStateAsync(
            harness.IdOf("阿尔法"), 3, CancellationToken.None);
        check(!repeatedConfirm.SettlementApplied
                && harness.Of("阿尔法").Count(packet =>
                        packet.ProtocolId == GameProtocolEngine.SetItemTradeStateCommand) == 1
                && harness.Of("阿尔法").Single(packet =>
                    packet.ProtocolId == GameProtocolEngine.SetItemTradeStateCommand) is { Payload: [0, 1] },
            "confirming twice from the same side is refused with CMD 26 [0,1] instead of re-settling");

        var settled = await harness.Service.HandleStateAsync(
            harness.IdOf("布拉沃"), 3, CancellationToken.None);
        check(settled.SettlementApplied
                && settled.ReplacementState is not null
                && settled.ReplacementState.Id == harness.IdOf("布拉沃")
                && settled.ReplacementState.Gold == 8_000,
            "the second final confirm settles the trade and hands the confirming side its own new record");

        var alphaFinish = harness.Of("阿尔法").FirstOrDefault(packet =>
            packet.ProtocolId == GameProtocolEngine.FinishItemTradeNotification);
        var bravoFinish = harness.Of("布拉沃").FirstOrDefault(packet =>
            packet.ProtocolId == GameProtocolEngine.FinishItemTradeNotification);
        check(alphaFinish is not null && bravoFinish is not null
                && BinaryPrimitives.ReadUInt16LittleEndian(alphaFinish.Payload.AsSpan(0, 2)) == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(alphaFinish.Payload.AsSpan(2, 2)) == 4
                && BinaryPrimitives.ReadUInt16LittleEndian(alphaFinish.Payload.AsSpan(4, 2)) == 9,
            "the giver is told (peer panel slot 4 -> my inventory slot 9) so the client merges the sword in");
        check(bravoFinish is not null
                && bravoFinish.Payload.Length == 2 + 4
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoFinish.Payload.AsSpan(2, 2)) == 3
                && BinaryPrimitives.ReadUInt16LittleEndian(bravoFinish.Payload.AsSpan(4, 2)) == 73,
            "the receiver is told (peer panel slot 3 -> my inventory slot 73, the first free material slot)");
        // NOTI 14 载荷 = [u8 listType][u16 count][u16 slot][u16 itemId][u32 CountOrValue]…
        check(harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.UpdateItemListNotification
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(7, 4)) == 4_000)
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.UpdateItemListNotification
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(7, 4)) == 8_000),
            "each side receives an authoritative gold refresh (NOTI 14 slot 0) after the settlement");

        // 设计 §2.3.5/§2.5.5：结算后**双方**都要拿到权威全量背包刷新（NOTI 13 含金币字段）。
        // 被动方（阿尔法）由会话顶部安装待安装状态时刷新；确认方（布拉沃）此前完全没有刷新包，
        // 必须在结算成功时就地补一条，否则它的背包只能靠后续的增量包自愈。
        check(harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.ItemListNotification
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(7, 4)) == 8_000),
            "the confirming side also receives the authoritative full inventory refresh (NOTI 13 with gold)");

        var alphaAfter = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        var bravoAfter = await harness.Store.GetCharacterAsync("bobby", harness.IdOf("布拉沃"), CancellationToken.None);
        check(alphaAfter is not null && alphaAfter.Inventory is not null
                && bravoAfter is not null && bravoAfter.Inventory is not null
                && alphaAfter.Gold == 4_000
                && alphaAfter.Inventory.Single(item => item.Slot == 5).CountOrValue == 6
                && alphaAfter.Inventory.Single(item => item.Slot == 9).InstanceId == sword.InstanceId
                && bravoAfter.Gold == 8_000
                && bravoAfter.Inventory.Single(item => item.Slot == 73).InstanceId == potion.InstanceId,
            "the persisted state matches the wire result on both sides");

        var takenInstall = harness.Service.TryTakePendingInstall(harness.IdOf("阿尔法"), out var install);
        var installInventory = install?.State.Inventory;
        check(takenInstall
                && install is not null
                && install.TradeId != Guid.Empty
                && installInventory is not null
                && installInventory.Single(item => item.Slot == 9).InstanceId == sword.InstanceId
                && !harness.Store.IsTradeDeliveryPending(harness.IdOf("布拉沃"))
                && harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法")),
            "only the passive side keeps a pending install (its own session will consume it)");
        check(!harness.Service.TryTakePendingInstall(harness.IdOf("布拉沃"), out _),
            "the confirming side has nothing left to install: its answer was already applied");
        if (takenInstall && install is not null)
        {
            harness.Store.AcknowledgeTradeDelivery(harness.IdOf("阿尔法"), install.TradeId);
        }

        check(!harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法")),
            "acknowledging the install clears the blind-write guard");
    }

    private static async Task CheckCancelFlowAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var bravoPotion = new CharacterItemRecord(6, TradeFixtures.PotionItemId, 3, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, [bravoPotion]);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 2, CancellationToken.None);
        check(harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification)
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification),
            "a cancel sends NOTI 16 to both sides (each returns its own panel items)");
        var alphaCancel = harness.Of("阿尔法").FirstOrDefault(packet =>
            packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification);
        check(alphaCancel is not null
                && alphaCancel.Payload.Length == 7
                && BinaryPrimitives.ReadUInt16LittleEndian(alphaCancel.Payload.AsSpan(0, 2)) == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(alphaCancel.Payload.AsSpan(2, 2)) == 3
                && alphaCancel.Payload[4] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(alphaCancel.Payload.AsSpan(5, 2)) == 5,
            "the cancel entry points back at the original (list type 0, slot 5)");
        var afterCancel = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        check(afterCancel is not null && afterCancel.Inventory is not null
                && afterCancel.Inventory.Single(item => item.Slot == 5).CountOrValue == 10
                && afterCancel.Gold == 5_000,
            "a cancelled trade changes nothing in the store: the items never left the owner");

        // 空闲超时：5 分钟后双方自动取消（第二次发起前要清限流时间戳，否则 CMD 10 被丢弃）
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.OpenTradeAsync();
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        check(await harness.Service.SweepExpiredAsync(
                    DateTimeOffset.UtcNow + TimeSpan.FromMinutes(6),
                    CancellationToken.None) == 1
                && harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification)
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification),
            "an idle trade window expires after five minutes and both sides are told");

        // 掉线：另一方收到取消，物品不丢（同样先清限流时间戳）
        var sessionId = harness.SessionIdOf("阿尔法");
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("布拉沃"), 0, 6, TradeFixtures.PotionItemId, 1, 4, 3, CancellationToken.None);
        harness.Clear("布拉沃");
        await harness.Service.DetachChannelAsync(harness.IdOf("阿尔法"), sessionId, CancellationToken.None);
        // 恰好一条：结构上由 RemoveSession 的原子摘除保证，重复取消会漏出第二条。
        check(harness.Of("布拉沃").Count(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification) == 1,
            "when one side disconnects the survivor is told to close the window and take its items back (exactly one NOTI 16)");
        var survivor = await harness.Store.GetCharacterAsync("bobby", harness.IdOf("布拉沃"), CancellationToken.None);
        check(survivor is not null && survivor.Inventory is not null
                && survivor.Gold == 7_000
                && survivor.Inventory.Single(item => item.Slot == 6).CountOrValue == 3,
            "the survivor's escrowed potion is still in his inventory: the store never moved it");
    }

    /// <summary>
    /// Task 4 评审的遗留项：掉线时除了摘掉待安装状态，还必须清掉 store 的「未确认交付」标记，
    /// 否则带着未安装交付掉线的角色会被 JsonGameStore 的全部盲写保存永久拒绝。
    /// </summary>
    private static async Task CheckDetachClearsDeliveryMarkAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);
        var settled = await harness.Service.HandleStateAsync(
            harness.IdOf("布拉沃"), 3, CancellationToken.None);
        check(settled.SettlementApplied
                && harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法")),
            "the passive side carries the store's pending-delivery mark after a settlement");

        await harness.Service.DetachChannelAsync(
            harness.IdOf("阿尔法"), harness.SessionIdOf("阿尔法"), CancellationToken.None);
        check(!harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法")),
            "a disconnect clears the pending-delivery mark so the character is never refused by every blind write");
    }

    /// <summary>
    /// 结算失败（收货方落点所在类别段全满）：确认方收 CMD 26 [0,4]，双方 NOTI 16 整笔中止，
    /// store、守门标记与会话都回到「没发生过」。
    /// </summary>
    private static async Task CheckSettlementFailureAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        // 布拉沃的材料段 73..104 全满：药水无处可放 → 规划器 SettlementNoSpace → CMD 26 错误码 4。
        var fullMaterial = Enumerable.Range(73, 32)
            .Select(slot => new CharacterItemRecord(
                (ushort)slot, TradeFixtures.PotionItemId, 1, InstanceId: Guid.NewGuid()))
            .ToList();
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, fullMaterial);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        var failed = await harness.Service.HandleStateAsync(
            harness.IdOf("布拉沃"), 3, CancellationToken.None);
        check(!failed.SettlementApplied
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.SetItemTradeStateCommand
                    && packet.Payload is [0, 4])
                && harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification)
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification),
            "a receiver with a full category segment aborts the whole trade: CMD 26 [0,4] and NOTI 16 to both");

        var alphaAfter = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        var bravoAfter = await harness.Store.GetCharacterAsync("bobby", harness.IdOf("布拉沃"), CancellationToken.None);
        check(alphaAfter is not null && alphaAfter.Inventory is not null
                && bravoAfter is not null && bravoAfter.Inventory is not null
                && alphaAfter.Gold == 5_000
                && alphaAfter.Inventory.Single(item => item.Slot == 5).CountOrValue == 10
                && bravoAfter.Gold == 7_000
                && bravoAfter.Inventory.Count(item => item.Slot >= 73) == 32
                && !harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法"))
                && !harness.Store.IsTradeDeliveryPending(harness.IdOf("布拉沃")),
            "a failed settlement leaves both inventories, both gold purses, and both delivery gates untouched");

        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        check(harness.Of("阿尔法").Count == 0 && harness.Of("布拉沃").Count == 0,
            "the aborted session is gone: a later CMD 26 gets no trade packets at all");
    }

    /// <summary>
    /// 结算失败（托管之后金币被别的途径扣走）：错误码走「其余=1」而不是空位/负重的 4，整笔照常中止。
    /// </summary>
    private static async Task CheckSettlementGoldMissingAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 0, 0, 1_000, 4, 0, CancellationToken.None);
        // 托管之后、确认之前把 4900 金币寄走（模拟邮件寄钱这类旁路写入）：结算必须失败，不能赔钱。
        await harness.SetInventoryAsync("阿尔法", 100, [potion]);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        var failed = await harness.Service.HandleStateAsync(
            harness.IdOf("布拉沃"), 3, CancellationToken.None);
        check(!failed.SettlementApplied
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.SetItemTradeStateCommand
                    && packet.Payload is [0, 1])
                && harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification)
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification),
            "gold spent behind the escrow's back aborts the trade with CMD 26 [0,1], not the space code");

        var alphaAfter = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        check(alphaAfter is not null && alphaAfter.Inventory is not null
                && alphaAfter.Gold == 100
                && alphaAfter.Inventory.Single(item => item.Slot == 5).CountOrValue == 10
                && !harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法")),
            "the aborted trade neither pays out nor moves the escrowed potion");
    }

    /// <summary>
    /// 评审 Important 1：结算的「提交 + 双方发包 + 登记待安装/ack」原先全是裸 await。磁盘 IO 失败
    /// （store 还原内存后重新抛出）会一路冒到会话循环的 catch 并断开连接，而会话此刻是 Settling，
    /// 所有退出路径都被守卫拦着——`_sessions` 里永远留着它：对手收不到 NOTI 16/18（客户端挂在交易窗），
    /// 两个角色永久"已在交易中"，托管物品在所有冻结路径上永久冻结（重登也是，会话按 characterId 键控）。
    /// 这里把存档目录换成一个同名文件，让结算落库必然 IOException，断言：确认方收 CMD 26 [0,1]、
    /// 双方各收一条 NOTI 16、会话真的被摘除（IsEscrowed 归 false）、store 与提交前一致、无守门标记残留。
    /// </summary>
    private static async Task CheckSettlementSaveFailureAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        var sword = new CharacterItemRecord(9, TradeFixtures.SwordItemId, 1, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, [sword]);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleMoveAsync(
            harness.IdOf("布拉沃"), 0, 9, TradeFixtures.SwordItemId, 1, 4, 4, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);

        // 让结算时的落库必然失败：把存档目录换成一个同名文件（SaveLockedAsync 的
        // Directory.CreateDirectory 会抛 IOException）。harness 的 store 已全在内存，其余断言不受影响。
        System.IO.Directory.Delete(harness.Root, recursive: true);
        File.WriteAllText(harness.Root, "blocked");
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        // 修复前这里会直接抛出 store 的 IOException（结算整段是裸 await），于是用例以未处理异常
        // 中断而不是 FAIL 行；先接住它，让"异常逃出结算"本身成为一条可读的失败断言。
        ItemTradeStateOutcome? outcome = null;
        Exception? escapedSettlementException = null;
        try
        {
            outcome = await harness.Service.HandleStateAsync(
                harness.IdOf("布拉沃"), 3, CancellationToken.None);
        }
        catch (Exception exception)
        {
            escapedSettlementException = exception;
        }

        check(escapedSettlementException is null
                && outcome is not null
                && !outcome.SettlementApplied
                && outcome.ReplacementState is null
                && harness.Of("布拉沃").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.SetItemTradeStateCommand
                    && packet.Payload is [0, 1])
                && harness.Of("阿尔法").Count(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification) == 1
                && harness.Of("布拉沃").Count(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification) == 1
                && harness.Of("阿尔法").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.FinishItemTradeNotification)
                && harness.Of("布拉沃").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.FinishItemTradeNotification)
                && !harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 5)
                && !harness.Service.IsEscrowed(harness.IdOf("布拉沃"), 0, 9)
                && !harness.Store.IsTradeDeliveryPending(harness.IdOf("阿尔法"))
                && !harness.Store.IsTradeDeliveryPending(harness.IdOf("布拉沃")),
            $"a settlement whose save throws is aborted on the wire and the session is really removed (not stuck in Settling; escaped={escapedSettlementException})");

        var alphaAfter = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        var bravoAfter = await harness.Store.GetCharacterAsync("bobby", harness.IdOf("布拉沃"), CancellationToken.None);
        check(alphaAfter is not null && alphaAfter.Inventory is not null
                && bravoAfter is not null && bravoAfter.Inventory is not null
                && alphaAfter.Gold == 5_000
                && alphaAfter.Inventory.Single(item => item.Slot == 5).CountOrValue == 10
                && bravoAfter.Gold == 7_000
                && bravoAfter.Inventory.Single(item => item.Slot == 9).InstanceId == sword.InstanceId,
            "the failed settlement rolled both characters back: the store matches the pre-commit state");

        // 会话确实从 _sessions 里摘掉了：之后的任何交易命令都不再产生包（也不会永久占着两个角色）。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        var later = await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 3, CancellationToken.None);
        check(!later.SettlementApplied
                && harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0,
            "after the aborted settlement neither side can re-enter the dead session");
    }

    /// <summary>
    /// 评审反例①：纯金币交易没有可「缺失」的托管物品，第二次提交照样能重新规划成功——
    /// 双方最终确认若不互斥，金币会被转两次。这里用栅栏让两个确认真正并发，断言只结算一次。
    /// （修复后结果与调度无关、稳定成立；缺陷版本则需要恰好撞上竞态窗口才会双结算。）
    /// </summary>
    private static async Task CheckConcurrentConfirmSettlesOnceAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        await harness.SetInventoryAsync("阿尔法", 5_000, []);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 0, 0, 1_000, 4, 0, CancellationToken.None);
        await harness.Service.HandleMoveAsync(
            harness.IdOf("布拉沃"), 0, 0, 0, 2_000, 4, 0, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        using var start = new Barrier(2);
        var confirmations = await Task.WhenAll(
            Task.Run(() =>
            {
                start.SignalAndWait();
                return harness.Service.HandleStateAsync(
                    harness.IdOf("阿尔法"), 3, CancellationToken.None);
            }),
            Task.Run(() =>
            {
                start.SignalAndWait();
                return harness.Service.HandleStateAsync(
                    harness.IdOf("布拉沃"), 3, CancellationToken.None);
            }));

        var alphaAfter = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        var bravoAfter = await harness.Store.GetCharacterAsync("bobby", harness.IdOf("布拉沃"), CancellationToken.None);
        check(confirmations.Count(outcome => outcome.SettlementApplied) == 1
                && harness.Of("阿尔法").Count(packet =>
                    packet.ProtocolId == GameProtocolEngine.FinishItemTradeNotification) == 1
                && harness.Of("布拉沃").Count(packet =>
                    packet.ProtocolId == GameProtocolEngine.FinishItemTradeNotification) == 1
                && alphaAfter is not null && bravoAfter is not null
                && alphaAfter.Gold == 6_000
                && bravoAfter.Gold == 6_000,
            "two simultaneous final confirms settle once: one delivery set and exactly one gold transfer each way");

        // 确定性兜底：即使并发一次也没撞上竞态窗口，结算后的再次确认也必须是无包无结算的空操作。
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        var lateConfirm = await harness.Service.HandleStateAsync(
            harness.IdOf("阿尔法"), 3, CancellationToken.None);
        check(!lateConfirm.SettlementApplied
                && harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0,
            "confirming again after the settlement is a no-op: no settlement and no packets at all");
    }

    /// <summary>
    /// 设计 §2.4⑥：确认前必须复核双方会话仍存活。取消之后到达的确认不得结算，也不得再发包。
    /// 公开 API 无法稳定复现「取消之前就取到会话引用」的陈旧引用路径（那条路径只可能出现在服务
    /// 内部与取消并发时），因此这里先用确定性用例锁住顺序语义，再用栅栏并发让两种顺序都至少
    /// 覆盖一次，断言「NOTI 16 与结算互斥」。
    /// </summary>
    private static async Task CheckConfirmAfterCancelIsInertAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);

        // ① 顺序路径：取消先完成，之后任何一方的确认都必须是无包无结算的空操作。
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 2, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");
        var lateAlpha = await harness.Service.HandleStateAsync(
            harness.IdOf("阿尔法"), 3, CancellationToken.None);
        var lateBravo = await harness.Service.HandleStateAsync(
            harness.IdOf("布拉沃"), 3, CancellationToken.None);
        var lateAlphaAfter = await harness.Store.GetCharacterAsync(
            "alice", harness.IdOf("阿尔法"), CancellationToken.None);
        check(!lateAlpha.SettlementApplied
                && !lateBravo.SettlementApplied
                && harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0
                && lateAlphaAfter is not null && lateAlphaAfter.Inventory is not null
                && lateAlphaAfter.Gold == 5_000
                && lateAlphaAfter.Inventory.Single(item => item.Slot == 5).CountOrValue == 10,
            "a final confirm arriving after the cancel is inert: no settlement, no packets, escrow untouched");

        // ② 并发路径：取消与确认同时冲进去，两种先后都不能既发 NOTI 16 又结算。
        harness.Service.ResetRequestThrottleForTests(harness.IdOf("阿尔法"));
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        using var start = new Barrier(2);
        var outcomes = await Task.WhenAll(
            Task.Run(() =>
            {
                start.SignalAndWait();
                return harness.Service.HandleStateAsync(
                    harness.IdOf("阿尔法"), 2, CancellationToken.None);
            }),
            Task.Run(() =>
            {
                start.SignalAndWait();
                return harness.Service.HandleStateAsync(
                    harness.IdOf("布拉沃"), 3, CancellationToken.None);
            }));

        var cancelled = harness.Of("布拉沃").Any(packet =>
            packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification);
        var settled = outcomes.Count(outcome => outcome.SettlementApplied) == 1;
        check(cancelled != settled,
            "a cancel racing a final confirm is mutually exclusive: either the cancel wins (no NOTI 18) "
            + "or the confirm wins (no NOTI 16), never both");
    }

    /// <summary>
    /// 评审给的顺序路径：双方锁定、一方已确认，随后对方 DetachChannelAsync，再让另一方确认。
    /// 注意这条路径**不能**当回归哨兵：DetachChannelAsync 的摘通道与取消会话是同一次同步调用，
    /// 会话会先被正常取消，之后的确认自然找不到会话。真正的窗口见
    /// <see cref="CheckOfflinePeerAbortsSettlementAsync"/>。
    /// </summary>
    private static async Task CheckConfirmAfterPeerDetachAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        await harness.Service.DetachChannelAsync(
            harness.IdOf("布拉沃"), harness.SessionIdOf("布拉沃"), CancellationToken.None);
        var late = await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 3, CancellationToken.None);
        check(!late.SettlementApplied
                && harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification)
                && harness.Of("阿尔法").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.FinishItemTradeNotification)
                && !harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 5),
            "a peer detaching after one side confirmed cancels the trade: the survivor is told and nothing settles");
    }

    /// <summary>
    /// 回归哨兵（评审第 2 轮 Important）：抢占 <c>Settling</c> 之后才发现对方通道已经消失
    /// （对方正在掉线：通道先摘、它自己的取消步骤还没走到），必须走**结算旁路**把整笔中止。
    /// 修复前这里会被取消守卫吞掉：会话既不摘除也不通知，`SweepExpiredAsync`/`CMD 26` 全被挡回，
    /// 两个角色从此永久无法交易、托管物品永久冻结，直到进程重启。
    /// 公开 API 只能「摘通道 + 结束会话」一起做，因此用反射只摘通道，构造真实掉线窗口的状态。
    /// </summary>
    private static async Task CheckOfflinePeerAbortsSettlementAsync(Action<bool, string> check)
    {
        await using var harness = await TradeHarness.CreateAsync();
        await harness.EnterTownAsync("阿尔法", 1, 2, 1);
        await harness.EnterTownAsync("布拉沃", 1, 2, 2);
        var potion = new CharacterItemRecord(5, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid());
        await harness.SetInventoryAsync("阿尔法", 5_000, [potion]);
        await harness.SetInventoryAsync("布拉沃", 7_000, []);
        await harness.OpenTradeAsync();
        await harness.Service.HandleMoveAsync(
            harness.IdOf("阿尔法"), 0, 5, TradeFixtures.PotionItemId, 4, 4, 3, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("阿尔法"), 1, CancellationToken.None);
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 1, CancellationToken.None);
        // 布拉沃先确认，于是阿尔法是「后确认者」，由它抢占 Settling。
        await harness.Service.HandleStateAsync(harness.IdOf("布拉沃"), 3, CancellationToken.None);
        DropChannelForTests(harness.Service, harness.IdOf("布拉沃"));
        harness.Clear("阿尔法");
        harness.Clear("布拉沃");

        var outcome = await harness.Service.HandleStateAsync(
            harness.IdOf("阿尔法"), 3, CancellationToken.None);
        check(!outcome.SettlementApplied
                && harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.CancelItemTradeNotification)
                && harness.Of("阿尔法").Any(packet =>
                    packet.ProtocolId == GameProtocolEngine.UpdateItemListNotification)
                && harness.Of("阿尔法").All(packet =>
                    packet.ProtocolId != GameProtocolEngine.FinishItemTradeNotification)
                && harness.Of("布拉沃").Count == 0,
            "a peer that vanished before the settle aborts the trade: NOTI 16 + gold refresh to the survivor, no NOTI 18");

        check(!harness.Service.IsEscrowed(harness.IdOf("阿尔法"), 0, 5),
            "the aborted session is really removed: the escrowed slot is released instead of freezing forever in Settling");
        var alphaAfter = await harness.Store.GetCharacterAsync("alice", harness.IdOf("阿尔法"), CancellationToken.None);
        check(alphaAfter is not null && alphaAfter.Inventory is not null
                && alphaAfter.Gold == 5_000
                && alphaAfter.Inventory.Single(item => item.Slot == 5).CountOrValue == 10,
            "the aborted trade never touched the store: the potion never left the owner");
    }

    /// <summary>
    /// 测试专用：只摘掉角色通道、不结束会话，构造「通道已消失但会话仍在册」的真实掉线窗口状态。
    /// 若字段被改名，这条测试会以 FAIL（结算照常成功）而不是静默跳过的方式暴露。
    /// </summary>
    private static void DropChannelForTests(ItemTradeService service, Guid characterId)
    {
        var field = typeof(ItemTradeService).GetField(
            "_channels",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var channels = (ConcurrentDictionary<Guid, ItemTradeChannel>?)field?.GetValue(service);
        channels?.TryRemove(characterId, out _);
    }
}

internal static class TradeFixtures
{
    public const ushort PotionItemId = 3001;   // 可交易、可堆叠（stack limit 100）
    public const ushort SwordItemId = 1000;    // 可交易装备
    public const ushort BannedItemId = 3002;   // [attach type] `[trade]`：CanTrade 判据拒绝
    public const ushort HeavyItemId = 1001;    // 可交易装备，[weight] 100000：结算负重超限场景

    public static void Create(string root)
    {
        var equipmentDirectory = Path.Combine(root, "equipment", "character", "test", "weapon");
        var materialDirectory = Path.Combine(root, "stackable", "material");
        Directory.CreateDirectory(equipmentDirectory);
        Directory.CreateDirectory(materialDirectory);

        File.WriteAllText(
            Path.Combine(root, "equipment", "equipment.lst"),
            $"{SwordItemId} `character/test/weapon/test.equ`\n"
            + $"{HeavyItemId} `character/test/weapon/heavy.equ`\n");
        File.WriteAllText(
            Path.Combine(equipmentDirectory, "test.equ"),
            "[name] `Test Sword`\n"
            + "[equipment type] `[weapon]`\n"
            + "[attach type] `[free]`\n"
            + "[weight] 10\n"
            + "[durability] 35\n");
        File.WriteAllText(
            Path.Combine(equipmentDirectory, "heavy.equ"),
            "[name] `Test Heavy`\n"
            + "[equipment type] `[weapon]`\n"
            + "[attach type] `[free]`\n"
            + "[weight] 100000\n"
            + "[durability] 35\n");
        File.WriteAllText(
            Path.Combine(root, "stackable", "stackable.lst"),
            $"{PotionItemId} `material/potion.stk`\n"
            + $"{BannedItemId} `material/banned.stk`\n");
        File.WriteAllText(
            Path.Combine(materialDirectory, "potion.stk"),
            "[name] `Test Potion`\n"
            + "[stackable type] `[material]`\n"
            + "[attach type] `[free]`\n"
            + "[weight] 1\n"
            + "[stack limit] 100\n");
        File.WriteAllText(
            Path.Combine(materialDirectory, "banned.stk"),
            "[name] `Test Banned`\n"
            + "[stackable type] `[material]`\n"
            + "[attach type] `[trade]`\n"
            + "[weight] 1\n"
            + "[stack limit] 100\n");
    }

    public static ItemCatalog CreateItems(string root)
    {
        var items = new ItemCatalog(
            new ScriptFileSystem(
                new ServerOptions { ScriptPvfPath = string.Empty, SkillScriptPath = root },
                NullLogger<ScriptFileSystem>.Instance),
            NullLogger<ItemCatalog>.Instance);
        items.Initialize();
        return items;
    }

    public static CharacterStatCatalog CreateStats(string root) =>
        new(
            new ScriptFileSystem(
                new ServerOptions { ScriptPvfPath = string.Empty, SkillScriptPath = root },
                NullLogger<ScriptFileSystem>.Instance),
            NullLogger<CharacterStatCatalog>.Instance);

    public static CharacterRecord CreateCharacter(
        string name,
        int gold,
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory) =>
        new(
            Guid.NewGuid(),
            name,
            Job: 0,
            GrowType: 3,
            Level: 20,
            Sp: 0,
            Cash: 0,
            Gold: gold,
            Inventory: [.. inventory.Values]);
}

internal sealed class TradeHarness : IAsyncDisposable
{
    private readonly Dictionary<string, List<GameServerPacket>> _packets = new();
    private readonly Dictionary<string, (Guid Id, string Account)> _characters = new();
    private readonly Dictionary<string, Guid> _sessionIds = new();
    private readonly Dictionary<string, TownMemberTicket?> _tickets = new();

    private TradeHarness(
        string root,
        ServerOptions options,
        ItemCatalog items,
        CharacterStatCatalog stats,
        JsonGameStore store,
        TownSessionDirectory directory)
    {
        Root = root;
        Options = options;
        Items = items;
        Stats = stats;
        Store = store;
        Directory = directory;
    }

    public string Root { get; }
    public ServerOptions Options { get; }
    public ItemCatalog Items { get; }
    public CharacterStatCatalog Stats { get; }
    public JsonGameStore Store { get; }
    public TownSessionDirectory Directory { get; }
    public ItemTradeService Service { get; private set; } = null!;

    public Guid IdOf(string characterName) => _characters[characterName].Id;

    /// <summary>本会话的会话 id：拆除通道（Task 6 的掉线用例）要带它才能生效。</summary>
    public Guid SessionIdOf(string characterName) => _sessionIds[characterName];

    public IReadOnlyList<GameServerPacket> Of(string characterName)
    {
        lock (_packets[characterName])
        {
            return [.. _packets[characterName]];
        }
    }

    public void Clear(string characterName)
    {
        lock (_packets[characterName])
        {
            _packets[characterName].Clear();
        }
    }

    public static async Task<TradeHarness> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dflegacy-trade-harness-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        TradeFixtures.Create(root);
        var options = new ServerOptions
        {
            DataPath = Path.Combine(root, "state.json"),
            ScriptPvfPath = string.Empty,
            SkillScriptPath = root
        };
        var items = TradeFixtures.CreateItems(root);
        var stats = TradeFixtures.CreateStats(root);
        var store = new JsonGameStore(
            options,
            NullLogger<JsonGameStore>.Instance,
            itemCatalog: items,
            statCatalog: stats);
        var directory = new TownSessionDirectory(
            options,
            TimeProvider.System,
            NullLogger<TownSessionDirectory>.Instance);

        await store.CreateAccountAsync(
            new CreateAccountRequest("alice", "password", OicqCode: "123456"),
            CancellationToken.None);
        await store.CreateAccountAsync(
            new CreateAccountRequest("bobby", "password", OicqCode: "123456"),
            CancellationToken.None);
        var alpha = (await store.CreateCharacterAsync(
            "alice", new CreateCharacterRequest("阿尔法", GrowType: 3), CancellationToken.None)).Character!;
        var bravo = (await store.CreateCharacterAsync(
            "bobby", new CreateCharacterRequest("布拉沃", GrowType: 3), CancellationToken.None)).Character!;

        // Fund this private trade fixture explicitly; ordinary creation is empty.
        alpha = (await store.SaveCharacterInventoryAsync("alice", alpha.Id, 50_000, [], []))!;
        bravo = (await store.SaveCharacterInventoryAsync("bobby", bravo.Id, 50_000, [], []))!;

        var harness = new TradeHarness(root, options, items, stats, store, directory);
        harness._packets["阿尔法"] = [];
        harness._packets["布拉沃"] = [];
        harness._characters["阿尔法"] = (alpha.Id, "alice");
        harness._characters["布拉沃"] = (bravo.Id, "bobby");
        harness._sessionIds["阿尔法"] = Guid.NewGuid();
        harness._sessionIds["布拉沃"] = Guid.NewGuid();
        harness.Service = new ItemTradeService(
            store,
            items,
            stats,
            directory,
            TimeProvider.System,
            NullLogger<ItemTradeService>.Instance);
        // 两个角色的区用户 id 与 EnterTownAsync 里登记的一致：交易命令里只有 uid。
        await harness.Service.AttachChannelAsync(
            "alice",
            "阿尔法",
            alpha.Id,
            areaUserId: 1,
            harness._sessionIds["阿尔法"],
            harness.Record("阿尔法"),
            CancellationToken.None);
        await harness.Service.AttachChannelAsync(
            "bobby",
            "布拉沃",
            bravo.Id,
            areaUserId: 2,
            harness._sessionIds["布拉沃"],
            harness.Record("布拉沃"),
            CancellationToken.None);
        return harness;
    }

    private Func<GameServerPacket, ValueTask> Record(string characterName) =>
        packet =>
        {
            lock (_packets[characterName])
            {
                _packets[characterName].Add(packet);
            }

            return ValueTask.CompletedTask;
        };

    public async Task<CharacterRecord?> SetInventoryAsync(
        string characterName,
        int gold,
        IReadOnlyList<CharacterItemRecord> inventory)
    {
        var record = await Store.SaveCharacterItemSpacesAsync(
            _characters[characterName].Account,
            _characters[characterName].Id,
            gold,
            inventory,
            [],
            [],
            CancellationToken.None);
        // 交易测试自己造出来的存档状态不需要守门：直接把该角色的守门标记清掉。
        if (record is not null)
        {
            Store.ClearTradeDeliveryPending(record.Id);
        }

        return record;
    }

    public async Task EnterTownAsync(
        string characterName,
        byte townId,
        byte areaId,
        ushort areaUserId)
    {
        var (id, account) = _characters[characterName];
        // 交易只用到快照里的 (TownId, AreaId)；CharacterNo 不是本测试的关注点，填 0。
        // 票据留下：LeaveTownAsync 要拿它真正离区（换区时后一张覆盖前一张）。
        _tickets[characterName] = await Directory.EnterAsync(
            new TownEntryRequest(
                TownId: townId,
                AreaId: areaId,
                AreaUserId: areaUserId,
                CharacterId: id,
                CharacterNo: 0,
                CharacterNameBytes: Encoding.ASCII.GetBytes(characterName),
                DisplayName: characterName,
                Peer: null,
                X: 474,
                Y: 234,
                Direction: 5,
                SendAsync: (_, _) => ValueTask.CompletedTask),
            CancellationToken.None);
    }

    public async Task LeaveTownAsync(string characterName)
    {
        // 与 EntranceService.LeaveTownAreaAsync 同序：先取消交易（此刻客户端仍在线上，对手
        // 还收得到 NOTI 16），再真正离开区域。没有票据 = 本来就不在城镇里，什么都不做。
        if (!_tickets.TryGetValue(characterName, out var ticket))
        {
            return;
        }

        _tickets.Remove(characterName);
        await Service.CancelForAreaChangeAsync(_characters[characterName].Id, CancellationToken.None);
        await Directory.LeaveAsync(ticket, CancellationToken.None);
    }

    /// <summary>
    /// 便捷方法：阿尔法向布拉沃发起、布拉沃接受（uid 与 AttachChannelAsync/EnterTownAsync 的约定一致）。
    /// 默认带一组真机形状的负值负重差（0xFFFF8Cxx）：NOTI 7/8 要把它逐字节透传，交易前置自检才过得去。
    /// </summary>
    public async Task OpenTradeAsync(
        uint requesterWeightDelta = 0xFFFF8C1D,
        uint accepterWeightDelta = 0xFFFF8C55)
    {
        await Service.HandleRequestAsync(
            _characters["阿尔法"].Id, 2, requesterWeightDelta, CancellationToken.None);
        await Service.HandleResponseAsync(
            _characters["布拉沃"].Id, 1, accept: true, accepterWeightDelta, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        // 服务是后台任务：先停掉扫描循环再删目录（没启动过时 StopAsync 是空操作）。
        await Service.StopAsync(CancellationToken.None);
        try
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
