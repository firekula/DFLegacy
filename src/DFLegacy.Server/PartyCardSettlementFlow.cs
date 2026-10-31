using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 一次会话侧翻牌推进的结果：是否处在组队翻牌结算里、代次、本会话自己那一列、
/// 是否被接受（自动补全/手动选列/付费购买）、以及本次是否真的发出了整表。
/// </summary>
internal readonly record struct PartyCardSettlementResult(
    bool Active,
    long Generation,
    byte Column,
    bool Accepted,
    bool Published);

/// <summary>
/// 会话侧翻牌顺序的**唯一一份**实现（EntranceService 与冒烟测试共用）。三条入口：
/// <list type="bullet">
/// <item>自动补全（队员不操作 / 卡牌选择超时 / EPLP 兜底）：幂等开局 → 整队自动补全 → 读**自己**那一列 → 推进。</item>
/// <item>手动选自由卡：幂等开局 → 先抢占该列 → 读**自己**那一列 → 推进。</item>
/// <item>购买付费卡：幂等开局 → 先抢占该列（抢不到/买不起都不扣钱）→ 读**自己**那一列 → 推进。</item>
/// </list>
/// 推进 = 由**该列归属成员自己的代理**用**它自己的参数**掷内容、用**它自己的基线**落库；
/// 随后按 R3′ 在归属集合增长时把整表 CMD 74 交给调用方（自己一份 + 其他成员各一份，字节相同）。
/// <para>
/// 这里只做顺序与门控，不做掷取与落库：掷取/落库在 <see cref="IPartyCardRewardAgent"/> 上，
/// 由会话用它自己的 <c>roomSettlement.ClearReward</c> 与它自己角色的基线实现（R1′/R4）。
/// 单人实例（<c>IsParty == false</c>）三条入口全部早退，单人路径继续走会话本地那一条（R6）。
/// </para>
/// </summary>
internal static class PartyCardSettlementFlow
{
    internal static async Task<PartyCardSettlementResult> SettleAutomaticFreeCardAsync(
        DungeonInstance instance,
        Guid characterId,
        bool alreadySettled,
        string source,
        Func<GameServerPacket, Task> sendToSelf,
        Func<GameServerPacket, Task> broadcastToOthers,
        Action<PartyCardAuditEntry> audit,
        CancellationToken cancellationToken)
    {
        if (instance is not { IsParty: true })
        {
            return Inactive;
        }

        try
        {
            var generation = instance.EnsurePartyCardSettlement();
            if (generation == 0)
            {
                return Inactive;
            }

            if (alreadySettled)
            {
                // 本会话已经定过自己那一列：只可能还有**别人的**列要发布（没有增长就不发）。
                // 生产调用点在进来之前就对同一条件早退（TryAutomaticallySelectFreeCardAsync），
                // 所以这里是**防御性的**（会话守卫若写松了也不会重开一局）+ 测试接缝（评审 F3）。
                instance.TryGetOwnPartyCardColumn(characterId, PartyCardType.Free, out var settledColumn);
                return await PublishAsync(instance, generation, settledColumn, accepted: false, sendToSelf, broadcastToOthers);
            }

            instance.CompletePartyCardSelection(generation);
            if (!instance.TryGetOwnPartyCardColumn(characterId, PartyCardType.Free, out var ownColumn))
            {
                // 没有归属（例如中途插入、没有槽位）：**绝不自选第 0 列**，只登记审计。
                audit(new PartyCardAuditEntry(instance.PartyCardSettlementId, generation, PartyCardType.Free, PartyCardSelectionState.Unassigned, characterId,
                    $"own-free-column-missing:{source}"));
                return await PublishAsync(instance, generation, PartyCardSelectionState.Unassigned, accepted: false, sendToSelf, broadcastToOthers);
            }

            await instance.SettleOwnPartyCardAsync(characterId, PartyCardType.Free, ownColumn, generation, cancellationToken);
            return await PublishAsync(instance, generation, ownColumn, accepted: true, sendToSelf, broadcastToOthers);
        }
        finally
        {
            Flush(instance, audit);
        }
    }

