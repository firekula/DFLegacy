using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record PartyClearRewardMember(
    Guid CharacterId, byte Slot, bool HasBlackDiamond, DungeonClearRewardRoll Roll);

/// <summary>
/// NOTI35 contains reward vectors for all party slots, even though EXP and the
/// initial free-card preview belong to the receiver. Generate each member's
/// independent roll once per settlement; their own session still commits it.
/// </summary>
internal sealed record PartyClearRewardSnapshot(IReadOnlyList<PartyClearRewardMember> Members)
{
    internal GameServerPacket CreateReward(ushort userId, Guid characterId,
        GameDungeonClearExperienceBreakdown? experience = null)
    {
        var own = Members.Single(member => member.CharacterId == characterId);
        var premium = Enumerable.Range(0, 4).Select(_ => new GamePremiumCardReward(false, null)).ToArray();
        foreach (var member in Members)
            premium[member.Slot] = new(member.HasBlackDiamond, member.Roll.PremiumItemId);
        return GameProtocolEngine.CreateClearDungeonReward(userId, own.Roll.GoldCardCost,
            own.Roll.FreeGoldAmount, own.Roll.FreeItemId, own.Roll.GoldCardEnabled,
            own.Roll.GoldItemId, own.HasBlackDiamond, own.Roll.PremiumItemId,
            own.Slot, experience, premium);
    }

    internal static async Task<PartyClearRewardSnapshot?> PrepareAsync(
        DungeonInstance instance, JsonGameStore store, ClearRewardGenerator generator,
        byte? dungeonBasisLevel, byte difficulty, bool goldCardEnabled, CancellationToken cancellationToken)
    {
        var generation = instance.EnsurePartyCardSettlement();
        var members = instance.Members;
        foreach (var member in members)
        {
            var character = await store.GetCharacterAsync(member.AccountName, member.CharacterId, cancellationToken);
            if (character is null) continue;
            var premium = await store.GetPremiumServiceAsync(member.AccountName, cancellationToken);
            instance.GetOrCreatePartyClearReward(member, generation, premium.Active, () => generator.Generate(
                dungeonBasisLevel ?? checked((byte)Math.Clamp(character.Level, 1, byte.MaxValue)),
                difficulty, premium.Active, goldCardEnabled,
                partyMemberCount: members.Length,
                normalMonsterKills: instance.NormalMonsterKills,
                championMonsterKills: instance.ChampionMonsterKills,
                bossMonsterKills: instance.BossMonsterKills));
        }
        return instance.SnapshotPartyClearRewards(generation);
    }

    internal static async Task<GameServerPacket?> CreateRightsAsync(
        DungeonInstance instance, JsonGameStore store, CancellationToken cancellationToken)
    {
        var generation = instance.EnsurePartyCardSettlement();
        var states = new List<(DungeonInstanceMember Member, ushort Right)>();
        foreach (var member in instance.Members)
        {
            if (member.PartySlot >= 4) continue;
            var fatigue = await store.GetCharacterFatigueAsync(member.AccountName, member.CharacterId, cancellationToken);
            var premium = await store.GetPremiumServiceAsync(member.AccountName, cancellationToken);
            if (fatigue is { } state)
                states.Add((member, (ushort)(state.UsedFatigue < store.GetFatigueLimits(premium.Active, state.DayKey).Maximum ? 1 : 0)));
        }
        lock (instance.Gate)
        {
            if (instance.SnapshotPartyClearRewards(generation) is null) return null;
            var rights = Enumerable.Repeat(ushort.MaxValue, 4).ToArray();
            foreach (var (member, right) in states)
                if (ReferenceEquals(instance.FindMember(member.CharacterId), member)) rights[member.PartySlot] = right;
            return GameProtocolEngine.CreateCardSelectRightState(rights);
        }
    }
}
