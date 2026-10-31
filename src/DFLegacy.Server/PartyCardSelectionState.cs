using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal enum PartyCardType : byte { Free = 0, Paid = 1 }
internal enum PartyCardSelectionStatus : byte { Reserved = 0, AlreadyReserved = 1, Rejected = 2 }
internal enum PartyCardCommitStatus : byte { Pending, Committed, Rejected, Unknown }
internal readonly record struct PartyCardCommitOutcome(DungeonRewardLedgerStatus Status, string? AuditReason);
internal interface IPartyCardRewardAgent
{
    /// <summary>
    /// 用**这个成员自己**的参数掷出它那一列的内容（<paramref name="settlementId"/> 是实例在
    /// 校验归属的同一把锁里给出的本局结算标识，幂等键必须用它，不能用会话当时读到的"当前值"）。
    /// </summary>
    PartyCardContent RollOwnColumn(Guid settlementId, PartyCardType cardType, byte column, long generation);

    Task<PartyCardCommitOutcome> CommitOwnColumnAsync(Guid settlementId, PartyCardType cardType, byte column, long generation, PartyCardContent content, CancellationToken cancellationToken);
}
internal sealed record PartyCardContent(PartyCardType Type, byte Column, byte PartySlot, DungeonClearRewardRoll Roll, PartyCardCommitStatus Status);
internal sealed record PartyCardPublishedSnapshot(long Generation, PartyCardSelectionSnapshot Selection, IReadOnlyList<PartyCardContent> Contents)
{
    internal GameClearCardReward?[] FreeRewards()
    {
        var rewards = new GameClearCardReward?[4];
        foreach (var content in Contents)
        {
            if (content.Type == PartyCardType.Free
                && content.Column < rewards.Length
                && Selection.FreeOwners[content.Column] == content.PartySlot
                && content.Status == PartyCardCommitStatus.Committed)
            {
                rewards[content.Column] = new(content.Roll.FreeGoldAmount, content.Roll.FreeItemId);
            }
        }
        return rewards;
    }

    /// <summary>
    /// 整表 CMD 74 的付费列记录：某列已归属时给出该列归属成员自己掷出的付费物品 id
    /// （没有物品 → null = 只有一条零金币记录）。构包口径见
    /// <c>GameProtocolEngine.CreateSelectCardReply(freeOwners, paidOwners, paidItems)</c>。
    /// <para>
    /// 只有**真的发放成功**（<see cref="PartyCardCommitStatus.Committed"/>）的列才写物品 id：
    /// `Rejected` 是确定性终态、该列可以带归属发布（R5），但存档里没扣钱也没进背包，
    /// 不能在整表里给客户端看一张不存在的卡（评审 F2）。
    /// </para>
    /// </summary>
    internal ushort?[] PaidItems()
    {
        var items = new ushort?[4];
        for (byte column = 0; column < 4; column++)
        {
            var owner = Selection.PaidOwners[column];
            if (owner == PartyCardSelectionState.Unassigned)
            {
                continue;
            }

            foreach (var content in Contents)
            {
                if (content.Type == PartyCardType.Paid
                    && content.Column == column
                    && content.PartySlot == owner
                    && content.Status == PartyCardCommitStatus.Committed)
                {
                    items[column] = content.Roll.GoldItemId;
                    break;
                }
            }
        }

        return items;
    }
}

/// <summary>
/// 翻牌接线的一笔审计：代理缺失 / 代理异常 / 代理取消 / 代理超时（含迟到结果）/ 内容串列 /
/// 上报被拒 / 提交非 Committed。这些路径一律**不重试、不发布**，只留痕（R4）。
/// <para>
/// 条目自带**产生它那一刻**的 <see cref="SettlementId"/> 与代次：会话写日志时不能拿"当前"结算 id，
/// 否则换房/停局之后同一条日志会显示"新结算 id + 旧代次"，运维无法据此对账（评审 F6）。
/// </para>
/// </summary>
internal readonly record struct PartyCardAuditEntry(
    Guid SettlementId,
    long Generation,
    PartyCardType Type,
    byte Column,
    Guid CharacterId,
    string Reason);

internal readonly record struct PartyCardSelectionOutcome(
    PartyCardSelectionStatus Status, long Generation, byte Column, byte PartySlot)
{
    public bool Accepted => Status is PartyCardSelectionStatus.Reserved or PartyCardSelectionStatus.AlreadyReserved;
}

