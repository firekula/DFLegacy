using System.Text.Json.Serialization;

namespace DFLegacy.Server;

public enum DungeonRewardLedgerStatus : byte
{
    Committed = 0,
    Rejected = 1,
    Unknown = 2
}

public sealed record DungeonRewardLedgerEntry(
    string IdempotencyKey,
    string PayloadFingerprint,
    Guid CharacterId,
    long DungeonGeneration,
    byte PartySlot,
    byte CardType,
    byte CardColumn,
    DungeonRewardLedgerStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ushort? RewardItemId,
    int GoldDelta,
    string? RejectionCode,
    string? UnknownReason);

public sealed record DungeonRewardCommitRequest(
    string IdempotencyKey,
    string PayloadFingerprint,
    string UserName,
    Guid CharacterId,
    long DungeonGeneration,
    byte PartySlot,
    byte CardType,
    byte CardColumn,
    int TargetGold,
    int ExpectedGold,
    IReadOnlyList<CharacterItemRecord> ExpectedInventory,
    IReadOnlyList<CharacterItemRecord> ExpectedEquipment,
    IReadOnlyList<CharacterItemRecord> PlannedInventory,
    IReadOnlyList<CharacterItemRecord> PlannedEquipment,
    IReadOnlyList<CreateCharacterMailRequest> GeneratedMails,
    ushort? WarehouseCapacity,
    ushort? ExpectedWarehouseCapacity,
    uint RequestedGoldReward = 0,
    Guid MentorGoldIncarnation = default);

public readonly record struct DungeonRewardCommitResult(
    DungeonRewardLedgerStatus Status,
    CharacterRecord? UpdatedCharacter,
    ushort? RewardItemId,
    int GoldDelta,
    string? RejectionCode,
    string? UnknownReason);
