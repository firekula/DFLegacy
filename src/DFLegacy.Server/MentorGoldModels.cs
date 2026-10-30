namespace DFLegacy.Server;

public enum MentorGoldRetirementReason { NormalLeave, ForcedReplacement }

// Only an irrevocably awarded amount is durable. Basis here is audit data, never a resumable pool.
public sealed record MentorGoldAwardReceipt(Guid FlushId, string Fingerprint, Guid ChildId,
    Guid ChildIncarnation, Guid MasterIncarnation, Guid EdgeGeneration, string SourceName,
    uint Basis, byte RelationshipLevel, int ConnectedChildren, int Capacity,
    uint RequestedGold, uint CreditedGold, bool Applied);

internal sealed record MentorGoldFlushResult(DungeonRewardLedgerStatus Status, Guid MasterId = default,
    Guid MasterIncarnation = default, bool NewlyCommitted = false, MentorGoldAwardReceipt? Receipt = null);
internal sealed record MentorGoldInstallResult(DungeonRewardLedgerStatus Status,
    CharacterRecord? Character = null, IReadOnlyList<MentorGoldAwardReceipt>? Awards = null);
