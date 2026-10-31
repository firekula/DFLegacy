namespace DFLegacy.Server;

/// <summary>
/// 一次"归属列落库"的结果：提交状态（含 Rejected/Unknown 的原因）、落库后的目标金币、
/// 放置计划（邮件/是否真的发出物品）、以及存档返回的、需要被会话采用的角色状态。
/// </summary>
internal readonly record struct PartyCardOwnColumnCommitResult(
    PartyCardCommitOutcome Outcome,
    int TargetGold,
    ClearRewardGrantPlan Plan,
    CharacterRecord? InstalledCharacter);

/// <summary>
/// R4 的落库机制（**生产代码**，会话与冒烟测试共用）：拿这个成员**自己角色的精确基线**
/// （金币/背包/装备/仓库容量）构造幂等提交请求，走
/// <see cref="JsonGameStore.CommitDungeonRewardAsync"/>；内容与物品 id 来自**它自己**那份
/// <see cref="DungeonClearRewardRoll"/>。
/// <para>
/// 会话只负责把结果"装回"自己的局部量（金币/背包/已选列/邮件提醒），不在这里做。
/// </para>
/// </summary>
internal static class PartyCardOwnColumnCommitter
{
    internal static async Task<PartyCardOwnColumnCommitResult> CommitAsync(
        JsonGameStore store,
        string accountName,
        CharacterRecord owner,
        byte partySlot,
        PartyCardType type,
        byte column,
        long generation,
        Guid settlementId,
        DungeonClearRewardRoll roll,
        int currentGold,
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        IReadOnlyList<CharacterItemRecord> equipment,
        ushort warehouseCapacity,
        uint inventoryWeightLimit,
        ItemCatalog itemCatalog,
        CancellationToken cancellationToken,
        Guid mentorGoldIncarnation = default)
    {
        var paidCost = (int)Math.Min(int.MaxValue, roll.GoldCardCost);
        var targetGold = type == PartyCardType.Free
            ? (int)Math.Min(int.MaxValue, (long)currentGold + roll.FreeGoldAmount)
            : currentGold - paidCost;
        IReadOnlyList<ushort?> itemIds = type == PartyCardType.Free
            ? [roll.FreeItemId, roll.PremiumItemId]
            : [roll.GoldItemId];
        // 放置语义与单人**同一份**（ClearRewardGrantPlanner）：放不下 → 邮件，绝不落地。
        var plan = ClearRewardGrantPlanner.Plan(
            inventory,
            warehouseCapacity,
            targetGold,
            itemIds,
            inventoryWeightLimit,
            itemCatalog);
        var request = ClearRewardGrantPlanner.BuildCommitRequest(
            ClearRewardGrantPlanner.IdempotencyKey(settlementId, owner.Id, type, column),
            accountName,
            owner.Id,
            generation,
            partySlot,
            type,
            column,
            currentGold,
            targetGold,
            inventory.Values.ToArray(),
            equipment,
            warehouseCapacity,
            plan,
            type == PartyCardType.Free ? roll.FreeGoldAmount : 0,
            mentorGoldIncarnation);
        var result = await store.CommitDungeonRewardAsync(request, cancellationToken);
        return new PartyCardOwnColumnCommitResult(
            new PartyCardCommitOutcome(result.Status, result.RejectionCode ?? result.UnknownReason),
            result.UpdatedCharacter?.Gold ?? targetGold,
            plan,
            result.UpdatedCharacter);
    }
}
