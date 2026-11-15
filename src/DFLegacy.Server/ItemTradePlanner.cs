using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>交易失败原因：放入/取回阶段回 CMD 19 失败包，结算阶段映射到 CMD 26 错误码。</summary>
public enum ItemTradeFailure
{
    None,
    NotOpen,
    Locked,
    InvalidSourceSpace,
    SourceMissing,
    SourceEscrowed,
    PanelSlotOutOfRange,
    PanelSlotOccupied,
    UnknownItem,
    NotTradeable,
    InvalidCount,
    NotEscrowed,
    InsufficientGold,
    SettlementEscrowMissing,
    SettlementNoSpace,
    SettlementWeightExceeded,
    SettlementGoldMissing,
    SettlementGoldOverflow
}

/// <summary>托管记录：物品仍留在原主背包里，这里只记「哪一格/哪个实例被押上了」。</summary>
public sealed record ItemTradeEscrow(
    ushort PanelSlot,
    byte SourceListType,
    ushort SourceSlot,
    Guid InstanceId,
    uint Count,
    CharacterItemRecord Snapshot);

public sealed record ItemTradeSideState(
    Guid CharacterId,
    IReadOnlyList<ItemTradeEscrow> Escrows,
    int OfferedGold);

public sealed record ItemTradeSettlementRequest(
    CharacterRecord Initiator,
    CharacterRecord Partner,
    ItemTradeSideState InitiatorState,
    ItemTradeSideState PartnerState);

public sealed record ItemTradeRemoval(
    ushort SourceSlot,
    Guid InstanceId,
    uint RemovedCount,
    uint RemainingCount);

public sealed record ItemTradeDelivery(
    ushort PanelSlot,
    ushort DestinationSlot,
    CharacterItemRecord Item);

public sealed record ItemTradeSidePlan(
    IReadOnlyList<ItemTradeRemoval> Removals,
    IReadOnlyList<ItemTradeDelivery> Deliveries,
    int GoldDelta);

public sealed record ItemTradeSettlementPlan(
    ItemTradeSidePlan Initiator,
    ItemTradeSidePlan Partner);

/// <summary>
/// 交易的全部纯逻辑：托管校验、结算计划与面板投影。不含 IO，便于把「复制防线」逐条断言。
/// 依据 docs/superpowers/specs/2026-09-28-trade-system-design.md §2.4 与
/// .superpowers/sdd/trade-protocol-recon.md §3/§5。
/// </summary>
public static class ItemTradePlanner
{
    public const ushort GoldPanelSlot = 0;
    public const ushort FirstItemPanelSlot = 3;
    public const ushort LastItemPanelSlot = 26;

    /// <summary>
    /// 放入方向的面板槽分配（recon-2 §4.2-1）。真机上客户端拖物品进交易栏时 CMD 19 的
    /// destinationSlot 恒为 0（0x4627A3 的 <c>xor esi,esi</c>：空位搜索的结果根本不进包，
    /// 分配权留给服务端），所以服务端必须自己挑槽。规则：客户端若给了一个 [3,26] 且本侧
    /// 尚未占用的槽就采用它（兼容将来送真实槽的请求），否则取最小空槽 3..26；24 格全占返回 false。
    /// </summary>
    public static bool TryAllocatePanelSlot(
        IReadOnlyList<ItemTradeEscrow> escrows,
        ushort requestedSlot,
        out ushort panelSlot)
    {
        if (requestedSlot is >= FirstItemPanelSlot and <= LastItemPanelSlot
            && escrows.All(candidate => candidate.PanelSlot != requestedSlot))
        {
            panelSlot = requestedSlot;
            return true;
        }

        for (var slot = (int)FirstItemPanelSlot; slot <= LastItemPanelSlot; slot++)
        {
            if (escrows.All(candidate => candidate.PanelSlot != slot))
            {
                panelSlot = (ushort)slot;
                return true;
            }
        }

        panelSlot = 0;
        return false;
    }