    internal static async Task<PartyCardSettlementResult> PickFreeCardAsync(
        DungeonInstance instance,
        Guid characterId,
        byte column,
        string source,
        Func<GameServerPacket, Task> sendToSelf,
        Func<GameServerPacket, Task> broadcastToOthers,
        Action<PartyCardAuditEntry> audit,
        CancellationToken cancellationToken)
    {
        if (instance is not { IsParty: true })
        {
            return Inactive;
        }

        try
        {
            var generation = instance.EnsurePartyCardSettlement();
            if (generation == 0)
            {
                return Inactive;
            }

            var reservation = instance.TryReservePartyCard(characterId, PartyCardType.Free, column, generation);
            if (!reservation.Accepted)
            {
                audit(new PartyCardAuditEntry(instance.PartyCardSettlementId, generation, PartyCardType.Free, column, characterId,
                    $"free-reservation-rejected:{reservation.Status}:{source}"));
                return new PartyCardSettlementResult(true, generation, column, false, false);
            }

            await instance.SettleOwnPartyCardAsync(characterId, PartyCardType.Free, column, generation, cancellationToken);
            return await PublishAsync(instance, generation, column, accepted: true, sendToSelf, broadcastToOthers);
        }
        finally
        {
            Flush(instance, audit);
        }
    }

    internal static async Task<PartyCardSettlementResult> BuyPaidCardAsync(
        DungeonInstance instance,
        Guid characterId,
        byte column,
        Func<bool> canAfford,
        string source,
        Func<GameServerPacket, Task> sendToSelf,
        Func<GameServerPacket, Task> broadcastToOthers,
        Action<PartyCardAuditEntry> audit,
        CancellationToken cancellationToken)
    {
        if (instance is not { IsParty: true })
        {
            return Inactive;
        }

        try
        {
            var generation = instance.EnsurePartyCardSettlement();
            if (generation == 0)
            {
                return Inactive;
            }

            if (!canAfford())
            {
                // 买不起：**先拒**，否则会白占一列，别人的整表里就会出现一个永不到账的付费列。
                audit(new PartyCardAuditEntry(instance.PartyCardSettlementId, generation, PartyCardType.Paid, column, characterId,
                    $"paid-unaffordable:{source}"));
                return new PartyCardSettlementResult(true, generation, column, false, false);
            }

            var reservation = instance.TryReservePartyCard(characterId, PartyCardType.Paid, column, generation);
            if (!reservation.Accepted)
            {
                audit(new PartyCardAuditEntry(instance.PartyCardSettlementId, generation, PartyCardType.Paid, column, characterId,
                    $"paid-reservation-rejected:{reservation.Status}:{source}"));
                return new PartyCardSettlementResult(true, generation, column, false, false);
            }

            await instance.SettleOwnPartyCardAsync(characterId, PartyCardType.Paid, column, generation, cancellationToken);
            return await PublishAsync(instance, generation, column, accepted: true, sendToSelf, broadcastToOthers);
        }
        finally
        {
            Flush(instance, audit);
        }
    }

    private static readonly PartyCardSettlementResult Inactive =
        new(false, 0, PartyCardSelectionState.Unassigned, false, false);

    /// <summary>
    /// 归属集合增长时发布整表：从**实例那一份表**构图（不是各会话自己那份），
    /// 同一份包对象交给"发给自己 + 其他成员"，因此所有成员字节相同、触发者不重复收（R3′）。
    /// </summary>
    private static async Task<PartyCardSettlementResult> PublishAsync(
        DungeonInstance instance,
        long generation,
        byte column,
        bool accepted,
        Func<GameServerPacket, Task> sendToSelf,
        Func<GameServerPacket, Task> broadcastToOthers)
    {
        if (!instance.TryTakePartyCardPublication(generation, out var snapshot) || snapshot is null)
        {
            return new PartyCardSettlementResult(true, generation, column, accepted, false);
        }

        var packet = GameProtocolEngine.CreateSelectCardReply(
            snapshot.Selection.FreeOwners,
            snapshot.Selection.PaidOwners,
            snapshot.PaidItems(),
            snapshot.FreeRewards());
        await sendToSelf(packet);
        await broadcastToOthers(packet);
        return new PartyCardSettlementResult(true, generation, column, accepted, true);
    }

    private static void Flush(DungeonInstance instance, Action<PartyCardAuditEntry> audit)
    {
        foreach (var entry in instance.DrainPartyCardAudit())
        {
            audit(entry);
        }
    }
}
