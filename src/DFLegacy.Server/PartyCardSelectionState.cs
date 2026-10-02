namespace DFLegacy.Server;

internal enum PartyCardType : byte { Free = 0, Paid = 1 }
internal enum PartyCardSelectionStatus : byte { Reserved = 0, AlreadyReserved = 1, Rejected = 2 }
internal enum PartyCardCommitStatus : byte { Pending, Committed, Rejected, Unknown }
internal readonly record struct PartyCardCommitOutcome(DungeonRewardLedgerStatus Status, string? AuditReason);
internal interface IPartyCardRewardAgent
{
    PartyCardContent RollOwnColumn(PartyCardType cardType, byte column, long generation);
    Task<PartyCardCommitOutcome> CommitOwnColumnAsync(PartyCardType cardType, byte column, long generation, PartyCardContent content, CancellationToken cancellationToken);
}
internal sealed record PartyCardContent(PartyCardType Type, byte Column, byte PartySlot, DungeonClearRewardRoll Roll, PartyCardCommitStatus Status);
internal sealed record PartyCardPublishedSnapshot(long Generation, PartyCardSelectionSnapshot Selection, IReadOnlyList<PartyCardContent> Contents)
{
    /// <summary>
    /// 整表 CMD 74 的付费列记录：某列已归属时给出该列归属成员自己掷出的付费物品 id
    /// （没有物品 → null = 只有一条零金币记录）。构包口径见
    /// <c>GameProtocolEngine.CreateSelectCardReply(freeOwners, paidOwners, paidItems)</c>。
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
                    && content.PartySlot == owner)
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
/// 翻牌接线的一笔审计：代理缺失 / 代理异常 / 代理超时 / 上报被拒 / 提交非 Committed。
/// 这些路径一律**不重试、不发布**，只留痕（R4）。持锁写入，由会话取出后写日志。
/// </summary>
internal readonly record struct PartyCardAuditEntry(
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
    internal long Generation => _generation;

    internal long Begin(IReadOnlyList<DungeonInstanceMember> members)
    {
        _generation++; _active = true; _autoCompleted = false;
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
