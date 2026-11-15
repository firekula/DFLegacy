namespace DFLegacy.Server;

internal sealed partial class DungeonInstance
{
    // Official party+900: a corpse retains eligibility until the room changes.
    // Revival reservations do not restore this flag before persistence commits.
    private readonly HashSet<Guid> _goldIneligibleMembers = [];

    internal DungeonInstanceMember[] CaptureGoldEligibleMembers()
    {
        lock (Gate)
            return _members.Where(member => !_goldIneligibleMembers.Contains(member.CharacterId))
                .OrderBy(member => member.PartySlot).ToArray();
    }

    internal bool TryClaimGoldPickup(Guid picker, DungeonGroundItem item, Guid pickupId,
        out DungeonInstanceMember[] recipients)
    {
        lock (Gate)
        {
            recipients = CaptureGoldEligibleMembers();
            return pickupId != Guid.Empty && recipients.Length != 0 && _members.Any(member => member.CharacterId == picker)
                && TryClaimGroundItem(item.GroundId, item.RoomX, item.RoomY, out _, item, pickupId);
        }
    }
}