    /// <summary>
    /// 放入/取回失败 → CMD 19 失败回包的错误码（客户端 handler 0x4110FE 的 switch，recon-2 §4.1）。
    /// 只有这个 switch 认识的码才会弹文案，其余会让客户端静默失败（"面板毫无变化"）。
    /// 目标面板格被占 = 0x17；不允许的移动（已锁定 / 非主背包来源 / 不可交易道具）= 0x13；
    /// 其余（源格没有物品、数量不足、金币不够、会话已关闭…）= 0x11。
    /// </summary>
    public static byte ToMoveErrorCode(ItemTradeFailure failure) => failure switch
    {
        ItemTradeFailure.PanelSlotOccupied =>
            GameProtocolEngine.MoveItemSpaceFailureDestinationOccupied,
        ItemTradeFailure.Locked
            or ItemTradeFailure.InvalidSourceSpace
            or ItemTradeFailure.NotTradeable =>
            GameProtocolEngine.MoveItemSpaceFailureNotAllowed,
        _ => GameProtocolEngine.MoveItemSpaceFailureNotFound
    };

    public static bool TryPlaceEscrow(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        IReadOnlyList<ItemTradeEscrow> escrows,
        bool locked,
        byte sourceListType,
        ushort sourceSlot,
        ushort panelSlot,
        uint count,
        ItemCatalog catalog,
        out ItemTradeEscrow escrow,
        out ItemTradeFailure failure)
    {
        escrow = null!;
        failure = ItemTradeFailure.NotOpen;
        if (locked)
        {
            failure = ItemTradeFailure.Locked;
            return false;
        }

        failure = ItemTradeFailure.InvalidSourceSpace;
        if (sourceListType != 0)
        {
            return false;
        }

        // 防线，不是入参校验：放入方向的面板槽由服务端分配（客户端 CMD 19 的 destSlot 恒为 0，
        // 证据 0x4627A3 的 xor esi,esi，见 recon-2 §3.2/§4.2-1），调用者必须先过
        // TryAllocatePanelSlot 得到 3..26 再进来；这里的范围断言只用于兜住任何绕过分配的路径。
        failure = ItemTradeFailure.PanelSlotOutOfRange;
        if (panelSlot < FirstItemPanelSlot || panelSlot > LastItemPanelSlot)
        {
            return false;
        }

        // 先核内容（这一格是什么、能不能交易、数量是否合法），再核面板/背包的占用冲突：
        // 否则「面板槽被占 + 数量非法」或「来源格已被托管 + 面板槽被占」时回的错误码会飘。
        failure = ItemTradeFailure.SourceMissing;
        if (!inventory.TryGetValue(sourceSlot, out var item) || item.ItemId is 0 or 1 or 2)
        {
            return false;
        }

        failure = ItemTradeFailure.UnknownItem;
        if (!catalog.TryGetDefinition(item.ItemId, out var definition))
        {
            return false;
        }

        failure = ItemTradeFailure.NotTradeable;
        if (!CharacterItemSealing.CanTrade(item, definition))
        {
            return false;
        }

        // 非堆叠物整件押上：CountOrValue 是本实例件数，装备的品质另存 EquipmentQualitySeed，
        // 客户端给的数量对它没有意义（T5），托管 Count 记 1。
        failure = ItemTradeFailure.InvalidCount;
        if (definition.IsStackable)
        {
            if (count == 0 || count > item.CountOrValue)
            {
                return false;
            }
        }
        else
        {
            count = 1;
        }

        failure = ItemTradeFailure.PanelSlotOccupied;
        if (escrows.Any(candidate => candidate.PanelSlot == panelSlot))
        {
            return false;
        }

        failure = ItemTradeFailure.SourceEscrowed;
        if (escrows.Any(candidate => candidate.SourceListType == 0 && candidate.SourceSlot == sourceSlot))
        {
            return false;
        }

        escrow = new ItemTradeEscrow(
            panelSlot,
            sourceListType,
            sourceSlot,
            item.InstanceId,
            count,
            item);
        failure = ItemTradeFailure.None;
        return true;
    }

