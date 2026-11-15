using System.Text.Json;

namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    private List<DungeonGoldLedgerEntry> _dungeonGoldLedger = [];

    private static long PendingDungeonGold(CharacterRecord character) =>
        character.PendingDungeonGoldCredits?.Sum(credit => (long)credit.Amount) ?? 0;

    private static bool HasDungeonGoldCapacity(CharacterRecord character, int targetGold) =>
        (long)Math.Max(0, targetGold) + PendingDungeonGold(character) + PendingStoreGold(character) + PendingMentorGold(character) <= int.MaxValue;

    internal async Task<DungeonGoldCommitResult> CommitDungeonGoldDistributionAsync(
        Guid pickupId, IReadOnlyList<DungeonInstanceMember> members, byte pickerSlot, uint total,
        CancellationToken cancellationToken = default, byte routingSource = 1)
    {
        var recipients = members.OrderBy(member => member.PartySlot).ToArray();
        if (pickupId == Guid.Empty || recipients.Length is < 1 or > 4 || pickerSlot >= 4
            || recipients.Any(member => member.PartySlot >= 4)
            || recipients.Select(member => member.PartySlot).Distinct().Count() != recipients.Length
            || recipients.Select(member => member.CharacterId).Distinct().Count() != recipients.Length)
            return new(DungeonRewardLedgerStatus.Rejected, new uint[4], "invalid-distribution");
        var requested = DungeonGoldSharing.Allocate(total, pickerSlot,
            recipients.Select(member => member.PartySlot).ToArray());
        var ids = recipients.Select(member => member.CharacterId).ToArray();
        var slots = recipients.Select(member => member.PartySlot).ToArray();
        var incarnations = recipients.Select(member => member.MentorGoldIncarnation).ToArray();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined)
                return new(DungeonRewardLedgerStatus.Unknown, new uint[4], "store-quarantined");
            var prior = _dungeonGoldLedger.FirstOrDefault(entry => entry.PickupId == pickupId);
            if (prior is not null)
                return prior.CharacterIds.SequenceEqual(ids) && prior.PartySlots.SequenceEqual(slots)
                        && prior.RequestedAmounts.SequenceEqual(requested)
                        && prior.RoutingSource == routingSource
                        && (prior.MentorGoldIncarnations ?? new Guid[recipients.Length]).SequenceEqual(incarnations)
                    ? new(DungeonRewardLedgerStatus.Committed, prior.GrantedAmounts.ToArray())
                    : new(DungeonRewardLedgerStatus.Rejected, new uint[4], "pickup-payload-mismatch");

            var accounts = JsonSerializer.Deserialize<List<AccountRecord>>(JsonSerializer.Serialize(_accounts))!;
            var granted = new uint[4];
            foreach (var member in recipients)
            {
                var account = accounts.FirstOrDefault(account => account.Characters.Any(c => c.Id == member.CharacterId));
                var index = account?.Characters.FindIndex(c => c.Id == member.CharacterId) ?? -1;
                if (account is null || index < 0)
                    return new(DungeonRewardLedgerStatus.Rejected, new uint[4], "character-not-found");
                var character = account.Characters[index];
                var capacity = Math.Max(0L, int.MaxValue - (long)character.Gold - PendingDungeonGold(character) - PendingStoreGold(character) - PendingMentorGold(character));
                var amount = (uint)Math.Min(requested[member.PartySlot], capacity);
                granted[member.PartySlot] = amount;
                if (amount == 0) continue;
                account.Characters[index] = character with
                {
                    PendingDungeonGoldCredits = [.. character.PendingDungeonGoldCredits ?? [], new(pickupId, amount)]
                };
            }
            var receipt = new DungeonGoldLedgerEntry(pickupId, ids, slots, requested, granted, routingSource, incarnations);
            var ledger = new List<DungeonGoldLedgerEntry>(_dungeonGoldLedger) { receipt };
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken, ledger);
            }
            catch (Exception exception)
            {
                _rewardQuarantined = true;
                return new(DungeonRewardLedgerStatus.Unknown, new uint[4], exception.Message);
            }
            _accounts = accounts;
            _dungeonGoldLedger = ledger;
            if (routingSource != 0)
                foreach (var member in recipients)
                    AccrueMentorGoldLocked(member.CharacterId, member.MentorGoldIncarnation, requested[member.PartySlot]);
            return new(DungeonRewardLedgerStatus.Committed, granted.ToArray());
        }
        finally { _gate.Release(); }
    }

    internal async Task<CharacterRecord?> ConsumeDungeonGoldCreditsAsync(
        Guid characterId, CancellationToken cancellationToken = default, Guid? pickupId = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (_pendingTradeDeliveries.ContainsKey(characterId)) return null;
            var account = _accounts.FirstOrDefault(a => a.Characters.Any(c => c.Id == characterId));
            var index = account?.Characters.FindIndex(c => c.Id == characterId) ?? -1;
            if (account is null || index < 0) return null;
            var character = account.Characters[index];
            if (character.PendingDungeonGoldCredits is not { Count: > 0 }) return null;
            var consumed = character.PendingDungeonGoldCredits
                .Where(credit => pickupId is null || credit.PickupId == pickupId).ToArray();
            if (consumed.Length == 0) return null;
            var remaining = character.PendingDungeonGoldCredits.Except(consumed).ToList();
            var updated = character with
            {
                Gold = checked((int)(character.Gold + consumed.Sum(credit => (long)credit.Amount))),
                PendingDungeonGoldCredits = remaining.Count == 0 ? null : remaining
            };
            var accounts = JsonSerializer.Deserialize<List<AccountRecord>>(JsonSerializer.Serialize(_accounts))!;
            accounts.First(a => a.Id == account.Id).Characters[index] = updated;
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken);
            }
            catch
            {
                _rewardQuarantined = true;
                throw;
            }
            _accounts = accounts;
            return updated;
        }
        finally { _gate.Release(); }
    }

}
