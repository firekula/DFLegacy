using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record CharacterItemRecord(
    ushort Slot,
    ushort ItemId,
    uint CountOrValue,
    // Ordinary equipment stores reinforcement in the low five bits and the
    // number of completed reseals in the high three bits.
    byte State = 0,
    ushort Durability = 0,
    // [sealing] equipment starts at 1 and is persisted as 0 after first equip.
    // DF2008 then treats it like an item whose [attach type] is [trade].
    byte SealState = 0,
    // Compatibility key only: DF2008 consumes this as a remaining-seconds
    // value. Zero means unlimited; it is never a Unix expiration timestamp.
    [property: JsonPropertyName("AvatarExpireAt")]
    uint? AvatarRemainingSeconds = null,
    ushort? AvatarAbilityIndex = null,
    byte? CreatureStomach = null,
    uint? CreatureExperience = null,
    byte? CreatureLevel = null,
    string? CreatureName = null,
    bool? CreatureNoCharge = null,
    byte? CreatureStomachRemainderSeconds = null,
    // DF2008 carries equipment quality as the 32-bit add_info seed. Keeping it
    // separate preserves CountOrValue as the local instance count.
    uint? EquipmentQualitySeed = null,
    // Server-only identity. DF2008's item wire format has no GUID field.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Guid InstanceId = default,
    // Server UTC deadline; the legacy AvatarExpireAt key above remains seconds.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? AvatarExpiresAtUnixSeconds = null);

public sealed record CharacterSkillRecord(
    byte Slot,
    byte SkillId,
    byte Level);

public sealed record CharacterQuestRecord(
    ushort QuestId,
    uint Trigger = 0);

public sealed record CharacterMailAttachmentRecord(
    ushort ItemId,
    uint CountOrValue,
    byte State = 0,
    ushort Durability = 0,
    byte SealState = 0,
    [property: JsonPropertyName("AvatarExpireAt")]
    uint? AvatarRemainingSeconds = null,
    ushort? AvatarAbilityIndex = null,
    uint? EquipmentQualitySeed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    Guid InstanceId = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? AvatarExpiresAtUnixSeconds = null);

public sealed record CharacterMailRecord(
    uint Id,
    string Sender,
    string Text,
    int Gold,
    CharacterMailAttachmentRecord? Attachment,
    uint SentAt,
    uint ExpiresAt,
    bool IsNew = true,
    ushort State = 1);

public sealed record CreateCharacterMailRequest(
    string Sender,
    string Text,
    int Gold = 0,
    CharacterMailAttachmentRecord? Attachment = null);

public sealed record CharacterMailClaimResult(
    CharacterRecord Character,
    CharacterMailRecord Mail);

public enum CharacterMailSendFailure
{
    None,
    InvalidRequest,
    SenderNotFound,
    RecipientNotFound,
    RecipientMailboxFull,
    SenderStateChanged
}

public sealed record CharacterMailSendResult(
    bool Sent,
    CharacterRecord? Sender,
    Guid RecipientCharacterId,
    CharacterMailRecord? Mail,
    CharacterMailSendFailure Failure);

/// <summary>账号级好友条目：客户端按角色名工作，这里只存名字与添加时间。</summary>
public sealed record AccountFriendRecord(string Name, long AddedAtUnix);

/// <summary>
/// 账号级黑名单条目：只存名字与添加当天的日期（客户端黑名单窗口没有 uid/等级/职业）。
/// 与好友条目不同，名字匹配在客户端是逐字节大小写敏感的（CNRDBlackListdManager::FindByName）。
/// </summary>
public sealed record AccountBlacklistRecord(string Name, int Year, int Month, int Day);

/// <summary>一笔交易的落库请求：两侧的托管快照 + 各自报出的金币。</summary>
public sealed record ItemTradeCommitRequest(
    Guid InitiatorCharacterId,
    string InitiatorAccountName,
    Guid PartnerCharacterId,
    string PartnerAccountName,
    IReadOnlyList<ItemTradeEscrow> InitiatorEscrows,
    IReadOnlyList<ItemTradeEscrow> PartnerEscrows,
    int InitiatorOfferedGold,
    int PartnerOfferedGold);

public enum ItemTradeCommitFailure
{
    None,
    CatalogUnavailable,
    CharacterMissing,
    EscrowMissing,
    NoSpace,
    WeightExceeded,
    GoldOverflow,
    // 托管期间金币被别的途径扣掉（如邮件寄钱）：结算失败并按 CMD 26 code 1 中止整笔。
    GoldMissing
}

public sealed record ItemTradeCommitResult(
    bool Committed,
    ItemTradeCommitFailure Failure,
    Guid TradeId,
    CharacterRecord? Initiator,
    CharacterRecord? Partner,
    ItemTradeSettlementPlan? Plan);

public enum FriendUpdateFailure
{
    None,
    AccountNotFound,
    CharacterNotFound,
    AlreadyFriend,
    Self,
    FriendListFull,
    NotFriend
}

public enum BlacklistUpdateFailure
{
    None,
    AccountNotFound,
    CharacterNotFound,
    AlreadyBlacklisted,
    BlacklistFull,
    NotBlacklisted
}

public enum IncreaseStatusItemUseFailure
{
    None,
    InvalidRequest,
    CharacterNotFound,
    ItemStateChanged
}

public sealed record IncreaseStatusItemUseResult(
    bool Success,
    CharacterRecord? Character,
    uint RemainingItemCount,
    IncreaseStatusItemUseFailure Failure);

public enum ExperienceBookItemUseFailure
{
    None,
    InvalidRequest,
    CharacterNotFound,
    ItemStateChanged,
    LevelLimitReached
}

public sealed record ExperienceBookItemUseResult(
    bool Success,
    CharacterRecord? Character,
    uint RemainingItemCount,
    CharacterExperienceGrant? Progression,
    int GrantedSkillPoints,
    ExperienceBookItemUseFailure Failure);

public enum PvpExperienceItemUseFailure
{
    None,
    InvalidRequest,
    CharacterNotFound,
    ItemStateChanged
}

public sealed record PvpExperienceItemUseResult(
    bool Success,
    CharacterRecord? Character,
    uint RemainingItemCount,
    int GrantedExperience,
    int PreviousPoints,
    int Points,
    byte Grade,
    PvpExperienceItemUseFailure Failure);

public enum SkillResetItemUseFailure
{
    None,
    InvalidRequest,
    CharacterNotFound,
    ItemStateChanged,
    SkillPointOverflow
}

public sealed record SkillResetItemUseResult(
    bool Success,
    CharacterRecord? Character,
    uint RemainingItemCount,
    int ResetSkillPoints,
    SkillResetItemUseFailure Failure);

public enum FatigueRecoveryItemUseFailure
{
    None,
    InvalidRequest,
    CharacterNotFound,
    ItemStateChanged,
    FatigueNotLowEnough
}

public sealed record FatigueRecoveryItemUseResult(
    bool Success,
    CharacterRecord? Character,
    uint RemainingItemCount,
    ushort PreviousUsedFatigue,
    ushort UsedFatigue,
    FatigueRecoveryItemUseFailure Failure);

public sealed record DungeonRoomFatigueConsumptionResult(
    CharacterRecord? Character,
    bool Consumed,
    ushort PreviousUsedFatigue,
    ushort UsedFatigue);

public enum WeaknessRecoveryFailure
{
    None,
    CharacterNotFound,
    NotWeakened,
    InsufficientGold,
    Busy
}

public sealed record WeaknessRecoveryResult(
    bool Success,
    CharacterRecord? Character,
    int RecoveryCost,
    WeaknessRecoveryFailure Failure);

public sealed record CharacterWeaknessRecoveryUpdate(
    Guid CharacterId,
    byte Recovery);

public sealed record CharacterRecord(
    Guid Id,
    string Name,
    int Job,
    int GrowType,
    int Level,
    int Sp,
    int Cash,
    int Gold = 50_000,
    List<CharacterItemRecord>? Inventory = null,
    List<CharacterItemRecord>? Equipment = null,
    List<CharacterSkillRecord>? Skills = null,
    // Per-character JSON quest progress: active triggers and completed history.
    // Completing a quest removes it from Quests, not from CompletedQuestIds.
    List<CharacterQuestRecord>? Quests = null,
    List<ushort>? CompletedQuestIds = null,
    uint Experience = 0,
    List<ushort>? RewardedQuestIds = null,
    byte TownId = 1,
    byte AreaId = 1,
    short X = 474,
    short Y = 234,
    byte Direction = 5,
    uint VictoryPoints = 0,
    List<CharacterItemRecord>? Warehouse = null,
    List<CharacterMailRecord>? Mailbox = null,
    List<CharacterItemRecord>? AvatarInventory = null,
    List<CharacterItemRecord>? CreatureInventory = null,
    uint CharacterNo = 0,
    uint AccountUid = 0,
    byte Slot = 0,
    bool IsPreset = false,
    ushort UsedFatigue = 0,
    int BonusSp = 0,
    int BonusMaximumHp = 0,
    int BonusMaximumMp = 0,
    int BonusStrength = 0,
    int BonusVitality = 0,
    int BonusIntelligence = 0,
    int BonusSpirit = 0,
    int BonusMovementSpeed = 0,
    int BonusAllElementResistance = 0,
    int AwakeningType = 0,
    List<ushort>? UnlockedDungeonIds = null,
    List<CharacterDungeonProgressRecord>? DungeonProgress = null,
    ushort WarehouseCapacity = CharacterWarehouseProgression.InitialCapacity,
    bool TutorialStarted = true,
    byte WeaknessRecovery = DungeonWeaknessPolicy.FullStamina,
    [property: JsonPropertyName("IsWeakened")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? LegacyIsWeakened = null,
    // Persisted fields mirror the legacy pvp_result row. PvpGrade is the
    // PVF-derived rank sent in NOTI 48; the counters are updated atomically
    // with the three legacy PvP experience books.
    int PvpPoints = 0,
    // Grade 0 is the DF2008 unrated/new-character rank (client displays it
    // as 10级). Do not normalize it to grade 1 before the first PvP point.
    byte PvpGrade = 0,
    int PvpWins = 0,
    int PvpLosses = 0,
    int PvpPlayCount = 0,
    int PvpCount = 0,
    byte PvpGradeExtension = 0,
    Guid? MentorCharacterId = null,
    byte MentorExperienceBonusPercent = 0,
    uint MentorRelationshipExperience = 0)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DungeonGoldCredit>? PendingDungeonGoldCredits { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MentorGoldAwardReceipt>? MentorGoldAwards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MentorRegistrationState? MentorRegistration { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrivateStoreStock? PrivateStoreStock { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PrivateStoreGoldCredit>? PendingPrivateStoreGoldCredits { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Guid>? ExpiredAvatarInstanceIds { get; init; }

    [JsonIgnore]
    public bool IsWeakened => WeaknessRecovery < DungeonWeaknessPolicy.FullStamina;
}

public sealed record AccountRecord(
    Guid Id,
    string UserName,
    string PasswordHash,
    List<CharacterRecord> Characters,
    uint AccountUid = 0,
    bool PresetCharactersInitialized = false,
    // Compatibility input for states written before every Premium service was
    // normalized into PremiumServices. Initialization migrates and clears it.
    [property: JsonPropertyName("PremiumExpireAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    uint? LegacyBlackDiamondExpireAt = null,
    List<AccountPremiumServiceRecord>? PremiumServices = null,
    int? Cash = null,
    string? OicqCode = null,
    // 8081 管理接口的权限档位。旧 state.json 没有这个字段，反序列化后是 Player（向后兼容）。
    AccountRole Role = AccountRole.Player,
    // 账号级好友名单。旧 state.json 里没有这个字段，反序列化后是 null（等同空名单）；
    // 写入端不忽略 null，因此之后任意一次整档保存都会写出 "Friends": null——与本类其它
    // 可空成员一致，读取端同样按空名单处理，无副作用。
    List<AccountFriendRecord>? Friends = null,
    // 账号级黑名单。旧 state.json 里没有这个字段，反序列化后是 null（等同空名单）；
    // 与 Friends 一样按可空成员处理，上限见 JsonGameStore.MaximumBlacklist。
    List<AccountBlacklistRecord>? Blacklist = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int GiftReceiveYearMonth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int GiftReceiveCount { get; init; }
}

public sealed record ClientCeraTopUpRequest(string Account, int Amount);
public sealed record ClientDiamondGrantRequest(string Account, int Days);
public sealed record ClientCeraTopUpResult(
    bool Success,
    string? Error,
    string Account,
    int Amount,
    int CharacterCount);
public sealed record ClientDiamondGrantResult(
    bool Success,
    string? Error,
    string Account,
    int Days,
    int CharacterCount,
    uint? ExpiresAt,
    uint RemainingSeconds,
    IReadOnlyList<Guid>? WeaknessRecoveredCharacterIds = null);
public sealed record PremiumServiceState(
    bool Active,
    uint RemainingSeconds,
    uint? ExpiresAt);

public sealed record AccountPremiumServiceRecord(
    byte ServiceType,
    uint? ExpiresAt);

public sealed record PremiumServicePurchaseGrant(
    byte ServiceType,
    int Days);

public sealed record PremiumServiceEntryState(
    byte ServiceType,
    bool Active,
    uint RemainingSeconds,
    uint? ExpiresAt);

public sealed record ClientPremiumGrantRequest(
    string Account,
    byte ServiceType,
    int Days);

public sealed record ClientPremiumGrantResult(
    bool Success,
    string? Error,
    string Account,
    byte ServiceType,
    int Days,
    int CharacterCount,
    uint? ExpiresAt,
    uint RemainingSeconds,
    IReadOnlyList<Guid>? WeaknessRecoveredCharacterIds = null);

public sealed record PublicAccountRecord(
    Guid Id,
    string UserName,
    List<CharacterRecord> Characters,
    uint AccountUid = 0,
    AccountKind Kind = AccountKind.Invalid,
    int Cash = 0,
    AccountRole Role = AccountRole.Player);

public sealed record CreateAccountRequest(
    string UserName,
    string Password,
    bool TestAccount = false,
    string? OicqCode = null);
public sealed record ChangeAccountPasswordRequest(
    string OldPassword,
    string NewPassword);
public sealed record ResetAccountPasswordRequest(
    string OicqCode,
    string NewPassword);
public enum AccountPasswordUpdateFailure
{
    None,
    InvalidRequest,
    InvalidCredentials,
    InvalidRecoveryInformation
}
public sealed record AccountPasswordUpdateResult(
    bool Success,
    AccountPasswordUpdateFailure Failure,
    string? Error);
public sealed record CreateCharacterRequest(
    string Name,
    int Job = 0,
    int GrowType = 2,
    int Level = CharacterExperienceCatalog.MaximumLevel);
public sealed record SetCharacterFatigueRequest(ushort UsedFatigue);
public sealed record SendCharacterMessageRequest(
    string Message,
    GameMessageType MessageType = GameMessageType.Type16Brown,
    ushort TargetAreaUserId = 0);
public sealed record SendCharacterPopupRequest(string Message);
public sealed record FatigueResetResult(
    bool NewFatigueDay,
    int ResetCharacterCount,
    IReadOnlyList<Guid> CharacterIds,
    int FatigueDayKey);

internal enum StoreRecoveryDecision { Recovered, Discarded, Refused }
internal readonly record struct StoreRecoveryResult(StoreRecoveryDecision Decision, string? Reason, int DiscardedEntryCount);

internal interface IDungeonRewardSnapshotWriter
{
    Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken cancellationToken);
}

internal sealed class DungeonRewardSnapshotWriter : IDungeonRewardSnapshotWriter
{
    public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path + ".tmp", snapshot.ToArray(), cancellationToken);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

public sealed partial class JsonGameStore(
    ServerOptions options,
    ILogger<JsonGameStore> logger,
    CharacterExperienceCatalog? experienceCatalog = null,
    ItemCatalog? itemCatalog = null,
    CharacterStatCatalog? statCatalog = null,
    PrivateStoreCatalog? privateStoreCatalog = null,
    TimeProvider? avatarTimeProvider = null)
{
    private const byte LegacyOverlordContractServiceType = 17;
    private const int DefaultAccountCash = 50_000;

    public const string PresetCharacterName = "Berserker";
    public const int PresetCharacterJob = 0;
    public const int PresetCharacterGrowType = 3;
    public const uint PresetCharacterExperience = 270_812_393;
    private const string PasswordHashPrefix = "dfls-local:";

    /// <summary>客户端好友窗口上限为 32 条（DNF.exe 0x4FF710），服务端独立校验。</summary>
    public const int MaximumFriends = 32;

    /// <summary>客户端黑名单上限为 10 条（DNF.exe 0x4FFE60 `cmp eax,0xA`），服务端独立校验。</summary>
    public const int MaximumBlacklist = 10;

    private static readonly CharacterItemRecord[] PresetArmor =
    [
        new(Slot: 11, ItemId: 11271, CountOrValue: 1),
        new(Slot: 12, ItemId: 15270, CountOrValue: 1),
        new(Slot: 13, ItemId: 13271, CountOrValue: 1),
        new(Slot: 14, ItemId: 19271, CountOrValue: 1),
        new(Slot: 15, ItemId: 17270, CountOrValue: 1)
    ];

    private static readonly ushort[] PresetAvatarItemIds =
    [
        39428, // hair/ahair_dgtail.equ
        39008, // cap/acap_yl4eyegear.equ
        39811, // face/earring_silver.equ
        40207, // neck/aneck_blgauzehood.equ
        40633, // coat/acoat_toughaloha.equ
        42206, // skin/askin_black.equ
        41407, // belt/tattoo_wgothic.equ
        41016, // pants/apants_graybelt.equ
        41817  // shoes/awalkers_br.equ
    ];

    private static readonly (ushort ItemId, uint CountOrValue, ushort Start, ushort End)[]
        PresetCreatureItems =
    [
        (63013, 1, CharacterCreatureInventoryLayout.CreatureBagStart,
            CharacterCreatureInventoryLayout.CreatureBagEnd),
        (24, 100, CharacterCreatureInventoryLayout.StackableBagStart,
            CharacterCreatureInventoryLayout.StackableBagEnd),
        (63501, 1, CharacterCreatureInventoryLayout.ArtifactBagStart,
            CharacterCreatureInventoryLayout.ArtifactBagEnd),
        (64001, 1, CharacterCreatureInventoryLayout.ArtifactBagStart,
            CharacterCreatureInventoryLayout.ArtifactBagEnd),
        (64501, 1, CharacterCreatureInventoryLayout.ArtifactBagStart,
            CharacterCreatureInventoryLayout.ArtifactBagEnd)
    ];

    private static readonly CharacterSkillRecord[] PresetSkillLayout =
    [
        new(0, 5, 5),
        new(1, 46, 5),
        new(2, 1, 5),
        new(3, 8, 5),
        new(4, 11, 5),
        new(5, 16, 5),
        new(6, 17, 1),
        new(7, 20, 5),
        new(8, 23, 5),
        new(9, 24, 5),
        new(10, 25, 5),
        new(11, 31, 5),
        new(12, 34, 5),
        new(13, 40, 5),
        new(14, 58, 5),
        new(15, 64, 5),
        new(16, 65, 5),
        new(17, 76, 5),
        new(18, 77, 5),
        new(19, 79, 5),
        new(20, 81, 5),
        new(21, 169, 1),
        new(22, 175, 5),
        new(23, 176, 5),
        new(24, 180, 5),
        new(102, 181, 2),
        new(103, 182, 2),
        new(104, 184, 2),
        new(105, 173, 1),
        new(106, 179, 5),
        new(107, 12, 5),
        new(108, 13, 5),
        new(109, 14, 5),
        new(110, 15, 5),
        new(111, 19, 5),
        new(112, 26, 1),
        new(113, 45, 1),
        new(114, 48, 1),
        new(115, 54, 1),
        new(116, 56, 1),
        new(117, 59, 1),
        new(118, 63, 1),
        new(119, 66, 1),
        new(120, 70, 1),
        new(121, 78, 1),
        new(122, 170, 1),
        new(123, 171, 1),
        new(124, 177, 5),
        new(125, 178, 5),
        new(126, 186, 5),
        new(127, 188, 5),
        new(128, 189, 5),
        new(129, 200, 5)
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDungeonRewardSnapshotWriter _rewardWriter = new DungeonRewardSnapshotWriter();
    private bool _rewardQuarantined;

    internal JsonGameStore(ServerOptions options, ILogger<JsonGameStore> logger, IDungeonRewardSnapshotWriter writer,
        CharacterExperienceCatalog? experienceCatalog = null, ItemCatalog? itemCatalog = null, CharacterStatCatalog? statCatalog = null,
        TimeProvider? avatarTimeProvider = null)
        : this(options, logger, experienceCatalog, itemCatalog, statCatalog, avatarTimeProvider: avatarTimeProvider) => _rewardWriter = writer;

    // 交易交付守门：一笔交易落库后，两个角色在各自会话安装新状态之前，
    // 拒绝任何"把调用方内存里的背包整份写回"的盲写保存——否则被动方会话会
    // 用陈旧内存覆盖掉刚收到的物品，等价于复制。
    private readonly ConcurrentDictionary<Guid, Guid> _pendingTradeDeliveries = new();

    private List<AccountRecord> _accounts = [];
    private uint _nextCharacterNo = 1;
    private uint _nextNormalAccountUid = AccountUidRules.NormalUidStart;
    private uint _nextTestAccountUid = 1;
    private int _fatigueDayKey;
    private List<DungeonRewardLedgerEntry> _dungeonRewardLedger = [];

    private sealed class JsonGameStoreState
    {
        public List<AccountRecord> Accounts { get; set; } = [];
        public uint NextCharacterNo { get; set; } = 1;
        public uint NextNormalAccountUid { get; set; } = AccountUidRules.NormalUidStart;
        public uint NextTestAccountUid { get; set; } = 1;
        public int FatigueDayKey { get; set; }
        public List<DungeonRewardLedgerEntry> DungeonRewardLedger { get; set; } = [];
        public List<DungeonGoldLedgerEntry> DungeonGoldLedger { get; set; } = [];
        public List<PvpSettlementReceipt> PvpLedger { get; set; } = [];
    }

    private string StorePath => Path.GetFullPath(options.DataPath, AppContext.BaseDirectory);

    public bool IsTradeDeliveryPending(Guid characterId) =>
        _pendingTradeDeliveries.ContainsKey(characterId);

    public void AcknowledgeTradeDelivery(Guid characterId, Guid tradeId) =>
        _pendingTradeDeliveries.TryRemove(new KeyValuePair<Guid, Guid>(characterId, tradeId));

    internal void ClearTradeDeliveryPending(Guid characterId) =>
        _pendingTradeDeliveries.TryRemove(characterId, out _);

    private bool TryFindCharacterLocked(
        string accountName,
        Guid characterId,
        out AccountRecord account,
        out int index,
        out CharacterRecord character)
    {
        var found = _accounts.FirstOrDefault(candidate =>
            string.Equals(candidate.UserName, accountName, StringComparison.OrdinalIgnoreCase));
        account = found!;
        index = found is null
            ? -1
            : found.Characters.FindIndex(candidate => candidate.Id == characterId);
        character = index >= 0 ? found!.Characters[index] : null!;
        return index >= 0;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(StorePath))
            {
                var json = await File.ReadAllTextAsync(StorePath, cancellationToken);
                using var document = JsonDocument.Parse(json);
                var stateChanged = false;
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    _accounts = JsonSerializer.Deserialize<List<AccountRecord>>(json) ?? [];
                    _nextCharacterNo = 1;
                    _nextNormalAccountUid = AccountUidRules.NormalUidStart;
                    _nextTestAccountUid = 1;
                    _fatigueDayKey = 0;
                    stateChanged = true;
                }
                else
                {
                    var state = JsonSerializer.Deserialize<JsonGameStoreState>(json)
                        ?? new JsonGameStoreState();
                    _accounts = state.Accounts ?? [];
                    _nextCharacterNo = state.NextCharacterNo == 0
                        ? 1
                        : state.NextCharacterNo;
                    _nextNormalAccountUid = state.NextNormalAccountUid < AccountUidRules.NormalUidStart
                        ? AccountUidRules.NormalUidStart
                        : state.NextNormalAccountUid;
                    _nextTestAccountUid = state.NextTestAccountUid == 0
                        ? 1
                        : state.NextTestAccountUid;
                    _fatigueDayKey = state.FatigueDayKey;
                    _dungeonRewardLedger = state.DungeonRewardLedger ?? [];
                    _dungeonGoldLedger = state.DungeonGoldLedger ?? [];
                    _pvpLedger = state.PvpLedger ?? [];
                    _rewardQuarantined = _dungeonRewardLedger.Any(x => x.Status == DungeonRewardLedgerStatus.Unknown);
                }

                await RecoverPrivateStoresAtStartupLockedAsync(cancellationToken);
                stateChanged |= MigrateIdentityLocked();
                stateChanged |= MigratePremiumServicesLocked();
                stateChanged |= MigrateWeaknessRecoveryLocked();
                stateChanged |= InitializeTestAccountPresetsLocked();
                stateChanged |= RefreshPresetCharactersLocked();
                stateChanged |= MigrateAccountCashLocked();
                stateChanged |= NormalizeCharacterExperienceLocked();
                stateChanged |= NormalizeMentorGrowthLocked();
                stateChanged |= SettleWarehouseUpgradeItemsLocked();
                stateChanged |= MigrateItemInstanceIdsLocked();
                await SettleAvatarLifetimesAtStartupLockedAsync(cancellationToken);
                if (ResetFatigueForNewDayLocked(
                        DateTimeOffset.Now,
                        out var startupResetCharacterIds))
                {
                    stateChanged = true;
                    logger.LogInformation(
                        "Advanced fatigue day to {FatigueDayKey} at startup and reset {CharacterCount} character(s).",
                        _fatigueDayKey,
                        startupResetCharacterIds.Length);
                }
                if (stateChanged && !_rewardQuarantined)
                {
                    await SaveLockedAsync(cancellationToken);
                }
                else if (stateChanged && _rewardQuarantined)
                {
                    logger.LogWarning("Startup migrations applied in memory but not persisted because reward store is quarantined; restart or rerun migrations after recovery.");
                }

                return;
            }

            _nextCharacterNo = 1;
            _nextNormalAccountUid = AccountUidRules.NormalUidStart;
            _nextTestAccountUid = 1;
            _fatigueDayKey = GetFatigueDayKey(DateTimeOffset.Now);
            if (!options.SeedTestAccount)
            {
                _accounts = [];
                await SaveLockedAsync(cancellationToken);
                return;
            }

            const uint defaultTestAccountUid = 1;
            var preset = CreatePresetCharacterLocked(defaultTestAccountUid, slot: 0);
            _accounts =
            [
                new AccountRecord(
                    Guid.NewGuid(),
                    "test",
                    HashPassword("test"),
                    [preset],
                    defaultTestAccountUid,
                    PresetCharactersInitialized: true,
                    Cash: preset.Cash)
            ];
            _nextTestAccountUid = 2;
            _ = MigrateItemInstanceIdsLocked();
            await SaveLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static List<CharacterItemRecord> CreatePresetEquipment(
        IEnumerable<CharacterItemRecord>? existing = null)
    {
        var existingItems = (existing ?? []).ToArray();
        return existingItems
            .Where(item => item.Slot is < 11 or > 15)
            .Concat(PresetArmor.Select(item =>
            {
                var existingItem = existingItems.FirstOrDefault(candidate =>
                    candidate.Slot == item.Slot
                    && candidate.ItemId == item.ItemId);
                return item with
                {
                    InstanceId = existingItem?.InstanceId is { } instanceId
                        && instanceId != Guid.Empty
                            ? instanceId
                            : CharacterItemIdentity.CreateInstanceId()
                };
            }))
            .OrderBy(item => item.Slot)
            .ToList();
    }

    private static bool NeedsPresetAvatarItems(
        IEnumerable<CharacterItemRecord>? existing)
    {
        var itemIds = (existing ?? [])
            .Where(item => item.ItemId != 0 && item.CountOrValue != 0)
            .Select(item => item.ItemId)
            .ToHashSet();
        return PresetAvatarItemIds.Any(itemId => !itemIds.Contains(itemId));
    }

    private static List<CharacterItemRecord> CreatePresetAvatarInventory(
        IEnumerable<CharacterItemRecord>? existing = null)
    {
        var items = (existing ?? [])
            .Where(item => item.ItemId != 0 && item.CountOrValue != 0)
            .OrderBy(item => item.Slot)
            .ToList();
        var itemIds = items.Select(item => item.ItemId).ToHashSet();
        var occupiedSlots = items
            .Where(item => item.Slot < 105)
            .Select(item => item.Slot)
            .ToHashSet();
        foreach (var itemId in PresetAvatarItemIds.Where(itemId => !itemIds.Contains(itemId)))
        {
            var slot = Enumerable.Range(0, 105)
                .Select(value => checked((ushort)value))
                .FirstOrDefault(candidate => !occupiedSlots.Contains(candidate), ushort.MaxValue);
            if (slot == ushort.MaxValue)
            {
                break;
            }

            items.Add(new CharacterItemRecord(
                Slot: slot,
                ItemId: itemId,
                CountOrValue: 1,
                AvatarRemainingSeconds: 0,
                AvatarAbilityIndex: 0,
                InstanceId: CharacterItemIdentity.CreateInstanceId()));
            itemIds.Add(itemId);
            occupiedSlots.Add(slot);
        }

        return items.OrderBy(item => item.Slot).ToList();
    }

    private static bool NeedsPresetCreatureItems(
        IEnumerable<CharacterItemRecord>? existing)
    {
        var itemIds = (existing ?? [])
            .Where(item => item.ItemId != 0 && item.CountOrValue != 0)
            .Select(item => item.ItemId)
            .ToHashSet();
        return PresetCreatureItems.Any(item => !itemIds.Contains(item.ItemId));
    }

    private static List<CharacterItemRecord> CreatePresetCreatureInventory(
        IEnumerable<CharacterItemRecord>? existing = null)
    {
        var items = (existing ?? [])
            .Where(item => item.ItemId != 0 && item.CountOrValue != 0)
            .OrderBy(item => item.Slot)
            .ToList();
        var itemIds = items.Select(item => item.ItemId).ToHashSet();
        var occupiedSlots = items
            .Where(item => CharacterCreatureInventoryLayout.IsInventorySlot(item.Slot))
            .Select(item => item.Slot)
            .ToHashSet();
        var usedCreatureUids = items
            .Where(item => item.Slot < CharacterCreatureInventoryLayout.CreatureBagEnd
                || item.Slot == CharacterCreatureInventoryLayout.EquippedCreatureSlot)
            .Select(item => item.CountOrValue)
            .Where(uid => uid != 0)
            .ToHashSet();

        foreach (var preset in PresetCreatureItems.Where(
                     preset => !itemIds.Contains(preset.ItemId)))
        {
            var slot = Enumerable.Range(preset.Start, preset.End - preset.Start)
                .Select(value => checked((ushort)value))
                .FirstOrDefault(candidate => !occupiedSlots.Contains(candidate), ushort.MaxValue);
            if (slot == ushort.MaxValue)
            {
                continue;
            }

            var isCreature = preset.Start == CharacterCreatureInventoryLayout.CreatureBagStart;
            var value = isCreature
                ? AllocatePresetCreatureUid(usedCreatureUids)
                : preset.CountOrValue;
            items.Add(new CharacterItemRecord(
                Slot: slot,
                ItemId: preset.ItemId,
                CountOrValue: value,
                Durability: preset.Start == CharacterCreatureInventoryLayout.ArtifactBagStart
                    ? (ushort)1_000
                    : (ushort)0,
                SealState: preset.ItemId == 63013 ? (byte)1 : (byte)0,
                InstanceId: preset.ItemId == 24
                    ? Guid.Empty
                    : CharacterItemIdentity.CreateInstanceId()));
            itemIds.Add(preset.ItemId);
            occupiedSlots.Add(slot);
            if (isCreature)
            {
                usedCreatureUids.Add(value);
            }
        }

        return items.OrderBy(item => item.Slot).ToList();
    }

    private static uint AllocatePresetCreatureUid(IReadOnlySet<uint> usedUids)
    {
        var uid = 1u;
        while (usedUids.Contains(uid))
        {
            uid++;
            if (uid == 0)
            {
                throw new InvalidDataException("The preset Creature uid space is exhausted.");
            }
        }

        return uid;
    }

    private static List<CharacterSkillRecord> CreatePresetSkills() =>
        PresetSkillLayout.ToList();

    private CharacterRecord CreatePresetCharacterLocked(uint accountUid, byte slot)
    {
        return new CharacterRecord(
            Id: Guid.NewGuid(),
            Name: PresetCharacterName,
            Job: PresetCharacterJob,
            GrowType: PresetCharacterGrowType,
            Level: CharacterExperienceCatalog.MaximumLevel,
            Sp: 500,
            Cash: 50000,
            Equipment: CreatePresetEquipment(),
            AvatarInventory: CreatePresetAvatarInventory(),
            CreatureInventory: CreatePresetCreatureInventory(),
            Skills: CreatePresetSkills(),
            Experience: PresetCharacterExperience,
            CharacterNo: AllocateCharacterNoLocked(),
            AccountUid: accountUid,
            Slot: slot,
            IsPreset: true,
            UnlockedDungeonIds: [],
            DungeonProgress: []);
    }

    private bool MigrateIdentityLocked()
    {
        var changed = false;
        var usedAccountUids = new HashSet<uint>();
        var seenCharacterNos = new HashSet<uint>();
        for (var accountIndex = 0; accountIndex < _accounts.Count; accountIndex++)
        {
            var account = _accounts[accountIndex];
            var accountUid = account.AccountUid;
            var accountUidNeedsAllocation = accountUid == AccountUidRules.InvalidUid
                || !usedAccountUids.Add(accountUid);
            if (accountUidNeedsAllocation)
            {
                var isLegacyTest = string.Equals(
                    account.UserName,
                    "test",
                    StringComparison.OrdinalIgnoreCase);
                accountUid = AllocateAccountUidLocked(isLegacyTest);
                usedAccountUids.Add(accountUid);
                changed = true;
            }

            AdvanceAccountUidCountersLocked(accountUid);
            var characters = account.Characters ?? [];
            for (var slot = 0; slot < characters.Count; slot++)
            {
                var character = characters[slot];
                var characterNo = character.CharacterNo;
                if (characterNo == 0 || !seenCharacterNos.Add(characterNo))
                {
                    characterNo = AllocateCharacterNoLocked();
                    seenCharacterNos.Add(characterNo);
                    changed = true;
                }
                else
                {
                    AdvanceCharacterNoCounterLocked(characterNo);
                }

                var nextCharacter = character with
                {
                    CharacterNo = characterNo,
                    AccountUid = accountUid,
                    Slot = checked((byte)slot),
                    IsPreset = AccountUidRules.IsTest(accountUid)
                        && (character.IsPreset || IsPresetCharacterCandidate(character))
                };
                if (nextCharacter != character)
                {
                    changed = true;
                    characters[slot] = nextCharacter;
                }
            }

            var nextAccount = account with
            {
                Characters = characters,
                AccountUid = accountUid
            };
            if (nextAccount != account)
            {
                changed = true;
                _accounts[accountIndex] = nextAccount;
            }
        }

        return changed;
    }

    private bool MigrateItemInstanceIdsLocked()
    {
        if (itemCatalog is null)
        {
            return false;
        }

        var changed = false;
        var assignedCount = 0;
        var duplicateCount = 0;
        var usedIds = new HashSet<Guid>();

        CharacterItemRecord NormalizeItem(
            CharacterItemRecord item,
            Guid characterId,
            string space)
        {
            if (!itemCatalog.TryGetDefinition(item.ItemId, out var definition)
                || !CharacterItemIdentity.RequiresInstanceId(definition))
            {
                return item;
            }

            if (item.InstanceId != Guid.Empty
                && usedIds.Add(item.InstanceId))
            {
                return item;
            }

            if (item.InstanceId != Guid.Empty)
            {
                duplicateCount++;
                logger.LogError(
                    "Duplicate equipment instance id {InstanceId} on character {CharacterId} in {Space} slot {Slot}, item {ItemId}; assigning a replacement id.",
                    item.InstanceId,
                    characterId,
                    space,
                    item.Slot,
                    item.ItemId);
            }

            Guid instanceId;
            do
            {
                instanceId = CharacterItemIdentity.CreateInstanceId();
            }
            while (!usedIds.Add(instanceId));

            assignedCount++;
            changed = true;
            return item with { InstanceId = instanceId };
        }

        CharacterMailAttachmentRecord NormalizeAttachment(
            CharacterMailAttachmentRecord attachment,
            Guid characterId,
            uint mailId)
        {
            if (!itemCatalog.TryGetDefinition(attachment.ItemId, out var definition)
                || !CharacterItemIdentity.RequiresInstanceId(definition))
            {
                return attachment;
            }

            if (attachment.InstanceId != Guid.Empty
                && usedIds.Add(attachment.InstanceId))
            {
                return attachment;
            }

            if (attachment.InstanceId != Guid.Empty)
            {
                duplicateCount++;
                logger.LogError(
                    "Duplicate equipment instance id {InstanceId} on character {CharacterId} mail {MailId}, item {ItemId}; assigning a replacement id.",
                    attachment.InstanceId,
                    characterId,
                    mailId,
                    attachment.ItemId);
            }

            Guid instanceId;
            do
            {
                instanceId = CharacterItemIdentity.CreateInstanceId();
            }
            while (!usedIds.Add(instanceId));

            assignedCount++;
            changed = true;
            return attachment with { InstanceId = instanceId };
        }

        List<CharacterItemRecord>? NormalizeItems(
            List<CharacterItemRecord>? items,
            Guid characterId,
            string space) => items?
            .Select(item => NormalizeItem(item, characterId, space))
            .ToList();

        for (var accountIndex = 0; accountIndex < _accounts.Count; accountIndex++)
        {
            var account = _accounts[accountIndex];
            for (var characterIndex = 0;
                 characterIndex < account.Characters.Count;
                 characterIndex++)
            {
                var character = account.Characters[characterIndex];
                var mailbox = character.Mailbox?
                    .Select(mail => mail.Attachment is null
                        ? mail
                        : mail with
                        {
                            Attachment = NormalizeAttachment(
                                mail.Attachment,
                                character.Id,
                                mail.Id)
                        })
                    .ToList();
                account.Characters[characterIndex] = character with
                {
                    Inventory = NormalizeItems(
                        character.Inventory,
                        character.Id,
                        "inventory"),
                    Equipment = NormalizeItems(
                        character.Equipment,
                        character.Id,
                        "equipment"),
                    Warehouse = NormalizeItems(
                        character.Warehouse,
                        character.Id,
                        "warehouse"),
                    AvatarInventory = NormalizeItems(
                        character.AvatarInventory,
                        character.Id,
                        "avatar inventory"),
                    CreatureInventory = NormalizeItems(
                        character.CreatureInventory,
                        character.Id,
                        "creature inventory"),
                    Mailbox = mailbox
                };
            }
        }

        if (assignedCount > 0)
        {
            logger.LogInformation(
                "Assigned persistent UUIDv7 instance ids to {EquipmentCount} legacy equipment item(s).",
                assignedCount);
        }

        if (duplicateCount > 0)
        {
            logger.LogWarning(
                "Replaced {DuplicateCount} duplicate non-empty equipment instance id(s); inspect preceding error records for the affected items.",
                duplicateCount);
        }

        return changed;
    }

    private bool MigratePremiumServicesLocked()
    {
        var changed = false;
        for (var accountIndex = 0; accountIndex < _accounts.Count; accountIndex++)
        {
            var account = _accounts[accountIndex];
            var sourceServices = (account.PremiumServices ?? []).ToList();
            if (account.LegacyBlackDiamondExpireAt is { } legacyBlackDiamondExpiry)
            {
                sourceServices.Add(new AccountPremiumServiceRecord(
                    GameProtocolEngine.BlackDiamondServiceType,
                    legacyBlackDiamondExpiry));
            }

            var normalizedServices = sourceServices
                .Select(service => service.ServiceType == LegacyOverlordContractServiceType
                    ? service with
                    {
                        ServiceType = GameProtocolEngine.OverlordContractServiceType
                    }
                    : service)
                .GroupBy(service => service.ServiceType)
                .Select(group => new AccountPremiumServiceRecord(
                    group.Key,
                    group.Select(service => service.ExpiresAt)
                        .Where(expiry => expiry.HasValue)
                        .Select(expiry => expiry!.Value)
                        .DefaultIfEmpty()
                        .Max() is var expiry && expiry > 0
                            ? expiry
                            : null))
                .OrderBy(service => service.ServiceType)
                .ToList();
            var storedServices = account.PremiumServices ?? [];
            var accountChanged = account.LegacyBlackDiamondExpireAt is not null
                || !storedServices.SequenceEqual(normalizedServices);
            if (!accountChanged)
            {
                continue;
            }

            _accounts[accountIndex] = account with
            {
                LegacyBlackDiamondExpireAt = null,
                PremiumServices = normalizedServices
            };
            changed = true;
            logger.LogInformation(
                "Normalized {ServiceCount} account Premium service(s) for account {Account}; legacy black-diamond and service-17 fields were migrated when present.",
                normalizedServices.Count,
                account.UserName);
        }

        return changed;
    }

    private bool MigrateWeaknessRecoveryLocked()
    {
        var changed = false;
        foreach (var account in _accounts)
        {
            for (var characterIndex = 0;
                 characterIndex < account.Characters.Count;
                 characterIndex++)
            {
                var character = account.Characters[characterIndex];
                var recovery = character.LegacyIsWeakened switch
                {
                    true => DungeonWeaknessPolicy.InitialRecovery,
                    false => DungeonWeaknessPolicy.FullStamina,
                    null => (byte)Math.Clamp(
                        character.WeaknessRecovery,
                        DungeonWeaknessPolicy.MinimumRecovery,
                        DungeonWeaknessPolicy.FullStamina)
                };
                if (character.LegacyIsWeakened is null
                    && character.WeaknessRecovery == recovery)
                {
                    continue;
                }

                account.Characters[characterIndex] = character with
                {
                    WeaknessRecovery = recovery,
                    LegacyIsWeakened = null
                };
                changed = true;
            }
        }

        if (changed)
        {
            logger.LogInformation(
                "Migrated legacy weakness flags and normalized persisted weakness recovery values to the 0-100 range.");
        }

        return changed;
    }

    private bool MigrateAccountCashLocked()
    {
        var changed = false;
        for (var accountIndex = 0; accountIndex < _accounts.Count; accountIndex++)
        {
            var account = _accounts[accountIndex];
            var cash = Math.Max(
                0,
                account.Cash
                    ?? account.Characters
                        .OrderBy(character => character.Slot)
                        .Select(character => (int?)character.Cash)
                        .FirstOrDefault()
                    ?? DefaultAccountCash);
            if (account.Cash == cash
                && account.Characters.All(character => character.Cash == cash))
            {
                continue;
            }

            _accounts[accountIndex] = SetAccountCash(account, cash);
            changed = true;
            logger.LogInformation(
                "Migrated account {Account} to the shared Cera wallet {Cash}; synchronized {CharacterCount} character mirror(s).",
                account.UserName,
                cash,
                account.Characters.Count);
        }

        return changed;
    }

    private static AccountRecord SetAccountCash(AccountRecord account, int cash)
    {
        var normalizedCash = Math.Max(0, cash);
        var characters = account.Characters
            .Select(character => character.Cash == normalizedCash
                ? character
                : character with { Cash = normalizedCash })
            .ToList();
        return account with
        {
            Cash = normalizedCash,
            Characters = characters
        };
    }

    private bool InitializeTestAccountPresetsLocked()
    {
        var changed = false;
        for (var accountIndex = 0; accountIndex < _accounts.Count; accountIndex++)
        {
            var account = _accounts[accountIndex];
            if (!AccountUidRules.IsTest(account.AccountUid))
            {
                if (!account.PresetCharactersInitialized)
                {
                    _accounts[accountIndex] = account with { PresetCharactersInitialized = true };
                    changed = true;
                }

                continue;
            }

            if (account.PresetCharactersInitialized)
            {
                continue;
            }

            var characters = account.Characters ?? [];
            if (!characters.Any(character => character.IsPreset)
                && characters.Count < MaximumTransferRoster.MaximumVisibleCharacters)
            {
                characters.Add(CreatePresetCharacterLocked(
                    account.AccountUid,
                    checked((byte)characters.Count)));
                changed = true;
            }

            _accounts[accountIndex] = account with
            {
                Characters = characters,
                PresetCharactersInitialized = true
            };
            changed = true;
        }

        return changed;
    }

    private bool RefreshPresetCharactersLocked()
    {
        var changed = false;
        foreach (var account in _accounts.Where(account =>
                     AccountUidRules.IsTest(account.AccountUid)))
        {
            for (var index = 0; index < account.Characters.Count; index++)
            {
                var character = account.Characters[index];
                if (!character.IsPreset
                    || character.Level is not (55 or CharacterExperienceCatalog.MaximumLevel)
                    || character.Level == CharacterExperienceCatalog.MaximumLevel
                        && character.Experience == PresetCharacterExperience
                        && !NeedsPresetAvatarItems(character.AvatarInventory)
                        && !NeedsPresetCreatureItems(character.CreatureInventory))
                {
                    continue;
                }

                account.Characters[index] = character with
                {
                    Level = CharacterExperienceCatalog.MaximumLevel,
                    Experience = PresetCharacterExperience,
                    AvatarInventory = CreatePresetAvatarInventory(character.AvatarInventory),
                    CreatureInventory = CreatePresetCreatureInventory(character.CreatureInventory)
                };
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsPresetCharacterCandidate(CharacterRecord character) =>
        string.Equals(character.Name, PresetCharacterName, StringComparison.Ordinal)
        && character.Job == PresetCharacterJob
        && character.GrowType == PresetCharacterGrowType
        && character.Level is 55 or CharacterExperienceCatalog.MaximumLevel;

    private uint AllocateAccountUidLocked(bool testAccount)
    {
        var next = testAccount ? _nextTestAccountUid : _nextNormalAccountUid;
        var minimum = testAccount ? 1u : AccountUidRules.NormalUidStart;
        if (next < minimum || testAccount && next > AccountUidRules.MaximumTestUid)
        {
            next = minimum;
        }

        while (_accounts.Any(account => account.AccountUid == next))
        {
            if (next == uint.MaxValue
                || testAccount && next == AccountUidRules.MaximumTestUid)
            {
                throw new InvalidOperationException("The account UID space is exhausted.");
            }

            next++;
        }

        var allocated = next;
        if (testAccount && next == AccountUidRules.MaximumTestUid)
        {
            _nextTestAccountUid = AccountUidRules.MaximumTestUid;
            return allocated;
        }

        if (next == uint.MaxValue)
        {
            throw new InvalidOperationException("The account UID space is exhausted.");
        }

        next++;
        if (testAccount)
        {
            _nextTestAccountUid = next;
        }
        else
        {
            _nextNormalAccountUid = next;
        }

        return allocated;
    }

    private void AdvanceAccountUidCountersLocked(uint accountUid)
    {
        if (AccountUidRules.IsTest(accountUid))
        {
            if (accountUid == AccountUidRules.MaximumTestUid)
            {
                _nextTestAccountUid = AccountUidRules.MaximumTestUid;
            }
            else if (_nextTestAccountUid <= accountUid)
            {
                _nextTestAccountUid = accountUid + 1;
            }
        }
        else if (AccountUidRules.IsNormal(accountUid)
            && _nextNormalAccountUid <= accountUid)
        {
            _nextNormalAccountUid = accountUid == uint.MaxValue
                ? uint.MaxValue
                : accountUid + 1;
        }
    }

    private uint AllocateCharacterNoLocked()
    {
        if (_nextCharacterNo == 0)
        {
            throw new InvalidOperationException("The CharacterNo sequence is exhausted.");
        }

        var allocated = _nextCharacterNo;
        if (_nextCharacterNo == uint.MaxValue)
        {
            throw new InvalidOperationException("The CharacterNo sequence is exhausted.");
        }

        _nextCharacterNo++;
        return allocated;
    }

    private void AdvanceCharacterNoCounterLocked(uint characterNo)
    {
        if (characterNo == uint.MaxValue)
        {
            _nextCharacterNo = uint.MaxValue;
        }
        else if (_nextCharacterNo <= characterNo)
        {
            _nextCharacterNo = characterNo + 1;
        }
    }

    public async Task<PublicAccountRecord[]> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _accounts.Select(ToPublic).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AuthenticatedAccount?> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || password is null)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(userName);
            return account is not null && PasswordMatches(account, password)
                ? ToAuthenticatedAccount(account)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountPasswordUpdateResult> ChangePasswordAsync(
        string userName,
        ChangeAccountPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || request is null
            || string.IsNullOrEmpty(request.OldPassword)
            || !IsValidPassword(request.NewPassword))
        {
            return new AccountPasswordUpdateResult(
                false,
                AccountPasswordUpdateFailure.InvalidRequest,
                "账号、原密码和新密码不能为空，新密码长度应为 6-128。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var accountIndex = _accounts.FindIndex(account =>
                string.Equals(account.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (accountIndex < 0
                || !PasswordMatches(_accounts[accountIndex], request.OldPassword))
            {
                return new AccountPasswordUpdateResult(
                    false,
                    AccountPasswordUpdateFailure.InvalidCredentials,
                    "账号或密码错误。");
            }

            _accounts[accountIndex] = _accounts[accountIndex] with
            {
                PasswordHash = HashPassword(request.NewPassword)
            };
            await SaveLockedAsync(cancellationToken);
            return new AccountPasswordUpdateResult(
                true,
                AccountPasswordUpdateFailure.None,
                null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountPasswordUpdateResult> ResetPasswordAsync(
        string userName,
        ResetAccountPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || request is null
            || string.IsNullOrWhiteSpace(request.OicqCode)
            || request.OicqCode.Length > 32
            || !IsValidPassword(request.NewPassword))
        {
            return new AccountPasswordUpdateResult(
                false,
                AccountPasswordUpdateFailure.InvalidRequest,
                "账号和 QQ 不能为空，新密码长度应为 6-128。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var accountIndex = _accounts.FindIndex(account =>
                string.Equals(account.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (accountIndex < 0
                || !FixedTimeEqualsText(_accounts[accountIndex].OicqCode, request.OicqCode.Trim()))
            {
                return new AccountPasswordUpdateResult(
                    false,
                    AccountPasswordUpdateFailure.InvalidRecoveryInformation,
                    "账号或 QQ 错误。");
            }

            _accounts[accountIndex] = _accounts[accountIndex] with
            {
                PasswordHash = HashPassword(request.NewPassword)
            };
            await SaveLockedAsync(cancellationToken);
            return new AccountPasswordUpdateResult(
                true,
                AccountPasswordUpdateFailure.None,
                null);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The 1.0.1.9 client sends the account name in LOGIN but does not include
    // the launcher password in the captured command body. Keep this resolver
    // separate from password authentication so the protocol adapter does not
    // accidentally treat a client-supplied UID as authoritative.
    public async Task<AuthenticatedAccount?> FindAccountAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(userName);
            return account is null ? null : ToAuthenticatedAccount(account);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AuthenticatedAccount?> GetAccountAsync(
        uint accountUid,
        CancellationToken cancellationToken = default)
    {
        if (accountUid == AccountUidRules.InvalidUid)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                candidate.AccountUid == accountUid);
            return account is null ? null : ToAuthenticatedAccount(account);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord[]> GetCharactersAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(userName);
            return account?.Characters.ToArray() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord[]> GetCharactersAsync(
        uint accountUid,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                candidate.AccountUid == accountUid);
            return account?.Characters.ToArray() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 按角色名跨账号查一条存档角色（只读）。好友名单快照/id63 需要离线好友的
    /// 等级/职业/成长（好友行 `Lv.%d 职业` 在**没有本地 user 记录**时就读这些字段，
    /// 离线好友正是这种情况），而好友条目只有名字、拿不到账号，所以必须在存档里反查。
    /// </summary>
    /// <remarks>
    /// 名字比较用 OrdinalIgnoreCase（角色名语义与好友/通道表一致；黑名单是唯一的
    /// Ordinal 例外）。同名不同大小写存在于多个账号时返回第一个命中，与
    /// <c>AddFriendAsync</c> 的“存在性检查”口径一致。
    /// </remarks>
    public async Task<CharacterRecord?> GetCharacterByNameAsync(
        string characterName,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in _accounts)
            {
                var character = account.Characters.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, characterName, StringComparison.OrdinalIgnoreCase));
                if (character is not null)
                {
                    return character;
                }
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>按 (账号, 角色 id) 读角色：交易服务校验托管与算金币要用。</summary>
    public async Task<CharacterRecord?> GetCharacterAsync(
        string accountName,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return TryFindCharacterLocked(accountName, characterId, out _, out _, out var character)
                ? character
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRosterEntry[]> GetCharacterRosterAsync(
        uint accountUid,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                candidate.AccountUid == accountUid);
            return account?.Characters
                       .Take(MaximumTransferRoster.MaximumVisibleCharacters)
                       .Select(character => new CharacterRosterEntry(
                           character.Slot,
                           character.CharacterNo,
                           character.Id,
                           character.Name))
                       .ToArray()
                   ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    private AccountRecord? FindAccountLocked(string userName) =>
        _accounts.FirstOrDefault(account =>
            string.Equals(account.UserName, userName, StringComparison.OrdinalIgnoreCase));

    private CharacterRecord? CreateCharacterLocked(
        AccountRecord account,
        CreateCharacterRequest request,
        out string? error)
    {
        error = null;
        if (account.Characters.Count >= MaximumTransferRoster.MaximumVisibleCharacters)
        {
            error = "Character roster is full.";
            return null;
        }

        var name = request.Name.Trim();
        if (_accounts.SelectMany(candidate => candidate.Characters).Any(character =>
                string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "角色名已存在。";
            return null;
        }

        var character = new CharacterRecord(
            Guid.NewGuid(),
            name,
            request.Job,
            request.GrowType,
            request.Level,
            Sp: 0,
            Cash: Math.Max(0, account.Cash ?? DefaultAccountCash),
            Experience: experienceCatalog?.GetMinimumCumulativeExperience(request.Level) ?? 0,
            CharacterNo: AllocateCharacterNoLocked(),
            AccountUid: account.AccountUid,
            Slot: checked((byte)account.Characters.Count),
            UnlockedDungeonIds: [],
            DungeonProgress: [],
            TutorialStarted: false);
        account.Characters.Add(character);
        return character;
    }

    private static void ReindexCharacterSlotsLocked(AccountRecord account)
    {
        for (var index = 0; index < account.Characters.Count; index++)
        {
            var character = account.Characters[index];
            var slot = checked((byte)index);
            if (character.Slot != slot || character.AccountUid != account.AccountUid)
            {
                account.Characters[index] = character with
                {
                    Slot = slot,
                    AccountUid = account.AccountUid
                };
            }
        }
    }

    public async Task<(bool Created, PublicAccountRecord? Account, string? Error)> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var userName = request.UserName?.Trim();
        var oicqCode = request.OicqCode?.Trim();
        if (string.IsNullOrWhiteSpace(userName)
            || userName.Length is < 4 or > 32
            || !IsValidPassword(request.Password)
            // QQ 码是找回密码的唯一凭据：太短等于没有（1 位数字只需一次猜测就能接管账号）。
            || string.IsNullOrWhiteSpace(oicqCode)
            || oicqCode.Length is < 5 or > 32)
        {
            return (false, null, "账号长度应为 4-32，密码长度应为 6-128，QQ 不能为空且至少 5 位。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined)
                throw new InvalidOperationException("store is quarantined after uncertain dungeon reward write");
            if (_accounts.Any(x => string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, null, "账号已存在。");
            }

            var accountUid = AllocateAccountUidLocked(request.TestAccount);
            var account = new AccountRecord(
                Guid.NewGuid(),
                userName,
                HashPassword(request.Password),
                [],
                accountUid,
                PresetCharactersInitialized: !request.TestAccount,
                Cash: DefaultAccountCash,
                OicqCode: oicqCode,
                // 新账号一律是普通玩家。引导管理员【只在启动时】提升（ApplyBootstrapAdminsAsync）：
                // 如果注册时就按名单提升，那么"名单里的账号尚未注册"的部署窗口里，
                // 任何人都能抢注该名字直接拿到管理员权限。
                Role: AccountRole.Player);
            if (IsBootstrapAdmin(userName))
            {
                logger.LogWarning(
                    "Account {UserName} was registered and is listed in Admin.Users, but bootstrap " +
                    "promotion only happens at startup. Restart the server to promote it (or promote it " +
                    "through PUT /api/admin/users/{{userName}}/role).",
                    userName);
            }
            if (request.TestAccount)
            {
                account.Characters.Add(CreatePresetCharacterLocked(accountUid, slot: 0));
                account = account with { PresetCharactersInitialized = true };
            }

            _accounts.Add(account);
            await SaveLockedAsync(cancellationToken);
            return (true, ToPublic(account), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Created, CharacterRecord? Character, string? Error)> CreateCharacterAsync(
        string userName,
        CreateCharacterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Length > 20
            || request.Level is < 1 or > CharacterExperienceCatalog.MaximumLevel)
        {
            return (
                false,
                null,
                $"角色名长度应为 1-20，等级应为 1-{CharacterExperienceCatalog.MaximumLevel}。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(userName);
            if (account is null)
            {
                return (false, null, "账号不存在。");
            }

            var character = CreateCharacterLocked(account, request, out var error);
            if (character is null)
            {
                return (false, null, error);
            }

            await SaveLockedAsync(cancellationToken);
            return (true, character, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Created, CharacterRecord? Character, string? Error)> CreateCharacterAsync(
        uint accountUid,
        CreateCharacterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Length > 20
            || request.Level is < 1 or > CharacterExperienceCatalog.MaximumLevel)
        {
            return (
                false,
                null,
                $"角色名长度应为 1-20，等级应为 1-{CharacterExperienceCatalog.MaximumLevel}。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                candidate.AccountUid == accountUid);
            if (account is null)
            {
                return (false, null, "账号不存在。");
            }

            var character = CreateCharacterLocked(account, request, out var error);
            if (character is null)
            {
                return (false, null, error);
            }

            await SaveLockedAsync(cancellationToken);
            return (true, character, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Deleted, CharacterRecord? Character, string? Error)> DeleteCharacterAsync(
        string userName,
        int slot,
        CancellationToken cancellationToken = default)
    {
        if (slot < 0 || slot >= MaximumTransferRoster.MaximumVisibleCharacters)
        {
            return (false, null, "Character slot is outside the visible roster.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var accounts = CopyMentorAccountsLocked();
            var account = accounts.FirstOrDefault(a => string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return (false, null, "Account does not exist.");
            }

            if (slot >= account.Characters.Count)
            {
                return (false, null, "Character slot is empty.");
            }

            var character = account.Characters[slot];
            account.Characters.RemoveAt(slot);
            ReindexCharacterSlotsLocked(account);
            var notices = PrepareRemovedMentorEdgesLocked(accounts);
            if (!await SaveMentorAccountsLockedAsync(accounts, cancellationToken))
                return (false, null, "Character deletion outcome is uncertain; store quarantined.");
            PublishMentorRemovalNotices(notices);
            logger.LogInformation(
                "Deleted character {CharacterName} ({CharacterNo}, {StorageId}) from slot {Slot} on account {AccountUid}.",
                character.Name,
                character.CharacterNo,
                character.Id,
                slot,
                account.AccountUid);
            return (true, character, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Deleted, CharacterRecord? Character, string? Error)> DeleteCharacterAsync(
        uint accountUid,
        int slot,
        CancellationToken cancellationToken = default)
    {
        if (slot < 0 || slot >= MaximumTransferRoster.MaximumVisibleCharacters)
        {
            return (false, null, "Character slot is outside the visible roster.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var accounts = CopyMentorAccountsLocked();
            var account = accounts.FirstOrDefault(candidate =>
                candidate.AccountUid == accountUid);
            if (account is null)
            {
                return (false, null, "Account does not exist.");
            }

            if (slot >= account.Characters.Count)
            {
                return (false, null, "Character slot is empty.");
            }

            var character = account.Characters[slot];
            account.Characters.RemoveAt(slot);
            ReindexCharacterSlotsLocked(account);
            var notices = PrepareRemovedMentorEdgesLocked(accounts);
            if (!await SaveMentorAccountsLockedAsync(accounts, cancellationToken))
                return (false, null, "Character deletion outcome is uncertain; store quarantined.");
            PublishMentorRemovalNotices(notices);
            logger.LogInformation(
                "Deleted character {CharacterName} ({CharacterNo}, {StorageId}) from slot {Slot} on account {AccountUid}.",
                character.Name,
                character.CharacterNo,
                character.Id,
                slot,
                account.AccountUid);
            return (true, character, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Replaced, int CharacterCount, string? Error)> ReplaceAllCharactersAsync(
        string targetUserName,
        IEnumerable<CharacterRecord> characters,
        CancellationToken cancellationToken = default)
    {
        var replacements = characters.ToArray();
        if (replacements.Length > MaximumTransferRoster.MaximumVisibleCharacters
            || replacements.Any(character =>
                string.IsNullOrWhiteSpace(character.Name)
                || character.Name.Length > 20
                || character.Level is < 1 or > CharacterExperienceCatalog.MaximumLevel)
            || replacements.Select(character => character.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != replacements.Length)
        {
            return (false, 0, "Replacement roster is invalid or exceeds the visible character limit.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var accounts = CopyMentorAccountsLocked();
            var targetAccount = accounts.FirstOrDefault(account =>
                string.Equals(account.UserName, targetUserName, StringComparison.OrdinalIgnoreCase));
            if (targetAccount is null)
            {
                return (false, 0, "Target account does not exist.");
            }

            var activeCharacterNos = _accounts
                .SelectMany(account => account.Characters)
                .Select(character => character.CharacterNo)
                .Where(characterNo => characterNo != 0)
                .ToHashSet();
            foreach (var account in accounts)
            {
                account.Characters.Clear();
            }

            var usedCharacterNos = new HashSet<uint>();
            for (var index = 0; index < replacements.Length; index++)
            {
                var replacement = replacements[index];
                var characterNo = replacement.CharacterNo;
                if (characterNo == 0
                    || !activeCharacterNos.Contains(characterNo)
                    || !usedCharacterNos.Add(characterNo))
                {
                    characterNo = AllocateCharacterNoLocked();
                    usedCharacterNos.Add(characterNo);
                }
                else
                {
                    AdvanceCharacterNoCounterLocked(characterNo);
                }

                var minimumExperience = experienceCatalog?
                    .GetMinimumCumulativeExperience(replacement.Level) ?? 0;
                targetAccount.Characters.Add(replacement with
                {
                    Experience = Math.Max(replacement.Experience, minimumExperience),
                    CharacterNo = characterNo,
                    AccountUid = targetAccount.AccountUid,
                    Slot = checked((byte)index),
                    IsPreset = false,
                    Cash = Math.Max(0, targetAccount.Cash ?? DefaultAccountCash)
                });
            }

            var notices = PrepareRemovedMentorEdgesLocked(accounts);
            if (!await SaveMentorAccountsLockedAsync(accounts, cancellationToken))
                return (false, 0, "Roster replacement outcome is uncertain; store quarantined.");
            Commerce.RevokeStores();
            PublishMentorRemovalNotices(notices);
            logger.LogInformation(
                "Replaced all emulator characters with {Count} maximum-level transfers on account {Account}.",
                replacements.Length,
                targetAccount.UserName);
            return (true, replacements.Length, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> AddCashToAllCharactersAsync(
        int amount,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var updatedCount = 0;
            for (var accountIndex = 0; accountIndex < _accounts.Count; accountIndex++)
            {
                var account = _accounts[accountIndex];
                var currentCash = Math.Max(0, account.Cash ?? DefaultAccountCash);
                var nextCash = currentCash > int.MaxValue - amount
                    ? int.MaxValue
                    : currentCash + amount;
                _accounts[accountIndex] = SetAccountCash(account, nextCash);
                updatedCount += account.Characters.Count;
            }

            if (updatedCount > 0)
            {
                await SaveLockedAsync(cancellationToken);
            }

            logger.LogInformation(
                "Added {Amount} Cera to {CharacterCount} characters.",
                amount,
                updatedCount);
            return updatedCount;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool NormalizeCharacterExperienceLocked()
    {
        if (experienceCatalog is null)
        {
            return false;
        }

        var changed = false;
        foreach (var account in _accounts)
        {
            for (var index = 0; index < account.Characters.Count; index++)
            {
                var character = account.Characters[index];
                var normalizedExperience =
                    experienceCatalog.NormalizeCumulativeExperience(
                        character.Level,
                        character.Experience);
                if (character.Experience == normalizedExperience)
                {
                    continue;
                }

                account.Characters[index] = character with
                {
                    Experience = normalizedExperience
                };
                changed = true;
                logger.LogWarning(
                    "Raised inconsistent cumulative experience for character {CharacterName} ({CharacterId}) at level {Level}: {OldExperience}->{Experience}.",
                    character.Name,
                    character.Id,
                    character.Level,
                    character.Experience,
                    normalizedExperience);
            }
        }

        return changed;
    }

    public async Task<ClientCeraTopUpResult> GrantCeraToAccountAsync(
        string accountName,
        int amount,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = accountName?.Trim() ?? string.Empty;
        if (normalizedAccount.Length == 0 || normalizedAccount.Length > 32)
        {
            return new(
                false,
                "账号不能为空，且长度不能超过 32。",
                normalizedAccount,
                amount,
                0);
        }

        if (amount <= 0 || amount % 100 != 0)
        {
            return new(
                false,
                "充值点数必须是正整数，且为 100 的倍数。",
                normalizedAccount,
                amount,
                0);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(normalizedAccount);
            if (account is null)
            {
                return new(false, "账号不存在。", normalizedAccount, amount, 0);
            }

            if (account.Characters.Count == 0)
            {
                return new(false, "账号没有可充值的角色。", account.UserName, amount, 0);
            }

            var currentCash = Math.Max(0, account.Cash ?? DefaultAccountCash);
            var nextCash = currentCash > int.MaxValue - amount
                ? int.MaxValue
                : currentCash + amount;
            account = SetAccountCash(account, nextCash);
            var accountIndex = _accounts.FindIndex(candidate => candidate.Id == account.Id);
            _accounts[accountIndex] = account;

            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Added {Amount} Cera to {CharacterCount} characters on account {Account}.",
                amount,
                account.Characters.Count,
                account.UserName);
            return new(
                true,
                null,
                account.UserName,
                amount,
                account.Characters.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ClientDiamondGrantResult> GrantDiamondToAccountAsync(
        string accountName,
        int days,
        CancellationToken cancellationToken = default)
    {
        var result = await GrantPremiumServiceToAccountAsync(
            accountName,
            GameProtocolEngine.BlackDiamondServiceType,
            days,
            cancellationToken);
        return new(
            result.Success,
            result.Error,
            result.Account,
            result.Days,
            result.CharacterCount,
            result.ExpiresAt,
            result.RemainingSeconds,
            result.WeaknessRecoveredCharacterIds);
    }

    public async Task<ClientPremiumGrantResult> GrantPremiumServiceToAccountAsync(
        string accountName,
        byte serviceType,
        int days,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = accountName?.Trim() ?? string.Empty;
        if (normalizedAccount.Length == 0 || normalizedAccount.Length > 32)
        {
            return new(
                false,
                "账号不能为空，且长度不能超过 32。",
                normalizedAccount,
                serviceType,
                days,
                0,
                null,
                0);
        }

        if (!IsSupportedPremiumServiceType(serviceType))
        {
            return new(
                false,
                "不支持的 Premium 服务类型。",
                normalizedAccount,
                serviceType,
                days,
                0,
                null,
                0);
        }

        if (days <= 0 || serviceType == GameProtocolEngine.BlackDiamondServiceType
            && days != 30)
        {
            return new(
                false,
                serviceType == GameProtocolEngine.BlackDiamondServiceType
                    ? "当前仅支持开通 30 天黑钻。"
                    : "契约有效期必须是正整数天数。",
                normalizedAccount,
                serviceType,
                days,
                0,
                null,
                0);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accountIndex = _accounts.FindIndex(candidate =>
                string.Equals(candidate.UserName, normalizedAccount, StringComparison.OrdinalIgnoreCase));
            var account = accountIndex < 0 ? null : _accounts[accountIndex];
            if (account is null)
            {
                return new(false, "账号不存在。", normalizedAccount, serviceType, days, 0, null, 0);
            }

            var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var currentExpiry = GetServiceExpiry(account, serviceType);
            var blackDiamondActivated =
                serviceType == GameProtocolEngine.BlackDiamondServiceType
                && !(currentExpiry is { } activeExpiry && activeExpiry > now);
            var start = currentExpiry is > 0 && currentExpiry.Value > now
                ? currentExpiry.Value
                : now;
            var extension = checked((uint)TimeSpan.FromDays(days).TotalSeconds);
            var expiry = start > uint.MaxValue - extension
                ? uint.MaxValue
                : start + extension;

            account = SetPremiumServiceExpiry(account, serviceType, expiry);
            Guid[] weaknessRecoveredCharacterIds = [];
            if (blackDiamondActivated)
            {
                var recoveredIds = new List<Guid>();
                account = account with
                {
                    Characters = account.Characters
                        .Select(character =>
                        {
                            if (character.IsWeakened)
                            {
                                recoveredIds.Add(character.Id);
                            }

                            return character with
                            {
                                WeaknessRecovery = DungeonWeaknessPolicy.FullStamina,
                                LegacyIsWeakened = null
                            };
                        })
                        .ToList()
                };
                weaknessRecoveredCharacterIds = recoveredIds.ToArray();
            }

            _accounts[accountIndex] = account;
            await SaveLockedAsync(cancellationToken);
            var remaining = expiry > now ? expiry - now : 0;
            logger.LogInformation(
                "Granted {Days} days of Premium service {ServiceType} to account {Account}; expires at {ExpiresAt}.",
                days,
                serviceType,
                account.UserName,
                expiry);
            return new(
                true,
                null,
                account.UserName,
                serviceType,
                days,
                account.Characters.Count,
                expiry,
                remaining,
                weaknessRecoveredCharacterIds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PremiumServiceState> GetPremiumServiceAsync(
        string accountName,
        CancellationToken cancellationToken = default)
    {
        var service = (await GetPremiumServicesAsync(accountName, cancellationToken))
            .FirstOrDefault(entry =>
                entry.ServiceType == GameProtocolEngine.BlackDiamondServiceType);
        return service is null
            ? new(false, 0, null)
            : new(service.Active, service.RemainingSeconds, service.ExpiresAt);
    }

    public async Task<PremiumServiceEntryState[]> GetPremiumServicesAsync(
        string accountName,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = accountName?.Trim() ?? string.Empty;
        if (normalizedAccount.Length == 0)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(normalizedAccount);
            if (account is null)
            {
                return [];
            }

            var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return (account.PremiumServices ?? [])
                .GroupBy(service => service.ServiceType)
                .Select(group => group.Last())
                .Where(service => IsSupportedPremiumServiceType(service.ServiceType))
                .Select(service => new PremiumServiceEntryState(
                    service.ServiceType,
                    service.ExpiresAt is { } expiry && expiry > now,
                    service.ExpiresAt is { } activeExpiry && activeExpiry > now
                        ? activeExpiry - now
                        : 0,
                    service.ExpiresAt))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsSupportedPremiumServiceType(byte serviceType) => serviceType is
        GameProtocolEngine.PrivateStoreServiceType
        or GameProtocolEngine.BlackDiamondServiceType
        or GameProtocolEngine.OverlordContractServiceType
        or GameProtocolEngine.MasterContractServiceType;

    private static uint? GetServiceExpiry(AccountRecord account, byte serviceType)
        => account.PremiumServices?
            .LastOrDefault(service => service.ServiceType == serviceType)
            ?.ExpiresAt;

    private static AccountRecord SetPremiumServiceExpiry(
        AccountRecord account,
        byte serviceType,
        uint expiry)
    {
        var services = (account.PremiumServices ?? [])
            .Where(service => service.ServiceType != serviceType)
            .Append(new AccountPremiumServiceRecord(serviceType, expiry))
            .OrderBy(service => service.ServiceType)
            .ToList();
        return account with { PremiumServices = services };
    }

    public async Task<CharacterMailRecord[]> GetCharacterMailsAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var character = _accounts
                .SelectMany(account => account.Characters)
                .FirstOrDefault(candidate => candidate.Id == characterId);
            return (character?.Mailbox ?? [])
                .Where(mail => IsMailAvailable(mail, now))
                .OrderByDescending(mail => mail.SentAt)
                .ThenByDescending(mail => mail.Id)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Created, CharacterMailRecord? Mail, string? Error)>
        SendCharacterMailAsync(
            Guid characterId,
            CreateCharacterMailRequest request,
            CancellationToken cancellationToken = default)
    {
        request = EnsureMailAttachmentInstanceId(request);
        var validationError = ValidateMailRequest(request);
        if (validationError is not null)
        {
            return (false, null, validationError);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            AccountRecord? owner = null;
            var characterIndex = -1;
            foreach (var account in _accounts)
            {
                var index = account.Characters.FindIndex(character => character.Id == characterId);
                if (index < 0)
                {
                    continue;
                }

                owner = account;
                characterIndex = index;
                break;
            }

            if (owner is null)
            {
                return (false, null, "Character does not exist.");
            }

            var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var character = owner.Characters[characterIndex];
            var mailbox = (character.Mailbox ?? [])
                .Where(mail => IsMailAvailable(mail, now))
                .ToList();
            if (mailbox.Count >= byte.MaxValue)
            {
                return (false, null, "Character mailbox is full.");
            }

            var mailId = AllocateMailIdLocked();
            var mail = new CharacterMailRecord(
                mailId,
                request.Sender.Trim(),
                request.Text,
                request.Gold,
                request.Attachment,
                now,
                checked(now + (uint)TimeSpan.FromDays(30).TotalSeconds));
            mailbox.Add(mail);
            var updated = ReconcileAvatarLifetimes(character with { Mailbox = mailbox }, character);
            owner.Characters[characterIndex] = updated;
            mail = updated.Mailbox!.Single(m => m.Id == mailId);
            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Created mail {MailId} for character {CharacterId}: gold={Gold}, item={ItemId}.",
                mail.Id,
                characterId,
                mail.Gold,
                mail.Attachment?.ItemId ?? 0);
            return (true, mail, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterMailSendResult> SendCharacterMailFromCharacterAsync(
        string userName,
        Guid senderCharacterId,
        string recipientName,
        CreateCharacterMailRequest request,
        int senderGold,
        IEnumerable<CharacterItemRecord> senderInventory,
        IEnumerable<CharacterItemRecord> senderEquipment,
        CancellationToken cancellationToken = default)
    {
        request = EnsureMailAttachmentInstanceId(request);
        if (request.Attachment is { } limited
            && (limited.AvatarExpiresAtUnixSeconds is not null || limited.AvatarRemainingSeconds is > 0))
            return new(false, null, Guid.Empty, null, CharacterMailSendFailure.InvalidRequest);
        if (ValidateMailRequest(request) is not null
            || string.IsNullOrWhiteSpace(recipientName)
            || recipientName.Length > 20
            || senderGold < 0)
        {
            return new CharacterMailSendResult(
                false,
                null,
                Guid.Empty,
                null,
                CharacterMailSendFailure.InvalidRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (IsEconomicWriteBlocked(senderCharacterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return new CharacterMailSendResult(
                    false,
                    null,
                    Guid.Empty,
                    null,
                    CharacterMailSendFailure.SenderStateChanged);
            }

            var senderOwner = _accounts.FirstOrDefault(account =>
                string.Equals(account.UserName, userName, StringComparison.OrdinalIgnoreCase));
            var senderIndex = senderOwner?.Characters.FindIndex(character =>
                character.Id == senderCharacterId) ?? -1;
            if (senderOwner is null || senderIndex < 0)
            {
                return new CharacterMailSendResult(
                    false,
                    null,
                    Guid.Empty,
                    null,
                    CharacterMailSendFailure.SenderNotFound);
            }

            AccountRecord? recipientOwner = null;
            var recipientIndex = -1;
            foreach (var account in _accounts)
            {
                var index = account.Characters.FindIndex(character =>
                    string.Equals(
                        character.Name,
                        recipientName.Trim(),
                        StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    continue;
                }

                recipientOwner = account;
                recipientIndex = index;
                break;
            }

            if (recipientOwner is null)
            {
                return new CharacterMailSendResult(
                    false,
                    null,
                    Guid.Empty,
                    null,
                    CharacterMailSendFailure.RecipientNotFound);
            }

            var sender = senderOwner.Characters[senderIndex];
            if (request.Gold > sender.Gold
                || senderGold != sender.Gold - request.Gold)
            {
                return new CharacterMailSendResult(
                    false,
                    sender,
                    recipientOwner.Characters[recipientIndex].Id,
                    null,
                    CharacterMailSendFailure.SenderStateChanged);
            }

            var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var senderUpdated = sender with
            {
                Gold = senderGold,
                Inventory = senderInventory.OrderBy(item => item.Slot).ToList(),
                Equipment = senderEquipment.OrderBy(item => item.Slot).ToList()
            };
            var recipient = recipientOwner.Characters[recipientIndex];
            if (recipient.Id == senderUpdated.Id)
            {
                recipient = senderUpdated;
            }

            var mailbox = (recipient.Mailbox ?? [])
                .Where(mail => IsMailAvailable(mail, now))
                .ToList();
            if (mailbox.Count >= byte.MaxValue)
            {
                return new CharacterMailSendResult(
                    false,
                    sender,
                    recipient.Id,
                    null,
                    CharacterMailSendFailure.RecipientMailboxFull);
            }

            var mail = new CharacterMailRecord(
                AllocateMailIdLocked(),
                request.Sender.Trim(),
                request.Text ?? string.Empty,
                request.Gold,
                request.Attachment,
                now,
                checked(now + (uint)TimeSpan.FromDays(30).TotalSeconds));
            mailbox.Add(mail);
            var recipientUpdated = recipient with { Mailbox = mailbox };
            if (recipientUpdated.Id == senderUpdated.Id)
            {
                senderUpdated = recipientUpdated;
                senderOwner.Characters[senderIndex] = senderUpdated;
            }
            else
            {
                senderOwner.Characters[senderIndex] = senderUpdated;
                recipientOwner.Characters[recipientIndex] = recipientUpdated;
            }

            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Character {SenderCharacterId} sent mail {MailId} to {RecipientCharacterId}: gold={Gold}, item={ItemId}.",
                senderCharacterId,
                mail.Id,
                recipientUpdated.Id,
                mail.Gold,
                mail.Attachment?.ItemId ?? 0);
            return new CharacterMailSendResult(
                true,
                senderUpdated,
                recipientUpdated.Id,
                mail,
                CharacterMailSendFailure.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> AcknowledgeCharacterMailNotificationsAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var character = account.Characters[characterIndex];
                var mailbox = character.Mailbox ?? [];
                var acknowledgedCount = mailbox.Count(mail => mail.IsNew);
                if (acknowledgedCount == 0)
                {
                    return 0;
                }

                account.Characters[characterIndex] = character with
                {
                    Mailbox = mailbox
                        .Select(mail => mail.IsNew ? mail with { IsNew = false } : mail)
                        .ToList()
                };
                await SaveLockedAsync(cancellationToken);
                return acknowledgedCount;
            }

            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterMailClaimResult?> ClaimCharacterMailAsync(
        string userName,
        Guid characterId,
        uint mailId,
        int gold,
        IEnumerable<CharacterItemRecord> inventory,
        IEnumerable<CharacterItemRecord> equipment,
        CancellationToken cancellationToken = default,
        IEnumerable<CharacterItemRecord>? avatarInventory = null,
        IEnumerable<CharacterItemRecord>? creatureInventory = null,
        ushort? warehouseCapacity = null,
        ushort? expectedWarehouseCapacity = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return null;
            }

            var character = account.Characters[characterIndex];
            if (!HasDungeonGoldCapacity(character, gold)) return null;
            if (!TryResolveWarehouseCapacityUpdate(
                    character,
                    warehouseCapacity,
                    expectedWarehouseCapacity,
                    out var nextWarehouseCapacity))
            {
                return null;
            }

            var mailbox = (character.Mailbox ?? []).ToList();
            var mailIndex = mailbox.FindIndex(mail => mail.Id == mailId);
            if (mailIndex < 0)
            {
                return null;
            }

            var mail = mailbox[mailIndex];
            var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (!IsMailAvailable(mail, now)
                || mail.Gold == 0 && mail.Attachment is null)
            {
                return null;
            }

            mailbox[mailIndex] = mail with
            {
                Gold = 0,
                Attachment = null,
                IsNew = false
            };
            var updated = character with
            {
                Gold = Math.Max(0, gold),
                Inventory = inventory.OrderBy(item => item.Slot).ToList(),
                Equipment = equipment.OrderBy(item => item.Slot).ToList(),
                AvatarInventory = avatarInventory?.OrderBy(item => item.Slot).ToList()
                    ?? character.AvatarInventory,
                CreatureInventory = creatureInventory?.OrderBy(item => item.Slot).ToList()
                    ?? character.CreatureInventory,
                WarehouseCapacity = nextWarehouseCapacity,
                Mailbox = mailbox
            };
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return new CharacterMailClaimResult(updated, mail);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> SetCharacterMailStateAsync(
        Guid characterId,
        uint mailId,
        ushort state,
        CancellationToken cancellationToken = default)
    {
        if (state is not (2 or 3))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var character = account.Characters[characterIndex];
                var mailbox = (character.Mailbox ?? []).ToList();
                var mailIndex = mailbox.FindIndex(mail => mail.Id == mailId);
                if (mailIndex < 0)
                {
                    return false;
                }

                var mail = mailbox[mailIndex];
                if (state == 3 && (mail.Gold != 0 || mail.Attachment is not null))
                {
                    return false;
                }

                mailbox[mailIndex] = mail with { IsNew = false, State = state };
                account.Characters[characterIndex] = character with { Mailbox = mailbox };
                await SaveLockedAsync(cancellationToken);
                return true;
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteCharacterMailAsync(
        Guid characterId,
        uint mailId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var character = account.Characters[characterIndex];
                var mailbox = (character.Mailbox ?? []).ToList();
                var removedCount = mailbox.RemoveAll(mail => mail.Id == mailId);
                if (removedCount == 0)
                {
                    return false;
                }

                account.Characters[characterIndex] = character with { Mailbox = mailbox };
                await SaveLockedAsync(cancellationToken);
                return true;
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AccountFriendRecord>> GetFriendsAsync(
        string accountName,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(accountName);
            return account?.Friends?.ToArray() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FriendUpdateFailure> AddFriendAsync(
        string accountName,
        string friendName,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = friendName.Trim();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accountIndex = _accounts.FindIndex(candidate =>
                string.Equals(candidate.UserName, accountName, StringComparison.OrdinalIgnoreCase));
            if (accountIndex < 0)
            {
                return FriendUpdateFailure.AccountNotFound;
            }

            var account = _accounts[accountIndex];
            if (account.Characters.Any(character =>
                    string.Equals(character.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                return FriendUpdateFailure.Self;
            }

            var targetExists = _accounts.Any(candidate => candidate.Characters.Any(character =>
                string.Equals(character.Name, trimmedName, StringComparison.OrdinalIgnoreCase)));
            if (!targetExists)
            {
                return FriendUpdateFailure.CharacterNotFound;
            }

            var friends = account.Friends ?? [];
            if (friends.Any(friend =>
                    string.Equals(friend.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                return FriendUpdateFailure.AlreadyFriend;
            }

            if (friends.Count >= MaximumFriends)
            {
                return FriendUpdateFailure.FriendListFull;
            }

            friends.Add(new AccountFriendRecord(
                trimmedName,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            _accounts[accountIndex] = account with { Friends = friends };
            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Account {Account} added friend {Friend}; total={Count}.",
                account.UserName,
                trimmedName,
                friends.Count);
            return FriendUpdateFailure.None;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FriendUpdateFailure> RemoveFriendAsync(
        string accountName,
        string friendName,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = friendName.Trim();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(accountName);
            if (account is null)
            {
                return FriendUpdateFailure.AccountNotFound;
            }

            var friend = account.Friends?.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
            if (friend is null)
            {
                return FriendUpdateFailure.NotFriend;
            }

            account.Friends!.Remove(friend);
            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Account {Account} removed friend {Friend}; total={Count}.",
                account.UserName,
                trimmedName,
                account.Friends.Count);
            return FriendUpdateFailure.None;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AccountBlacklistRecord>> GetBlacklistAsync(
        string accountName,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(accountName);
            return account?.Blacklist?.ToArray() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 往账号黑名单追加一条。失败判定与好友一致，但**名字比较全部用 Ordinal**：
    /// 客户端 CNRDBlackListdManager 的 FindByName 是逐字节大小写敏感比较，连
    /// “角色是否存在”也按精确拼写判定（客户端失败码 0x4C 的文案就是“无法找到的名字，
    /// 请确认是否大小写错误或频道错误”）。这与好友名单的 OrdinalIgnoreCase 语义相反。
    /// </summary>
    /// <remarks>
    /// 自己的角色名同样可以加进黑名单：原版服务端对被拉黑的自我的行为未知，客户端
    /// 社区菜单只在“目标不是本机角色”时才提供该项，而任务给定的失败码只有 74/76/78，
    /// 所以这里不做自身特判（悄悄话到自己的路径本来就被“不能给自己发悄悄话”拦住）。
    /// </remarks>
    public async Task<BlacklistUpdateFailure> AddBlacklistAsync(
        string accountName,
        string blacklistName,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = blacklistName.Trim();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accountIndex = _accounts.FindIndex(candidate =>
                string.Equals(candidate.UserName, accountName, StringComparison.OrdinalIgnoreCase));
            if (accountIndex < 0)
            {
                return BlacklistUpdateFailure.AccountNotFound;
            }

            var account = _accounts[accountIndex];
            var targetExists = _accounts.Any(candidate => candidate.Characters.Any(character =>
                string.Equals(character.Name, trimmedName, StringComparison.Ordinal)));
            if (!targetExists)
            {
                return BlacklistUpdateFailure.CharacterNotFound;
            }

            var blacklist = account.Blacklist ?? [];
            if (blacklist.Any(entry =>
                    string.Equals(entry.Name, trimmedName, StringComparison.Ordinal)))
            {
                return BlacklistUpdateFailure.AlreadyBlacklisted;
            }

            if (blacklist.Count >= MaximumBlacklist)
            {
                return BlacklistUpdateFailure.BlacklistFull;
            }

            // 日期语义与客户端一致：128 回包不带日期，客户端按本机时间填；服务端在
            // 130 快照里回 (年-1900, 月-1, 日)，用添加当天的本地日期最贴近用户看到的值。
            var addedAt = DateTime.Now;
            blacklist.Add(new AccountBlacklistRecord(
                trimmedName,
                addedAt.Year,
                addedAt.Month,
                addedAt.Day));
            _accounts[accountIndex] = account with { Blacklist = blacklist };
            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Account {Account} blacklisted {Name}; total={Count}.",
                account.UserName,
                trimmedName,
                blacklist.Count);
            return BlacklistUpdateFailure.None;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>按名字（Ordinal，大小写敏感）从账号黑名单里删掉一条。</summary>
    public async Task<BlacklistUpdateFailure> RemoveBlacklistAsync(
        string accountName,
        string blacklistName,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = blacklistName.Trim();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(accountName);
            if (account is null)
            {
                return BlacklistUpdateFailure.AccountNotFound;
            }

            var entry = account.Blacklist?.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, trimmedName, StringComparison.Ordinal));
            if (entry is null)
            {
                return BlacklistUpdateFailure.NotBlacklisted;
            }

            account.Blacklist!.Remove(entry);
            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Account {Account} removed blacklist entry {Name}; total={Count}.",
                account.UserName,
                trimmedName,
                account.Blacklist.Count);
            return BlacklistUpdateFailure.None;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Trusted server reward planner contract; not exposed as a network mutation API.
    internal async Task<DungeonRewardCommitResult> CommitDungeonRewardAsync(DungeonRewardCommitRequest request, CancellationToken cancellationToken = default)
    {
        var expectedInventory = request.ExpectedInventory?.ToArray(); var expectedEquipment = request.ExpectedEquipment?.ToArray(); var plannedInventory = request.PlannedInventory?.ToArray(); var plannedEquipment = request.PlannedEquipment?.ToArray(); var mails = request.GeneratedMails?.ToArray();
        if (expectedInventory is null || expectedEquipment is null || plannedInventory is null || plannedEquipment is null || mails is null) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "invalid-request", null);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var frozenRequest = request with { ExpectedInventory = expectedInventory, ExpectedEquipment = expectedEquipment, PlannedInventory = plannedInventory, PlannedEquipment = plannedEquipment, GeneratedMails = mails };
            var fingerprint = ComputeDungeonRewardFingerprint(frozenRequest);
            if (fingerprint.Length == 0 || string.IsNullOrWhiteSpace(request.IdempotencyKey)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "invalid-request", null);
            if (string.IsNullOrWhiteSpace(request.PayloadFingerprint) || !string.Equals(request.PayloadFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "payload-fingerprint-mismatch", null);
            var prior = _dungeonRewardLedger.FirstOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (prior is not null) { if (prior.PayloadFingerprint != fingerprint) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "idempotency-payload-mismatch", null); var c = _accounts.SelectMany(a => a.Characters).FirstOrDefault(x => x.Id == request.CharacterId); return new(prior.Status, c, prior.RewardItemId, prior.GoldDelta, prior.RejectionCode, prior.UnknownReason); }
            if (_rewardQuarantined)
                return new(DungeonRewardLedgerStatus.Unknown, null, null, 0, null, "store-quarantined");
            if (request.TargetGold < 0 || request.ExpectedGold < 0) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "invalid-gold", null);
            if (plannedInventory.GroupBy(x => x.Slot).Any(g => g.Count() > 1) || plannedEquipment.GroupBy(x => x.Slot).Any(g => g.Count() > 1) || plannedInventory.Any(x => x.ItemId == 0) || plannedEquipment.Any(x => x.ItemId == 0)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "invalid-planning", null);
            var account = _accounts.FirstOrDefault(a => string.Equals(a.UserName, request.UserName, StringComparison.OrdinalIgnoreCase)); var index = account?.Characters.FindIndex(x => x.Id == request.CharacterId) ?? -1; if (account is null) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "account-not-found", null); if (index < 0) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "character-not-found", null); if (IsEconomicWriteBlocked(request.CharacterId)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "pending-trade-delivery", null);
            var current = account.Characters[index];
            var targetGold = request.TargetGold;
            if (request.CardType == (byte)PartyCardType.Free && request.RequestedGoldReward > 0 && targetGold >= current.Gold)
                targetGold = (int)Math.Min(targetGold, Math.Max(current.Gold, int.MaxValue - PendingDungeonGold(current)
                    - PendingStoreGold(current) - PendingMentorGold(current)));
            if (!HasDungeonGoldCapacity(current, targetGold)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "gold-capacity-reserved", null);
            if (current.Gold != request.ExpectedGold || !ItemsEqual(current.Inventory, expectedInventory) || !ItemsEqual(current.Equipment, expectedEquipment)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "character-state-conflict", null);
            if (!InventoryPlanPreservesBaseline(current.Inventory, plannedInventory) || !ItemsEqual(current.Equipment, plannedEquipment)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "invalid-planning", null);
            if (!TryResolveWarehouseCapacityUpdate(current, request.WarehouseCapacity, request.ExpectedWarehouseCapacity, out var capacity)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "warehouse-capacity-conflict", null);
            if (!TryAppendGeneratedMailsLocked(current, mails, out var mailbox, out _, out var generated)) return new(DungeonRewardLedgerStatus.Rejected, null, null, 0, "invalid-mails", null); capacity = Math.Max(capacity, generated);
            var accounts = JsonSerializer.Deserialize<List<AccountRecord>>(JsonSerializer.Serialize(_accounts))!; var updated = current with { Gold = targetGold, Inventory = plannedInventory.OrderBy(x => x.Slot).ToList(), Equipment = plannedEquipment.OrderBy(x => x.Slot).ToList(), Mailbox = mailbox, WarehouseCapacity = capacity }; accounts.First(a => a.Id == account.Id).Characters[index] = updated; var now = DateTimeOffset.UtcNow; var entry = new DungeonRewardLedgerEntry(request.IdempotencyKey, fingerprint, request.CharacterId, request.DungeonGeneration, request.PartySlot, request.CardType, request.CardColumn, DungeonRewardLedgerStatus.Committed, now, now, null, targetGold - current.Gold, null, null); var ledger = new List<DungeonRewardLedgerEntry>(_dungeonRewardLedger) { entry };
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, ledger, cancellationToken);
            }
            catch (Exception ex)
            {
                _rewardQuarantined = true;
                _dungeonRewardLedger.Add(entry with
                {
                    Status = DungeonRewardLedgerStatus.Unknown,
                    UnknownReason = $"snapshot-write: {ex.GetType().Name}: {ex.Message}"
                });
                return new(DungeonRewardLedgerStatus.Unknown, null, null, 0, null,
                    _dungeonRewardLedger[^1].UnknownReason);
            }
            _accounts = accounts;
            _dungeonRewardLedger = ledger;
            if (request.CardType == (byte)PartyCardType.Free)
                AccrueMentorGoldLocked(request.CharacterId, request.MentorGoldIncarnation, request.RequestedGoldReward);
            return new(DungeonRewardLedgerStatus.Committed, updated, null, entry.GoldDelta, null, null);
        }
        finally { _gate.Release(); }
    }
    internal async Task<StoreRecoveryResult> ForceClearRewardQuarantineAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return new(StoreRecoveryDecision.Refused, "reason-required", 0);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var unknown = _dungeonRewardLedger.Where(x => x.Status == DungeonRewardLedgerStatus.Unknown).ToArray();
            if (!File.Exists(StorePath)) return new(StoreRecoveryDecision.Refused, "official-state-missing", 0);
            var state = JsonSerializer.Deserialize<JsonGameStoreState>(await File.ReadAllTextAsync(StorePath, cancellationToken))
                ?? throw new InvalidDataException("Invalid official store state");
            unknown = unknown.Concat(state.DungeonRewardLedger.Where(x => x.Status == DungeonRewardLedgerStatus.Unknown))
                .GroupBy(x => x.IdempotencyKey).Select(x => x.Last()).ToArray();
            var allCommitted = unknown.Length > 0 && unknown.All(pending => state.DungeonRewardLedger.Any(disk =>
                disk.IdempotencyKey == pending.IdempotencyKey && disk.PayloadFingerprint == pending.PayloadFingerprint
                && disk.Status == DungeonRewardLedgerStatus.Committed));
            if (allCommitted && !state.DungeonRewardLedger.Any(x => x.Status == DungeonRewardLedgerStatus.Unknown))
            {
                InstallRewardRootLocked(state);
                logger.LogWarning("Reward quarantine recovered; reason={Reason}, decision=Recovered, keys={Keys}, count={Count}",
                    reason, unknown.Select(x => x.IdempotencyKey).ToArray(), unknown.Length);
                return new(StoreRecoveryDecision.Recovered, "official-committed-receipt", 0);
            }
            var discarded = unknown.Where(pending => !state.DungeonRewardLedger.Any(disk =>
                disk.IdempotencyKey == pending.IdempotencyKey && disk.PayloadFingerprint == pending.PayloadFingerprint
                && disk.Status == DungeonRewardLedgerStatus.Committed)).ToArray();
            var alreadyResolved = unknown.Length > 0 && unknown.All(pending => state.DungeonRewardLedger.Any(disk =>
                disk.IdempotencyKey == pending.IdempotencyKey && disk.PayloadFingerprint == pending.PayloadFingerprint
                && (disk.Status == DungeonRewardLedgerStatus.Committed ||
                    disk.Status == DungeonRewardLedgerStatus.Rejected && disk.RejectionCode == "operator-discarded")));
            if (alreadyResolved && !state.DungeonRewardLedger.Any(x => x.Status == DungeonRewardLedgerStatus.Unknown))
            {
                InstallRewardRootLocked(state);
                logger.LogWarning("Reward quarantine resolved from official receipts; reason={Reason}, decision=Discarded, keys={Keys}, count={Count}",
                    reason, unknown.Select(x => x.IdempotencyKey).ToArray(), unknown.Length);
                return new(StoreRecoveryDecision.Discarded, reason, discarded.Length);
            }
            foreach (var pending in discarded)
            {
                state.DungeonRewardLedger.RemoveAll(x => x.IdempotencyKey == pending.IdempotencyKey);
                state.DungeonRewardLedger.Add(pending with
                {
                    Status = DungeonRewardLedgerStatus.Rejected, RejectionCode = "operator-discarded", UnknownReason = reason
                });
            }
            // Operator privilege: persist only the official root plus rejection decisions while quarantine remains held.
            var snapshot = JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions { WriteIndented = true });
            await _rewardWriter.WriteAsync(StorePath, snapshot, cancellationToken);
            InstallRewardRootLocked(state);
            logger.LogWarning("Reward quarantine discarded; reason={Reason}, decision=Discarded, keys={Keys}, count={Count}", reason, unknown.Select(x => x.IdempotencyKey).ToArray(), unknown.Length);
            return new(StoreRecoveryDecision.Discarded, reason, unknown.Length);
        }
        finally { _gate.Release(); }
    }

    private void InstallRewardRootLocked(JsonGameStoreState state)
    {
        Commerce.RevokeStores();
        _nextCharacterNo = state.NextCharacterNo;
        _nextNormalAccountUid = state.NextNormalAccountUid;
        _nextTestAccountUid = state.NextTestAccountUid;
        _fatigueDayKey = state.FatigueDayKey;
        _accounts = state.Accounts;
        _dungeonRewardLedger = state.DungeonRewardLedger; _dungeonGoldLedger = state.DungeonGoldLedger; _pvpLedger = state.PvpLedger ?? [];
        _rewardQuarantined = false;
    }

    internal async Task<DungeonRewardCommitResult> RecoverDungeonRewardAsync(string idempotencyKey)
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(StorePath)) return new(DungeonRewardLedgerStatus.Unknown, null, null, 0, null, "official-state-missing");
            var state = JsonSerializer.Deserialize<JsonGameStoreState>(await File.ReadAllTextAsync(StorePath));
            var expected = _dungeonRewardLedger.FirstOrDefault(x => x.IdempotencyKey == idempotencyKey);
            var entry = state?.DungeonRewardLedger.FirstOrDefault(x => x.IdempotencyKey == idempotencyKey && x.Status == DungeonRewardLedgerStatus.Committed);
            if (entry is null || expected is null || entry.PayloadFingerprint != expected.PayloadFingerprint)
                return new(DungeonRewardLedgerStatus.Unknown, null, null, 0, null, "committed-receipt-missing-or-mismatch");
            InstallRewardRootLocked(state!);
            var character = _accounts.SelectMany(x => x.Characters).FirstOrDefault(x => x.Id == entry.CharacterId);
            return new(DungeonRewardLedgerStatus.Committed, character, entry.RewardItemId, entry.GoldDelta, null, null);
        }
        finally { _gate.Release(); }
    }

    internal static string ComputeDungeonRewardFingerprint(DungeonRewardCommitRequest request)
    {
        var expectedInventory = request.ExpectedInventory?.OrderBy(x => x.Slot).ToArray();
        var expectedEquipment = request.ExpectedEquipment?.OrderBy(x => x.Slot).ToArray();
        var plannedInventory = request.PlannedInventory?.OrderBy(x => x.Slot).ToArray();
        var plannedEquipment = request.PlannedEquipment?.OrderBy(x => x.Slot).ToArray();
        var mails = request.GeneratedMails?.ToArray();
        var canonical = JsonSerializer.Serialize(new { request.IdempotencyKey, request.UserName, request.CharacterId, request.DungeonGeneration, request.PartySlot, request.CardType, request.CardColumn, request.TargetGold, request.ExpectedGold, expectedInventory, expectedEquipment, plannedInventory, plannedEquipment, mails, request.WarehouseCapacity, request.ExpectedWarehouseCapacity, request.RequestedGoldReward, request.MentorGoldIncarnation });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool ItemsEqual(IEnumerable<CharacterItemRecord>? a, IEnumerable<CharacterItemRecord> b) => (a ?? []).OrderBy(x => x.Slot).SequenceEqual(b.OrderBy(x => x.Slot));

    private static bool InventoryPlanPreservesBaseline(IEnumerable<CharacterItemRecord>? baseline, IEnumerable<CharacterItemRecord> planned)
    {
        var oldItems = (baseline ?? []).ToDictionary(x => x.Slot);
        var newItems = planned.ToDictionary(x => x.Slot);
        foreach (var pair in oldItems)
        {
            if (!newItems.TryGetValue(pair.Key, out var next) || next with { CountOrValue = pair.Value.CountOrValue } != pair.Value || next.CountOrValue < pair.Value.CountOrValue)
                return false;
        }
        return true;
    }

    public async Task<CharacterRecord?> SaveCharacterInventoryAsync(
        string userName,
        Guid characterId,
        int gold,
        IEnumerable<CharacterItemRecord> inventory,
        IEnumerable<CharacterItemRecord> equipment,
        CancellationToken cancellationToken = default,
        IEnumerable<CreateCharacterMailRequest>? generatedMails = null,
        ushort? warehouseCapacity = null,
        ushort? expectedWarehouseCapacity = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined)
                throw new InvalidOperationException("store is quarantined after uncertain dungeon reward write");
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var character = account.Characters[index];
            if (!HasDungeonGoldCapacity(character, gold)) return null;
            if (!TryResolveWarehouseCapacityUpdate(
                    character,
                    warehouseCapacity,
                    expectedWarehouseCapacity,
                    out var nextWarehouseCapacity))
            {
                return null;
            }

            if (!TryAppendGeneratedMailsLocked(
                    character,
                    generatedMails,
                    out var mailbox,
                    out var createdMailCount,
                    out var generatedWarehouseCapacity))
            {
                return null;
            }

            nextWarehouseCapacity = Math.Max(
                nextWarehouseCapacity,
                generatedWarehouseCapacity);

            var updated = character with
            {
                Gold = Math.Max(0, gold),
                Inventory = inventory.OrderBy(item => item.Slot).ToList(),
                Equipment = equipment.OrderBy(item => item.Slot).ToList(),
                WarehouseCapacity = nextWarehouseCapacity,
                Mailbox = mailbox
            };
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            LogGeneratedMails(characterId, createdMailCount);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 一笔交易的原子结算：同一把 _gate 内读完双方 → 校验与规划 → 应用 → 一次落库。
    /// 任一步失败都返回失败且**不写盘**，所以不存在"一方加货一方没扣"的中间态。
    /// </summary>
    public async Task<ItemTradeCommitResult> CommitItemTradeAsync(
        ItemTradeCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.InitiatorEscrows);
        ArgumentNullException.ThrowIfNull(request.PartnerEscrows);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined)
                throw new InvalidOperationException("store is quarantined after uncertain dungeon reward write");
            if (itemCatalog is null || statCatalog is null)
            {
                return new ItemTradeCommitResult(
                    false, ItemTradeCommitFailure.CatalogUnavailable, Guid.Empty, null, null, null);
            }

            if (!TryFindCharacterLocked(
                    request.InitiatorAccountName,
                    request.InitiatorCharacterId,
                    out var initiatorAccount,
                    out var initiatorIndex,
                    out var initiator)
                || !TryFindCharacterLocked(
                    request.PartnerAccountName,
                    request.PartnerCharacterId,
                    out var partnerAccount,
                    out var partnerIndex,
                    out var partner))
            {
                return new ItemTradeCommitResult(
                    false, ItemTradeCommitFailure.CharacterMissing, Guid.Empty, null, null, null);
            }

            if (IsEconomicWriteBlocked(request.InitiatorCharacterId) || IsEconomicWriteBlocked(request.PartnerCharacterId))
                return new(false, ItemTradeCommitFailure.EscrowMissing, Guid.Empty, null, null, null);
            var settlementRequest = new ItemTradeSettlementRequest(
                initiator,
                partner,
                new ItemTradeSideState(
                    request.InitiatorCharacterId,
                    request.InitiatorEscrows,
                    request.InitiatorOfferedGold),
                new ItemTradeSideState(
                    request.PartnerCharacterId,
                    request.PartnerEscrows,
                    request.PartnerOfferedGold));
            if (!ItemTradePlanner.TryPlanSettlement(
                    settlementRequest,
                    itemCatalog,
                    statCatalog,
                    out var plan,
                    out var failure))
            {
                return new ItemTradeCommitResult(
                    false,
                    failure switch
                    {
                        ItemTradeFailure.SettlementNoSpace => ItemTradeCommitFailure.NoSpace,
                        ItemTradeFailure.SettlementWeightExceeded => ItemTradeCommitFailure.WeightExceeded,
                        ItemTradeFailure.SettlementGoldOverflow => ItemTradeCommitFailure.GoldOverflow,
                        ItemTradeFailure.SettlementGoldMissing => ItemTradeCommitFailure.GoldMissing,
                        ItemTradeFailure.SettlementEscrowMissing => ItemTradeCommitFailure.EscrowMissing,
                        _ => ItemTradeCommitFailure.EscrowMissing
                    },
                    Guid.Empty,
                    null,
                    null,
                    null);
            }

            var tradeId = Guid.NewGuid();
            if (!HasDungeonGoldCapacity(initiator, checked(initiator.Gold + plan.Initiator.GoldDelta))
                || !HasDungeonGoldCapacity(partner, checked(partner.Gold + plan.Partner.GoldDelta)))
                return new(false, ItemTradeCommitFailure.GoldOverflow, Guid.Empty, null, null, null);
            var previousInitiator = initiator;
            var previousPartner = partner;
            var updatedInitiator = ApplyTradePlan(initiator, plan.Initiator);
            var updatedPartner = ApplyTradePlan(partner, plan.Partner);
            initiatorAccount.Characters[initiatorIndex] = updatedInitiator;
            partnerAccount.Characters[partnerIndex] = updatedPartner;
            try
            {
                await SaveLockedAsync(cancellationToken);
            }
            catch
            {
                // 保存失败 = 结算没成功：把内存里的双方状态还原，并且不能留下未安装交付标记，
                // 否则两个角色会在进程余生里拒绝全部盲写 API，而没有任何人被告知交易成功。
                initiatorAccount.Characters[initiatorIndex] = previousInitiator;
                partnerAccount.Characters[partnerIndex] = previousPartner;
                throw;
            }

            // 设计 §2.5：结算成功落盘之后才给两侧打「未安装交付」标记。
            _pendingTradeDeliveries[request.InitiatorCharacterId] = tradeId;
            _pendingTradeDeliveries[request.PartnerCharacterId] = tradeId;
            logger.LogInformation(
                "Committed item trade {TradeId} between {Initiator} and {Partner}: gold {InitiatorGold:+0;-0;0}/{PartnerGold:+0;-0;0}, {InitiatorItems}/{PartnerItems} item(s).",
                tradeId,
                initiator.Name,
                partner.Name,
                plan.Initiator.GoldDelta,
                plan.Partner.GoldDelta,
                plan.Initiator.Deliveries.Count,
                plan.Partner.Deliveries.Count);
            return new ItemTradeCommitResult(
                true,
                ItemTradeCommitFailure.None,
                tradeId,
                updatedInitiator,
                updatedPartner,
                plan);
        }
        finally
        {
            _gate.Release();
        }
    }

    private CharacterRecord ApplyTradePlan(CharacterRecord character, ItemTradeSidePlan plan)
    {
        var inventory = (character.Inventory ?? []).ToDictionary(item => item.Slot);
        foreach (var removal in plan.Removals)
        {
            if (!inventory.TryGetValue(removal.SourceSlot, out var current)
                || current.InstanceId != removal.InstanceId)
            {
                // 规划阶段刚在同一把 _gate 内复核过（TryPlanSide 的托管复核），这里再对不上就说明
                // 不变量被破坏。静默 continue 会变成"一方加货、一方没扣"的复制，必须整笔失败。
                // 抛出点在两条记录写盘之前（内存的 Characters[] 赋值也在两次 ApplyTradePlan 之后），
                // 因此不会留下部分应用的状态。
                logger.LogError(
                    "Refused to apply a trade plan for character {CharacterId}: slot {Slot} no longer holds instance {InstanceId}.",
                    character.Id,
                    removal.SourceSlot,
                    removal.InstanceId);
                throw new InvalidOperationException(
                    $"Trade plan for character {character.Id} no longer matches inventory slot {removal.SourceSlot}.");
            }

            if (removal.RemainingCount == 0)
            {
                inventory.Remove(removal.SourceSlot);
            }
            else
            {
                inventory[removal.SourceSlot] = current with
                {
                    CountOrValue = removal.RemainingCount
                };
            }
        }

        foreach (var delivery in plan.Deliveries)
        {
            inventory[delivery.DestinationSlot] = delivery.Item;
        }

        return character with
        {
            Inventory = inventory.Values.OrderBy(item => item.Slot).ToList(),
            Gold = Math.Max(0, character.Gold + plan.GoldDelta)
        };
    }

    public async Task<CharacterRecord?> CommitCompoundItemAsync(
        string userName,
        Guid characterId,
        int expectedGold,
        IEnumerable<CharacterItemRecord> expectedInventory,
        int remainingGold,
        IEnumerable<CharacterItemRecord> plannedInventory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedInventory);
        ArgumentNullException.ThrowIfNull(plannedInventory);
        if (expectedGold < 0 || remainingGold < 0)
        {
            return null;
        }

        var expectedItems = expectedInventory
            .OrderBy(item => item.Slot)
            .ToArray();
        var plannedItems = plannedInventory
            .OrderBy(item => item.Slot)
            .ToArray();
        if (expectedItems.Select(item => item.Slot).Distinct().Count()
                != expectedItems.Length
            || plannedItems.Select(item => item.Slot).Distinct().Count()
                != plannedItems.Length)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return null;
            }

            var character = account.Characters[characterIndex];
            if (!HasDungeonGoldCapacity(character, remainingGold)) return null;
            var persistedItems = (character.Inventory ?? [])
                .OrderBy(item => item.Slot)
                .ToArray();
            if (character.Gold != expectedGold
                || !persistedItems.SequenceEqual(expectedItems))
            {
                logger.LogWarning(
                    "Rejected stale compound commit for character {CharacterId}: expectedGold={ExpectedGold}, persistedGold={PersistedGold}, expectedItems={ExpectedItemCount}, persistedItems={PersistedItemCount}.",
                    characterId,
                    expectedGold,
                    character.Gold,
                    expectedItems.Length,
                    persistedItems.Length);
                return null;
            }

            var updated = character with
            {
                Gold = remainingGold,
                Inventory = plannedItems.ToList()
            };
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> CommitItemAttributeModificationAsync(
        string userName,
        Guid characterId,
        IEnumerable<CharacterItemRecord> expectedInventory,
        IEnumerable<CharacterItemRecord> expectedEquipment,
        IEnumerable<CharacterItemRecord> plannedInventory,
        IEnumerable<CharacterItemRecord> plannedEquipment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedInventory);
        ArgumentNullException.ThrowIfNull(expectedEquipment);
        ArgumentNullException.ThrowIfNull(plannedInventory);
        ArgumentNullException.ThrowIfNull(plannedEquipment);

        var expectedMainItems = expectedInventory
            .OrderBy(item => item.Slot)
            .ToArray();
        var expectedEquippedItems = expectedEquipment
            .OrderBy(item => item.Slot)
            .ToArray();
        var plannedMainItems = plannedInventory
            .OrderBy(item => item.Slot)
            .ToArray();
        var plannedEquippedItems = plannedEquipment
            .OrderBy(item => item.Slot)
            .ToArray();
        if (HasDuplicateSlots(expectedMainItems)
            || HasDuplicateSlots(expectedEquippedItems)
            || HasDuplicateSlots(plannedMainItems)
            || HasDuplicateSlots(plannedEquippedItems))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return null;
            }

            var character = account.Characters[characterIndex];
            var persistedMainItems = (character.Inventory ?? [])
                .OrderBy(item => item.Slot)
                .ToArray();
            var persistedEquippedItems = (character.Equipment ?? [])
                .OrderBy(item => item.Slot)
                .ToArray();
            if (!persistedMainItems.SequenceEqual(expectedMainItems)
                || !persistedEquippedItems.SequenceEqual(expectedEquippedItems))
            {
                logger.LogWarning(
                    "Rejected stale item-attribute commit for character {CharacterId}: expectedMain={ExpectedMainCount}, persistedMain={PersistedMainCount}, expectedEquipment={ExpectedEquipmentCount}, persistedEquipment={PersistedEquipmentCount}.",
                    characterId,
                    expectedMainItems.Length,
                    persistedMainItems.Length,
                    expectedEquippedItems.Length,
                    persistedEquippedItems.Length);
                return null;
            }

            var updated = character with
            {
                Inventory = plannedMainItems.ToList(),
                Equipment = plannedEquippedItems.ToList()
            };
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool HasDuplicateSlots(
        IReadOnlyCollection<CharacterItemRecord> items) =>
        items.Select(item => item.Slot).Distinct().Count() != items.Count;

    public async Task<CharacterRecord?> SaveCharacterItemSpacesAsync(
        string userName,
        Guid characterId,
        int gold,
        IEnumerable<CharacterItemRecord> inventory,
        IEnumerable<CharacterItemRecord> equipment,
        IEnumerable<CharacterItemRecord> warehouse,
        CancellationToken cancellationToken = default,
        IEnumerable<CreateCharacterMailRequest>? generatedMails = null,
        IEnumerable<CharacterItemRecord>? avatarInventory = null,
        IEnumerable<CharacterItemRecord>? creatureInventory = null,
        ushort? warehouseCapacity = null,
        ushort? expectedWarehouseCapacity = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var character = account.Characters[index];
            if (!HasDungeonGoldCapacity(character, gold)) return null;
            if (!TryResolveWarehouseCapacityUpdate(
                    character,
                    warehouseCapacity,
                    expectedWarehouseCapacity,
                    out var nextWarehouseCapacity))
            {
                return null;
            }

            if (!TryAppendGeneratedMailsLocked(
                    character,
                    generatedMails,
                    out var mailbox,
                    out var createdMailCount,
                    out var generatedWarehouseCapacity))
            {
                return null;
            }

            nextWarehouseCapacity = Math.Max(
                nextWarehouseCapacity,
                generatedWarehouseCapacity);

            var updated = character with
            {
                Gold = Math.Max(0, gold),
                Inventory = inventory.OrderBy(item => item.Slot).ToList(),
                Equipment = equipment.OrderBy(item => item.Slot).ToList(),
                Warehouse = warehouse.OrderBy(item => item.Slot).ToList(),
                AvatarInventory = avatarInventory?.OrderBy(item => item.Slot).ToList()
                    ?? character.AvatarInventory,
                CreatureInventory = creatureInventory?.OrderBy(item => item.Slot).ToList()
                    ?? character.CreatureInventory,
                WarehouseCapacity = nextWarehouseCapacity,
                Mailbox = mailbox
            };
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[index] = updated;
            var snapshotCommitted = false;
            try
            {
                await SaveLockedAsync(cancellationToken, () => snapshotCommitted = true,
                    isolateCommittedDiagnostics: true);
            }
            catch
            {
                // Only File.Move commits the atomic replacement. A failed
                // temporary write must not leave an uncommitted inventory/gold
                // debit in memory. Never undo memory after replacement succeeded.
                if (!snapshotCommitted) account.Characters[index] = character;
                throw;
            }
            LogGeneratedMails(characterId, createdMailCount);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SaveCharacterCeraShopPurchaseAsync(
        string userName,
        Guid characterId,
        int cash,
        int gold,
        IEnumerable<CharacterItemRecord> inventory,
        IEnumerable<CharacterItemRecord> equipment,
        CancellationToken cancellationToken = default,
        IEnumerable<CreateCharacterMailRequest>? generatedMails = null,
        IEnumerable<CharacterItemRecord>? avatarInventory = null,
        IEnumerable<CharacterItemRecord>? creatureInventory = null,
        IEnumerable<PremiumServicePurchaseGrant>? premiumServiceGrants = null,
        uint? victoryPoints = null,
        int? expectedCash = null,
        ushort? warehouseCapacity = null,
        ushort? expectedWarehouseCapacity = null,
        uint? expectedVictoryPoints = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var accountIndex = _accounts.FindIndex(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (accountIndex < 0)
            {
                return null;
            }

            var account = _accounts[accountIndex];

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var grants = premiumServiceGrants?.ToArray() ?? [];
            if (!TryApplyPremiumServicePurchaseGrants(account, grants, out account))
            {
                return null;
            }

            var accountCash = Math.Max(0, account.Cash ?? DefaultAccountCash);
            if (expectedCash.HasValue && expectedCash.Value != accountCash)
            {
                logger.LogWarning(
                    "Rejected stale Cera wallet update for account {Account}: expected={ExpectedCash}, actual={ActualCash}.",
                    account.UserName,
                    expectedCash.Value,
                    accountCash);
                return null;
            }

            var character = account.Characters[index];
            if (!IsVictoryPointUpdateCurrent(character, victoryPoints, expectedVictoryPoints)) return null;
            if (!HasDungeonGoldCapacity(character, gold)) return null;
            if (!TryResolveWarehouseCapacityUpdate(
                    character,
                    warehouseCapacity,
                    expectedWarehouseCapacity,
                    out var nextWarehouseCapacity))
            {
                logger.LogWarning(
                    "Rejected warehouse-capacity update for character {CharacterId}: expected={ExpectedCapacity}, current={CurrentCapacity}, requested={RequestedCapacity}.",
                    characterId,
                    expectedWarehouseCapacity,
                    CharacterWarehouseProgression.Normalize(character.WarehouseCapacity),
                    warehouseCapacity);
                return null;
            }

            if (!TryAppendGeneratedMailsLocked(
                    character,
                    generatedMails,
                    out var mailbox,
                    out var createdMailCount,
                    out var generatedWarehouseCapacity))
            {
                return null;
            }

            nextWarehouseCapacity = Math.Max(
                nextWarehouseCapacity,
                generatedWarehouseCapacity);

            var updated = character with
            {
                Cash = Math.Max(0, cash),
                Gold = Math.Max(0, gold),
                VictoryPoints = victoryPoints ?? character.VictoryPoints,
                Inventory = inventory.OrderBy(item => item.Slot).ToList(),
                Equipment = equipment.OrderBy(item => item.Slot).ToList(),
                AvatarInventory = avatarInventory?.OrderBy(item => item.Slot).ToList()
                    ?? character.AvatarInventory,
                CreatureInventory = creatureInventory?.OrderBy(item => item.Slot).ToList()
                    ?? character.CreatureInventory,
                WarehouseCapacity = nextWarehouseCapacity,
                Mailbox = mailbox
            };
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[index] = updated;
            account = SetAccountCash(account, updated.Cash);
            updated = account.Characters[index];
            _accounts[accountIndex] = account;
            await SaveLockedAsync(cancellationToken);
            LogGeneratedMails(characterId, createdMailCount);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool TryApplyPremiumServicePurchaseGrants(
        AccountRecord account,
        IReadOnlyCollection<PremiumServicePurchaseGrant> grants,
        out AccountRecord updatedAccount)
    {
        updatedAccount = account;
        if (grants.Count == 0)
        {
            return true;
        }

        var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        foreach (var grantGroup in grants.GroupBy(grant => grant.ServiceType))
        {
            if (grantGroup.Key is not (GameProtocolEngine.PrivateStoreServiceType or GameProtocolEngine.OverlordContractServiceType
                    or GameProtocolEngine.MasterContractServiceType)
                || grantGroup.Any(grant => grant.Days <= 0))
            {
                return false;
            }

            var totalDays = grantGroup.Sum(grant => (long)grant.Days);
            var extensionSeconds = Math.Min(
                (long)uint.MaxValue,
                totalDays * (long)TimeSpan.FromDays(1).TotalSeconds);
            var currentExpiry = GetServiceExpiry(updatedAccount, grantGroup.Key);
            var start = currentExpiry is > 0 && currentExpiry.Value > now
                ? currentExpiry.Value
                : now;
            var expiry = start >= uint.MaxValue - extensionSeconds
                ? uint.MaxValue
                : checked((uint)(start + extensionSeconds));
            updatedAccount = SetPremiumServiceExpiry(
                updatedAccount,
                grantGroup.Key,
                expiry);
        }

        return true;
    }

    public async Task<CharacterRecord?> SaveCharacterSkillsAsync(
        string userName,
        Guid characterId,
        int remainingSp,
        IEnumerable<CharacterSkillRecord> skills,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var original = account.Characters[index];
            var updated = original with
            {
                Sp = Math.Max(0, remainingSp),
                Skills = skills
                    .Where(skill => skill.Level > 0)
                    .OrderBy(skill => skill.Slot)
                    .ThenBy(skill => skill.SkillId)
                    .ToList()
            };
            account.Characters[index] = updated;
            var snapshotCommitted = false;
            try
            {
                await SaveLockedAsync(cancellationToken, () => snapshotCommitted = true,
                    isolateCommittedDiagnostics: true);
            }
            catch
            {
                // A failed write/replace must not install a rejected layout in
                // the store cache, where an unrelated later save could revive it.
                if (!snapshotCommitted) account.Characters[index] = original;
                throw;
            }
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IncreaseStatusItemUseResult> UseIncreaseStatusItemAsync(
        string userName,
        Guid characterId,
        ushort sourceSlot,
        ushort expectedItemId,
        IncreaseStatusEffect effect,
        CancellationToken cancellationToken = default)
    {
        if (expectedItemId == 0
            || effect is null
            || effect.Amount <= 0
            || !IsSupportedIncreaseStatusType(effect.Type))
        {
            return new IncreaseStatusItemUseResult(
                false,
                null,
                0,
                IncreaseStatusItemUseFailure.InvalidRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return new IncreaseStatusItemUseResult(
                    false,
                    null,
                    0,
                    IncreaseStatusItemUseFailure.ItemStateChanged);
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return new IncreaseStatusItemUseResult(
                    false,
                    null,
                    0,
                    IncreaseStatusItemUseFailure.CharacterNotFound);
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return new IncreaseStatusItemUseResult(
                    false,
                    null,
                    0,
                    IncreaseStatusItemUseFailure.CharacterNotFound);
            }

            var character = account.Characters[characterIndex];
            var inventory = (character.Inventory ?? []).ToList();
            var itemIndex = inventory.FindIndex(item => item.Slot == sourceSlot);
            if (itemIndex < 0
                || inventory[itemIndex].ItemId != expectedItemId
                || inventory[itemIndex].CountOrValue == 0)
            {
                return new IncreaseStatusItemUseResult(
                    false,
                    character,
                    0,
                    IncreaseStatusItemUseFailure.ItemStateChanged);
            }

            var remainingItemCount = inventory[itemIndex].CountOrValue - 1;
            if (remainingItemCount == 0)
            {
                inventory.RemoveAt(itemIndex);
            }
            else
            {
                inventory[itemIndex] = inventory[itemIndex] with
                {
                    CountOrValue = remainingItemCount
                };
            }

            var updated = ApplyIncreaseStatus(character, effect) with
            {
                Inventory = inventory.OrderBy(item => item.Slot).ToList()
            };
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return new IncreaseStatusItemUseResult(
                true,
                updated,
                remainingItemCount,
                IncreaseStatusItemUseFailure.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExperienceBookItemUseResult> UseExperienceBookItemAsync(
        string userName,
        Guid characterId,
        ushort sourceSlot,
        ushort expectedItemId,
        uint experienceAmount,
        CharacterExperienceCatalog characterExperienceCatalog,
        CharacterSpCatalog spCatalog,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || expectedItemId == 0
            || experienceAmount == 0
            || characterExperienceCatalog is null
            || spCatalog is null)
        {
            return new ExperienceBookItemUseResult(
                false,
                null,
                0,
                null,
                0,
                ExperienceBookItemUseFailure.InvalidRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return new ExperienceBookItemUseResult(
                    false,
                    null,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.ItemStateChanged);
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    null,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.CharacterNotFound);
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    null,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.CharacterNotFound);
            }

            var character = account.Characters[characterIndex];
            var inventory = (character.Inventory ?? []).ToList();
            var itemIndex = inventory.FindIndex(item => item.Slot == sourceSlot);
            if (itemIndex < 0
                || inventory[itemIndex].ItemId != expectedItemId
                || inventory[itemIndex].CountOrValue == 0)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    character,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.ItemStateChanged);
            }

            var progression = characterExperienceCatalog.Grant(
                character,
                experienceAmount);
            var grantedSkillPoints = spCatalog.CalculateProgressiveSpGrant(
                progression,
                characterExperienceCatalog.ExperienceThresholds);
            var remainingItemCount = inventory[itemIndex].CountOrValue - 1;
            if (remainingItemCount == 0)
            {
                inventory.RemoveAt(itemIndex);
            }
            else
            {
                inventory[itemIndex] = inventory[itemIndex] with
                {
                    CountOrValue = remainingItemCount
                };
            }

            var updated = character with
            {
                Level = progression.Level,
                Experience = progression.Experience,
                Sp = SaturatingAdd(character.Sp, grantedSkillPoints),
                Inventory = inventory.OrderBy(item => item.Slot).ToList()
            };
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return new ExperienceBookItemUseResult(
                true,
                updated,
                remainingItemCount,
                progression,
                grantedSkillPoints,
                ExperienceBookItemUseFailure.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExperienceBookItemUseResult> UseLevelUpCouponItemAsync(
        string userName,
        Guid characterId,
        ushort sourceSlot,
        ushort expectedItemId,
        CharacterExperienceCatalog characterExperienceCatalog,
        CharacterSpCatalog spCatalog,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || expectedItemId == 0
            || characterExperienceCatalog is null
            || spCatalog is null)
        {
            return new ExperienceBookItemUseResult(
                false,
                null,
                0,
                null,
                0,
                ExperienceBookItemUseFailure.InvalidRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return new ExperienceBookItemUseResult(
                    false,
                    null,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.ItemStateChanged);
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    null,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.CharacterNotFound);
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    null,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.CharacterNotFound);
            }

            var character = account.Characters[characterIndex];
            var inventory = (character.Inventory ?? []).ToList();
            var itemIndex = inventory.FindIndex(item => item.Slot == sourceSlot);
            if (itemIndex < 0
                || inventory[itemIndex].ItemId != expectedItemId
                || inventory[itemIndex].CountOrValue == 0)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    character,
                    0,
                    null,
                    0,
                    ExperienceBookItemUseFailure.ItemStateChanged);
            }

            var currentLevel = Math.Clamp(
                character.Level,
                0,
                CharacterExperienceCatalog.MaximumLevel);
            if (currentLevel >= CharacterExperienceCatalog.MaximumLevel)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    character,
                    inventory[itemIndex].CountOrValue,
                    null,
                    0,
                    ExperienceBookItemUseFailure.LevelLimitReached);
            }

            var nextLevelExperience = characterExperienceCatalog
                .GetMinimumCumulativeExperience(currentLevel + 1);
            if (nextLevelExperience <= character.Experience)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    character,
                    inventory[itemIndex].CountOrValue,
                    null,
                    0,
                    ExperienceBookItemUseFailure.InvalidRequest);
            }

            var experienceAmount = nextLevelExperience - character.Experience;
            var progression = characterExperienceCatalog.Grant(
                character,
                experienceAmount);
            if (progression.Level != currentLevel + 1)
            {
                return new ExperienceBookItemUseResult(
                    false,
                    character,
                    inventory[itemIndex].CountOrValue,
                    null,
                    0,
                    ExperienceBookItemUseFailure.InvalidRequest);
            }

            var grantedSkillPoints = spCatalog.CalculateProgressiveSpGrant(
                progression,
                characterExperienceCatalog.ExperienceThresholds);
            var remainingItemCount = inventory[itemIndex].CountOrValue - 1;
            if (remainingItemCount == 0)
            {
                inventory.RemoveAt(itemIndex);
            }
            else
            {
                inventory[itemIndex] = inventory[itemIndex] with
                {
                    CountOrValue = remainingItemCount
                };
            }

            var updated = character with
            {
                Level = progression.Level,
                Experience = progression.Experience,
                Sp = SaturatingAdd(character.Sp, grantedSkillPoints),
                Inventory = inventory.OrderBy(item => item.Slot).ToList()
            };
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return new ExperienceBookItemUseResult(
                true,
                updated,
                remainingItemCount,
                progression,
                grantedSkillPoints,
                ExperienceBookItemUseFailure.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PvpExperienceItemUseResult> UsePvpExperienceItemAsync(
        string userName,
        Guid characterId,
        ushort sourceSlot,
        ushort expectedItemId,
        uint experienceAmount,
        PvpExperienceCatalog pvpCatalog,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || expectedItemId == 0
            || experienceAmount == 0
            || pvpCatalog is null
            || !pvpCatalog.TryGetBookExperience(expectedItemId, out var expectedAmount)
            || expectedAmount != experienceAmount)
        {
            return new PvpExperienceItemUseResult(
                false,
                null,
                0,
                0,
                0,
                0,
                0,
                PvpExperienceItemUseFailure.InvalidRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return new PvpExperienceItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    0,
                    0,
                    0,
                    PvpExperienceItemUseFailure.ItemStateChanged);
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return new PvpExperienceItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    0,
                    0,
                    0,
                    PvpExperienceItemUseFailure.CharacterNotFound);
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return new PvpExperienceItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    0,
                    0,
                    0,
                    PvpExperienceItemUseFailure.CharacterNotFound);
            }

            var character = account.Characters[characterIndex];
            var persistedPoints = Math.Max(0, character.PvpPoints);
            var inventory = (character.Inventory ?? []).ToList();
            var itemIndex = inventory.FindIndex(item => item.Slot == sourceSlot);
            if (itemIndex < 0
                || inventory[itemIndex].ItemId != expectedItemId
                || inventory[itemIndex].CountOrValue == 0)
            {
                return new PvpExperienceItemUseResult(
                    false,
                    character,
                    0,
                    0,
                    persistedPoints,
                    persistedPoints,
                    pvpCatalog.GetGradeForPoints(persistedPoints),
                    PvpExperienceItemUseFailure.ItemStateChanged);
            }

            var previousPoints = persistedPoints;
            var grantedExperience = checked((int)Math.Min(
                int.MaxValue,
                experienceAmount));
            var points = SaturatingAdd(previousPoints, grantedExperience);
            var grade = pvpCatalog.GetGradeForPoints(points);
            var remainingItemCount = inventory[itemIndex].CountOrValue - 1;
            if (remainingItemCount == 0)
            {
                inventory.RemoveAt(itemIndex);
            }
            else
            {
                inventory[itemIndex] = inventory[itemIndex] with
                {
                    CountOrValue = remainingItemCount
                };
            }

            // api_update_pvp_result in the 13339 reference increments the
            // result counters together with the book's PvP-point update.
            var updated = character with
            {
                PvpPoints = points,
                PvpGrade = grade,
                PvpWins = SaturatingAdd(character.PvpWins, 1),
                PvpPlayCount = SaturatingAdd(character.PvpPlayCount, 1),
                PvpCount = SaturatingAdd(character.PvpCount, 1),
                Inventory = inventory.OrderBy(item => item.Slot).ToList()
            };
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return new PvpExperienceItemUseResult(
                true,
                updated,
                remainingItemCount,
                grantedExperience,
                previousPoints,
                points,
                grade,
                PvpExperienceItemUseFailure.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SkillResetItemUseResult> UseSkillResetItemAsync(
        string userName,
        Guid characterId,
        ushort sourceSlot,
        ushort expectedItemId,
        int accumulatedLevelSkillPoints,
        IReadOnlyList<CharacterSkillRecord> resetSkills,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || expectedItemId == 0
            || accumulatedLevelSkillPoints < 0
            || resetSkills is null)
        {
            return new SkillResetItemUseResult(
                false,
                null,
                0,
                0,
                SkillResetItemUseFailure.InvalidRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return new SkillResetItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    SkillResetItemUseFailure.ItemStateChanged);
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return new SkillResetItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    SkillResetItemUseFailure.CharacterNotFound);
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return new SkillResetItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    SkillResetItemUseFailure.CharacterNotFound);
            }

            var character = account.Characters[characterIndex];
            var inventory = (character.Inventory ?? []).ToList();
            var itemIndex = inventory.FindIndex(item => item.Slot == sourceSlot);
            if (itemIndex < 0
                || inventory[itemIndex].ItemId != expectedItemId
                || inventory[itemIndex].CountOrValue == 0)
            {
                return new SkillResetItemUseResult(
                    false,
                    character,
                    0,
                    0,
                    SkillResetItemUseFailure.ItemStateChanged);
            }

            int resetSkillPoints;
            try
            {
                resetSkillPoints = checked(
                    accumulatedLevelSkillPoints + Math.Max(0, character.BonusSp));
            }
            catch (OverflowException)
            {
                return new SkillResetItemUseResult(
                    false,
                    character,
                    inventory[itemIndex].CountOrValue,
                    0,
                    SkillResetItemUseFailure.SkillPointOverflow);
            }

            if (resetSkillPoints > ushort.MaxValue)
            {
                return new SkillResetItemUseResult(
                    false,
                    character,
                    inventory[itemIndex].CountOrValue,
                    0,
                    SkillResetItemUseFailure.SkillPointOverflow);
            }

            var remainingItemCount = inventory[itemIndex].CountOrValue - 1;
            if (remainingItemCount == 0)
            {
                inventory.RemoveAt(itemIndex);
            }
            else
            {
                inventory[itemIndex] = inventory[itemIndex] with
                {
                    CountOrValue = remainingItemCount
                };
            }

            var updated = character with
            {
                Sp = resetSkillPoints,
                Skills = resetSkills
                    .Where(skill => skill.Level > 0)
                    .OrderBy(skill => skill.Slot)
                    .ThenBy(skill => skill.SkillId)
                    .ToList(),
                Inventory = inventory.OrderBy(item => item.Slot).ToList()
            };
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return new SkillResetItemUseResult(
                true,
                updated,
                remainingItemCount,
                resetSkillPoints,
                SkillResetItemUseFailure.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FatigueRecoveryItemUseResult> UseFatigueRecoveryItemAsync(
        string userName,
        Guid characterId,
        ushort sourceSlot,
        ushort expectedItemId,
        ushort maximumFatigue,
        ushort maximumRemainingFatigue,
        ushort recoveryAmount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || expectedItemId == 0
            || maximumFatigue == 0
            || maximumRemainingFatigue > maximumFatigue
            || recoveryAmount == 0)
        {
            return new FatigueRecoveryItemUseResult(
                false,
                null,
                0,
                0,
                0,
                FatigueRecoveryItemUseFailure.InvalidRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return new FatigueRecoveryItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    0,
                    FatigueRecoveryItemUseFailure.ItemStateChanged);
            }

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return new FatigueRecoveryItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    0,
                    FatigueRecoveryItemUseFailure.CharacterNotFound);
            }

            var characterIndex = account.Characters.FindIndex(character =>
                character.Id == characterId);
            if (characterIndex < 0)
            {
                return new FatigueRecoveryItemUseResult(
                    false,
                    null,
                    0,
                    0,
                    0,
                    FatigueRecoveryItemUseFailure.CharacterNotFound);
            }

            var character = account.Characters[characterIndex];
            var inventory = (character.Inventory ?? []).ToList();
            var itemIndex = inventory.FindIndex(item => item.Slot == sourceSlot);
            if (itemIndex < 0
                || inventory[itemIndex].ItemId != expectedItemId
                || inventory[itemIndex].CountOrValue == 0)
            {
                return new FatigueRecoveryItemUseResult(
                    false,
                    character,
                    0,
                    Math.Min(character.UsedFatigue, maximumFatigue),
                    Math.Min(character.UsedFatigue, maximumFatigue),
                    FatigueRecoveryItemUseFailure.ItemStateChanged);
            }

            var previousUsedFatigue = Math.Min(
                character.UsedFatigue,
                maximumFatigue);
            var remainingFatigue = maximumFatigue - previousUsedFatigue;
            if (remainingFatigue >= maximumRemainingFatigue)
            {
                return new FatigueRecoveryItemUseResult(
                    false,
                    character,
                    inventory[itemIndex].CountOrValue,
                    previousUsedFatigue,
                    previousUsedFatigue,
                    FatigueRecoveryItemUseFailure.FatigueNotLowEnough);
            }

            var remainingItemCount = inventory[itemIndex].CountOrValue - 1;
            if (remainingItemCount == 0)
            {
                inventory.RemoveAt(itemIndex);
            }
            else
            {
                inventory[itemIndex] = inventory[itemIndex] with
                {
                    CountOrValue = remainingItemCount
                };
            }

            var usedFatigue = checked((ushort)Math.Max(
                0,
                previousUsedFatigue - recoveryAmount));
            var updated = character with
            {
                UsedFatigue = usedFatigue,
                Inventory = inventory.OrderBy(item => item.Slot).ToList()
            };
            account.Characters[characterIndex] = updated;
            await SaveLockedAsync(cancellationToken);
            return new FatigueRecoveryItemUseResult(
                true,
                updated,
                remainingItemCount,
                previousUsedFatigue,
                usedFatigue,
                FatigueRecoveryItemUseFailure.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsSupportedIncreaseStatusType(IncreaseStatusType type) => type is
        IncreaseStatusType.SkillPoints
        or IncreaseStatusType.MaximumHp
        or IncreaseStatusType.MaximumMp
        or IncreaseStatusType.Strength
        or IncreaseStatusType.Vitality
        or IncreaseStatusType.Intelligence
        or IncreaseStatusType.Spirit
        or IncreaseStatusType.MovementSpeed
        or IncreaseStatusType.AllElementResistance;

    private static CharacterRecord ApplyIncreaseStatus(
        CharacterRecord character,
        IncreaseStatusEffect effect) => effect.Type switch
        {
            IncreaseStatusType.SkillPoints => character with
            {
                Sp = SaturatingAdd(character.Sp, effect.Amount),
                BonusSp = SaturatingAdd(character.BonusSp, effect.Amount)
            },
            IncreaseStatusType.MaximumHp => character with
            {
                BonusMaximumHp = SaturatingAdd(character.BonusMaximumHp, effect.Amount)
            },
            IncreaseStatusType.MaximumMp => character with
            {
                BonusMaximumMp = SaturatingAdd(character.BonusMaximumMp, effect.Amount)
            },
            IncreaseStatusType.Strength => character with
            {
                BonusStrength = SaturatingAdd(character.BonusStrength, effect.Amount)
            },
            IncreaseStatusType.Vitality => character with
            {
                BonusVitality = SaturatingAdd(character.BonusVitality, effect.Amount)
            },
            IncreaseStatusType.Intelligence => character with
            {
                BonusIntelligence = SaturatingAdd(character.BonusIntelligence, effect.Amount)
            },
            IncreaseStatusType.Spirit => character with
            {
                BonusSpirit = SaturatingAdd(character.BonusSpirit, effect.Amount)
            },
            IncreaseStatusType.MovementSpeed => character with
            {
                BonusMovementSpeed = SaturatingAdd(
                    character.BonusMovementSpeed,
                    effect.Amount)
            },
            IncreaseStatusType.AllElementResistance => character with
            {
                BonusAllElementResistance = SaturatingAdd(
                    character.BonusAllElementResistance,
                    effect.Amount)
            },
            _ => character
        };

    private static int SaturatingAdd(int current, int amount) =>
        checked((int)Math.Min(
            int.MaxValue,
            (long)Math.Max(0, current) + Math.Max(0, amount)));

    /// <summary>
    /// Repairs persisted ranks written by older builds that treated a PVF
    /// threshold as an inclusive upper bound.  PvP points remain the source
    /// of truth; this only updates the derived rank byte.
    /// </summary>
    public async Task<int> NormalizePvpGradesAsync(
        PvpExperienceCatalog pvpCatalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pvpCatalog);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var changed = 0;
            foreach (var account in _accounts)
            {
                for (var index = 0; index < account.Characters.Count; index++)
                {
                    var character = account.Characters[index];
                    var expectedGrade = pvpCatalog.GetGradeForPoints(
                        character.PvpPoints);
                    if (character.PvpGrade == expectedGrade)
                    {
                        continue;
                    }

                    account.Characters[index] = character with
                    {
                        PvpGrade = expectedGrade
                    };
                    changed++;
                }
            }

            if (changed != 0)
            {
                await SaveLockedAsync(cancellationToken);
            }

            return changed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SaveCharacterQuestsAsync(
        string userName,
        Guid characterId,
        IEnumerable<CharacterQuestRecord> quests,
        IEnumerable<ushort> completedQuestIds,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var updated = account.Characters[index] with
            {
                Quests = quests
                    .Where(quest => quest.QuestId != ushort.MaxValue)
                    .DistinctBy(quest => quest.QuestId)
                    .Take(3)
                    .ToList(),
                CompletedQuestIds = completedQuestIds
                    .Distinct()
                    .OrderBy(questId => questId)
                    .ToList()
            };
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> CompleteCharacterTutorialAsync(
        string userName,
        Guid characterId,
        CharacterQuestRecord? tutorialQuest = null,
        int? level = null,
        uint? experience = null,
        int? skillPoints = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var character = account.Characters[index];
            var quests = (character.Quests ?? [])
                .Where(quest => quest.QuestId != ushort.MaxValue)
                .DistinctBy(quest => quest.QuestId)
                .Take(3)
                .ToList();
            var canAddTutorialQuest = tutorialQuest is not null
                && tutorialQuest.QuestId == QuestCatalog.TutorialCompletionQuestId
                && quests.Count < 3
                && quests.All(quest => quest.QuestId != tutorialQuest.QuestId)
                && !(character.CompletedQuestIds ?? []).Contains(tutorialQuest.QuestId);
            if (canAddTutorialQuest)
            {
                quests.Add(tutorialQuest!);
            }

            if (character.TutorialStarted && !canAddTutorialQuest)
            {
                return character;
            }

            var updated = character with
            {
                TutorialStarted = true,
                Quests = quests,
                Level = level.HasValue
                    ? Math.Clamp(level.Value, 1, CharacterExperienceCatalog.MaximumLevel)
                    : character.Level,
                Experience = experience ?? character.Experience,
                Sp = skillPoints.HasValue
                    ? Math.Max(0, skillPoints.Value)
                    : character.Sp
            };
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SaveCharacterQuestProgressAsync(
        string userName,
        Guid characterId,
        IEnumerable<CharacterItemRecord> inventory,
        IEnumerable<CharacterQuestRecord> quests,
        IEnumerable<ushort> completedQuestIds,
        CancellationToken cancellationToken = default,
        ushort? warehouseCapacity = null,
        ushort? expectedWarehouseCapacity = null,
        IEnumerable<ushort>? unlockedDungeonIds = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var character = account.Characters[index];
            if (!TryResolveWarehouseCapacityUpdate(
                    character,
                    warehouseCapacity,
                    expectedWarehouseCapacity,
                    out var nextWarehouseCapacity))
            {
                return null;
            }

            var updated = character with
            {
                Inventory = inventory.OrderBy(item => item.Slot).ToList(),
                WarehouseCapacity = nextWarehouseCapacity,
                Quests = quests
                    .Where(quest => quest.QuestId != ushort.MaxValue)
                    .DistinctBy(quest => quest.QuestId)
                    .Take(3)
                    .ToList(),
                CompletedQuestIds = completedQuestIds
                    .Distinct()
                    .OrderBy(questId => questId)
                    .ToList()
            };
            if (unlockedDungeonIds is not null)
            {
                updated = HiddenDungeonUnlockCatalog.ApplyUnlocks(updated, unlockedDungeonIds);
            }
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SaveCharacterProgressionAsync(
        string userName,
        Guid characterId,
        int level,
        uint experience,
        int skillPoints,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var updated = account.Characters[index] with
            {
                Level = Math.Clamp(level, 1, CharacterExperienceCatalog.MaximumLevel),
                Experience = experience,
                Sp = Math.Max(0, skillPoints)
            };
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SaveCharacterDungeonUnlocksAsync(
        string userName,
        Guid characterId,
        IEnumerable<ushort> unlockedDungeonIds,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var normalizedUnlocks = unlockedDungeonIds
                .Where(dungeonId => dungeonId != 0)
                .Distinct()
                .OrderBy(dungeonId => dungeonId)
                .ToArray();
            var dungeonProgress = DungeonDifficultyProgression.Normalize(
                (account.Characters[index].DungeonProgress ?? [])
                    .Concat(normalizedUnlocks.Select(dungeonId =>
                        new CharacterDungeonProgressRecord(dungeonId, 0))));
            var updated = account.Characters[index] with
            {
                UnlockedDungeonIds = normalizedUnlocks.ToList(),
                DungeonProgress = dungeonProgress.ToList()
            };
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SaveCharacterDungeonProgressAsync(
        string userName,
        Guid characterId,
        IEnumerable<CharacterDungeonProgressRecord> dungeonProgress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var updated = account.Characters[index] with
            {
                DungeonProgress = DungeonDifficultyProgression.Normalize(dungeonProgress)
                    .ToList()
            };
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> UnlockNextDungeonDifficultyAsync(
        string userName,
        Guid characterId,
        ushort dungeonId,
        byte clearedDifficulty,
        CancellationToken cancellationToken = default)
    {
        if (dungeonId == 0)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.UserName,
                    userName,
                    StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var character = account.Characters[index];
            var currentMaximum = DungeonDifficultyProgression.GetMaximumDifficulty(
                character.DungeonProgress,
                dungeonId);
            var requestedMaximum = DungeonDifficultyProgression.NormalizeDifficulty(
                clearedDifficulty + 1);
            var maximumDifficulty = Math.Max(currentMaximum, requestedMaximum);
            var progress = DungeonDifficultyProgression.Normalize(
                (character.DungeonProgress ?? [])
                    .Where(entry => entry.DungeonId != dungeonId)
                    .Append(new CharacterDungeonProgressRecord(
                        dungeonId,
                        maximumDifficulty)));
            var updated = character with { DungeonProgress = progress.ToList() };
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SetCharacterUsedFatigueAsync(
        Guid characterId,
        ushort usedFatigue,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var updated = account.Characters[characterIndex] with
                {
                    UsedFatigue = usedFatigue
                };
                account.Characters[characterIndex] = updated;
                await SaveLockedAsync(cancellationToken);
                return updated;
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SetCharacterDungeonDifficultyAsync(
        Guid characterId,
        ushort dungeonId,
        byte maximumDifficulty,
        CancellationToken cancellationToken = default)
    {
        if (dungeonId == 0
            || maximumDifficulty > DungeonDifficultyProgression.HighestDifficulty)
        {
            throw new ArgumentOutOfRangeException(
                dungeonId == 0 ? nameof(dungeonId) : nameof(maximumDifficulty));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var character = account.Characters[characterIndex];
                var unlocks = (character.UnlockedDungeonIds ?? [])
                    .Append(dungeonId)
                    .Where(id => id != 0)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
                var progress = DungeonDifficultyProgression.Normalize(
                        (character.DungeonProgress ?? [])
                        .Where(entry => entry.DungeonId != dungeonId)
                        .Append(new CharacterDungeonProgressRecord(
                            dungeonId,
                            maximumDifficulty)))
                    .ToList();
                var updated = character with
                {
                    UnlockedDungeonIds = unlocks,
                    DungeonProgress = progress
                };
                account.Characters[characterIndex] = updated;
                await SaveLockedAsync(cancellationToken);
                return updated;
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> SetCharacterWeaknessRecoveryAsync(
        Guid characterId,
        byte recovery,
        CancellationToken cancellationToken = default)
    {
        if (recovery > DungeonWeaknessPolicy.FullStamina)
        {
            throw new ArgumentOutOfRangeException(nameof(recovery));
        }

        recovery = Math.Max(recovery, DungeonWeaknessPolicy.MinimumRecovery);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var updated = account.Characters[characterIndex] with
                {
                    WeaknessRecovery = recovery,
                    LegacyIsWeakened = null
                };
                account.Characters[characterIndex] = updated;
                await SaveLockedAsync(cancellationToken);
                return updated;
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterWeaknessRecoveryUpdate[]>
        AdvanceCharacterWeaknessRecoveryAsync(
            DateTimeOffset utcNow,
            CancellationToken cancellationToken = default)
    {
        var now = checked((uint)utcNow.ToUnixTimeSeconds());
        var updates = new List<CharacterWeaknessRecoveryUpdate>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in _accounts)
            {
                var hasBlackDiamond = (account.PremiumServices ?? []).Any(service =>
                    service.ServiceType == GameProtocolEngine.BlackDiamondServiceType
                    && service.ExpiresAt is { } expiry
                    && expiry > now);
                for (var characterIndex = 0;
                     characterIndex < account.Characters.Count;
                     characterIndex++)
                {
                    var character = account.Characters[characterIndex];
                    if (!character.IsWeakened)
                    {
                        continue;
                    }

                    var recovery = hasBlackDiamond
                        ? DungeonWeaknessPolicy.FullStamina
                        : (byte)Math.Min(
                            DungeonWeaknessPolicy.FullStamina,
                            Math.Max(
                                character.WeaknessRecovery,
                                DungeonWeaknessPolicy.MinimumRecovery)
                                + DungeonWeaknessPolicy.RecoveryPerMinute);
                    var updated = character with
                    {
                        WeaknessRecovery = recovery,
                        LegacyIsWeakened = null
                    };
                    account.Characters[characterIndex] = updated;
                    updates.Add(new CharacterWeaknessRecoveryUpdate(
                        updated.Id,
                        recovery));
                }
            }

            if (updates.Count > 0)
            {
                await SaveLockedAsync(cancellationToken);
            }

            return updates.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WeaknessRecoveryResult> RecoverCharacterWeaknessAsync(
        Guid characterId,
        int recoveryCost,
        CancellationToken cancellationToken = default)
    {
        if (recoveryCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recoveryCost));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var character = account.Characters[characterIndex];
                if (IsEconomicWriteBlocked(characterId))
                    return new(false, character, recoveryCost, WeaknessRecoveryFailure.Busy);
                if (!character.IsWeakened)
                {
                    return new(
                        false,
                        character,
                        recoveryCost,
                        WeaknessRecoveryFailure.NotWeakened);
                }

                if (character.Gold < recoveryCost)
                {
                    return new(
                        false,
                        character,
                        recoveryCost,
                        WeaknessRecoveryFailure.InsufficientGold);
                }

                var updated = character with
                {
                    Gold = character.Gold - recoveryCost,
                    WeaknessRecovery = DungeonWeaknessPolicy.FullStamina,
                    LegacyIsWeakened = null
                };
                account.Characters[characterIndex] = updated;
                await SaveLockedAsync(cancellationToken);
                return new(
                    true,
                    updated,
                    recoveryCost,
                    WeaknessRecoveryFailure.None);
            }

            return new(
                false,
                null,
                recoveryCost,
                WeaknessRecoveryFailure.CharacterNotFound);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DungeonRoomFatigueConsumptionResult>
        ConsumeDungeonRoomFatigueAsync(
            Guid characterId,
            ushort maximumFatigue,
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in _accounts)
            {
                var characterIndex = account.Characters.FindIndex(character =>
                    character.Id == characterId);
                if (characterIndex < 0)
                {
                    continue;
                }

                var character = account.Characters[characterIndex];
                var previousUsedFatigue = character.UsedFatigue;
                if (previousUsedFatigue >= maximumFatigue)
                {
                    return new DungeonRoomFatigueConsumptionResult(
                        character,
                        Consumed: false,
                        previousUsedFatigue,
                        previousUsedFatigue);
                }

                var usedFatigue = checked((ushort)Math.Min(
                    maximumFatigue,
                    previousUsedFatigue + 1));
                var updated = character with { UsedFatigue = usedFatigue };
                account.Characters[characterIndex] = updated;
                await SaveLockedAsync(cancellationToken);
                return new DungeonRoomFatigueConsumptionResult(
                    updated,
                    Consumed: true,
                    previousUsedFatigue,
                    usedFatigue);
            }

            return new DungeonRoomFatigueConsumptionResult(
                Character: null,
                Consumed: false,
                PreviousUsedFatigue: 0,
                UsedFatigue: 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FatigueResetResult> ResetFatigueForCurrentDayAsync(
        DateTimeOffset localNow,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!ResetFatigueForNewDayLocked(localNow, out var characterIds))
            {
                return new FatigueResetResult(
                    false,
                    0,
                    [],
                    _fatigueDayKey);
            }

            await SaveLockedAsync(cancellationToken);
            logger.LogInformation(
                "Advanced fatigue day to {FatigueDayKey} at local time {LocalTime} and reset {CharacterCount} character(s).",
                _fatigueDayKey,
                localNow,
                characterIds.Length);
            return new FatigueResetResult(
                true,
                characterIds.Length,
                characterIds,
                _fatigueDayKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static int GetFatigueDayKey(DateTimeOffset localNow)
    {
        var fatigueDate = localNow.TimeOfDay < TimeSpan.FromHours(6)
            ? localNow.Date.AddDays(-1)
            : localNow.Date;
        return checked(
            fatigueDate.Year * 10_000
            + fatigueDate.Month * 100
            + fatigueDate.Day);
    }

    public async Task<CharacterRecord?> SaveCharacterLocationAsync(
        string userName,
        Guid characterId,
        byte townId,
        byte areaId,
        short x,
        short y,
        byte direction,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(account =>
                string.Equals(account.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var updated = account.Characters[index] with
            {
                TownId = townId is >= 1 and <= 5 ? townId : (byte)1,
                AreaId = areaId,
                X = x,
                Y = y,
                Direction = direction <= 7 ? direction : (byte)5
            };
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterRecord?> CompleteCharacterQuestAsync(
        string userName,
        Guid characterId,
        ushort questId,
        int level,
        uint experience,
        int skillPoints,
        int gold,
        IEnumerable<CharacterItemRecord> inventory,
        IEnumerable<CharacterQuestRecord> quests,
        IEnumerable<ushort> completedQuestIds,
        CancellationToken cancellationToken = default,
        IEnumerable<CreateCharacterMailRequest>? generatedMails = null,
        IEnumerable<CharacterItemRecord>? creatureInventory = null,
        IEnumerable<CharacterSkillRecord>? skills = null,
        int? awakeningType = null,
        uint? victoryPoints = null,
        ushort? warehouseCapacity = null,
        ushort? expectedWarehouseCapacity = null,
        int? growType = null,
        IEnumerable<ushort>? unlockedDungeonIds = null,
        uint? expectedVictoryPoints = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsEconomicWriteBlocked(characterId))
            {
                // 该角色有一笔已落库、尚未被其会话安装的交易交付：拒绝盲写，
                // 否则会话会用陈旧的内存背包覆盖掉刚收到的物品（复制）。
                return null;
            }

            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                return null;
            }

            var index = account.Characters.FindIndex(character => character.Id == characterId);
            if (index < 0)
            {
                return null;
            }

            var character = account.Characters[index];
            if (!IsVictoryPointUpdateCurrent(character, victoryPoints, expectedVictoryPoints)) return null;
            if (!HasDungeonGoldCapacity(character, gold)) return null;
            if (character.Quests?.Any(quest => quest.QuestId == questId) != true)
            {
                return null;
            }

            if (!TryResolveWarehouseCapacityUpdate(
                    character,
                    warehouseCapacity,
                    expectedWarehouseCapacity,
                    out var nextWarehouseCapacity))
            {
                return null;
            }

            if (!TryAppendGeneratedMailsLocked(
                    character,
                    generatedMails,
                    out var mailbox,
                    out var createdMailCount,
                    out var generatedWarehouseCapacity))
            {
                return null;
            }

            nextWarehouseCapacity = Math.Max(
                nextWarehouseCapacity,
                generatedWarehouseCapacity);

            var updated = character with
            {
                Level = Math.Clamp(level, 1, CharacterExperienceCatalog.MaximumLevel),
                Experience = experience,
                Sp = Math.Max(0, skillPoints),
                Gold = Math.Max(0, gold),
                VictoryPoints = victoryPoints ?? character.VictoryPoints,
                Inventory = inventory.OrderBy(item => item.Slot).ToList(),
                WarehouseCapacity = nextWarehouseCapacity,
                CreatureInventory = creatureInventory?.OrderBy(item => item.Slot).ToList()
                    ?? character.CreatureInventory,
                Skills = skills?.Where(skill => skill.Level > 0)
                    .OrderBy(skill => skill.Slot)
                    .ToList()
                    ?? character.Skills,
                GrowType = growType.HasValue
                    ? Math.Clamp(growType.Value, 0, 15)
                    : character.GrowType,
                AwakeningType = awakeningType.HasValue
                    ? Math.Max(character.AwakeningType, awakeningType.Value)
                    : character.AwakeningType,
                Quests = quests
                    .Where(quest => quest.QuestId != ushort.MaxValue)
                    .DistinctBy(quest => quest.QuestId)
                    .Take(3)
                    .ToList(),
                CompletedQuestIds = completedQuestIds
                    .Distinct()
                    .OrderBy(completedQuestId => completedQuestId)
                    .ToList(),
                RewardedQuestIds = (character.RewardedQuestIds ?? [])
                    .Append(questId)
                    .Distinct()
                    .OrderBy(rewardedQuestId => rewardedQuestId)
                    .ToList(),
                Mailbox = mailbox
            };
            if (unlockedDungeonIds is not null)
            {
                updated = HiddenDungeonUnlockCatalog.ApplyUnlocks(updated, unlockedDungeonIds);
            }
            updated = ReconcileAvatarLifetimes(SettleWarehouseUpgradeItems(updated), character);
            account.Characters[index] = updated;
            await SaveLockedAsync(cancellationToken);
            LogGeneratedMails(characterId, createdMailCount);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> MigrateLegacyQuestRewardsAsync(
        QuestCatalog questCatalog,
        CharacterExperienceCatalog experienceCatalog,
        CharacterSpCatalog spCatalog,
        ItemCatalog itemCatalog,
        CancellationToken cancellationToken = default,
        SkillCatalog? skillCatalog = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var migratedCount = 0;
            var growthMigrationCount = 0;
            var skillMigrationCount = 0;
            foreach (var account in _accounts)
            {
                for (var index = 0; index < account.Characters.Count; index++)
                {
                    var character = account.Characters[index];
                    character = RestoreLegacyQuestGrowthState(
                        character,
                        questCatalog,
                        skillCatalog,
                        out var growthStateChanged,
                        out var growthSkillsChanged);
                    if (growthStateChanged)
                    {
                        growthMigrationCount++;
                    }
                    if (growthSkillsChanged)
                    {
                        skillMigrationCount++;
                    }

                    var rewardedQuestIds = (character.RewardedQuestIds ?? []).ToHashSet();
                    foreach (var questId in character.CompletedQuestIds ?? [])
                    {
                        if (!questCatalog.TryGetDefinition(questId, out var quest))
                        {
                            continue;
                        }

                        if (rewardedQuestIds.Contains(questId))
                        {
                            continue;
                        }

                        var rewardPlan = QuestRewardPlanner.Create(
                            character,
                            quest,
                            -1,
                            itemCatalog,
                            uint.MaxValue);
                        if (!rewardPlan.Success)
                        {
                            logger.LogWarning(
                                "Legacy reward migration skipped quest {QuestId} for character {CharacterId}: {Reason}",
                                questId,
                                character.Id,
                                rewardPlan.Error);
                            continue;
                        }

                        if (!HasDungeonGoldCapacity(character, rewardPlan.Gold))
                        {
                            continue;
                        }

                        var migrationMailRequests = rewardPlan.MailedItems
                            .Select(item => new CreateCharacterMailRequest(
                                "DFLegacy",
                                "Legacy quest reward: delivered because the inventory was full.",
                                Attachment: item))
                            .ToArray();
                        if (!TryAppendGeneratedMailsLocked(
                                character,
                                migrationMailRequests,
                                out var migrationMailbox,
                                out var migrationMailCount,
                                out var migrationWarehouseCapacity))
                        {
                            logger.LogWarning(
                                "Legacy reward migration skipped quest {QuestId} for character {CharacterId}: mailbox is full.",
                                questId,
                                character.Id);
                            continue;
                        }

                        var experience = experienceCatalog.CalculateQuestExperience(character, quest);
                        var progression = experienceCatalog.Grant(character, experience);
                        var grantedSkillPoints = spCatalog.CalculateProgressiveSpGrant(
                            progression,
                            experienceCatalog.ExperienceThresholds);
                        character = character with
                        {
                            Level = progression.Level,
                            Experience = progression.Experience,
                            Sp = checked(Math.Max(0, character.Sp) + grantedSkillPoints),
                            Gold = rewardPlan.Gold,
                            Inventory = rewardPlan.Inventory,
                            WarehouseCapacity = Math.Max(
                                rewardPlan.WarehouseCapacity,
                                migrationWarehouseCapacity),
                            Mailbox = migrationMailbox
                        };
                        LogGeneratedMails(character.Id, migrationMailCount);
                        rewardedQuestIds.Add(questId);
                        migratedCount++;
                    }

                    if (rewardedQuestIds.Count > 0)
                    {
                        character = character with
                        {
                            RewardedQuestIds = rewardedQuestIds.OrderBy(questId => questId).ToList()
                        };
                    }

                    account.Characters[index] = character;
                }
            }

            if (migratedCount > 0
                || growthMigrationCount > 0
                || skillMigrationCount > 0)
            {
                await SaveLockedAsync(cancellationToken);
                logger.LogInformation(
                    "Migrated rewards for {RewardCount} legacy completed character quests, restored growth state for {GrowthCount} characters and normalized PVF growth skills for {SkillCount} characters.",
                    migratedCount,
                    growthMigrationCount,
                    skillMigrationCount);
            }

            return migratedCount;
        }
        finally
        {
            _gate.Release();
        }
    }

    private CharacterRecord RestoreLegacyQuestGrowthState(
        CharacterRecord character,
        QuestCatalog questCatalog,
        SkillCatalog? skillCatalog,
        out bool stateChanged,
        out bool skillsChanged)
    {
        var growthQuests = (character.CompletedQuestIds ?? [])
            .Select(questId => questCatalog.TryGetDefinition(questId, out var definition)
                ? definition
                : null)
            .Where(definition => definition is not null)
            .Select(definition =>
            {
                definition!.TryGetGrowthReward(out var chainType, out var growNumber);
                return (Definition: definition, ChainType: chainType, GrowNumber: growNumber);
            })
            .Where(entry => entry.ChainType != 0)
            .ToArray();
        var primaryGrowType = character.GrowType & 0x0F;
        var awakeningType = Math.Max(
            character.AwakeningType,
            Math.Max(0, character.GrowType >> 4));
        if (primaryGrowType == 0)
        {
            var candidates = growthQuests
                .Where(entry => entry.ChainType == 1)
                .Select(entry => entry.GrowNumber)
                .Distinct()
                .ToArray();
            if (candidates.Length == 1)
            {
                primaryGrowType = candidates[0];
            }
        }

        if (primaryGrowType != 0 && awakeningType == 0)
        {
            var candidates = growthQuests
                .Where(entry => entry.ChainType == 2
                    && (entry.Definition.GrowType < 0
                        || entry.Definition.GrowType == primaryGrowType))
                .Select(entry => entry.GrowNumber)
                .Distinct()
                .ToArray();
            if (candidates.Length == 1)
            {
                awakeningType = candidates[0];
            }
        }

        stateChanged = character.GrowType != primaryGrowType
            || character.AwakeningType != awakeningType;
        if (stateChanged)
        {
            character = character with
            {
                GrowType = primaryGrowType,
                AwakeningType = awakeningType
            };
        }

        skillsChanged = false;
        if (skillCatalog is null || primaryGrowType == 0)
        {
            return character;
        }

        IReadOnlyList<CharacterSkillRecord> normalizedSkills;
        if (character.Skills is null)
        {
            normalizedSkills = skillCatalog.CreateResetLayout(
                character.Job,
                primaryGrowType,
                [],
                awakeningType: awakeningType);
        }
        else
        {
            IReadOnlyList<CharacterSkillRecord> working = character.Skills
                .Where(skill => skill.Level > 0)
                .OrderBy(skill => skill.Slot)
                .ToArray();
            var changes = skillCatalog.GetGrowTypeSkillChanges(
                character.Job,
                primaryGrowType);
            if (!skillCatalog.TryApplyGrowthSkillChanges(
                    character.Job,
                    working,
                    changes,
                    out working))
            {
                logger.LogWarning(
                    "Could not normalize class skills for legacy character {CharacterId} grow type {GrowType}: no valid skill slot remains.",
                    character.Id,
                    primaryGrowType);
                return character;
            }

            for (var stage = 1; stage <= awakeningType; stage++)
            {
                changes = skillCatalog.GetAwakeningSkillChanges(
                    character.Job,
                    primaryGrowType,
                    stage);
                if (!skillCatalog.TryApplyGrowthSkillChanges(
                        character.Job,
                        working,
                        changes,
                        out working))
                {
                    logger.LogWarning(
                        "Could not normalize awakening skills for legacy character {CharacterId} stage {AwakeningType}: no valid skill slot remains.",
                        character.Id,
                        stage);
                    return character;
                }
            }

            var existing = working.Select(skill => new GameSkillEntry(
                skill.SkillId,
                skill.Level,
                skill.Slot));
            normalizedSkills = skillCatalog.CreateInitialLayout(
                character.Job,
                existing.Concat(skillCatalog.GetGrantedSkills(
                    character.Job,
                    primaryGrowType,
                    awakeningType)));
        }

        var sourceSkills = (character.Skills ?? [])
            .Where(skill => skill.Level > 0)
            .OrderBy(skill => skill.Slot)
            .ToArray();
        skillsChanged = !sourceSkills.SequenceEqual(normalizedSkills);
        return skillsChanged
            ? character with { Skills = normalizedSkills.ToList() }
            : character;
    }

    private bool SettleWarehouseUpgradeItemsLocked()
    {
        var changed = false;
        foreach (var account in _accounts)
        {
            for (var index = 0; index < account.Characters.Count; index++)
            {
                var character = account.Characters[index];
                var settled = SettleWarehouseUpgradeItems(character);
                if (ReferenceEquals(settled, character))
                {
                    continue;
                }

                account.Characters[index] = settled;
                changed = true;
            }
        }

        return changed;
    }

    private static CharacterRecord SettleWarehouseUpgradeItems(CharacterRecord character)
    {
        var settlement = CharacterWarehouseProgression.SettleUpgradeItems(
            character.Inventory ?? [],
            character.Warehouse ?? [],
            character.WarehouseCapacity);
        if (!settlement.ConsumedItems
            && settlement.Capacity == character.WarehouseCapacity)
        {
            return character;
        }

        return character with
        {
            Inventory = character.Inventory is null
                ? null
                : settlement.Inventory.ToList(),
            Warehouse = character.Warehouse is null
                ? null
                : settlement.Warehouse.ToList(),
            WarehouseCapacity = settlement.Capacity
        };
    }

    private static bool TryResolveWarehouseCapacityUpdate(
        CharacterRecord character,
        ushort? requestedCapacity,
        ushort? expectedCapacity,
        out ushort capacity)
    {
        capacity = CharacterWarehouseProgression.Normalize(character.WarehouseCapacity);
        if (expectedCapacity.HasValue && expectedCapacity.Value != capacity)
        {
            return false;
        }

        if (!requestedCapacity.HasValue)
        {
            return true;
        }

        var requested = requestedCapacity.Value;
        if (CharacterWarehouseProgression.Normalize(requested) != requested
            || requested < capacity)
        {
            return false;
        }

        capacity = requested;
        return true;
    }

    // Privileged operator recovery: persists a rejection ledger while quarantine is still held.
    private async Task WriteRewardStatePrivilegedAsync(List<AccountRecord> accounts, List<DungeonRewardLedgerEntry> ledger,
        CancellationToken cancellationToken, List<DungeonGoldLedgerEntry>? goldLedger = null, List<PvpSettlementReceipt>? pvpLedger = null)
    {
        var snapshot = JsonSerializer.SerializeToUtf8Bytes(new JsonGameStoreState { Accounts = accounts, NextCharacterNo = _nextCharacterNo, NextNormalAccountUid = _nextNormalAccountUid, NextTestAccountUid = _nextTestAccountUid, FatigueDayKey = _fatigueDayKey, DungeonRewardLedger = ledger, DungeonGoldLedger = goldLedger ?? _dungeonGoldLedger, PvpLedger = pvpLedger ?? _pvpLedger }, new JsonSerializerOptions { WriteIndented = true });
        await _rewardWriter.WriteAsync(StorePath, snapshot, cancellationToken);
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken, Action? snapshotCommitted = null,
        bool isolateCommittedDiagnostics = false)
    {
        ThrowIfRewardQuarantinedLocked();
        _ = SettleWarehouseUpgradeItemsLocked();
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var temporary = StorePath + ".tmp";
        await using (var output = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(
                output,
                new JsonGameStoreState
                {
                    Accounts = _accounts,
                    NextCharacterNo = _nextCharacterNo,
                    NextNormalAccountUid = _nextNormalAccountUid,
                    NextTestAccountUid = _nextTestAccountUid,
                    FatigueDayKey = _fatigueDayKey,
                    DungeonRewardLedger = _dungeonRewardLedger,
                    DungeonGoldLedger = _dungeonGoldLedger,
                    PvpLedger = _pvpLedger
                },
                new JsonSerializerOptions { WriteIndented = true },
                cancellationToken);
        }

        File.Move(temporary, StorePath, overwrite: true);
        snapshotCommitted?.Invoke();
        if (!isolateCommittedDiagnostics)
            logger.LogInformation("Saved emulator state to {Path}", StorePath);
        else
        {
            // Item-space callers must receive the successful commit even if a
            // logging provider fails. This contains diagnostics only: the atomic
            // replacement is already done, and must never be retried or undone.
            try { logger.LogInformation("Saved emulator state to {Path}", StorePath); }
            catch (Exception) { }
        }
    }

    private void ThrowIfRewardQuarantinedLocked()
    {
        if (_rewardQuarantined)
            throw new InvalidOperationException("store is quarantined after uncertain dungeon reward write");
    }

    private bool ResetFatigueForNewDayLocked(
        DateTimeOffset localNow,
        out Guid[] characterIds)
    {
        var fatigueDayKey = GetFatigueDayKey(localNow);
        if (_fatigueDayKey == fatigueDayKey)
        {
            characterIds = [];
            return false;
        }

        var resetCharacterIds = new List<Guid>();
        foreach (var account in _accounts)
        {
            for (var index = 0; index < account.Characters.Count; index++)
            {
                var character = account.Characters[index];
                if (character.UsedFatigue == 0)
                {
                    continue;
                }

                account.Characters[index] = character with { UsedFatigue = 0 };
                resetCharacterIds.Add(character.Id);
            }
        }

        _fatigueDayKey = fatigueDayKey;
        characterIds = resetCharacterIds.ToArray();
        return true;
    }

    private static bool IsMailAvailable(CharacterMailRecord mail, uint now) =>
        mail.State == 3 || mail.ExpiresAt == 0 || mail.ExpiresAt > now;

    private bool TryAppendGeneratedMailsLocked(
        CharacterRecord character,
        IEnumerable<CreateCharacterMailRequest>? requests,
        out List<CharacterMailRecord>? mailbox,
        out int createdMailCount,
        out ushort warehouseCapacity)
    {
        warehouseCapacity = CharacterWarehouseProgression.Normalize(
            character.WarehouseCapacity);
        var generated = new List<CreateCharacterMailRequest>();
        foreach (var request in requests ?? [])
        {
            var identifiedRequest = EnsureMailAttachmentInstanceId(request);
            if (identifiedRequest.Attachment is not { } attachment
                || !CharacterWarehouseProgression.TryGetItemTargetCapacity(
                    attachment.ItemId,
                    out var targetCapacity))
            {
                generated.Add(identifiedRequest);
                continue;
            }

            warehouseCapacity = Math.Max(warehouseCapacity, targetCapacity);
            if (identifiedRequest.Gold != 0)
            {
                generated.Add(identifiedRequest with { Attachment = null });
            }
        }

        createdMailCount = 0;
        if (generated.Count == 0)
        {
            mailbox = character.Mailbox;
            return true;
        }

        if (generated.Any(request => ValidateMailRequest(request) is not null))
        {
            mailbox = null;
            return false;
        }

        var now = checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        mailbox = (character.Mailbox ?? [])
            .Where(mail => IsMailAvailable(mail, now))
            .ToList();
        if (mailbox.Count + generated.Count > byte.MaxValue)
        {
            mailbox = null;
            return false;
        }

        var usedIds = _accounts
            .SelectMany(account => account.Characters)
            .SelectMany(candidate => candidate.Mailbox ?? [])
            .Select(mail => mail.Id)
            .ToHashSet();
        foreach (var request in generated)
        {
            var mailId = AllocateMailIdLocked(usedIds);
            usedIds.Add(mailId);
            mailbox.Add(new CharacterMailRecord(
                mailId,
                request.Sender.Trim(),
                request.Text,
                request.Gold,
                request.Attachment,
                now,
                checked(now + (uint)TimeSpan.FromDays(30).TotalSeconds)));
        }

        createdMailCount = generated.Count;
        return true;
    }

    private void LogGeneratedMails(Guid characterId, int count)
    {
        if (count > 0)
        {
            logger.LogInformation(
                "Created {MailCount} overflow mail(s) for character {CharacterId}.",
                count,
                characterId);
        }
    }

    private uint AllocateMailIdLocked(ISet<uint>? additionallyUsedIds = null)
    {
        var usedIds = _accounts
            .SelectMany(account => account.Characters)
            .SelectMany(character => character.Mailbox ?? [])
            .Select(mail => mail.Id)
            .ToHashSet();
        if (additionallyUsedIds is not null)
        {
            usedIds.UnionWith(additionallyUsedIds);
        }

        for (var attempt = 0; attempt < 1024; attempt++)
        {
            var candidate = checked((uint)RandomNumberGenerator.GetInt32(1, int.MaxValue));
            if (!usedIds.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique mail id.");
    }

    private static string? ValidateMailRequest(CreateCharacterMailRequest request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Sender)
            || request.Sender.Length > 20)
        {
            return "Mail sender length must be 1-20 characters.";
        }

        if ((request.Text?.Length ?? 0) > 255 || request.Gold < 0)
        {
            return "Mail text is too long or gold is negative.";
        }

        if (request.Attachment is { ItemId: 0 }
            || request.Attachment is { CountOrValue: 0 })
        {
            return "Mail attachments require a non-zero item id and value.";
        }

        return null;
    }

    public async Task<PublicAccountRecord?> GetAccountAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = FindAccountLocked(userName);
            return account is null ? null : ToPublic(account);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountRole> GetRoleAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return AccountRole.Player;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return FindAccountLocked(userName)?.Role ?? AccountRole.Player;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 启动引导：把 <c>Admin.Users</c> 名单里的账号提升为管理员。
    /// 名单里写了但还不存在的账号记一条警告（账号注册时会自动带上管理员角色）。
    /// </summary>
    public async Task<int> ApplyBootstrapAdminsAsync(CancellationToken cancellationToken = default)
    {
        if (options.Admin.Users.Length == 0)
        {
            return 0;
        }

        var promoted = 0;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var name in options.Admin.Users)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var index = _accounts.FindIndex(account =>
                    string.Equals(account.UserName, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    logger.LogWarning(
                        "Admin.Users lists {UserName}, but no such account exists yet; " +
                        "the account becomes an admin automatically once it is registered.",
                        name);
                    continue;
                }

                if (_accounts[index].Role == AccountRole.Admin)
                {
                    continue;
                }

                _accounts[index] = _accounts[index] with { Role = AccountRole.Admin };
                promoted++;
            }

            if (promoted > 0)
            {
                await SaveLockedAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (promoted > 0)
        {
            logger.LogInformation("Promoted {Count} bootstrap admin account(s).", promoted);
        }

        return promoted;
    }

    /// <summary>
    /// 提升/降级账号角色。<paramref name="actorUserName"/> 是发起人，用于两条安全约束：
    /// 不能降级自己、不能把最后一名管理员降级（否则没人能再进管理接口）。
    /// </summary>
    public async Task<(bool Success, string? Error)> SetRoleAsync(
        string userName,
        AccountRole role,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(actorUserName))
        {
            return (false, "账号不能为空。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var index = _accounts.FindIndex(account =>
                string.Equals(account.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return (false, "账号不存在。");
            }

            if (_accounts[index].Role == role)
            {
                return (true, null);
            }

            if (role == AccountRole.Player)
            {
                if (string.Equals(
                        _accounts[index].UserName,
                        actorUserName.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "不能降级自己的账号。");
                }

                if (_accounts.Count(account => account.Role == AccountRole.Admin) <= 1)
                {
                    return (false, "至少需要保留一名管理员。");
                }
            }

            _accounts[index] = _accounts[index] with { Role = role };
            await SaveLockedAsync(cancellationToken);
            return (true, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsBootstrapAdmin(string userName) =>
        options.Admin.Users.Any(name =>
            !string.IsNullOrWhiteSpace(name)
            && string.Equals(name.Trim(), userName, StringComparison.OrdinalIgnoreCase));

    private static PublicAccountRecord ToPublic(AccountRecord account) =>
        new(
            account.Id,
            account.UserName,
            account.Characters,
            account.AccountUid,
            AccountUidRules.Classify(account.AccountUid),
            Math.Max(0, account.Cash ?? DefaultAccountCash),
            account.Role);

    private static AuthenticatedAccount ToAuthenticatedAccount(AccountRecord account) =>
        new(account.AccountUid, account.UserName, AccountUidRules.Classify(account.AccountUid));

    private static string HashPassword(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PasswordHashPrefix + password)));

    private static bool IsValidPassword(string? password) =>
        password is { Length: >= 6 and <= 128 };

    // 找回密码的 QQ 码比对：定长比较，避免按字符提前返回泄露前缀（安全复审 F1）。
    private static bool FixedTimeEqualsText(string? expected, string? actual)
    {
        if (expected is null || actual is null)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static bool PasswordMatches(AccountRecord account, string password)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(account.PasswordHash),
                SHA256.HashData(Encoding.UTF8.GetBytes(PasswordHashPrefix + password)));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private CreateCharacterMailRequest EnsureMailAttachmentInstanceId(
        CreateCharacterMailRequest request)
    {
        if (request is null)
        {
            return null!;
        }

        if (request.Attachment is not { } attachment
            || itemCatalog is null
            || !itemCatalog.TryGetDefinition(attachment.ItemId, out var definition))
        {
            return request;
        }

        var identified = CharacterItemIdentity.Ensure(attachment, definition);
        return identified.Equals(attachment)
            ? request
            : request with { Attachment = identified };
    }
}
