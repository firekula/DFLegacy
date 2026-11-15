namespace DFLegacy.Server;

internal sealed record MentorGrowthEdge(Guid ChildId, Guid MasterId, uint Experience);
internal sealed record MentorGrowthResult(DungeonRewardLedgerStatus Status, IReadOnlyList<MentorGrowthEdge> Edges);

public sealed partial class JsonGameStore
{
    private bool NormalizeMentorGrowthLocked()
    {
        var changed = false;
        foreach (var account in _accounts)
        for (var index = 0; index < account.Characters.Count; index++)
        {
            var character = account.Characters[index];
            var experience = character.MentorCharacterId is { } parent && parent != character.Id
                && MentorCharacter(_accounts, parent) is not null
                ? Math.Min(character.MentorRelationshipExperience, MentorGrowthPolicy.MaximumExperience) : 0;
            if (experience == character.MentorRelationshipExperience) continue;
            account.Characters[index] = character with { MentorRelationshipExperience = experience };
            changed = true;
        }
        return changed;
    }

    // Called with the service gate held. A single endpoint may grow both its parent and child edges.
    internal async Task<MentorGrowthResult> GrowMentorEndpointAsync(Guid endpoint, IReadOnlySet<Guid> online,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown, []);
            if (!online.Contains(endpoint) || MentorCharacter(_accounts, endpoint) is null)
                return new(DungeonRewardLedgerStatus.Rejected, []);
            var edges = _accounts.SelectMany(a => a.Characters)
                .Where(c => c.MentorCharacterId is { } parent && (c.Id == endpoint || parent == endpoint)
                    && online.Contains(c.Id) && online.Contains(parent) && MentorCharacter(_accounts, parent) is not null
                    && c.MentorRelationshipExperience < MentorGrowthPolicy.MaximumExperience)
                .Select(c => new MentorGrowthEdge(c.Id, c.MentorCharacterId!.Value, c.MentorRelationshipExperience + 1)).ToArray();
            if (edges.Length == 0) return new(DungeonRewardLedgerStatus.Committed, []);
            var accounts = CopyMentorAccountsLocked();
            foreach (var edge in edges)
                SetMentorCharacter(accounts, MentorCharacter(accounts, edge.ChildId)! with { MentorRelationshipExperience = edge.Experience });
            if (!await SaveMentorAccountsLockedAsync(accounts, ct)) return new(DungeonRewardLedgerStatus.Unknown, []);
            return new(DungeonRewardLedgerStatus.Committed, edges);
        }
        finally { _gate.Release(); }
    }
}
