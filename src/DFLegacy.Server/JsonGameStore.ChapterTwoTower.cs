using DFLegacy.Protocol;
using System.Text.Json.Serialization;

namespace DFLegacy.Server;

public sealed record ChapterTwoTowerRankMember(string Name, byte Level, byte Job)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public byte GrowType { get; init; }

    internal static ChapterTwoTowerRankMember Capture(CharacterRecord character) =>
        new(character.Name, checked((byte)character.Level), checked((byte)character.Job))
        {
            GrowType = (byte)(character.GrowType & 0x0F)
        };
}

public sealed record ChapterTwoTowerPersonalBest(ChapterTwoTowerRankMember[] Members,
    ushort ClearedFloor, uint TotalMilliseconds);

public sealed partial record CharacterRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChapterTwoTowerPersonalBest>? ChapterTwoTowerPersonalBests { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? LastChapterTwoTowerRewardAttempt { get; init; }
}

public sealed partial class JsonGameStore
{
    internal async Task<(ChapterTwoTowerPersonalBest? Best, bool Improved)> SaveChapterTwoTowerPersonalBestAsync(
        string accountName, Guid characterId, ChapterTwoTowerPersonalBest attempt, CancellationToken cancellationToken)
    {
        if (attempt.Members.Length is < 1 or > 4 || attempt.ClearedFloor == 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!TryFindCharacterLocked(accountName, characterId, out _, out _, out var character))
                return (null, false);
            var records = character.ChapterTwoTowerPersonalBests ?? [];
            var prior = records.FirstOrDefault(record => record.Members.Length == attempt.Members.Length);
            if (_rewardQuarantined || prior is not null
                && (prior.ClearedFloor > attempt.ClearedFloor || prior.ClearedFloor == attempt.ClearedFloor
                    && prior.TotalMilliseconds <= attempt.TotalMilliseconds))
                return (prior, false);

            var next = character with
            {
                ChapterTwoTowerPersonalBests = [.. records.Where(record => record.Members.Length != attempt.Members.Length), attempt]
            };
            var accounts = CopyStoreAccountsLocked();
            SetStoreCharacter(accounts, next);
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                logger.LogError(error, "Tower personal best snapshot outcome uncertain; writes quarantined.");
                return (prior, false);
            }
            _accounts = accounts;
            return (attempt, prior is not null);
        }
        finally { _gate.Release(); }
    }

    internal async Task<IReadOnlyDictionary<Guid, TowerPartyTicketResult>?> ConsumeChapterTwoTowerPartyTicketsAsync(
        IReadOnlyList<TowerPartyApplicant> members, DungeonTowerDefinition tower, Func<bool> entryIsCurrent,
        Func<Guid, ushort, bool> isEscrowed, Func<CharacterItemRecord, GameInventoryEntry> project,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined || members.Count is < 2 or > 4 || !entryIsCurrent()) return null;
            var results = new Dictionary<Guid, TowerPartyTicketResult>();
            foreach (var applicant in members)
            {
                var id = applicant.Character.Id;
                if (IsEconomicWriteBlocked(id) || !TryFindCharacterLocked(applicant.Account, id, out _, out _, out var current)
                    || current.Level < tower.MinimumLevel || !ItemsEqual(current.Inventory, applicant.Character.Inventory ?? []))
                    return null;

                var planned = (current.Inventory ?? []).ToDictionary(item => item.Slot);
                var updates = new List<GameInventoryEntry>();
                var needed = tower.Ticket.Quantity;
                foreach (var stack in planned.Values.Where(item => item.ItemId == tower.Ticket.ItemId
                             && !isEscrowed(id, item.Slot)).OrderBy(item => item.Slot).ToArray())
                {
                    if (needed == 0) break;
                    var used = Math.Min(needed, stack.CountOrValue);
                    needed -= used;
                    if (!tower.Ticket.Consume) continue;
                    var remaining = stack with { CountOrValue = stack.CountOrValue - used };
                    if (remaining.CountOrValue == 0)
                    {
                        planned.Remove(stack.Slot);
                        updates.Add(new GameInventoryEntry(stack.Slot, ushort.MaxValue, 0));
                    }
                    else
                    {
                        planned[stack.Slot] = remaining;
                        updates.Add(project(remaining));
                    }
                }
                if (needed != 0 || !results.TryAdd(id, new TowerPartyTicketResult(
                    current with { Inventory = planned.Values.OrderBy(item => item.Slot).ToList() }, [.. updates])))
                    return null;
            }
            if (!entryIsCurrent()) return null;
            if (!tower.Ticket.Consume) return results;

            var accounts = CopyStoreAccountsLocked();
            foreach (var result in results.Values) SetStoreCharacter(accounts, result.Character);
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                logger.LogError(error, "Tower party ticket snapshot outcome uncertain; writes quarantined.");
                return null;
            }
            _accounts = accounts;
            return results;
        }
        finally { _gate.Release(); }
    }

    internal async Task<bool> HasChapterTwoTowerPartyTicketsAsync(IReadOnlyCollection<Guid> members,
        DungeonTowerTicket ticket, Func<Guid, ushort, bool> isEscrowed, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined || members.Count == 0) return false;
            var matching = _accounts.SelectMany(account => account.Characters).Where(c => members.Contains(c.Id)).ToArray();
            return matching.Length == members.Count && matching.All(character =>
                (character.Inventory ?? []).Where(item => item.ItemId == ticket.ItemId
                    && !isEscrowed(character.Id, item.Slot)).Sum(item => (long)item.CountOrValue) >= ticket.Quantity);
        }
        finally { _gate.Release(); }
    }

    internal async Task<CharacterRecord?> SaveChapterTwoTowerRewardAsync(string accountName, Guid attemptId,
        CharacterRecord expected, ClearRewardGrantPlan reward, CharacterExperienceGrant experience,
        int skillPoints, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined || attemptId == Guid.Empty || IsEconomicWriteBlocked(expected.Id)
                || !TryFindCharacterLocked(accountName, expected.Id, out _, out _, out var current)
                || current.LastChapterTwoTowerRewardAttempt == attemptId
                || current.Gold != expected.Gold || current.Level != expected.Level
                || current.Experience != expected.Experience || current.Sp != expected.Sp
                || current.WarehouseCapacity != expected.WarehouseCapacity
                || !ItemsEqual(current.Inventory, expected.Inventory ?? [])
                || !ItemsEqual(current.Equipment, expected.Equipment ?? [])
                || !HasDungeonGoldCapacity(current, reward.TargetGold)
                || !InventoryPlanPreservesBaseline(current.Inventory, reward.Inventory.Values)
                || !TryAppendGeneratedMailsLocked(current, reward.Mails, out var mailbox, out _, out var mailCapacity))
                return null;

            var updated = current with
            {
                Gold = reward.TargetGold,
                Inventory = reward.Inventory.Values.OrderBy(item => item.Slot).ToList(),
                Mailbox = mailbox,
                WarehouseCapacity = Math.Max(reward.WarehouseCapacity, mailCapacity),
                Level = experience.Level,
                Experience = experience.Experience,
                Sp = skillPoints,
                LastChapterTwoTowerRewardAttempt = attemptId
            };
            var accounts = CopyStoreAccountsLocked();
            SetStoreCharacter(accounts, updated);

            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                logger.LogError(error, "Tower reward snapshot outcome uncertain; writes quarantined.");
                return null;
            }

            _accounts = accounts;
            return updated;
        }
        finally { _gate.Release(); }
    }
}
