using System.Buffers.Binary;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// The foreign party actor must start with the same combat/stamina projection
/// as its owner's actor. NOTI 4 only updates the receiving client's local actor,
/// so omitting stamina here leaves a weakened teammate at full HP remotely.
/// </summary>
internal static class PartyMemberDetailProjection
{
    internal static GameSkillEntry[] ProjectLearnedSkills(IEnumerable<CharacterSkillRecord> skills) =>
        skills.Where(skill => skill.Level > 0)
            .OrderBy(skill => skill.Slot)
            .Select(skill => new GameSkillEntry(skill.SkillId, skill.Level, skill.Slot))
            .ToArray();

    internal static GameServerPacket CreatePacket(
        ushort areaUserId,
        CharacterRecord character,
        GameCharacterCombatStats combatStats,
        IReadOnlyCollection<GameInventoryEntry> wornEquipment,
        bool hasBlackDiamond,
        IReadOnlyList<GameSkillEntry>? learnedSkills = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(combatStats);
        ArgumentNullException.ThrowIfNull(wornEquipment);

        return GameProtocolEngine.CreateRequestedUserInfoDetail(
            areaUserId,
            equippedItems: wornEquipment,
            combatStats: combatStats,
            equippedCreatureLevel: 0,
            staminaRecoveryPercentage: DungeonWeaknessPolicy.ProjectStaminaRecovery(
                character, hasBlackDiamond),
            // This legacy foreign-model path must retain mode 1 / EXP 0.
            // Town Creature appearance is introduced by its preceding mode 0.
            experience: 0,
            mode: 1,
            learnedSkills: learnedSkills);
    }

    internal static GameServerPacket WithStaminaRecovery(GameServerPacket packet, byte recovery)
    {
        ArgumentNullException.ThrowIfNull(packet);
        const int statOffset = 13;
        const int statLength = 81;
        if (packet.Type != GameProtocolEngine.NotificationPacketType
            || packet.ProtocolId != GameProtocolEngine.UserInfoNotification
            || packet.Payload.Length < statOffset + statLength
            || packet.Payload[0] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(1)) != 1
            || BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(9)) != statLength)
        {
            throw new ArgumentException("A single mode-1 combat detail is required.", nameof(packet));
        }

        // The recovery callback runs outside the equipment-owning session.
        // Preserve its last immutable stats/equipment projection and replace
        // only the byte that NOTI 4 has just changed on the owner's actor.
        var payload = (byte[])packet.Payload.Clone();
        payload[statOffset + statLength - 1] = recovery;
        return packet with { Payload = payload };
    }
}