internal sealed record PartyCardSelectionSnapshot(
    long Generation, bool AutoCompleted, byte[] FreeOwners, byte[] PaidOwners, byte[] MemberSlots)
{
    public PartyCardSelectionSnapshot Copy() => new(Generation, AutoCompleted,
        (byte[])FreeOwners.Clone(), (byte[])PaidOwners.Clone(), (byte[])MemberSlots.Clone());
}

internal sealed class PartyCardSelectionState
{
    internal const byte Unassigned = 0xFF;
    private readonly byte[] _freeOwners = [Unassigned, Unassigned, Unassigned, Unassigned];
    private readonly byte[] _paidOwners = [Unassigned, Unassigned, Unassigned, Unassigned];
    private readonly Dictionary<Guid, byte> _slots = [];
    private long _generation;
    private bool _active;
    private bool _autoCompleted;
    // 每一局结算一份全局唯一的 settlement id：代次号是**实例内**的（新实例又从 0 开始），
    // 幂等键若只用代次号，第二次进同一个副本就会撞上第一次留下的冻结载荷
    // → `idempotency-payload-mismatch` → 本局奖励永久发不出去。
    private Guid _settlementId = Guid.NewGuid();
    internal long Generation => _generation;
    internal Guid SettlementId => _settlementId;

    internal long Begin(IReadOnlyList<DungeonInstanceMember> members)
    {
        _generation++; _active = true; _autoCompleted = false; _settlementId = Guid.NewGuid();
        Array.Fill(_freeOwners, Unassigned); Array.Fill(_paidOwners, Unassigned); _slots.Clear();
        foreach (var member in members.Where(m => m.PartySlot < 4).GroupBy(m => m.PartySlot).Where(g => g.Count() == 1).Select(g => g.Single())) _slots.TryAdd(member.CharacterId, member.PartySlot);
        return _generation;
    }
    internal void Invalidate()
    {
        _generation++; _active = false; _autoCompleted = false; _slots.Clear();
        Array.Fill(_freeOwners, Unassigned); Array.Fill(_paidOwners, Unassigned);
    }
    internal PartyCardSelectionOutcome TryReserve(Guid characterId, PartyCardType type, byte column, long generation)
    {
        if (!_active || generation != _generation || column >= 4 || !_slots.TryGetValue(characterId, out var slot)) return new(PartyCardSelectionStatus.Rejected, _generation, column, Unassigned);
        var owners = type == PartyCardType.Free ? _freeOwners : type == PartyCardType.Paid ? _paidOwners : null;
        if (owners is null) return new(PartyCardSelectionStatus.Rejected, _generation, column, slot);
        if (owners[column] == slot) return new(PartyCardSelectionStatus.AlreadyReserved, _generation, column, slot);
        if (_autoCompleted && type == PartyCardType.Free) return new(PartyCardSelectionStatus.Rejected, _generation, column, slot);
        if (owners.Contains(slot) || owners[column] != Unassigned) return new(PartyCardSelectionStatus.Rejected, _generation, column, slot);
        owners[column] = slot; return new(PartyCardSelectionStatus.Reserved, _generation, column, slot);
    }
    internal PartyCardSelectionOutcome CompleteAuto(long generation)
    {
        if (!_active || generation != _generation) return new(PartyCardSelectionStatus.Rejected, _generation, Unassigned, Unassigned);
        if (_autoCompleted) return new(PartyCardSelectionStatus.AlreadyReserved, _generation, Unassigned, Unassigned);
        _autoCompleted = true; var used = _freeOwners.Where(x => x != Unassigned).ToHashSet(); var available = _slots.Values.Distinct().OrderBy(x => x).Where(x => !used.Contains(x)).ToArray(); var index = 0;
        for (byte column = 0; column < 4 && index < available.Length; column++) if (_freeOwners[column] == Unassigned) _freeOwners[column] = available[index++];
        return new(PartyCardSelectionStatus.Reserved, _generation, Unassigned, Unassigned);
    }
    internal void RemoveMember(Guid characterId) => _slots.Remove(characterId);
    internal PartyCardSelectionSnapshot Snapshot() => new(_generation, _autoCompleted, (byte[])_freeOwners.Clone(), (byte[])_paidOwners.Clone(), _slots.Values.Distinct().OrderBy(x => x).ToArray());
}