    public static bool TryPlaceGoldEscrow(
        IReadOnlyList<ItemTradeEscrow> escrows,
        bool locked,
        uint count,
        int currentGold,
        out ItemTradeEscrow escrow,
        out ItemTradeFailure failure)
    {
        escrow = null!;
        failure = ItemTradeFailure.Locked;
        if (locked)
        {
            return false;
        }

        failure = ItemTradeFailure.InsufficientGold;
        if (count > (uint)Math.Max(0, currentGold))
        {
            return false;
        }

        // 客户端面板把金币固定放在 0 号格；重复放入按「覆盖」（T3）。
        var existing = escrows.FirstOrDefault(candidate => candidate.PanelSlot == GoldPanelSlot);
        if (existing is not null)
        {
            escrow = existing with { Count = count };
            failure = ItemTradeFailure.None;
            return true;
        }

        escrow = new ItemTradeEscrow(
            GoldPanelSlot,
            0,
            0,
            Guid.Empty,
            count,
            new CharacterItemRecord(0, 0, count));
        failure = ItemTradeFailure.None;
        return true;
    }

    public static bool TryReleaseEscrow(
        IReadOnlyList<ItemTradeEscrow> escrows,
        bool locked,
        ushort panelSlot,
        out ItemTradeEscrow released,
        out ItemTradeFailure failure)
    {
        released = null!;
        if (locked)
        {
            failure = ItemTradeFailure.Locked;
            return false;
        }

        var existing = escrows.FirstOrDefault(candidate => candidate.PanelSlot == panelSlot);
        if (existing is null)
        {
            failure = ItemTradeFailure.NotEscrowed;
            return false;
        }

        released = existing;
        failure = ItemTradeFailure.None;
        return true;
    }

    public static GameItemTradeEntry ProjectPanelEntry(ItemTradeEscrow escrow, ItemCatalog catalog)
    {
        if (escrow.Snapshot.ItemId == 0)
        {
            return new GameItemTradeEntry(
                escrow.PanelSlot,
                0,
                escrow.Count,
                0,
                0);
        }

        var wire = CharacterItemWireProjection.Project(escrow.Snapshot, catalog);
        var count = catalog.TryGetDefinition(escrow.Snapshot.ItemId, out var definition)
            && definition.IsStackable
                ? escrow.Count
                : wire.AddInfo;
        return new GameItemTradeEntry(
            escrow.PanelSlot,
            escrow.Snapshot.ItemId,
            count,
            wire.ItemAttr,
            wire.Durability);
    }

    public static bool TryPlanSettlement(
        ItemTradeSettlementRequest request,
        ItemCatalog catalog,
        CharacterStatCatalog statCatalog,
        out ItemTradeSettlementPlan plan,
        out ItemTradeFailure failure)
    {
        plan = null!;
        if (!TryPlanSide(
                request.Partner,
                request.PartnerState,
                request.InitiatorState.Escrows,
                request.InitiatorState.OfferedGold,
                catalog,
                statCatalog,
                out var partnerPlan,
                out failure))
        {
            return false;
        }

        if (!TryPlanSide(
                request.Initiator,
                request.InitiatorState,
                request.PartnerState.Escrows,
                request.PartnerState.OfferedGold,
                catalog,
                statCatalog,
                out var initiatorPlan,
                out failure))
        {
            return false;
        }

        plan = new ItemTradeSettlementPlan(initiatorPlan, partnerPlan);
        return true;
    }

