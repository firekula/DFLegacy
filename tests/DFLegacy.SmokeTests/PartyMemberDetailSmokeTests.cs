using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class PartyMemberDetailSmokeTests
{
    private static readonly GameCharacterCombatStats Stats = new(
        MaximumHp: 14_400, MaximumMp: 19_600,
        PhysicalAttack: 110, PhysicalDefense: 220, MagicalAttack: 330, MagicalDefense: 440,
        FireResistance: 1, WaterResistance: 2, DarkResistance: 3, LightResistance: 4,
        InventoryLimit: 410_000, HpRegeneration: 5, MpRegeneration: 6,
        MovementSpeed: 8_500, AttackSpeed: 8_600, CastSpeed: 7_000,
        HitRecovery: 6_000, JumpPower: 4_300, Weight: 500_000);

    public static async Task RunAsync(Action<bool, string> check)
    {
        await PartyMemberSkillDetailSmokeTests.RunAsync(check);
        await EntranceWeaknessRecoverySmokeTests.RunAsync(check);
        await TownAppearanceDetailSmokeTests.RunAsync(check);
        await PartyRosterDetailSmokeTests.RunAsync(check);
        await using var harness = await PartyHarness.CreateAsync();
        var ownerId = harness.IdOf("阿尔法");
        var uid = harness.UidOf("阿尔法");
        var character = new CharacterRecord(ownerId, "阿尔法", 3, 1, 45, 10, 0,
            Experience: 12_345, WeaknessRecovery: 37);
        GameInventoryEntry[] worn = [new(9, 1234, 77, Durability: 20)];

        await harness.Service.HandleRequestAsync(ownerId, harness.UidOf("布拉沃"),
            PartyHarness.DefaultInfo, CancellationToken.None);
        await harness.Service.HandleResponseAsync(harness.IdOf("布拉沃"), uid,
            accepted: true, CancellationToken.None);
        harness.ClearAll();

        // Exercise the projection used by EntranceService and the actual
        // cross-session party delivery, starting from persisted character state.
        var foreign = PartyMemberDetailProjection.CreatePacket(uid, character, Stats, worn,
            hasBlackDiamond: false);
        var sent = await harness.Service.SendToOtherMembersAsync(ownerId, foreign, CancellationToken.None);
        var received = harness.Of("布拉沃").SingleOrDefault();
        check(sent == 1 && received is not null && harness.Of("阿尔法").Count == 0
                && harness.Of("查理").Count == 0,
            "a party member's combat detail is delivered once to its teammate, not to self or an unrelated town user");
        if (received is null) return;

        var self = GameProtocolEngine.CreateCurrentCharacterDetails(uid,
            experience: character.Experience, learnedSkills: [new(25, 10)],
            combatStats: Stats, equippedItems: worn,
            staminaRecoveryPercentage: DungeonWeaknessPolicy.ProjectStaminaRecovery(character, false));
        check(StatBlock(received).SequenceEqual(StatBlock(self))
                && StatBlock(received)[80] == 37,
            "a weakened character's remote party actor receives the same 81-byte HP/MP/stamina block as its owner, not default recovery 100");
        check(received.Type == GameProtocolEngine.NotificationPacketType
                && received.ProtocolId == GameProtocolEngine.UserInfoNotification
                && received.Payload[0] == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(received.Payload.AsSpan(3)) == uid
                && BinaryPrimitives.ReadUInt32LittleEndian(received.Payload.AsSpan(5)) == 0
                && received.Payload.Length == 107,
            "party detail keeps the legacy model-only mode 1, owner UID, zero EXP, equipment and no profile-card tail");

        var wire = received.Encode();
        GamePayloadCipher.DecryptServerPayload(wire.AsSpan(GameServerPacket.HeaderLength));
        check(wire.AsSpan(GameServerPacket.HeaderLength).SequenceEqual(received.Payload),
            "the delivered recovery value survives the actual encrypted server packet layout");

        foreach (var (recovery, blackDiamond, expected) in new (byte, bool, byte)[]
                 { (0, false, 10), (10, false, 10), (28, false, 28), (82, false, 82),
                   (100, false, 100), (255, false, 100), (28, true, 100) })
        {
            var snapshot = character with { WeaknessRecovery = recovery };
            var packet = PartyMemberDetailProjection.CreatePacket(uid, snapshot, Stats, worn, blackDiamond);
            check(StatBlock(packet)[80] == expected,
                $"remote party detail projects persisted recovery {recovery}, black diamond={blackDiamond}, to {expected}");
        }
        check(DungeonWeaknessPolicy.ProjectStaminaRecovery(null, false) == 100,
            "a session without an active character retains full-stamina projection");

        // A later entry rebuild reads the updated record rather than retaining
        // an old weak snapshot or mutating the packet already delivered.
        var recovered = PartyMemberDetailProjection.CreatePacket(uid,
            character with { WeaknessRecovery = 100 }, Stats, worn, false);
        harness.ClearAll();
        await harness.Service.SendToOtherMembersAsync(ownerId, recovered, CancellationToken.None);
        check(StatBlock(harness.Of("布拉沃").Single())[80] == 100 && StatBlock(received)[80] == 37,
            "a later party actor rebuild uses current recovered state while the prior delivery remains an immutable snapshot");
    }

    private static ReadOnlySpan<byte> StatBlock(GameServerPacket packet)
    {
        var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(9)));
        return packet.Payload.AsSpan(13, size);
    }
}
