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
        CheckPlanner(check);
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
