using System.Collections.Concurrent;
using System.Text.Json;

namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    private readonly ConcurrentQueue<MentorRemovalNotice> _mentorRemovalNotices = new();

    internal bool TryTakeMentorRemovalNotice(out MentorRemovalNotice? notice) =>
        _mentorRemovalNotices.TryDequeue(out notice);

    private static CharacterRecord? MentorCharacter(IEnumerable<AccountRecord> accounts, Guid id) =>
        accounts.SelectMany(a => a.Characters).FirstOrDefault(c => c.Id == id);

    private static void SetMentorCharacter(List<AccountRecord> accounts, CharacterRecord character)
    {
        var account = accounts.First(a => a.Characters.Any(c => c.Id == character.Id));
        account.Characters[account.Characters.FindIndex(c => c.Id == character.Id)] = character;
    }

    private static bool HasMentorRelations(IEnumerable<AccountRecord> accounts, Guid id) =>
        MentorCharacter(accounts, id)?.MentorCharacterId is not null
        || accounts.SelectMany(a => a.Characters).Any(c => c.MentorCharacterId == id);

    private List<AccountRecord> CopyMentorAccountsLocked() =>
        JsonSerializer.Deserialize<List<AccountRecord>>(JsonSerializer.Serialize(_accounts))!;

    private List<MentorRemovalNotice> PrepareRemovedMentorEdgesLocked(List<AccountRecord> after)
    {
        var survivors = after.SelectMany(a => a.Characters).ToDictionary(c => c.Id);
        foreach (var character in survivors.Values.ToArray())
        {
            if (character.MentorCharacterId is not { } parent)
            {
                if (character.MentorRelationshipExperience != 0 || character.MentorExperienceBonusPercent != 0)
                {
                    var orphanClean = character with { MentorRelationshipExperience = 0, MentorExperienceBonusPercent = 0 };
                    SetMentorCharacter(after, orphanClean);
                    survivors[character.Id] = orphanClean;
                }
                continue;
            }
            var seen = new HashSet<Guid> { character.Id };
            var cursor = parent;
            while (survivors.TryGetValue(cursor, out var ancestor) && seen.Add(cursor))
            {
                if (ancestor.MentorCharacterId is not { } next) break;
                cursor = next;
            }
            if (survivors.ContainsKey(parent) && cursor != character.Id && !seen.Contains(cursor)) continue;
            // A retained valid chain ends at a root already visited above.
            var rootValid = survivors.TryGetValue(cursor, out var root) && root.MentorCharacterId is null;
            if (rootValid && parent != character.Id) continue;
            var clean = character with { MentorCharacterId = null, MentorExperienceBonusPercent = 0, MentorRelationshipExperience = 0 };
            SetMentorCharacter(after, clean);
            survivors[character.Id] = clean;
        }
        foreach (var character in survivors.Values.ToArray())
            if (!HasMentorRelations(after, character.Id))
                SetMentorCharacter(after, character with { MentorRegistration = null });
        var before = _accounts.SelectMany(a => a.Characters).ToArray();
        var notices = new List<MentorRemovalNotice>();
        foreach (var owner in survivors.Values)
        {
            var old = before.FirstOrDefault(c => c.Id == owner.Id);
            if (old is null) continue;
            var peers = before.Where(c => c.Id == old.MentorCharacterId || c.MentorCharacterId == old.Id);
            foreach (var removed in peers.Where(c => !survivors.ContainsKey(c.Id)))
                notices.Add(new(owner.Id, removed.Name));
        }
        return notices;
    }

    private void PublishMentorRemovalNotices(IEnumerable<MentorRemovalNotice> notices)
    {
        foreach (var notice in notices) _mentorRemovalNotices.Enqueue(notice);
    }

    private async Task<bool> SaveMentorAccountsLockedAsync(List<AccountRecord> accounts, CancellationToken ct)
    {
        try { await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct); }
        catch (Exception exception)
        {
            _rewardQuarantined = true;
            logger.LogError(exception, "Mentorship snapshot outcome is uncertain; store quarantined.");
            return false;
        }
        _accounts = accounts;
        ReconcileMentorGoldEdgesLocked();
        return true;
    }

    internal async Task<MentorRoster?> GetMentorRosterAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var owner = MentorCharacter(_accounts, id);
            if (owner is null) return null;
            MentorListMember? Entry(Guid peer)
            {
                var account = _accounts.FirstOrDefault(a => a.Characters.Any(c => c.Id == peer));
                var character = account?.Characters.First(c => c.Id == peer);
                return character is null ? null : new(character, account!.UserName,
                    account.Blacklist?.Any(b => string.Equals(b.Name, owner.Name, StringComparison.Ordinal)) == true);
            }
            var master = owner.MentorCharacterId is { } parent ? Entry(parent) : null;
            var apprentices = _accounts.SelectMany(a => a.Characters)
                .Where(c => c.MentorCharacterId == id).OrderBy(c => c.CharacterNo)
                .Select(c => Entry(c.Id)!).ToArray();
            return new(owner, master, apprentices);
        }
        finally { _gate.Release(); }
    }

    private byte CheckMentorPairLocked(Guid requesterId, Guid targetId, DateTimeOffset now)
    {
        var all = _accounts.SelectMany(a => a.Characters).ToArray();
        var requester = all.FirstOrDefault(c => c.Id == requesterId);
        var target = all.FirstOrDefault(c => c.Id == targetId);
        if (requester is null || target is null || requesterId == targetId) return 49;
        if (requester.MentorCharacterId == targetId || target.MentorCharacterId == requesterId) return 50;
        if ((HasMentorRelations(_accounts, requesterId) && !MentorRules.CanRegister(requester.MentorRegistration, now))
            || (HasMentorRelations(_accounts, targetId) && !MentorRules.CanRegister(target.MentorRegistration, now))) return 55;
        var targetAccount = _accounts.First(a => a.Characters.Any(c => c.Id == targetId));
        if (targetAccount.Blacklist?.Any(b => string.Equals(b.Name, requester.Name, StringComparison.Ordinal)) == true) return 54;
        if (requester.Level == target.Level) return 44;
        var master = requester.Level > target.Level ? requester : target;
        var child = master.Id == requester.Id ? target : requester;
        var requesterIsChild = child.Id == requesterId;
        if (master.Level < 18) return 46;
        if (child.MentorCharacterId is not null) return requesterIsChild ? (byte)53 : (byte)43;
        if (all.Count(c => c.MentorCharacterId == master.Id) >= MentorRules.Capacity(master.Level))
            return requesterIsChild ? (byte)42 : (byte)52;
        var seen = new HashSet<Guid>();
        var ancestor = master;
        while (ancestor.MentorCharacterId is { } parent)
        {
            if (parent == child.Id || !seen.Add(parent)) return requesterIsChild ? (byte)53 : (byte)43;
            ancestor = all.FirstOrDefault(c => c.Id == parent);
            if (ancestor is null) break;
        }
        return 0;
    }

    internal async Task<byte> CheckMentorPairAsync(Guid requester, Guid target, DateTimeOffset now,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return CheckMentorPairLocked(requester, target, now); }
        finally { _gate.Release(); }
    }

    internal async Task<MentorMutationResult> EstablishMentorAsync(Guid requesterId, Guid targetId,
        DateTimeOffset now, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown, Reason: "store-quarantined");
            var error = CheckMentorPairLocked(requesterId, targetId, now);
            if (error != 0) return new(DungeonRewardLedgerStatus.Rejected, error);
            var accounts = CopyMentorAccountsLocked();
            var requester = MentorCharacter(accounts, requesterId)!;
            var target = MentorCharacter(accounts, targetId)!;
            var requesterIsMaster = requester.Level > target.Level;
            var master = requesterIsMaster ? requester : target;
            var child = requesterIsMaster ? target : requester;
            child = child with { MentorCharacterId = master.Id, MentorExperienceBonusPercent = 0, MentorRelationshipExperience = 0 };
            master = master with { MentorRegistration = (master.MentorRegistration ?? new()) with { RegisteredAtUnix = now.ToUnixTimeSeconds() } };
            child = child with { MentorRegistration = (child.MentorRegistration ?? new()) with { RegisteredAtUnix = now.ToUnixTimeSeconds() } };
            SetMentorCharacter(accounts, master);
            SetMentorCharacter(accounts, child);
            if (!await SaveMentorAccountsLockedAsync(accounts, ct)) return new(DungeonRewardLedgerStatus.Unknown, Reason: "uncertain-write");
            return new(DungeonRewardLedgerStatus.Committed, Requester: requesterIsMaster ? master : child,
                Target: requesterIsMaster ? child : master);
        }
        finally { _gate.Release(); }
    }

    internal async Task<MentorMutationResult> RemoveMentorAsync(Guid requesterId, string targetName,
        DateTimeOffset now, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown, Reason: "store-quarantined");
            var requester = MentorCharacter(_accounts, requesterId);
            var target = _accounts.SelectMany(a => a.Characters).FirstOrDefault(c =>
                string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (requester is null || target is null
                || (requester.MentorCharacterId != target.Id && target.MentorCharacterId != requester.Id))
                return new(DungeonRewardLedgerStatus.Rejected, 49);
            var accounts = CopyMentorAccountsLocked();
            var child = requester.MentorCharacterId == target.Id ? requester : target;
            SetMentorCharacter(accounts, child with { MentorCharacterId = null, MentorExperienceBonusPercent = 0, MentorRelationshipExperience = 0 });
            requester = MentorCharacter(accounts, requesterId)!;
            SetMentorCharacter(accounts, requester with
            {
                MentorRegistration = (requester.MentorRegistration ?? new()) with { DeletedAtUnix = now.ToUnixTimeSeconds() }
            });
            var requesterEmpty = !HasMentorRelations(accounts, requester.Id);
            var targetEmpty = !HasMentorRelations(accounts, target.Id);
            foreach (var id in new[] { requester.Id, target.Id })
                if (!HasMentorRelations(accounts, id))
                    SetMentorCharacter(accounts, MentorCharacter(accounts, id)! with { MentorRegistration = null });
            if (!await SaveMentorAccountsLockedAsync(accounts, ct)) return new(DungeonRewardLedgerStatus.Unknown, Reason: "uncertain-write");
            return new(DungeonRewardLedgerStatus.Committed, Requester: MentorCharacter(accounts, requester.Id),
                Target: MentorCharacter(accounts, target.Id), RequesterEmpty: requesterEmpty, TargetEmpty: targetEmpty);
        }
        finally { _gate.Release(); }
    }
}
