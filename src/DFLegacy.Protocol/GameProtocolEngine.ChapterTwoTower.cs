namespace DFLegacy.Protocol;

public readonly record struct GameChapterTwoTowerRewardItem(ushort ItemId, uint QuantityOrValue);
public sealed record GameChapterTwoTowerRankMember(byte[] Name, byte Job, byte GrowType);
public sealed record GameChapterTwoTowerRankRecord(IReadOnlyList<GameChapterTwoTowerRankMember> Members,
    ushort ClearedFloor, uint TotalMilliseconds, uint Rank = uint.MaxValue);

public static partial class GameProtocolEngine
{
    public const byte ChapterTwoTowerInfoNotification = 154;
    public const byte ChapterTwoTowerFloorNotification = 155;
    public const byte ChapterTwoTowerResultNotification = 156;
    public const byte ChapterTwoTowerRewardNotification = 157;
    public const byte ChapterTwoTowerSettlementOptionsNotification = 158;
    public const byte ChapterTwoTowerStageCommand = 170;
    private const int ChapterTwoTowerPartySlots = 4;
    private const int ChapterTwoTowerLeaderboardEntries = 5;
    private const int ChapterTwoTowerRewardSlotsPerMember = 10;

    public static GameServerPacket CreateChapterTwoTowerInfo(uint dungeonId, ushort lastFloor)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(dungeonId);
        writer.Write(lastFloor);
        return new GameServerPacket(NotificationPacketType, ChapterTwoTowerInfoNotification, payload.ToArray());
    }

    public static GameServerPacket CreateChapterTwoTowerFloor(
        ushort floor,
        uint randomSeed,
        ushort mapId,
        IReadOnlyList<GameDungeonMonster> monsters,
        IReadOnlyList<GamePassiveObjectDrop>? groundItems = null)
    {
        ArgumentNullException.ThrowIfNull(monsters);
        groundItems ??= [];
        if (monsters.Count > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(monsters));
        if (groundItems.Count > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(groundItems));

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(floor);
        writer.Write(randomSeed);
        writer.Write(mapId);
        writer.Write((byte)monsters.Count);
        foreach (var monster in monsters)
        {
            writer.Write(monster.MapListIndex);
            writer.Write(monster.UniqueId);
            writer.Write(monster.MonsterIndex);
            // In the native spawn record, the second byte after the actor ID
            // selects the actor kind (47A58E). The first supplies the level.
            writer.Write(monster.Level);
            writer.Write(monster.Type);
            writer.Write(monster.IsBoxMonster);
            writer.Write(monster.BoxIndex);
        }

        writer.Write((byte)groundItems.Count);
        foreach (var item in groundItems)
        {
            writer.Write(item.AssociationSlot);
            writer.Write(item.GroundId);
            writer.Write(item.ItemId);
            writer.Write(item.AddInfo);
            writer.Write(item.OwnerUserId);
        }

        return new GameServerPacket(NotificationPacketType, ChapterTwoTowerFloorNotification, payload.ToArray());
    }

    public static GameServerPacket CreateChapterTwoTowerResult(
        uint clearedFloor,
        uint totalMilliseconds,
        bool newPersonalRecord,
        uint virtualRank = uint.MaxValue,
        GameChapterTwoTowerRankRecord? personalBest = null,
        IReadOnlyList<GameChapterTwoTowerRankRecord>? topRecords = null)
    {
        topRecords ??= [];
        if (topRecords.Count > ChapterTwoTowerLeaderboardEntries)
            throw new ArgumentOutOfRangeException(nameof(topRecords));

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // Ver_OB_216 renders the first field as milliseconds and the second
        // as the cleared floor (9725F2 and 97249F).
        writer.Write(totalMilliseconds);
        writer.Write(clearedFloor);
        writer.Write(newPersonalRecord);
        writer.Write(virtualRank);
        writer.Write(personalBest is not null);
        if (personalBest is not null)
            WriteRankRecord(personalBest);
        for (var rank = 0; rank < ChapterTwoTowerLeaderboardEntries; rank++)
            WriteRankRecord(rank < topRecords.Count ? topRecords[rank] : null);
        return new GameServerPacket(NotificationPacketType, ChapterTwoTowerResultNotification, payload.ToArray());

        void WriteRankRecord(GameChapterTwoTowerRankRecord? record)
        {
            var members = record?.Members ?? [];
            if (members.Count > ChapterTwoTowerPartySlots)
                throw new ArgumentOutOfRangeException(nameof(record));
            for (var slot = 0; slot < ChapterTwoTowerPartySlots; slot++)
            {
                var member = slot < members.Count ? members[slot] : null;
                writer.Write((uint)(member?.Name.Length ?? 0));
                if (member is not null)
                    writer.Write(member.Name);
                writer.Write(member?.Job ?? (byte)0);
                writer.Write(member?.GrowType ?? (byte)0);
            }
            writer.Write(record?.ClearedFloor ?? (ushort)0);
            writer.Write(record?.TotalMilliseconds ?? 0u);
            writer.Write(record?.Rank ?? 0u);
        }
    }

    public static GameServerPacket CreateChapterTwoTowerReward(
        uint experience,
        IReadOnlyList<GameChapterTwoTowerRewardItem> rewards,
        byte partySlot = 0)
    {
        ArgumentNullException.ThrowIfNull(rewards);
        if (partySlot >= ChapterTwoTowerPartySlots) throw new ArgumentOutOfRangeException(nameof(partySlot));
        if (rewards.Count > ChapterTwoTowerRewardSlotsPerMember) throw new ArgumentOutOfRangeException(nameof(rewards));

        var slots = new IReadOnlyList<GameChapterTwoTowerRewardItem>[ChapterTwoTowerPartySlots];
        slots[partySlot] = rewards;
        return CreateChapterTwoTowerPartyReward(experience, slots);
    }

    public static GameServerPacket CreateChapterTwoTowerPartyReward(
        uint experience, IReadOnlyList<IReadOnlyList<GameChapterTwoTowerRewardItem>?> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count != ChapterTwoTowerPartySlots) throw new ArgumentOutOfRangeException(nameof(slots));
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(experience);
        for (var slot = 0; slot < ChapterTwoTowerPartySlots; slot++)
        {
            var rewards = slots[slot] ?? [];
            if (rewards.Count > ChapterTwoTowerRewardSlotsPerMember) throw new ArgumentOutOfRangeException(nameof(slots));
            writer.Write((byte)rewards.Count);
            foreach (var reward in rewards)
            {
                writer.Write(reward.ItemId);
                writer.Write(reward.QuantityOrValue);
            }
        }
        return new GameServerPacket(NotificationPacketType, ChapterTwoTowerRewardNotification, payload.ToArray());
    }

    public static GameServerPacket CreateChapterTwoTowerSettlementOptions(bool canChallengeAgain) =>
        new(NotificationPacketType, ChapterTwoTowerSettlementOptionsNotification, [canChallengeAgain ? (byte)1 : (byte)0]);
}
