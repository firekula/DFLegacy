using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    private sealed record GoldEdge(Guid Master, Guid Generation);
    private sealed record GoldBatch(Guid ChildIncarnation, Guid Master, Guid MasterIncarnation,
        Guid EdgeGeneration, uint Basis);
    private readonly Dictionary<Guid, Guid> _mentorGoldEndpoints = [];
    private readonly Dictionary<Guid, GoldEdge> _mentorGoldEdges = [];
    private readonly Dictionary<Guid, GoldBatch> _mentorGoldBatches = [];
    private static long PendingMentorGold(CharacterRecord c) =>
        c.MentorGoldAwards?.Where(a => !a.Applied).Sum(a => (long)a.CreditedGold) ?? 0;

    private void DropMentorGoldEndpointLocked(Guid id)
    {
        _mentorGoldEndpoints.Remove(id);
        _mentorGoldBatches.Remove(id);
        foreach (var child in _mentorGoldBatches.Where(p => p.Value.Master == id).Select(p => p.Key).ToArray())
            _mentorGoldBatches.Remove(child);
    }

    // Called immediately after every successful canonical edge mutation, including deletion.
    private void ReconcileMentorGoldEdgesLocked()
    {
        foreach (var edge in _mentorGoldEdges.ToArray())
            if (MentorCharacter(_accounts, edge.Key)?.MentorCharacterId != edge.Value.Master
                || MentorCharacter(_accounts, edge.Value.Master) is null)
            { _mentorGoldEdges.Remove(edge.Key); _mentorGoldBatches.Remove(edge.Key); }
        foreach (var id in _mentorGoldEndpoints.Keys.ToArray())
            if (MentorCharacter(_accounts, id) is null) DropMentorGoldEndpointLocked(id);
    }

    internal async Task SetMentorGoldEndpointAsync(Guid id, Guid incarnation, bool online, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (incarnation == Guid.Empty) return;
            if (!online)
            {
                if (_mentorGoldEndpoints.GetValueOrDefault(id) == incarnation) DropMentorGoldEndpointLocked(id);
                return;
            }
            if (MentorCharacter(_accounts, id) is null) return;
            if (_mentorGoldEndpoints.GetValueOrDefault(id) == incarnation) return;
            DropMentorGoldEndpointLocked(id);
            _mentorGoldEndpoints[id] = incarnation;
        }
        finally { _gate.Release(); }
    }

    // This hook is deliberately synchronous and nonthrowing for ineligible/overflow sources.
    private void AccrueMentorGoldLocked(Guid childId, Guid incarnation, uint requested)
    {
        if (requested is 0 or > int.MaxValue || incarnation == Guid.Empty
            || _mentorGoldEndpoints.GetValueOrDefault(childId) != incarnation) return;
        var child = MentorCharacter(_accounts, childId);
        if (child?.MentorCharacterId is not { } masterId || masterId == childId
            || MentorCharacter(_accounts, masterId) is not { Level: >= 18 and <= 99 }
            || !_mentorGoldEndpoints.TryGetValue(masterId, out var masterIncarnation)) return;
        if (!_mentorGoldEdges.TryGetValue(childId, out var edge) || edge.Master != masterId)
        {
            edge = new(masterId, Guid.NewGuid()); _mentorGoldEdges[childId] = edge;
            _mentorGoldBatches.Remove(childId);
        }
        var batch = _mentorGoldBatches.GetValueOrDefault(childId);
        if (batch is null || batch.ChildIncarnation != incarnation || batch.MasterIncarnation != masterIncarnation
            || batch.EdgeGeneration != edge.Generation)
            batch = new(incarnation, masterId, masterIncarnation, edge.Generation, 0);
        if ((ulong)batch.Basis + requested > int.MaxValue)
        { logger.LogWarning("Mentor gold basis overflow rejected for {Character} attachment {Attachment}.", childId, incarnation); return; }
        _mentorGoldBatches[childId] = batch with { Basis = batch.Basis + requested };
    }

    internal async Task<MentorGoldFlushResult> FlushMentorGoldAsync(Guid childId, Guid incarnation,
        Guid flushId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await FlushMentorGoldLockedAsync(childId, incarnation, flushId, ct); }
        finally { _gate.Release(); }
    }

    internal async Task<MentorGoldFlushResult> RetireMentorGoldEndpointAsync(Guid childId, Guid incarnation,
        Func<MentorGoldRetirementReason> reason, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (incarnation == Guid.Empty || _mentorGoldEndpoints.GetValueOrDefault(childId) != incarnation)
                return new(DungeonRewardLedgerStatus.Rejected);
            // Read the captured old lease only here: a replacement can arrive while awaiting this gate.
            var result = reason() == MentorGoldRetirementReason.NormalLeave
                ? await FlushMentorGoldLockedAsync(childId, incarnation, Guid.NewGuid(), ct)
                : new MentorGoldFlushResult(DungeonRewardLedgerStatus.Rejected);
            DropMentorGoldEndpointLocked(childId);
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<MentorGoldFlushResult> FlushMentorGoldLockedAsync(Guid childId, Guid incarnation,
        Guid flushId, CancellationToken ct)
    {
        if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown);
        if (flushId == Guid.Empty || incarnation == Guid.Empty) return new(DungeonRewardLedgerStatus.Rejected);
        foreach (var owner in _accounts.SelectMany(a => a.Characters))
        {
            var prior = owner.MentorGoldAwards?.FirstOrDefault(a => a.FlushId == flushId);
            if (prior is not null)
                return prior.ChildId == childId && prior.ChildIncarnation == incarnation
                    ? new(DungeonRewardLedgerStatus.Committed, owner.Id, prior.MasterIncarnation, Receipt: prior)
                    : new(DungeonRewardLedgerStatus.Rejected);
        }
        if (_mentorGoldEndpoints.GetValueOrDefault(childId) != incarnation)
            return new(DungeonRewardLedgerStatus.Rejected);
        // Claim once, including zero/capped/unknown outcomes. Never reconstruct this runtime batch.
        if (!_mentorGoldBatches.Remove(childId, out var batch)) return new(DungeonRewardLedgerStatus.Committed);
        var child = MentorCharacter(_accounts, childId);
        var master = MentorCharacter(_accounts, batch.Master);
        if (child?.MentorCharacterId != batch.Master || master is null || master.Level is < 18 or > 99
            || batch.ChildIncarnation != incarnation
            || _mentorGoldEndpoints.GetValueOrDefault(master.Id) != batch.MasterIncarnation
            || !_mentorGoldEdges.TryGetValue(childId, out var edge) || edge.Generation != batch.EdgeGeneration
            || !StoreCatalog.TryGetGoldLimit(master.Level, out var limit))
            return new(DungeonRewardLedgerStatus.Rejected);
        var count = _accounts.SelectMany(a => a.Characters).Count(c => c.MentorCharacterId == master.Id
            && _mentorGoldEndpoints.ContainsKey(c.Id));
        var capacity = MentorGoldPolicy.Capacity(master.Level);
        if (capacity == 0) return new(DungeonRewardLedgerStatus.Rejected);
        var level = MentorGrowthPolicy.Project(child.MentorRelationshipExperience).Level;
        var requested = MentorGoldPolicy.Award(batch.Basis, MentorGoldPolicy.Rate(level, count, capacity));
        var available = Math.Max(0L, limit - (long)master.Gold - PendingDungeonGold(master)
            - PendingStoreGold(master) - PendingMentorGold(master));
        var actual = (uint)Math.Min(requested, available);
        var receipt = new MentorGoldAwardReceipt(flushId, "", childId, incarnation, batch.MasterIncarnation,
            batch.EdgeGeneration, child.Name, batch.Basis, level, count, capacity, requested, actual, actual == 0);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            receipt.FlushId, receipt.ChildId, receipt.ChildIncarnation, receipt.MasterIncarnation,
            receipt.EdgeGeneration, receipt.SourceName, receipt.Basis, receipt.RelationshipLevel,
            receipt.ConnectedChildren, receipt.Capacity, receipt.RequestedGold, receipt.CreditedGold
        })));
        receipt = receipt with { Fingerprint = fingerprint };
        var accounts = CopyMentorAccountsLocked();
        SetMentorCharacter(accounts, master with { MentorGoldAwards = [.. master.MentorGoldAwards ?? [], receipt] });
        if (!await SaveMentorAccountsLockedAsync(accounts, ct)) return new(DungeonRewardLedgerStatus.Unknown);
        logger.LogInformation("Mentor gold flush {Flush} child {Child} master {Master}: basis={Basis}, L={Level}, N={Count}, C={Capacity}, requested={Requested}, actual={Actual}.",
            flushId, childId, master.Id, batch.Basis, level, count, capacity, requested, actual);
        return new(DungeonRewardLedgerStatus.Committed, master.Id, batch.MasterIncarnation, true, receipt);
    }

    internal async Task<MentorGoldInstallResult> ConsumeMentorGoldCreditsAsync(Guid masterId,
        Guid? expectedIncarnation = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown);
            if (expectedIncarnation is { } token && (token == Guid.Empty
                || _mentorGoldEndpoints.GetValueOrDefault(masterId) != token)) return new(DungeonRewardLedgerStatus.Rejected);
            if (_pendingTradeDeliveries.ContainsKey(masterId)) return new(DungeonRewardLedgerStatus.Rejected);
            var character = MentorCharacter(_accounts, masterId);
            if (character is null) return new(DungeonRewardLedgerStatus.Rejected);
            var awards = (character.MentorGoldAwards ?? []).Where(a => !a.Applied
                && (expectedIncarnation is null || a.MasterIncarnation == expectedIncarnation)).ToArray();
            if (awards.Length == 0) return new(DungeonRewardLedgerStatus.Committed);
            var ids = awards.Select(a => a.FlushId).ToHashSet();
            var updated = character with
            {
                Gold = checked((int)(character.Gold + awards.Sum(a => (long)a.CreditedGold))),
                MentorGoldAwards = character.MentorGoldAwards!.Select(a => ids.Contains(a.FlushId) ? a with { Applied = true } : a).ToList()
            };
            var accounts = CopyMentorAccountsLocked(); SetMentorCharacter(accounts, updated);
            if (!await SaveMentorAccountsLockedAsync(accounts, ct)) return new(DungeonRewardLedgerStatus.Unknown);
            return new(DungeonRewardLedgerStatus.Committed, updated, awards);
        }
        finally { _gate.Release(); }
    }
}
