namespace DFLegacy.Server;

internal sealed partial class DungeonInstance
{
    private readonly Dictionary<Guid, PartyClearRewardMember> _partyClearRewards = [];

    internal PartyClearRewardMember? GetOrCreatePartyClearReward(
        DungeonInstanceMember member, long generation, bool hasBlackDiamond, Func<DungeonClearRewardRoll> generate)
    {
        lock (Gate)
        {
            if (!_partyCardSettlementOpen || generation != _partyCards.Generation
                || member.PartySlot >= 4 || !ReferenceEquals(FindMember(member.CharacterId), member)) return null;
            if (_partyClearRewards.TryGetValue(member.CharacterId, out var existing)) return existing;
            var reward = new PartyClearRewardMember(member.CharacterId, member.PartySlot, hasBlackDiamond, generate());
            _partyClearRewards.Add(member.CharacterId, reward);
            return reward;
        }
    }

    internal PartyClearRewardSnapshot? SnapshotPartyClearRewards(long generation)
    {
        lock (Gate)
        {
            if (!_partyCardSettlementOpen || generation != _partyCards.Generation) return null;
            return new(_members.Where(member => _partyClearRewards.ContainsKey(member.CharacterId))
                .Select(member => _partyClearRewards[member.CharacterId]).ToArray());
        }
    }
}
