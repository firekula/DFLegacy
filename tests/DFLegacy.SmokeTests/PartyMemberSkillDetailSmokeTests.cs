using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class PartyMemberSkillDetailSmokeTests
{
    private static readonly GameCharacterCombatStats Stats = new(
        19_050, 19_850, 1315, 1315, 1855, 1855, 0, 0, 0, 0,
        407_500, 0, 2840, 8_000, 10_000, 10_000, 5510, 3500, 400_000);

    public static async Task RunAsync(Action<bool, string> check)
    {
        var character = new CharacterRecord(Guid.NewGuid(), "PeerMage", 3, 1, 52, 10, 0,
            Experience: 100_127_744, WeaknessRecovery: 37);
        // Include a skill outside quick slots and a high byte id: neither may
        // be omitted or widened to a u16 while building the peer snapshot.
        CharacterSkillRecord[] stored = [new(204, 181, 3), new(48, 20, 10), new(0, 11, 20), new(49, 25, 0)];
        var skills = PartyMemberDetailProjection.ProjectLearnedSkills(stored);
        check(skills.Select(skill => (skill.SkillId, skill.Level, skill.Slot))
                .SequenceEqual(new (byte, byte, byte?)[] { (11, 20, 0), (20, 10, 48), (181, 3, 204) }),
            "the shared owner/profile/peer projection retains learned non-quickslot and guild skills, filters zero levels and orders by slot");
        GameInventoryEntry[] worn = [new(9, 1234, 77, Durability: 20), new(10, 4321, 88, State: 1)];
        var foreign = PartyMemberDetailProjection.CreatePacket(2, character, Stats, worn, false, skills);
        var owner = GameProtocolEngine.CreateCurrentCharacterDetails(2, character.Experience, skills,
            Stats, worn, equippedCreatureLevel: 6, staminaRecoveryPercentage: 37);
        var peer = ReadWire(foreign);
        var self = ReadWire(owner);
        check(peer.Mode == 1 && peer.Uid == 2 && peer.Experience == 0
                && self.Experience == character.Experience && peer.ProfileTail.Length == 0,
            "peer skill synchronization retains mode1 EXP0 and never appends the mode3 profile tail");
        check(peer.Stats.SequenceEqual(self.Stats) && peer.Stats.Length == 81
                && peer.Equipment.SequenceEqual(self.Equipment),
            "adding peer skills preserves the owner's exact 81-byte stats and complete worn equipment");
        check(peer.Skills.Select(skill => (skill.SkillId, skill.Level))
                .SequenceEqual(self.Skills.Select(skill => (skill.SkillId, skill.Level)))
                && peer.Skills.Any(skill => skill is { SkillId: 20, Level: 10 })
                && peer.CreatureLevel == 0 && self.CreatureLevel == 6,
            "the actual encrypted foreign wire carries skill20 level10 and all owner skill levels as u8/u8 without consuming the creature byte");
        check(foreign.Payload.AsSpan(foreign.Payload.Length - 8)
                .SequenceEqual(new byte[] { 3, 11, 20, 20, 10, 181, 3, 0 }),
            "the peer tail uses one count byte, three two-byte skill pairs and the legacy zero creature level");

        var recovered = PartyMemberDetailProjection.WithStaminaRecovery(foreign, 55);
        check(ReadWire(recovered).Skills.Select(s => (s.SkillId, s.Level))
                .SequenceEqual(peer.Skills.Select(s => (s.SkillId, s.Level)))
                && recovered.Payload[93] == 55 && foreign.Payload[93] == 37
                && recovered.Payload.AsSpan(94).SequenceEqual(foreign.Payload.AsSpan(94)),
            "cached recovery cloning changes only stamina and preserves all equipment/skills and the old immutable packet");

        var profile = ReadWire(GameProtocolEngine.CreateRequestedUserInfoDetail(2,
            equippedItems: worn, combatStats: Stats, equippedCreatureLevel: 6,
            staminaRecoveryPercentage: 37, experience: character.Experience,
            mode: 3, learnedSkills: skills));
        check(profile.Mode == 3 && profile.Experience == character.Experience
                && profile.Skills.Any(s => s is { SkillId: 20, Level: 10 })
                && profile.CreatureLevel == 6 && profile.ProfileTail.Length == 28
                && profile.ProfileTail.All(value => value == 0),
            "a profile detail carries real EXP and learned skills without shifting its creature byte or 28-byte tail");

        foreach (var count in new[] { 0, 1, 255 })
        {
            var sample = Enumerable.Range(0, count)
                .Select(i => new GameSkillEntry((byte)i, (byte)(i % 10 + 1))).ToArray();
            var packet = GameProtocolEngine.CreateRequestedUserInfoDetail(2, mode: 1, learnedSkills: sample);
            var parsed = ReadWire(packet);
            check(parsed.Skills.Length == count && packet.Payload.Length == 97 + 2 * count
                    && parsed.ProfileTail.Length == 0 && parsed.CreatureLevel == 0,
                $"peer detail count {count} has an exact u8 count and two-byte skills with no trailing or missing data");
        }
        var rejected = false;
        try
        {
            GameProtocolEngine.CreateRequestedUserInfoDetail(2, mode: 1,
                learnedSkills: Enumerable.Repeat(new GameSkillEntry(20, 10), 256).ToArray());
        }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        check(rejected, "peer skill count256 is rejected before its count can wrap to zero");
        check(ReadWire(GameProtocolEngine.CreateRequestedUserInfoDetail(2)).Skills.Length == 0,
            "the unknown-character profile fallback keeps its existing empty skill list");

        await using var harness = await PartyHarness.CreateAsync();
        var id = harness.IdOf("布拉沃");
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        harness.ClearAll();
        await harness.Service.SendToOtherMembersAsync(id, foreign, CancellationToken.None);
        check(ReadWire(harness.Of("阿尔法").Single()).Skills.Any(s => s is { SkillId: 20, Level: 10 })
                && harness.UidOf("布拉沃") == peer.Uid
                && harness.Of("布拉沃").Count == 0 && harness.Of("查理").Count == 0,
            "real party delivery transmits the skill20 snapshot to its peer without sending it to self or unrelated users");
    }

    // Decode the real encrypted frame and walk the native layout with a reader;
    // do not locate skills using the builder's computed suffix offset.
    private static Detail ReadWire(GameServerPacket packet)
    {
        var frame = packet.Encode();
        if (frame[0] != 0 || frame[1] != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(2)) != frame.Length)
            throw new InvalidDataException("Expected complete USERINFO frame.");
        var payload = frame[6..];
        GamePayloadCipher.DecryptServerPayload(payload);
        using var reader = new BinaryReader(new MemoryStream(payload));
        var mode = reader.ReadByte();
        if (reader.ReadUInt16() != 1) throw new InvalidDataException("Expected one actor.");
        var uid = reader.ReadUInt16();
        var experience = reader.ReadUInt32();
        var stats = reader.ReadBytes(checked((int)reader.ReadUInt32()));
        var equipment = new List<GameInventoryEntry>();
        var count = reader.ReadByte();
        for (var i = 0; i < count; i++)
            equipment.Add(new(reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt32(),
                reader.ReadByte(), reader.ReadUInt16()));
        var skills = new GameSkillEntry[reader.ReadByte()];
        for (var i = 0; i < skills.Length; i++)
            skills[i] = new(reader.ReadByte(), reader.ReadByte());
        var creature = reader.ReadByte();
        var tail = reader.ReadBytes(checked((int)(reader.BaseStream.Length - reader.BaseStream.Position)));
        if (stats.Length != 81 || tail.Length != (mode == 3 ? 28 : 0))
            throw new InvalidDataException("Misaligned actor detail.");
        return new(mode, uid, experience, stats, equipment.ToArray(), skills, creature, tail);
    }

    private sealed record Detail(byte Mode, ushort Uid, uint Experience, byte[] Stats,
        GameInventoryEntry[] Equipment, GameSkillEntry[] Skills, byte CreatureLevel, byte[] ProfileTail);
}
