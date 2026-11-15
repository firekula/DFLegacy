namespace DFLegacy.Server;

/// <summary>
/// 一次卡牌奖励的"放哪里"计划（<see cref="ClearRewardGrantPlanner.Plan"/> 的产物）：
/// 目标金币、计划背包、计划仓库容量、溢出邮件、以及每个请求物品是否真的发出去了。
/// 单人路径与组队翻牌路径**共用同一份**放置语义（背包放不下 → 邮件，绝不落地；
/// 仓库升级券只抬容量不进背包）。
/// </summary>
internal readonly record struct ClearRewardGrantPlan(
    int TargetGold,
    IReadOnlyDictionary<ushort, CharacterItemRecord> Inventory,
    ushort WarehouseCapacity,
    IReadOnlyList<CreateCharacterMailRequest> Mails,
    IReadOnlyList<bool> Granted)
{
    internal bool AnyGranted => Granted.Any(granted => granted);
}

/// <summary>
/// 卡牌奖励的计划器 + 组队翻牌的落库请求构造。
/// <para>
/// 放置语义只有这一份：单人的 <c>EntranceService.SaveClearRewardEquipmentAsync</c> 与组队的
/// <c>CommitDungeonRewardAsync</c> 都调用 <see cref="Plan"/>，避免两套"背包放不下怎么办"的实现漂移。
/// </para>
/// <para>
/// R4：组队翻牌的请求必须由**该列归属成员自己的会话**用它**自己角色的精确基线**构造
/// （ExpectedGold/ExpectedInventory/ExpectedEquipment/ExpectedWarehouseCapacity 都是它自己的），
/// 指纹由服务端同一函数复算（<see cref="JsonGameStore.ComputeDungeonRewardFingerprint"/>）。
/// </para>
/// </summary>
internal static class ClearRewardGrantPlanner
{
    /// <summary>
    /// 幂等键：**同一局结算**（<paramref name="settlementId"/>，全局唯一）里同一角色、同一卡型、同一列
    /// → 同一键（重放不会发第二份）。**不能**只用实例内代次号：新实例的代次又从 1 开始，
    /// 第二次进同一个副本会撞上第一次留下的冻结载荷，本局奖励会被永久拒收。
    /// </summary>
    internal static string IdempotencyKey(Guid settlementId, Guid characterId, PartyCardType type, byte column) =>
        $"dungeon-card:{settlementId:N}:{characterId:N}:{(byte)type}:{column}";

    internal static ClearRewardGrantPlan Plan(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        ushort warehouseCapacity,
        int targetGold,
        IReadOnlyList<ushort?> itemIds,
        uint inventoryWeightLimit,
        ItemCatalog itemCatalog)
    {
        var plannedInventory = inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var plannedWarehouseCapacity = warehouseCapacity;
        var overflowMails = new List<CreateCharacterMailRequest>();
        var granted = new bool[itemIds.Count];
        for (var index = 0; index < itemIds.Count; index++)
        {
            if (itemIds[index] is not { } itemId)
            {
                continue;
            }

            if (CharacterWarehouseProgression.TryGetItemTargetCapacity(itemId, out var targetCapacity))
            {
                plannedWarehouseCapacity = Math.Max(plannedWarehouseCapacity, targetCapacity);
                granted[index] = true;
                continue;
            }

            var rewardItem = CharacterItemIdentity.Ensure(
                new CharacterMailAttachmentRecord(
                    itemId,
                    1,
                    Durability: itemCatalog.GetInitialDurability(itemId),
                    SealState: CharacterItemSealing.GetInitialSealState(itemCatalog, itemId)),
                itemCatalog);
            if (CharacterInventoryPlanner.TryPlace(
                    plannedInventory,
                    rewardItem,
                    itemCatalog,
                    inventoryWeightLimit,
                    out var plan,
                    out _))
            {
                plannedInventory = plan.Inventory;
                granted[index] = true;
                continue;
            }

            if (!itemCatalog.TryGetDefinition(rewardItem.ItemId, out _))
            {
                continue;
            }

            // 卡牌奖励绝不落地：放不下就进邮件，与单人路径同一份实现。
            overflowMails.AddRange(EntranceService.CreateOverflowMailRequests(
                [rewardItem],
                "Dungeon clear reward"));
            granted[index] = true;
        }

        return new ClearRewardGrantPlan(
            targetGold,
            plannedInventory,
            plannedWarehouseCapacity,
            overflowMails,
            granted);
    }

    /// <summary>
    /// 构造组队翻牌的幂等落库请求：基线字段全部来自该成员自己的会话（R4），
    /// 指纹用服务端函数复算（篡改 planned/基线会被 <c>payload-fingerprint-mismatch</c> 挡掉）。
    /// </summary>
    internal static DungeonRewardCommitRequest BuildCommitRequest(
        string idempotencyKey,
        string userName,
        Guid characterId,
        long generation,
        byte partySlot,
        PartyCardType type,
        byte column,
        int expectedGold,
        int targetGold,
        IReadOnlyList<CharacterItemRecord> expectedInventory,
        IReadOnlyList<CharacterItemRecord> expectedEquipment,
        ushort? expectedWarehouseCapacity,
        ClearRewardGrantPlan plan,
        uint requestedGoldReward = 0,
        Guid mentorGoldIncarnation = default)
    {
        var request = new DungeonRewardCommitRequest(
            idempotencyKey,
            "",
            userName,
            characterId,
            generation,
            partySlot,
            (byte)type,
            column,
            targetGold,
            expectedGold,
            expectedInventory,
            expectedEquipment,
            plan.Inventory.Values.ToArray(),
            expectedEquipment,
            plan.Mails,
            plan.WarehouseCapacity,
            expectedWarehouseCapacity,
            requestedGoldReward,
            mentorGoldIncarnation);
        return request with { PayloadFingerprint = JsonGameStore.ComputeDungeonRewardFingerprint(request) };
    }
}
