namespace DFLegacy.Server;

internal static class DungeonGoldSharing
{
    internal static uint[] Allocate(uint total, byte pickerSlot, IReadOnlyList<byte> eligibleSlots)
    {
        ArgumentNullException.ThrowIfNull(eligibleSlots);
        if (pickerSlot >= 4 || eligibleSlots.Count == 0 || eligibleSlots.Count > 4
            || eligibleSlots.Any(slot => slot >= 4) || eligibleSlots.Distinct().Count() != eligibleSlots.Count)
            throw new ArgumentException("Gold allocation requires one to four distinct party slots.");
        var result = new uint[4];
        foreach (var slot in eligibleSlots) result[slot] = total / (uint)eligibleSlots.Count;
        if (eligibleSlots.Contains(pickerSlot)) result[pickerSlot] += total % (uint)eligibleSlots.Count;
        return result;
    }
}

public sealed record DungeonGoldCredit(Guid PickupId, uint Amount);

internal sealed record DungeonGoldLedgerEntry(
    Guid PickupId, Guid[] CharacterIds, byte[] PartySlots, uint[] RequestedAmounts, uint[] GrantedAmounts,
    byte RoutingSource = 1, Guid[]? MentorGoldIncarnations = null);

internal sealed record DungeonGoldCommitResult(
    DungeonRewardLedgerStatus Status, uint[] Amounts, string? Reason = null);