    /// <summary>
    /// 一侧的结算计划：<paramref name="own"/> 是收货方（他自己的托管要被移除、对方的托管要交付进来）。
    /// 校验顺序 = 托管复核 → 落点 → 负重 → 金币；任一步失败整笔不写。
    /// </summary>
    private static bool TryPlanSide(
        CharacterRecord own,
        ItemTradeSideState ownState,
        IReadOnlyList<ItemTradeEscrow> incoming,
        int incomingGold,
        ItemCatalog catalog,
        CharacterStatCatalog statCatalog,
        out ItemTradeSidePlan plan,
        out ItemTradeFailure failure)
    {
        plan = null!;
        var working = (own.Inventory ?? []).ToDictionary(item => item.Slot);
        var removals = new List<ItemTradeRemoval>(ownState.Escrows.Count);
        foreach (var escrow in ownState.Escrows.Where(candidate => candidate.Snapshot.ItemId != 0))
        {
            failure = ItemTradeFailure.SettlementEscrowMissing;
            // State/Durability 也一并复核：原地改状态（强化、属性修改）不换 InstanceId 也不换
            // 数量，只看身份与数量会把「已经变了的物品」按旧快照交付出去。
            if (!working.TryGetValue(escrow.SourceSlot, out var current)
                || current.InstanceId != escrow.InstanceId
                || current.ItemId != escrow.Snapshot.ItemId
                || current.CountOrValue < escrow.Count
                || current.State != escrow.Snapshot.State
                || current.Durability != escrow.Snapshot.Durability)
            {
                return false;
            }

            // 非堆叠物整件押上、整件移除（CountOrValue 是本实例件数，不参与拆分）；
            // 堆叠物只扣掉托管的那一份。
            var stackable = catalog.TryGetDefinition(current.ItemId, out var currentDefinition)
                && currentDefinition.IsStackable;
            var remaining = stackable ? current.CountOrValue - escrow.Count : 0;
            if (remaining == 0)
            {
                working.Remove(escrow.SourceSlot);
            }
            else
            {
                working[escrow.SourceSlot] = current with { CountOrValue = remaining };
            }

            removals.Add(new ItemTradeRemoval(
                escrow.SourceSlot,
                escrow.InstanceId,
                escrow.Count,
                remaining));
        }

        var deliveries = new List<ItemTradeDelivery>(incoming.Count);
        foreach (var escrow in incoming.Where(candidate => candidate.Snapshot.ItemId != 0))
        {
            failure = ItemTradeFailure.UnknownItem;
            if (!catalog.TryGetDefinition(escrow.Snapshot.ItemId, out var definition))
            {
                return false;
            }

            failure = ItemTradeFailure.SettlementNoSpace;
            var destination = FindFreeSlot(working, definition);
            if (destination is null)
            {
                return false;
            }

            // 堆叠物只交付托管的份额；非堆叠物整件搬运，记录（含品质种子等字段）原样保留。
            var moved = definition.IsStackable
                ? escrow.Snapshot with { CountOrValue = escrow.Count }
                : escrow.Snapshot;
            var placed = moved with { Slot = destination.Value };
            working[destination.Value] = placed;
            deliveries.Add(new ItemTradeDelivery(escrow.PanelSlot, destination.Value, placed));
        }

        var equipped = own.Equipment ?? [];
        var creature = own.CreatureInventory ?? [];
        var weightLimit = CharacterInventoryPlanner.CalculateCarriedWeightLimit(
            statCatalog.Get(own).InventoryLimit,
            equipped,
            catalog);
        failure = ItemTradeFailure.SettlementWeightExceeded;
        if (CharacterInventoryPlanner.CalculateCarriedWeight(working.Values, equipped, creature, catalog)
            > weightLimit)
        {
            return false;
        }

        // 本侧必须**现在**还拿得出自己承诺的金币：托管之后金币可能被别的途径扣掉（如邮件寄钱），
        // 只校验「结果不为负」会把别人的钱赔出去（设计 §2.4②）。
        failure = ItemTradeFailure.SettlementGoldMissing;
        if (own.Gold < ownState.OfferedGold)
        {
            return false;
        }

        // 上一条已保证 own.Gold >= ownState.OfferedGold，因此 finalGold 不可能为负，只可能上溢。
        failure = ItemTradeFailure.SettlementGoldOverflow;
        var goldDelta = incomingGold - ownState.OfferedGold;
        var finalGold = (long)own.Gold + goldDelta;
        if (finalGold > int.MaxValue)
        {
            return false;
        }

        plan = new ItemTradeSidePlan(removals, deliveries, goldDelta);
        failure = ItemTradeFailure.None;
        return true;
    }

    private static ushort? FindFreeSlot(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        ItemDefinition definition)
    {
        // 只落该物品的**类别段**（装备 9..41 / 消耗 41..73 / 材料 73..105 / 任务 105..137）。
        // 快速栏（3..8）虽然 IsCompatibleSlot 也放行（那是玩家手工摆放的自由；邮件/地面拾取
        // 的 EnumerateCandidateSlots 也把快速栏当兜底候选），但交易交付不往那儿塞，
        // 免得与原作的自动整理观感不一致。
        if (!CharacterInventoryLayout.TryGetCategoryRange(
                definition.InventoryCategory,
                out var start,
                out var end))
        {
            return null;
        }

        for (var slot = (int)start; slot < end; slot++)
        {
            if (!inventory.ContainsKey((ushort)slot))
            {
                return (ushort)slot;
            }
        }

        return null;
    }
}
