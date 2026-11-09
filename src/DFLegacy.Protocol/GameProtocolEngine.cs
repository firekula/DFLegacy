using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public sealed record GameCharacterSummary(
    ushort Slot,
    byte[] NameBytes,
    byte Job,
    byte GrowType,
    byte Level,
    IReadOnlyList<GameInventoryEntry>? AppearanceItems = null,
    uint CharacterNo = 0,
    byte AwakeningType = 0,
    byte PvpGrade = 0);

public sealed record GameInventoryEntry(
    ushort Slot,
    ushort ItemId,
    uint CountOrValue,
    byte State = 0,
    ushort Durability = 0,
    byte SealState = 0);
public sealed record GameCreatureInfoEntry(
    uint Uid,
    byte Stomach,
    uint Experience,
    byte Level,
    byte[] NameBytes,
    bool NoCharge = false);
public sealed record GameCreatureAppearanceEntry(
    ushort ItemId,
    byte[] NameBytes,
    byte State = 1);
public sealed record GameItemConsumptionEntry(
    ushort Slot,
    uint ConsumedCount);
public sealed record GameDisjointRewardEntry(
    ushort DestinationSlot,
    ushort ItemId,
    uint Count);
public readonly record struct GameCeraShopReward(ushort ItemId, uint Count);

public sealed record GameSkillEntry(byte SkillId, byte Level, byte? Slot = null);
public sealed record GameQuestEntry(ushort QuestId, uint Trigger = 0);
public sealed record GameQuestConsumedItem(ushort Slot, uint RemainingCount);
public sealed record GameQuestInsertedItem(ushort Slot, ushort ItemId, uint CountOrSeed);
public sealed record GameQuestGrowthSkillChange(byte SkillId, byte SkillClass, byte Level);
public sealed record GamePremiumServiceEntry(byte ServiceType, uint RemainSeconds);
public sealed record GameCharacterCombatStats(
    uint MaximumHp,
    uint MaximumMp,
    short PhysicalAttack,
    short PhysicalDefense,
    short MagicalAttack,
    short MagicalDefense,
    short FireResistance,
    short WaterResistance,
    short DarkResistance,
    short LightResistance,
    uint InventoryLimit,
    ushort HpRegeneration,
    ushort MpRegeneration,
    ushort MovementSpeed,
    ushort AttackSpeed,
    ushort CastSpeed,
    ushort HitRecovery,
    ushort JumpPower,
    uint Weight);
public sealed record GameDungeonPermissionEntry(ushort DungeonId, byte ClearState);
public sealed record GameTownUser(
    ushort UserId,
    short X,
    short Y,
    byte Direction);
// 好友列表条目：名字为 CP949/GBK 编码的原始字节（列表内为定长 30 字节字段）。
// 好友行绘制（0x7F7F30）的等级/职业：**优先用本地 user 表记录**（0x500070 会用记录里的
// 等级、职业系、成长/觉醒覆盖条目字段），**没有记录时退回快照记录的这三个字段** ——
// 0x21（等级 u16）、0x23（职业系，必须 <5）、0x24（打包成长：低半字节 GrowType、
// 高半字节 AwakeningType）。离线好友没有 user 记录，正是靠回退路径画 `Lv.%d 职业名`，
// 不填就是“Lv.0 鬼剑士”。旧结论“0x21 必须为 0”是把 SecureInt 解密 0x402030 误当成取
// 字符串得来的，已作废。
// 客户端“查看信息”用的 uid 仍来自本地 user 注册表（由 USERINFO mode 0 建立），
// 不从这份快照记录里取。
public sealed record GameFriendEntry(
    byte[] NameBytes,
    bool Online,
    // 等级：R+0x21 的 u16，好友行 `Lv.%d` 直接读它。
    ushort Level = 0,
    // 职业系 0..4（0=鬼剑士/1=格斗/2=枪手/3=魔法师/4=圣职者）：R+0x23。
    // 客户端 0x500B00 有 `cmp arg3,5; jge` 守卫，>=5 会**整条拒绝**，构建器会钳到 0..4。
    byte Job = 0,
    // 成长阶（低半字节）与觉醒阶（高半字节）：R+0x24。
    byte GrowType = 0,
    byte AwakeningType = 0);

/// <summary>
/// CMD 130 黑名单快照条目。线上的日期是 (年-1900, 月-1, 日) 三个字节，
/// 客户端收包后做 +1900 / +1 修正，所以这里保留日历语义、由构建器做偏移。
/// </summary>
public sealed record GameBlacklistEntry(byte[] NameBytes, int Year, int Month, int Day);
public sealed record GameDungeonMonster(
    byte MapListIndex,
    ushort UniqueId,
    ushort MonsterIndex,
    byte Level,
    byte Type = 0,
    byte IsBoxMonster = 0,
    byte BoxIndex = 0,
    bool IsHellHidden = false,
    ushort X = 0,
    ushort Y = 0)
{
    // PVF [monster team], indexed by the map's monster rows. The client reads
    // this from its map script; it is not an extra field in NOTI29's wire row.
    public int Team { get; init; } = 100;
    public bool IsHostile => Team == 100;
}

public static class GameDungeonMonsterTypes
{
    public const byte Normal = 0;
    public const byte Champion = 1;
    public const byte SuperChampion = 2;
    public const byte Boss = 3;
}

public sealed record GameDungeonClearExperienceBreakdown
{
    public static GameDungeonClearExperienceBreakdown Empty { get; } = new();

    public uint BaseExperience { get; init; }

    public uint RankBonus { get; init; }

    public uint PartyBonus { get; init; }

    public uint AvatarBonus { get; init; }

    public uint EventBonus { get; init; }

    public uint BlackDiamondBonus { get; init; }

    public uint ChannelBonus { get; init; }

    public uint MentorBonus { get; init; }

    public uint CreatureBonus { get; init; }

    public uint TotalExperience => (uint)Math.Min(
        uint.MaxValue,
        (ulong)BaseExperience
        + RankBonus
        + PartyBonus
        + AvatarBonus
        + EventBonus
        + BlackDiamondBonus
        + ChannelBonus
        + MentorBonus
        + CreatureBonus);
}

public sealed record GameDungeonDrop(
    ushort GroundId,
    ushort ItemId,
    uint AddInfo,
    ushort Durability = 0,
    ushort OwnerUserId = 0,
    byte ItemAttr = 0);
public sealed record GamePassiveObjectDrop(
    byte AssociationSlot,
    ushort GroundId,
    ushort ItemId,
    uint AddInfo,
    ushort OwnerUserId = 0);
public readonly record struct GameDieMonsterCommand(
    ushort EntityId,
    bool IsPassiveObject);

public readonly record struct GameUseCoinCommand(ushort TargetUserId);
public readonly record struct GameGetItemCommand(
    ushort GroundId, bool RouteRequested, byte ClientRarity, ushort CheckToken);
public readonly record struct GameDropItemCommand(
    short X,
    short Y,
    byte ItemSpace,
    ushort Slot,
    uint Count);
public readonly record struct GameBossDieCheckCommand(
    ushort ParticipantId,
    ushort BossId,
    uint SkillChecksum);
public readonly record struct GameUpgradeItemCommand(
    ushort TargetSlot,
    ushort TargetItemId,
    ushort MaterialSlot);
public readonly record struct GameResetItemAttributeCommand(
    ushort TargetSlot,
    ushort TargetItemId,
    ushort MaterialSlot);
public readonly record struct GameCompoundItemCommand(
    ushort Sequence,
    ushort SourceValue,
    bool SourceIsItemId,
    byte CraftCount);
public readonly record struct GameCompoundItemConsumption(
    byte ListType,
    ushort Slot,
    uint RemainingCount);
public readonly record struct GameCompoundItemResult(
    ushort Slot,
    ushort ItemId,
    uint CountOrValue,
    byte State,
    ushort Durability);
public readonly record struct GameCompoundAvatarCommand(
    ushort MaterialSlot,
    ushort FirstAvatarItemId,
    ushort SecondAvatarItemId,
    byte SelectedOption,
    byte FirstAvatarSlot,
    byte SecondAvatarSlot);
public readonly record struct GameAvatarCompoundConsumption(
    byte ListType,
    ushort Slot,
    uint RemainingCount);

public sealed record GameMailAttachment(
    ushort ItemId,
    uint CountOrValue,
    byte State = 0,
    ushort Durability = 0,
    byte SealState = 0);

public sealed record GameMailEntry(
    uint MailId,
    byte[] SenderBytes,
    byte[] TextBytes,
    uint Gold,
    GameMailAttachment? Attachment,
    uint SentAt,
    uint ExpiresAt,
    ushort State = 1);

public enum GameMessageType : byte
{
    Type00DualRouteBrown = 0,
    Type01LightGreen = 1,
    Type02LightCyan = 2,
    Type03White = 3,
    Type04Brown = 4,
    Type05White = 5,
    Type06Magenta = 6,
    Type07LightGreen = 7,
    Type08Orange = 8,
    Type09White = 9,
    Type10White = 10,
    Type11LightCyan = 11,
    Type12Yellow = 12,
    Type13SpeakerYellow = 13,
    Type14SpeakerYellow = 14,
    Type15SpeakerYellow = 15,
    Type16Brown = 16,
    Type17ClientFormattedNotice = 17,
    Type18DirectOutput = 18
}

/// <summary>NOTI 15 的载荷：与背包 ITEM_LIST 记录同构（少一个 SealState）。</summary>
public readonly record struct GameItemTradeEntry(
    ushort Slot,
    ushort ItemId,
    uint CountOrValue,
    byte State,
    ushort Durability);

/// <summary>NOTI 16 的退条目：把「我的」面板格放回 (destListType, destSlot)。</summary>
public readonly record struct GameItemTradeCancelEntry(
    ushort PanelSlot,
    byte DestinationListType,
    ushort DestinationSlot);

/// <summary>NOTI 18 的交付条目：对方面板格 → 我的主背包格。</summary>
public readonly record struct GameItemTradeDeliveryEntry(
    ushort OtherPanelSlot,
    ushort DestinationSlot);

/// <summary>
/// Handles the initial command exchange used after the client selects a channel.
/// Game/channel packets share the ten-byte transport header with entrance packets,
/// but the body begins with a two-byte command sequence number. Command payload
/// serialization starts after that sequence number.
/// </summary>
public static partial class GameProtocolEngine
{
    private const byte GrowthSkillClassCount = 5;

    public const int ClearRewardCardColumnCount = 4;
    public const byte NotificationPacketType = 0;
    public const byte CheckConnectionNotification = 0;
    public const byte ChannelInfoNotification = 1;
    public const byte UserInfoNotification = 2;
    public const byte UserStateNotification = 3;
    public const byte StaminaNotification = 4;
    public const byte DungeonPermissionNotification = 5;
    public const byte UdpPeerInfoNotification = 11;
    public const byte MessageNotification = 12;
    public const byte ItemListNotification = 13;
    public const byte UpdateItemListNotification = 14;
    public const byte SkillInfoNotification = 19;
    public const byte AcceptableQuestListNotification = 21;
    public const byte UserPositionNotification = 22;
    public const byte UserAreaNotification = 23;
    public const byte AreaUsersNotification = 24;
    public const byte UdpHostNotification = 26;
    public const byte EnterSelectDungeonNotification = 27;
    public const byte DungeonInfoNotification = 28;
    public const byte StartMapNotification = 29;
    public const byte FinishLoadingNotification = 30;
    public const byte EnableClearDungeonNotification = 31;
    public const byte DieStateNotification = 32;
    public const byte FailClearDungeonNotification = 33;
    public const byte PlayResultNotification = 34;
    public const byte ClearDungeonRewardNotification = 35;
    public const byte FatigueNotification = 36;
    public const byte ExperienceGainedNotification = 37;
    public const byte MonsterDieNotification = 38;
    // NOTI 38's compact trailer is [reserved, champion ordinal, reserved].
    // The 13338 client uses ordinal 1..11 to play the Champion/SuperChampion
    // clear-reward animation before the card screen; 255 means no such reward.
    public const sbyte NoChampionRewardOrdinal = -1;
    public const sbyte MaximumChampionRewardOrdinal = 11;
    public const byte GetItemNotification = 39;
    public const byte DropItemNotification = 40;
    /// <summary>
    /// `NOTI 86 SERVER_BROADCAST`：官服拾取成功后向**全场**广播"地面物已被取走"。
    /// 本客户端 86 号处理器（`0x427104`）只实现子类型 1/2（文本广播），官服发的 **5 落 default（空操作）**
    /// ⇒ 照发以对齐语义，但**本客户端画面不会因它变化**（已知用户可见后果，见 PD-3001/3002）。
    /// </summary>
    public const byte PickupRemovedNotification = 86;
    // 13338's notification dispatcher case 48 reads five little-endian
    // int32 values followed by two bytes: win, lose, PvP points, current
    // rank point, next rank point, grade and grade-extension.
    public const byte PvpRecordNotification = 48;
    public const byte CeraUpdateNotification = 53;
    public const byte PremiumServiceNotification = 66;
    public const byte MailboxMailListNotification = 97;
    public const byte MailboxRemoveMailNotification = 98;
    public const byte MailboxAlarmNotification = 99;
    // DF2008 reads both Creature life notifications as one u16 area-user id.
    // The 13339 server names these DIED_CREATURE and REVIVAL_CREATURE.
    public const byte DiedCreatureNotification = 100;
    public const byte CreatureRenameNotification = 101;
    public const byte CreatureGainExperienceNotification = 102;
    public const byte CreatureStateNotification = 103;
    public const byte CreatureResponseNotification = 104;
    public const byte CreatureInfoNotification = 105;
    public const byte CreatureEvolutionNotification = 106;
    public const byte CreatureEvoluteNotification = 106;
    public const byte RevivalCreatureNotification = 107;
    public const byte RenameCreatureCommand = 103;
    public const byte HatchCreatureCommand = 105;
    public const byte ResponseCreatureCommand = 104;
    public const byte EventInfoNotification = 120;
    public const byte CreatureMessageNotification = 126;
    public const byte BossDieCheckNotification = 127;
    public const byte CreatureScriptMessageNotification = 131;
    public const byte EnterGameWorldCompleteNotification = 136;
    public const byte CommandPacketType = 1;
    public const byte CheckConnectionCommand = 0;
    public const byte LoginCommand = 1;
    public const byte SetUdpEndpointCommand = 2;
    public const byte ExitCommand = 3;
    public const byte SelectCharacterCommand = 4;
    public const byte CreateCharacterCommand = 5;
    public const byte DeleteCharacterCommand = 6;
    public const byte ReturnSelectCharacterCommand = 7;
    public const byte GetUserInfoCommand = 8;
    public const byte RecoverStaminaCommand = 9;
    public const byte StartGameCommand = 15;
    public const byte SelectDungeonCommand = 16;
    public const byte SendMessageCommand = 17;
    public const byte CreatureSendMessageCommand = 124;
    public const byte CreatureScriptMessageCommand = 132;

    /// <summary>
    /// ACT1 CMD125：客户端错误诊断。004AF42B/004AF4A6 发送 type2/error；对应官服
    /// Dispatcher_TraceError::dispatch_sig@08204B0C 的诊断语义，不回包、不改业务状态。
    /// </summary>
    public const byte Act1TraceErrorCommand = 125;
    /// <summary>ACT1 CMD118 的历史客户端报告；保留 code/flag 样本，完整语义尚未证明。</summary>
    public const byte LegacyClientReportCommand = 118;
    /// <summary>历史公开名称兼容；仍为118，不代表ACT1已证实的TRACE_ERROR命令。</summary>
    public const byte TraceErrorCommand = LegacyClientReportCommand;
    public const byte DeleteItemCommand = 18;
    public const byte MoveItemSpaceCommand = 19;
    public const byte SortItemCommand = 20;
    public const byte BuyItemCommand = 21;
    public const byte SellItemCommand = 24;
    public const byte RepairEquipmentCommand = 25;
    public const byte SetItemTradeStateCommand = 26;
    // ── 交易（ITEMTRADE）：客户端 id 表 通知 0xB54170 / 命令 0xB54430 ──────────
    // 逐字段布局与证据地址见 .superpowers/sdd/trade-protocol-recon.md 与设计稿
    // docs/superpowers/specs/2026-09-28-trade-system-design.md §1。
    public const byte RequestPeerCommand = 10;
    public const byte ResponsePeerCommand = 11;
    /// <summary>CMD 12：队伍名（预置名下标）与人数上限。</summary>
    public const byte SetPartyInfoCommand = 12;
    /// <summary>CMD 13：退出队伍（无载荷）。</summary>
    public const byte LeavePartyCommand = 13;
    /// <summary>CMD 14：踢人（载荷 = u8 槽位下标）。</summary>
    public const byte WalkoutPartyMemberCommand = 14;
    /// <summary>CMD 131：转交队长（客户端 → 服务端；载荷 = u8 槽位。下行被客户端丢弃，所以只用 NOTI 9 广播结果）。</summary>
    public const byte ChangeHostCommand = 131;
    public const byte RequestPeerNotification = 7;
    public const byte ResponsePeerNotification = 8;
    /// <summary>NOTI 9：队伍名册/解散。</summary>
    public const byte PartyInfoNotification = 9;
    /// <summary>NOTI 10：被踢/掉线离队（按槽位）。</summary>
    public const byte WalkoutNotification = 10;
    public const byte ChangeItemTradeItemNotification = 15;
    public const byte CancelItemTradeNotification = 16;
    public const byte StateItemTradeNotification = 17;
    public const byte FinishItemTradeNotification = 18;

    /// <summary>CMD 11 失败包的 peerType：0x64 命中客户端 0x7570「对方不在线，或拒绝了您的交易请求」。</summary>
    public const byte ItemTradePeerType = 100;

    /// <summary>CMD 19 结果包里的交易栏 listType（客户端固定 4，见 0x462660）。</summary>
    public const byte ItemTradeListType = 4;

    /// <summary>NOTI 15 用 itemId 0xFFFF 表示清空该面板格（0x41D942 分支）。</summary>
    public const ushort ClearItemTradeSlot = 0xFFFF;

    /// <summary>
    /// CMD 19 失败回包的错误码。客户端 handler 0x4110FE 的 switch（recon-2 §4.1）只认这几档，
    /// 其余码一个字都不弹：0x11 = 要移动的物品不存在或数量不足（含金币不够），
    /// 0x13 = 您没有移动的权限，0x17 = 栏中有其他物品、无法放置。
    /// </summary>
    public const byte MoveItemSpaceFailureNotFound = 0x11;
    public const byte MoveItemSpaceFailureNotAllowed = 0x13;
    public const byte MoveItemSpaceFailureDestinationOccupied = 0x17;

    // ── 反外挂/插件通道（send-only，客户端永不接收）────────────────────────────
    // 真机反汇编：命令行名表 0xB54430 的 160/161/162（recon-2 §2.1），161 是 TerSafe.dll
    // 每 60 秒一条的 ANTI_BOT 上报（§2.3）。客户端入站分发只覆盖 id 1..159（0x40F2B6），
    // 服务端既不解析也不回包，只需在兜底告警前静默。
    public const byte SdcDamageCheckCommand = 160;
    public const byte AntibotCommand = 161;
    public const byte DprotoCommand = 162;
    public const byte CompoundItemCommand = 27;
    public const byte DisjointItemCommand = 28;
    public const byte UseLotteryItemCommand = 29;
    public const byte ChangeSkillSlotCommand = 30;
    public const byte BuySkillCommand = 31;
    public const byte IncreaseStatusCommand = 32;
    public const byte AcceptQuestCommand = 33;
    public const byte GiveupQuestCommand = 34;
    public const byte SetQuestTriggerCommand = 35;
    public const byte FinishQuestCommand = 36;
    public const byte SetUserPositionCommand = 37;
    public const byte SetUserAreaCommand = 38;
    public const byte FinishLoadingCommand = 40;
    public const byte UseSkillCommand = 41;
    public const byte DieMonsterCommand = 42;
    public const byte DieCharacterCommand = 43;
    public const byte UseCoinCommand = 44;
    public const byte GiveupGameCommand = 45;
    public const byte GetItemCommand = 46;
    public const byte UseStackableItemCommand = 47;
    public const byte DungeonItemActionCommand = UseStackableItemCommand;
    public const byte MoveMapCommand = 48;
    public const byte SetPlayResultCommand = 49;
    public const byte DropItemCommand = 50;
    public const byte DecreaseDurabilityCommand = 51;
    // 好友系统（对照 DNF.exe 1.0.1.9 的 id→名字表，见 .superpowers/sdd/friends-protocol-recon.md）
    public const byte FriendInfoNotification = 52;   // 本版本客户端空实现，禁止使用
    public const byte BuddyInNotification = 54;      // 好友上线/状态更新（只更新已有条目）
    public const byte BuddyOutNotification = 55;     // 好友下线（条目状态置 -1）
    public const byte BuddyConnListNotification = 56; // 好友列表快照（唯一能新建条目的通知）
    // 跨频道/按名字投递的消息（1:1 窗口与悄悄话发送方那一行的载体）。客户端
    // handler 0x41CD9D：payload = [u8 type][u8 flag][u32 名字长度 + 名字][u32 文本
    // 长度 + 文本]。flag 是状态码，不是“投递开关”：
    //   flag == 0        → 正文按名字投递：type 0x12 进 1:1 窗口（0x810A90），
    //                      type 1/7 进主聊天栏 '%s : %s'（0x849452）。
    //   flag == 0x4D     → '对方在黑名单中'；0x5A → '无法向GM发送聊天信息'；
    //   其它非 0（含我们此前发的 1）→ 默认提示 0x81E '对方的当前状态为无法接收信息
    //   的状态'，且正文被整段丢弃——这正是首轮 1:1 双方都看不到正文的原因。
    public const byte MessageOtherChannelNotification = 65;
    public const byte MessengerMessageType = 0x12;
    public const byte MessengerMessageFlag = 0;
    public const byte AddFriendCommand = 63;
    public const byte RemoveFriendCommand = 64;
    // 黑名单（对照 DNF.exe 1.0.1.9，见 .superpowers/sdd/viewinfo-blacklist-recon.md §2）：
    // 三条命令都走 type=1 命令通道，**没有任何通知包**；130 是“清空 + 全量追加”的
    // 快照（客户端打开社区窗口的黑名单页时发，空载荷），可随时重发，没有“每会话一次”
    // 的约束。名字匹配在客户端是逐字节大小写敏感比较。
    public const byte RegisterToBlacklistCommand = 128;
    public const byte DeleteToBlacklistCommand = 129;
    // CMD 128/129 失败码（客户端 0x414CFA/0x414D0C/0x414D1E/0x414D34 分支）：
    // 74=0x4A“对方已在黑名单中”、76=0x4C“无法找到的名字…”、78=0x4E“黑名单已满…”、
    // 0x4B=75“对方不在黑名单中”。
    public const byte BlacklistAlreadyListedError = 74;
    public const byte BlacklistNotListedError = 0x4B;
    public const byte BlacklistCharacterNotFoundError = 76;
    public const byte BlacklistFullError = 78;
    public const byte CeraShopBuyCommand = 67;
    public const byte GenerateCeraTicketCommand = 68;
    public const byte ScoreScrollStateCommand = 72;
    public const byte CardSelectRightStateCommand = 73;
    public const byte SelectCardCommand = 74;
    public const byte EplpCommand = 75;
    // Historical public alias; ACT1 command 82 is the mentorship list.
    public const byte PrivateStorePollCommand = MentorListCommand;
    public const byte UpgradeItemCommand = 83;
    public const byte ResetItemAttributeCommand = 84;
    public const byte MailboxSendCommand = 97;
    public const byte MailboxExtractItemCommand = 98;
    public const byte MailboxOpenCommand = 99;
    public const byte CompoundAvatarCommand = 102;
    public const byte BossDieCheckCommand = 127;
    // 130 现按“黑名单请求”处理：定点勘察确认它就是 REQUEST_BLACKLIST（push 0x82、
    // 空载荷、客户端仅从社区窗口的黑名单页发出）。早期把它猜成“交换服务器角色信息”
    // 并回 [1][0]；旧名保留给既有调用点与冒烟测试，新代码用 RequestBlacklistCommand。
    public const byte ExchangeServerCharacterInfoCommand = 130;
    public const byte RequestBlacklistCommand = ExchangeServerCharacterInfoCommand;
    public const byte BackToVillageCommand = 142;
    public const byte MailboxStateCommand = 144;
    public const byte AllDieMonsterCommand = 148;
    public const byte TutorialCompletionCommand = 153;
    public const ushort TutorialDungeonId = 10_000;
    public const ushort TutorialMapId = 61_000;
    public const uint TutorialCompletionModule = 31;
    public const ushort DefaultMaximumFatigue = 156;
    public const ushort BlackDiamondBonusFatigue = 32;
    public const ushort BlackDiamondMaximumFatigue =
        DefaultMaximumFatigue + BlackDiamondBonusFatigue;

    // DF2008 CMD 15 reads error 0x16 followed by the exhausted party-member index.
    public const byte EnterDungeonSelectionInsufficientFatigueError = 0x16;
    // DF2008 starts ground-item pickup optimistically. CMD 46 error 0x15 stays
    // silent but enters the common rollback at 0x00414198, which clears the
    // pending pickup flag at item-object + 0x7A5 so the same item falls back.
    public const byte GetItemSilentRejectionError = 0x15;
    public const uint DefaultCompletedTutorialFlags = uint.MaxValue;
    public const byte BlackDiamondServiceType = 16;
    public const byte OverlordContractServiceType = 22;
    public const byte MasterContractServiceType = 27;
    public const ushort PremiumServiceUpdateAction = 2;

    public static GameServerPacket? Handle(PacketFrame request)
    {
        return Handle(request, []);
    }

    public static GameServerPacket? Handle(PacketFrame request, ReadOnlySpan<byte> challenge)
    {
        if (request.Type != CommandPacketType || request.Body.Length < sizeof(ushort))
        {
            return null;
        }

        return request.ProtocolId switch
        {
            CheckConnectionCommand when challenge.Length == 16 => CreateChannelInfo(
                "Local Channel 1"u8,
                channelType: 1,
                result: 1,
                serverId: 1,
                channelNumber: 1,
                seed: 0,
                ["127.0.0.1"u8.ToArray()],
                port1: 2311,
                port2: 2311),
            // ACT1 also reads UID, relay address/ports and two profile bytes.
            // This stateless serializer has no identity or relay availability;
            // the live authenticated channel supplies its complete context.
            LoginCommand => CreateChannelLoginSuccess(0, string.Empty, 0, 0),
            // The 1.0.1.9 client clears a possible old game session before it
            // begins the selected-channel login flow. A one-byte true result is
            // the complete successful response consumed by its EXIT handler.
            ExitCommand => CreateCommandReply(request, success: true),
            // SELECT_CHARACTER uses a larger acknowledgement than the other
            // character-screen commands. It supplies the small account/town
            // state block consumed before the client leaves character select.
            SelectCharacterCommand => CreateSelectCharacterReply(request),
            // CREATE_CHARACTER also consumes the command's generic one-byte
            // result. A successful result closes the creation panel and makes
            // this client refresh USERINFO.
            CreateCharacterCommand => CreateCommandReply(request, success: true),
            // DELETE_CHARACTER success carries the deleted character slot
            // after the common result byte. The client uses that slot to
            // remove the matching character card.
            DeleteCharacterCommand when request.Body.Length >= 3 =>
                CreateDeleteCharacterReply(request.Body[2]),
            // RETURN_SELECT_CHARACTER acknowledges the town-side transition.
            // The channel service follows this reply with a fresh USERINFO
            // roster after it has persisted and detached the active character.
            ReturnSelectCharacterCommand => CreateCommandReply(request, success: true),
            // The dungeon USE_SKILL command only needs the common success
            // acknowledgement. Its cube-item transaction is completed by a
            // following UPDATE_ITEM_LIST notification from the channel.
            UseSkillCommand => CreateUseSkillReply(),
            // GET_USERINFO is followed by an asynchronous USERINFO
            // notification rather than a command reply.
            GetUserInfoCommand => CreateEmptyUserInfo(),
            // DFLegacy starts both town-side polling systems as soon as its
            // local actor is created. Their replies use compact list layouts.
            PrivateStorePollCommand => CreatePrivateStorePollReply(),
            ExchangeServerCharacterInfoCommand =>
                CreateExchangeServerCharacterInfoReply(),
            _ => null
        };
    }

    public static GameServerPacket CreateCommandReply(PacketFrame request, bool success)
    {
        if (request.Body.Length < sizeof(ushort))
        {
            throw new ArgumentException("A game command must contain a two-byte sequence number.", nameof(request));
        }

        return new GameServerPacket(
            CommandPacketType,
            request.ProtocolId,
            [success ? (byte)1 : (byte)0]);
    }

    public static GameServerPacket CreateUseSkillReply() =>
        new(
            CommandPacketType,
            UseSkillCommand,
            [1]);

    public static GameServerPacket CreatePrivateStorePollReply() =>
        CreateMentorListReply(null, []);

    public static GameServerPacket CreateExchangeServerCharacterInfoReply() =>
        new(
            CommandPacketType,
            ExchangeServerCharacterInfoCommand,
            [1, 0]);           // result, character count

    public static GameServerPacket CreateDeleteCharacterReply(byte slot) =>
        new(
            CommandPacketType,
            DeleteCharacterCommand,
            [1, slot]);

    public static GameServerPacket CreateDeleteItemReply(
        byte listType,
        IReadOnlyList<GameItemConsumptionEntry> entries)
    {
        if (entries.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(listType);
        writer.Write((byte)entries.Count);
        foreach (var entry in entries)
        {
            writer.Write(entry.Slot);
            writer.Write(entry.ConsumedCount);
        }

        return new GameServerPacket(
            CommandPacketType,
            DeleteItemCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateDeleteItemError(
        byte listType,
        byte errorCode) =>
        new(
            CommandPacketType,
            DeleteItemCommand,
            [0, errorCode, listType]);

    public static GameServerPacket CreateUseStackableItemReply(
        ushort slot,
        byte listType) =>
        new(
            CommandPacketType,
            UseStackableItemCommand,
            [
                1,
                (byte)slot,
                (byte)(slot >> 8),
                listType
            ]);

    public static GameServerPacket CreateUseStackableItemError(
        byte errorCode,
        byte listType) =>
        new(
            CommandPacketType,
            UseStackableItemCommand,
            [0, errorCode, listType]);

    public static GameServerPacket CreateDungeonItemActionReply(
        ushort slot,
        byte listType) =>
        CreateUseStackableItemReply(slot, listType);

    public static GameServerPacket CreateCommandError(PacketFrame request, byte errorCode)
    {
        if (request.Body.Length < sizeof(ushort))
        {
            throw new ArgumentException("A game command must contain a two-byte sequence number.", nameof(request));
        }

        return new GameServerPacket(
            CommandPacketType,
            request.ProtocolId,
            [0, errorCode]);
    }

    public static GameServerPacket CreateGetItemSilentRejection(PacketFrame request)
    {
        if (request.ProtocolId != GetItemCommand)
        {
            throw new ArgumentException(
                "A silent item-pickup rejection requires a GET_ITEM request.",
                nameof(request));
        }

        return CreateGetItemSilentRejection();
    }

    /// <summary>
    /// 同上，但不需要请求帧：routing 由**得主会话**代收时，请求帧在**拾取者**那边，
    /// 得主会话只拿得到"这是 `CMD 46` 的失败"这一件事（协议号与错误码都是常量）。
    /// </summary>
    public static GameServerPacket CreateGetItemSilentRejection() =>
        CreateGetItemError(GetItemSilentRejectionError);

    /// <summary>
    /// `CMD 46` 的**失败**回包（官服 `CUser::SendCmdErrorPacket(46, code)` = body `[0, code]`）。
    /// 不需要请求帧，供"回包发给请求者会话"的路径使用（得主 ≠ 拾取者时）。
    /// </summary>
    public static GameServerPacket CreateGetItemError(byte errorCode) =>
        new(CommandPacketType, GetItemCommand, [0, errorCode]);

    /// <summary>
    /// `CMD 46` 的**成功**回包，不需要请求帧（理由同上）。
    /// </summary>
    public static GameServerPacket CreateGetItemAcceptance() =>
        new(CommandPacketType, GetItemCommand, [1]);

    public static GameServerPacket CreateEnterDungeonSelectionInsufficientFatigueReply(
        byte partyMemberIndex = 0) =>
        new(
            CommandPacketType,
            StartGameCommand,
            [0, EnterDungeonSelectionInsufficientFatigueError, partyMemberIndex]);

    public static GameServerPacket CreateDisjointItemReply(
        ushort sourceSlot,
        byte itemSpace,
        IReadOnlyList<GameDisjointRewardEntry> rewards)
    {
        if (rewards.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rewards));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(sourceSlot);
        writer.Write(itemSpace);
        writer.Write((byte)rewards.Count);
        foreach (var reward in rewards)
        {
            writer.Write(reward.DestinationSlot);
            writer.Write(reward.ItemId);
            writer.Write(reward.Count);
        }

        return new GameServerPacket(
            CommandPacketType,
            DisjointItemCommand,
            payload.ToArray());
    }

    public static bool TryParseCompoundItemCommand(
        ReadOnlySpan<byte> body,
        out GameCompoundItemCommand command)
    {
        command = default;
        const int bodyLength = sizeof(ushort) // command sequence
            + sizeof(ushort)                 // recipe item id or inventory slot
            + sizeof(byte)                   // source is item id
            + sizeof(byte)                   // craft count
            + sizeof(byte)                   // reserved zero
            + sizeof(byte);                  // fixed one
        if (body.Length != bodyLength
            || body[4] > 1
            || body[5] == 0
            || body[6] != 0
            || body[7] != 1)
        {
            return false;
        }

        command = new GameCompoundItemCommand(
            BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, sizeof(ushort))),
            body[4] != 0,
            body[5]);
        return true;
    }

    public static GameServerPacket CreateCompoundItemReply(
        IReadOnlyList<GameCompoundItemConsumption> consumedItems,
        IReadOnlyList<GameCompoundItemResult> resultItems)
    {
        ArgumentNullException.ThrowIfNull(consumedItems);
        ArgumentNullException.ThrowIfNull(resultItems);
        if (consumedItems.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(consumedItems));
        }

        if (resultItems.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(resultItems));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(checked((byte)consumedItems.Count));
        foreach (var item in consumedItems)
        {
            writer.Write(item.ListType);
            writer.Write(item.Slot);
            writer.Write(item.RemainingCount);
        }

        writer.Write(checked((byte)resultItems.Count));
        foreach (var item in resultItems)
        {
            writer.Write(item.Slot);
            writer.Write(item.ItemId);
            writer.Write(item.CountOrValue);
            writer.Write(item.State);
            writer.Write(item.Durability);
        }

        return new GameServerPacket(
            CommandPacketType,
            CompoundItemCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateCompoundItemFailure(byte errorCode) =>
        new(
            CommandPacketType,
            CompoundItemCommand,
            [0, errorCode]);

    public static bool TryParseUpgradeItemCommand(
        ReadOnlySpan<byte> body,
        out GameUpgradeItemCommand command)
    {
        command = default;
        const int bodyLength = sizeof(ushort) // command sequence
            + sizeof(ushort)                 // target slot
            + sizeof(ushort)                 // target item id
            + sizeof(ushort);                // material slot
        if (body.Length != bodyLength)
        {
            return false;
        }

        command = new GameUpgradeItemCommand(
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, sizeof(ushort))),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, sizeof(ushort))),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, sizeof(ushort))));
        return command.TargetItemId != 0
            && command.TargetSlot != command.MaterialSlot;
    }

    public static GameServerPacket CreateUpgradeItemReply(
        ushort materialSlot,
        uint materialRemaining,
        byte resultCode,
        byte newLevel,
        ushort targetSlot)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(materialSlot);
        writer.Write(materialRemaining);
        writer.Write(resultCode);
        writer.Write(newLevel);
        writer.Write(targetSlot);
        if (resultCode > 2)
        {
            writer.Write((byte)0);
        }

        return new GameServerPacket(
            CommandPacketType,
            UpgradeItemCommand,
            payload.ToArray());
    }

    public static bool TryParseResetItemAttributeCommand(
        ReadOnlySpan<byte> body,
        out GameResetItemAttributeCommand command)
    {
        command = default;
        const int bodyLength = sizeof(ushort) // command sequence
            + sizeof(ushort)                 // target slot
            + sizeof(ushort)                 // target item id
            + sizeof(ushort);                // material slot
        if (body.Length != bodyLength)
        {
            return false;
        }

        command = new GameResetItemAttributeCommand(
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, sizeof(ushort))),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, sizeof(ushort))),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, sizeof(ushort))));
        return command.TargetItemId != 0
            && command.TargetSlot != command.MaterialSlot;
    }

    public static GameServerPacket CreateResetItemAttributeReply(
        ushort materialSlot,
        uint materialRemaining,
        ushort operationType)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(materialSlot);
        writer.Write(materialRemaining);
        writer.Write(operationType);
        return new GameServerPacket(
            CommandPacketType,
            ResetItemAttributeCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateResetItemAttributeFailure(byte errorCode) =>
        new(
            CommandPacketType,
            ResetItemAttributeCommand,
            [0, errorCode]);

    public static bool TryParseCompoundAvatarCommand(
        ReadOnlySpan<byte> body,
        out GameCompoundAvatarCommand command)
    {
        command = default;
        const int bodyLength = sizeof(ushort) // command sequence
            + sizeof(ushort)                 // material slot
            + sizeof(ushort)                 // first item-list marker
            + sizeof(ushort)                 // first avatar item id
            + sizeof(ushort)                 // second item-list marker
            + sizeof(ushort)                 // second avatar item id
            + sizeof(ushort)                 // option-list marker
            + sizeof(byte)                   // selected option row
            + sizeof(byte)                   // first avatar slot
            + sizeof(byte);                  // second avatar slot
        if (body.Length != bodyLength)
        {
            return false;
        }

        command = new GameCompoundAvatarCommand(
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, sizeof(ushort))),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, sizeof(ushort))),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(10, sizeof(ushort))),
            body[14],
            body[15],
            body[16]);
        return true;
    }

    public static GameServerPacket CreateCompoundAvatarReply(
        IEnumerable<GameAvatarCompoundConsumption> consumedItems,
        ushort resultSlot,
        ushort resultItemId,
        uint remainingSeconds,
        ushort selectedOption)
    {
        var consumed = (consumedItems ?? [])
            .Take(byte.MaxValue)
            .ToArray();
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(checked((byte)consumed.Length));
        foreach (var item in consumed)
        {
            writer.Write(item.ListType);
            writer.Write(item.Slot);
            writer.Write(item.RemainingCount);
        }

        writer.Write(resultSlot);
        writer.Write(resultItemId);
        writer.Write(remainingSeconds);
        writer.Write(selectedOption);
        return new GameServerPacket(
            CommandPacketType,
            CompoundAvatarCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateCompoundAvatarFailure(byte errorCode = 4) =>
        new(
            CommandPacketType,
            CompoundAvatarCommand,
            [0, errorCode]);

    public static GameServerPacket CreateUseLotteryItemReply(
        ushort sourceSlot,
        ushort destinationSlot,
        ushort itemId,
        uint countOrValue,
        ushort instanceValue = 0,
        byte itemAttribute = 0)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(sourceSlot);
        writer.Write(destinationSlot);
        writer.Write(itemId);
        writer.Write(countOrValue);
        writer.Write(instanceValue);
        writer.Write(itemAttribute);
        return new GameServerPacket(
            CommandPacketType,
            UseLotteryItemCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateIncreaseStatusReply(
        ushort sourceSlot,
        byte statusType,
        int amount,
        ushort firstAuxiliaryValue = 0,
        ushort secondAuxiliaryValue = 0)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(sourceSlot);
        writer.Write(statusType);
        writer.Write(amount);
        writer.Write(firstAuxiliaryValue);
        writer.Write(secondAuxiliaryValue);
        return new GameServerPacket(
            CommandPacketType,
            IncreaseStatusCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateMailboxOpenReply(ushort notLoadedCount = 0)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(notLoadedCount);
        return new GameServerPacket(
            CommandPacketType,
            MailboxOpenCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateMailboxExtractReply(uint mailId)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(mailId);
        return new GameServerPacket(
            CommandPacketType,
            MailboxExtractItemCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateMailboxStateReply(uint mailId, ushort state)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(mailId);
        writer.Write(state);
        return new GameServerPacket(
            CommandPacketType,
            MailboxStateCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateMailboxList(
        IReadOnlyList<GameMailEntry> mails,
        short notLoadedCount = 0,
        bool reset = true,
        bool includeEmptyPackageCarriers = false)
    {
        ArgumentNullException.ThrowIfNull(mails);
        if (mails.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(mails));
        }

        foreach (var mail in mails)
        {
            ValidateMailEntry(mail);
        }

        var packageMails = includeEmptyPackageCarriers
            ? mails.ToArray()
            : mails
                .Where(mail => mail.Gold != 0 || mail.Attachment is not null)
                .ToArray();

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // The first section is a reward-package list, not a mail-summary list.
        // Text-only and archived mail belongs only to the body section below.
        writer.Write((byte)packageMails.Length);
        writer.Write(reset ? (byte)0 : (byte)1);
        foreach (var mail in packageMails)
        {
            var attachment = mail.Attachment;
            writer.Write(mail.MailId);
            WriteLengthPrefixedBytes(writer, mail.SenderBytes);
            writer.Write(mail.Gold);
            writer.Write(attachment?.ItemId ?? (ushort)0);

            // DFLegacy's mailbox item tuple predates ITEM_LIST and uses a
            // different byte order: seal, instance/value, durability, state.
            writer.Write(attachment?.SealState ?? (byte)0);
            writer.Write(attachment?.CountOrValue ?? 0u);
            writer.Write(attachment?.Durability ?? (ushort)0);
            writer.Write(attachment?.State ?? (byte)0);
            writer.Write(mail.SentAt);
            writer.Write(mail.MailId); // joins the package and text records
        }

        writer.Write(notLoadedCount);

        // Every visible mail has a body record. The state selects the client's
        // inbox (1/2) or archive (3); MailId correlates it with a package above.
        writer.Write((ushort)mails.Count);
        foreach (var mail in mails)
        {
            writer.Write(mail.MailId);
            writer.Write(mail.ExpiresAt);
            WriteLengthPrefixedBytes(writer, mail.SenderBytes);
            WriteLengthPrefixedBytes(writer, mail.TextBytes);
            writer.Write(mail.SentAt);
            writer.Write(mail.State);
        }

        return new GameServerPacket(
            NotificationPacketType,
            MailboxMailListNotification,
            payload.ToArray());
    }

    public static IReadOnlyList<GameServerPacket> CreateMailboxSnapshot(
        IReadOnlyList<GameMailEntry> mails,
        short notLoadedCount = 0)
    {
        ArgumentNullException.ThrowIfNull(mails);

        var packets = new List<GameServerPacket>();
        var reset = true;

        // The old client carries one package-header id into every body in the
        // same NOTI 97. Restore archived bodies one at a time while the inbox
        // is empty so their empty carriers set that id without adding rows.
        foreach (var archivedMail in mails.Where(mail => mail.State == 3))
        {
            packets.Add(CreateMailboxList(
                [archivedMail],
                notLoadedCount,
                reset,
                includeEmptyPackageCarriers: true));
            reset = false;
        }

        var inboxMails = mails
            .Where(mail => mail.State != 3)
            .ToArray();
        if (inboxMails.Length != 0 || packets.Count == 0)
        {
            packets.Add(CreateMailboxList(
                inboxMails,
                notLoadedCount,
                reset));
        }

        return packets;
    }

    public static GameServerPacket CreateMailboxRemoveMail(
        IReadOnlyCollection<uint> mailIds)
    {
        ArgumentNullException.ThrowIfNull(mailIds);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(checked((uint)mailIds.Count));
        foreach (var mailId in mailIds)
        {
            writer.Write(mailId);
        }

        return new GameServerPacket(
            NotificationPacketType,
            MailboxRemoveMailNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateMailboxAlarm(short newMailCount) =>
        new(
            NotificationPacketType,
            MailboxAlarmNotification,
            [(byte)newMailCount, (byte)(newMailCount >> 8)]);

    private static void ValidateMailEntry(GameMailEntry mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (mail.MailId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mail), "Mail ids must be non-zero.");
        }

        if (mail.SenderBytes.Length is 0 or >= 30)
        {
            throw new ArgumentException(
                "DFLegacy mail senders must contain 1-29 encoded bytes.",
                nameof(mail));
        }

        if (mail.TextBytes.Length >= 512)
        {
            throw new ArgumentException(
                "DFLegacy mail text must contain at most 511 encoded bytes.",
                nameof(mail));
        }

        if (mail.Attachment is { ItemId: 0 })
        {
            throw new ArgumentException(
                "Mail attachments must have a non-zero item id.",
                nameof(mail));
        }

        if (mail.State is not (1 or 2 or 3))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mail),
                "DFLegacy mail states must be received, read or archived.");
        }
    }

    public static GameServerPacket CreateSelectCharacterReply(
        PacketFrame request,
        uint characterId = 1,
        ushort serverId = 1,
        ushort usedFatigue = 0,
        ushort maximumFatigue = DefaultMaximumFatigue,
        ushort premiumFatigue = 0,
        uint cash = 0,
        byte townId = 1,
        uint completedTutorialFlags = DefaultCompletedTutorialFlags,
        IReadOnlyList<GameQuestEntry>? activeQuests = null,
        IReadOnlyList<ushort>? completedQuestIds = null,
        IReadOnlyList<GamePremiumServiceEntry>? premiumServices = null,
        IReadOnlyDictionary<ushort, int>? questCompletionMapping = null)
    {
        if (request.Type != CommandPacketType
            || request.ProtocolId != SelectCharacterCommand
            || request.Body.Length < 3)
        {
            throw new ArgumentException(
                "SELECT_CHARACTER must contain a two-byte sequence and a character slot.",
                nameof(request));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);       // result
        writer.Write(characterId);
        writer.Write(serverId);
        writer.Write(usedFatigue);
        writer.Write(maximumFatigue);
        writer.Write(premiumFatigue);
        premiumServices ??= [];
        if (premiumServices.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(premiumServices));
        }

        writer.Write((byte)premiumServices.Count);
        foreach (var premiumService in premiumServices)
        {
            writer.Write(premiumService.ServiceType);
            writer.Write(premiumService.RemainSeconds);
        }

        writer.Write(cash);

        activeQuests ??= [];
        completedQuestIds ??= [];
        if (activeQuests.Count > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(activeQuests));
        }

        if (completedQuestIds.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(completedQuestIds));
        }

        if (completedQuestIds.Count != 0 && questCompletionMapping is null)
        {
            throw new NotSupportedException(
                "DFLegacy completed quests require the PVF quest completion mapping.");
        }

        // DFLegacy consumes exactly three (u16 quest id, u32 trigger) slots.
        // FFFF marks an unused slot; zero is a real quest id.
        for (var index = 0; index < 3; index++)
        {
            if (index < activeQuests.Count)
            {
                writer.Write(activeQuests[index].QuestId);
                writer.Write(activeQuests[index].Trigger);
            }
            else
            {
                writer.Write(ushort.MaxValue);
                writer.Write(0u);
            }
        }

        // DF2008 0x40FFF2: u8 group, u32 byte length, 32 bytes of bits.
        // 0x4EEB60 maps each bit through questmappingtable.tbl, not quest ID.
        var completedGroups = new SortedDictionary<byte, byte[]>();
        foreach (var questId in completedQuestIds.Distinct())
        {
            if (!questCompletionMapping!.TryGetValue(questId, out var index))
            {
                continue;
            }

            var group = index / 512;
            var bit = index % 512;
            if (index < 0 || group > 8 || bit >= 256)
            {
                throw new ArgumentOutOfRangeException(nameof(questCompletionMapping));
            }

            var groupId = (byte)group;
            if (!completedGroups.TryGetValue(groupId, out var bitmap))
            {
                bitmap = new byte[32];
                completedGroups.Add(groupId, bitmap);
            }
            bitmap[bit / 8] |= (byte)(1 << (bit % 8));
        }
        writer.Write((byte)completedGroups.Count);
        foreach (var (group, bitmap) in completedGroups)
        {
            writer.Write(group);
            writer.Write((uint)bitmap.Length);
            writer.Write(bitmap);
        }
        writer.Write(townId);
        // DFLegacy reads this dword directly into its lower 32 tutorial flags.
        // A newly created character receives zero until the mandatory tutorial
        // reports its final module; migrated and preset characters receive all bits.
        writer.Write(completedTutorialFlags);

        return new GameServerPacket(
            CommandPacketType,
            SelectCharacterCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreatePremiumServiceNotification(
        ushort action,
        byte serviceType,
        uint remainSeconds)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(action);
        writer.Write(serviceType);
        writer.Write(remainSeconds);
        return new GameServerPacket(
            NotificationPacketType,
            PremiumServiceNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateMessageNotification(
        GameMessageType messageType,
        ushort targetAreaUserId,
        ReadOnlySpan<byte> messageBytes) =>
        CreateChatMessageNotification(
            MessageNotification,
            messageType,
            targetAreaUserId,
            messageBytes);

    public static GameServerPacket CreateCreatureScriptMessageNotification(
        GameMessageType messageType,
        ushort targetAreaUserId,
        ReadOnlySpan<byte> messageBytes) =>
        CreateChatMessageNotification(
            CreatureScriptMessageNotification,
            messageType,
            targetAreaUserId,
            messageBytes);

    public static GameServerPacket CreateCreatureMessageNotification(
        GameMessageType messageType,
        ushort targetAreaUserId,
        ReadOnlySpan<byte> messageBytes) =>
        CreateChatMessageNotification(
            CreatureMessageNotification,
            messageType,
            targetAreaUserId,
            messageBytes);

    private static GameServerPacket CreateChatMessageNotification(
        byte notificationId,
        GameMessageType messageType,
        ushort targetAreaUserId,
        ReadOnlySpan<byte> messageBytes)
    {
        // The 2008 client uses a fixed 0x100-byte destination buffer and only
        // accepts non-empty strings whose encoded length is below that size.
        // DNF.exe 0x41CA99 parses NOTI 126 and 131 through this same payload
        // layout; the notification id only selects the final display mode.
        if (messageBytes.Length is < 1 or >= 0x100)
        {
            throw new ArgumentOutOfRangeException(nameof(messageBytes));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)messageType);
        writer.Write(targetAreaUserId);
        writer.Write((uint)messageBytes.Length);
        writer.Write(messageBytes);
        return new GameServerPacket(
            NotificationPacketType,
            notificationId,
            payload.ToArray());
    }

    public static GameServerPacket CreatePopupNotification(
        ReadOnlySpan<byte> messageBytes)
    {
        // NOTI 126 is the legacy CREATURE_MESSAGE packet. Message type zero
        // formats the received text with client string 640 and opens popup 44.
        if (messageBytes.Length is < 1 or >= 0x100)
        {
            throw new ArgumentOutOfRangeException(nameof(messageBytes));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)0);
        writer.Write((ushort)0);
        writer.Write((uint)messageBytes.Length);
        writer.Write(messageBytes);
        return new GameServerPacket(
            NotificationPacketType,
            CreatureMessageNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateItemList(
        byte listType,
        IReadOnlyList<GameInventoryEntry> items,
        ushort listParameter = 0)
    {
        if (items.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(items));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(listType);
        if (listType == 2)
        {
            writer.Write(listParameter);
        }

        writer.Write((ushort)items.Count);
        foreach (var item in items)
        {
            writer.Write(item.Slot);
            writer.Write(item.ItemId);
            writer.Write(item.CountOrValue);
            writer.Write(item.State);
            writer.Write(item.Durability);
            writer.Write(item.SealState);
        }

        return new GameServerPacket(
            NotificationPacketType,
            ItemListNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateMainItemList(
        uint gold,
        IReadOnlyList<GameInventoryEntry> items)
    {
        return CreateMainItemList(gold, victoryPoints: 0, items);
    }

    public static GameServerPacket CreateMainItemList(
        uint gold,
        uint victoryPoints,
        IReadOnlyList<GameInventoryEntry> items)
    {
        if (items.Count > ushort.MaxValue - 2)
        {
            throw new ArgumentOutOfRangeException(nameof(items));
        }

        // DFLegacy keeps three virtual currency objects in main-list slots 0..2:
        // gold (slot 0/item 0), revival coins (slot 1/item 1), and victory
        // points (slot 2/item 2). ITEM_LIST has no separate wallet header.
        // Revival coins remain inventory-backed, while the two account/character
        // numeric balances are synthesized here on every full main-list update.
        var entries = new GameInventoryEntry[items.Count + 2];
        entries[0] = new GameInventoryEntry(
            Slot: 0,
            ItemId: 0,
            CountOrValue: gold);
        entries[1] = new GameInventoryEntry(
            Slot: 2,
            ItemId: 2,
            CountOrValue: victoryPoints);
        for (var index = 0; index < items.Count; index++)
        {
            entries[index + 2] = items[index];
        }

        return CreateItemList(0, entries);
    }

    public static GameServerPacket CreateUpdateItemList(
        byte listType,
        IReadOnlyList<GameInventoryEntry> items)
    {
        if (items.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(items));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(listType);
        writer.Write((ushort)items.Count);
        foreach (var item in items)
        {
            writer.Write(item.Slot);
            writer.Write(item.ItemId);
            writer.Write(item.CountOrValue);
            writer.Write(item.State);
            writer.Write(item.Durability);
            writer.Write(item.SealState);
        }

        return new GameServerPacket(
            NotificationPacketType,
            UpdateItemListNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCreatureInfo(
        IReadOnlyList<GameCreatureInfoEntry> creatures)
    {
        if (creatures.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(creatures));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(checked((byte)creatures.Count));
        foreach (var creature in creatures)
        {
            if (creature.Uid == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(creatures));
            }

            if (creature.NameBytes.Length >= 30)
            {
                throw new ArgumentOutOfRangeException(nameof(creatures));
            }

            writer.Write(creature.Uid);
            writer.Write(creature.Stomach);
            writer.Write(creature.Experience);
            writer.Write(creature.Level);
            WriteLengthPrefixedBytes(writer, creature.NameBytes);
            writer.Write((byte)(creature.NoCharge ? 1 : 0));
        }

        return new GameServerPacket(
            NotificationPacketType,
            CreatureInfoNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCreatureState(
        uint creatureUid,
        uint state)
    {
        if (creatureUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creatureUid));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(creatureUid);
        writer.Write(state);
        return new GameServerPacket(
            NotificationPacketType,
            CreatureStateNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateDiedCreature(ushort userId)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // NOTI 100 is not keyed by the Creature uid.  The old client first
        // resolves this area-user id and then marks that user's Creature map
        // object as dead.
        writer.Write(userId);
        return new GameServerPacket(
            NotificationPacketType,
            DiedCreatureNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateRevivalCreature(ushort userId)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // NOTI 107 has the same one-field layout as NOTI 100.  It clears the
        // death flag on the resolved Creature object.
        writer.Write(userId);
        return new GameServerPacket(
            NotificationPacketType,
            RevivalCreatureNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCreatureGainExperience(
        byte level,
        uint cumulativeExperience)
    {
        if (level == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // DF2008 NOTI 102 has no Creature uid or normal/grow discriminator.
        // The client applies this fixed record to the currently equipped pet.
        writer.Write(level);
        writer.Write(cumulativeExperience);
        return new GameServerPacket(
            NotificationPacketType,
            CreatureGainExperienceNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCreatureEvolution(
        byte evolutionCreatureId,
        ushort userId)
    {
        if (evolutionCreatureId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(evolutionCreatureId));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(evolutionCreatureId);
        writer.Write(userId);
        return new GameServerPacket(
            NotificationPacketType,
            CreatureEvolutionNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCreatureRename(
        ushort userId,
        byte[] nameBytes)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (nameBytes.Length is 0 or >= 20)
        {
            throw new ArgumentOutOfRangeException(nameof(nameBytes));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(userId);
        WriteLengthPrefixedBytes(writer, nameBytes);
        return new GameServerPacket(
            NotificationPacketType,
            CreatureRenameNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCreatureResponse(ushort userId)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(userId);
        return new GameServerPacket(
            NotificationPacketType,
            CreatureResponseNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateHatchCreatureReply(
        bool success,
        byte errorCode = 1) =>
        new(
            CommandPacketType,
            HatchCreatureCommand,
            success ? [1] : [0, errorCode]);

    public static GameServerPacket CreateRenameCreatureReply(
        bool success,
        ushort cardSlot = 0,
        byte itemSpace = 7,
        byte errorCode = 1)
    {
        if (!success)
        {
            return new(
                CommandPacketType,
                RenameCreatureCommand,
                [0, errorCode]);
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(cardSlot);
        writer.Write(itemSpace);
        return new GameServerPacket(
            CommandPacketType,
            RenameCreatureCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateMoveItemSpaceReply(
        byte sourceListType,
        ushort sourceSlot,
        uint movedCount,
        byte destinationListType,
        ushort destinationSlot)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(sourceListType);
        writer.Write(sourceSlot);
        writer.Write(movedCount);
        writer.Write(destinationListType);
        writer.Write(destinationSlot);

        return new GameServerPacket(
            CommandPacketType,
            MoveItemSpaceCommand,
            payload.ToArray());
    }

    /// <summary>
    /// CMD 19 失败：真机反汇编（recon-2 §4.1，客户端 handler 0x4110FE）读的是
    /// <c>[0, u8 errorCode, u8 srcType, u8 destType]</c> 共 4 字节，随后按 errorCode 弹文案，
    /// 末尾只用 srcType/destType 刷新显示。**不要**照抄成功回包的 11 字节回显：
    /// 那会让客户端把 srcType 当 errorCode（0 不在它的 switch 里 ⇒ 一个字都不弹，面板毫无变化）。
    /// 成功回包仍是 11 字节 <see cref="CreateMoveItemSpaceReply"/>。
    /// </summary>
    public static GameServerPacket CreateMoveItemSpaceFailure(
        byte errorCode,
        byte sourceListType,
        byte destinationListType) =>
        new(
            CommandPacketType,
            MoveItemSpaceCommand,
            [0, errorCode, sourceListType, destinationListType]);

    // ACT1 CMD 19's zero-code branch releases the pending move in both lists
    // without an alert. The caller supplies both authoritative item snapshots.
    public static GameServerPacket CreateMoveItemSpaceSnapshotReply(
        byte sourceListType,
        byte destinationListType) =>
        CreateMoveItemSpaceFailure(0, sourceListType, destinationListType);

    public static GameServerPacket CreateSortItemReply(byte itemSpace) =>
        new(CommandPacketType, SortItemCommand, [1, itemSpace]);

    public static GameServerPacket CreateSortItemError(
        byte itemSpace,
        byte errorCode = 1) =>
        new(CommandPacketType, SortItemCommand, [0, errorCode, itemSpace]);

    public static GameServerPacket CreateBuyItemReply(
        uint updatedGold,
        uint updatedVictoryPoints,
        uint auxiliaryItemSpaceValue,
        uint updatedCera,
        ushort slot,
        ushort itemId,
        uint countOrValue)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        // DF2008's command-21 success handler reads four complete wallet values
        // before the purchased item tuple. In particular, the fourth u32 is
        // copied into the Cera balance. A shorter reply makes the client read
        // its ASCII-'0' receive-buffer padding as 0x30303030 (808464432 Cera).
        writer.Write(updatedGold);
        writer.Write(updatedVictoryPoints);
        writer.Write(auxiliaryItemSpaceValue);
        writer.Write(updatedCera);
        writer.Write(slot);
        writer.Write(itemId);
        writer.Write(countOrValue);
        return new GameServerPacket(
            CommandPacketType,
            BuyItemCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateCeraShopBuyReply(
        int shopCategory,
        uint commodityNo,
        IReadOnlyList<GameCeraShopReward>? rewards = null,
        bool isGift = false)
    {
        rewards ??= [];
        if (rewards.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rewards),
                "DF2008 command 67 supports at most 65535 result entries.");
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write((byte)(isGift ? 1 : 0));
        writer.Write(Math.Clamp(shopCategory, 0, 3));
        writer.Write(commodityNo);
        // The DF2008 client keys its Cera result-item map by these values.
        // Keeping them equal also selects the normal purchase-completion path.
        writer.Write(commodityNo);
        writer.Write(commodityNo);
        writer.Write(ushort.MaxValue);
        writer.Write((ushort)rewards.Count);
        foreach (var reward in rewards)
        {
            writer.Write(reward.ItemId);
            writer.Write(reward.Count);
        }

        writer.Write(ushort.MaxValue);
        writer.Write(0u);
        return new GameServerPacket(
            CommandPacketType,
            CeraShopBuyCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateCeraShopBuyError(
        byte errorCode,
        int shopCategory = 0,
        uint commodityNo = uint.MaxValue)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)0);
        writer.Write((byte)0); // ACT1 reads this unused byte before the byte error.
        writer.Write(errorCode);
        writer.Write(Math.Clamp(shopCategory, 0, 3));
        writer.Write(commodityNo);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)0);
        return new GameServerPacket(
            CommandPacketType,
            CeraShopBuyCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateCeraTicketReply(
        string billingKey,
        uint ticketNumber = 0)
    {
        var keyBytes = System.Text.Encoding.ASCII.GetBytes(billingKey);
        if (keyBytes.Length is 0 or >= 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(billingKey),
                "The DFLegacy Cera ticket must contain 1 to 63 ASCII bytes.");
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write((uint)keyBytes.Length);
        writer.Write(keyBytes);
        writer.Write(ticketNumber);
        return new GameServerPacket(
            CommandPacketType,
            GenerateCeraTicketCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateCeraUpdate(bool success, uint value)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(success ? (byte)1 : (byte)0);
        writer.Write(value);
        return new GameServerPacket(
            NotificationPacketType,
            CeraUpdateNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateSellItemReply(
        uint updatedGold,
        byte itemSpace,
        ushort slot,
        ushort soldCount)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(updatedGold);
        writer.Write(itemSpace);
        writer.Write(slot);
        writer.Write(soldCount);
        return new GameServerPacket(
            CommandPacketType,
            SellItemCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateRepairEquipmentReply(
        uint updatedGold,
        byte itemSpace,
        ushort slot)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(updatedGold);
        writer.Write(itemSpace);
        writer.Write(slot);
        return new GameServerPacket(
            CommandPacketType,
            RepairEquipmentCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateDecreaseDurabilityReply(byte slot) =>
        new(
            CommandPacketType,
            DecreaseDurabilityCommand,
            [1, slot]);

    public static bool TryParseDropItemCommand(
        ReadOnlySpan<byte> body,
        out GameDropItemCommand command)
    {
        command = default;
        const int bodyLength = sizeof(ushort) // command sequence
            + sizeof(short)                  // map x
            + sizeof(short)                  // map y
            + sizeof(byte)                   // item space
            + sizeof(ushort)                 // source slot
            + sizeof(uint);                  // count
        if (body.Length != bodyLength)
        {
            return false;
        }

        command = new GameDropItemCommand(
            BinaryPrimitives.ReadInt16LittleEndian(body.Slice(2, sizeof(short))),
            BinaryPrimitives.ReadInt16LittleEndian(body.Slice(4, sizeof(short))),
            body[6],
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(7, sizeof(ushort))),
            BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(9, sizeof(uint))));
        return command.Count > 0;
    }

    public static GameServerPacket CreateDropItemReply(
        byte itemSpace,
        ushort slot,
        uint count)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(itemSpace);
        writer.Write(slot);
        writer.Write(count);
        return new GameServerPacket(
            CommandPacketType,
            DropItemCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateDropItemErrorReply(
        byte errorCode,
        byte itemSpace) =>
        new(
            CommandPacketType,
            DropItemCommand,
            [0, errorCode, itemSpace]);

    /// <summary>
    /// NOTI 7：被请求方弹「接受/拒绝」小窗。<paramref name="weightDelta"/> 必须是请求方 CMD 10 带来的
    /// 原值「当前负重 − 负重上限」（克，负数走二补码）：客户端 type==1 分支把它写进
    /// <c>CNUser+0xF4</c>（0x41B5E9），锁定时的负重自检（0x4623A0）直接读它。
    /// 传 uid 或 0 会让对方算出 1 克的假缺额并拒绝发锁定包。
    /// </summary>
    public static GameServerPacket CreateRequestPeerNotification(ushort requesterUid, uint weightDelta)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(requesterUid);
        writer.Write((byte)1);
        writer.Write(weightDelta);
        return new GameServerPacket(
            NotificationPacketType,
            RequestPeerNotification,
            payload.ToArray());
    }

    /// <summary>
    /// NOTI 8：请求方开窗并在聊天栏打「[X]已接受交易请求」；<paramref name="weightDelta"/> 必须是
    /// 接受方 CMD 11 带来的原值（0x41B727 写进请求方客户端上的 <c>CNUser+0xF4</c>），mode 填 0。
    /// </summary>
    public static GameServerPacket CreateResponsePeerNotification(
        ushort accepterUid,
        uint weightDelta,
        byte mode)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(accepterUid);
        writer.Write((byte)1);
        writer.Write(weightDelta);
        writer.Write(mode);
        return new GameServerPacket(
            NotificationPacketType,
            ResponsePeerNotification,
            payload.ToArray());
    }

    /// <summary>
    /// NOTI 8（组队）：请求方聊天栏打出"[X]已接受组队请求"（dnf.str 0x270，A 卷 §5）。
    /// 组队分支**不开窗、不建队伍**（那要靠 NOTI 9）；尾部的 u32 客户端在组队分支不读，填 0。
    /// </summary>
    public static GameServerPacket CreatePartyAcceptedNotification(ushort accepterUid)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(accepterUid);
        writer.Write((byte)0);
        writer.Write(0u);
        return new GameServerPacket(NotificationPacketType, ResponsePeerNotification, payload.ToArray());
    }

    /// <summary>CMD 11 成功：给接受方开交易窗（0x4104FF 分支）。</summary>
    public static GameServerPacket CreateResponsePeerReply(ushort requesterUid, byte mode)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(requesterUid);
        writer.Write((byte)1);
        writer.Write(mode);
        return new GameServerPacket(
            CommandPacketType,
            ResponsePeerCommand,
            payload.ToArray());
    }

    /// <summary>CMD 11 失败：peerType 100/code 1 = 「对方不在线，或拒绝了您的交易请求」。</summary>
    public static GameServerPacket CreateResponsePeerError(byte peerType, byte code) =>
        new(CommandPacketType, ResponsePeerCommand, [0, peerType, code]);

    /// <summary>
    /// NOTI 9：队伍名册（客户端建立队伍与刷新成员列表的**唯一**来源；A 卷 §6）。
    /// body 无序列号前缀，布局 = u16 count(=1), u16 partyId, u8 kind(=0), u8 nameSel, u8 maxMembers,
    /// {u16 slotUid; u8 memberByte}×4（**交错**，合并卷 §2.1）, u8 leaderSlot —— 共 20 字节。
    /// <paramref name="slotUids"/> 必须 4 项：**空槽只能是 0xFFFF；非空槽永远填一个真实的成员 uid**
    /// （收包者解析不到时只是那一行空白）。**绝不能填 0**：真机上 0 会被客户端当成"该成员已离开"，
    /// 跨区域时两端会同时弹出"队伍中只剩下[X]"并散队（设计稿 §2.2.2 的真机证据）。
    /// memberByte 语义未知，恒 0。
    /// </summary>
    public static GameServerPacket CreatePartyInfoNotification(
        ushort partyId,
        byte nameSel,
        byte maxMembers,
        ReadOnlySpan<ushort> slotUids,
        byte leaderSlot)
    {
        if (slotUids.Length != PartyMemberSlotCount)
        {
            throw new ArgumentException(
                $"Party rosters always carry {PartyMemberSlotCount} slots.",
                nameof(slotUids));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((ushort)1);
        writer.Write(partyId);
        writer.Write((byte)0);
        writer.Write(nameSel);
        writer.Write(maxMembers);
        for (var slot = 0; slot < PartyMemberSlotCount; slot++)
        {
            writer.Write(slotUids[slot]);
            writer.Write((byte)0);
        }

        writer.Write(leaderSlot);
        return new GameServerPacket(NotificationPacketType, PartyInfoNotification, payload.ToArray());
    }

    /// <summary>
    /// NOTI 9 的解散形式：1 条 kind=3 记录（**不带**名册，必须每条单独发；A 卷 §6.3）。
    /// 客户端据此清空 4 个槽、按 uid 把该队伍从本地队伍表摘除、清成员的队伍指针，并提示"队伍已解散"。
    /// </summary>
    public static GameServerPacket CreatePartyDisbandNotification(ushort partyId)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((ushort)1);
        writer.Write(partyId);
        writer.Write((byte)3);
        return new GameServerPacket(NotificationPacketType, PartyInfoNotification, payload.ToArray());
    }

    /// <summary>
    /// NOTI 10：被踢（kind=0）/ 掉线（kind=1）离队。载荷 = u8 槽位(0..3) + u8 kind。
    /// **必须早于**把该槽清空的 NOTI 9 发出：它的分支按槽位查成员对象取名字（B 卷 §4.1）。
    /// </summary>
    public static GameServerPacket CreateWalkoutNotification(byte slot, bool disconnected)
    {
        if (slot >= PartyMemberSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return new GameServerPacket(
            NotificationPacketType,
            WalkoutNotification,
            [slot, disconnected ? (byte)1 : (byte)0]);
    }

    /// <summary>
    /// NOTI 7（组队）：被请求者的应答窗。比交易版多两个 u16（A 卷 §3.1）：
    /// u16 请求者 uid, u8 peerType(=0), u32 复活币, u16 疲劳值, u16 虚弱% —— 共 11 字节。
    /// </summary>
    public static GameServerPacket CreateRequestPartyNotification(
        ushort requesterUid,
        uint reviveCoins,
        ushort fatigue,
        ushort weaknessPercent)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(requesterUid);
        writer.Write((byte)0);
        writer.Write(reviveCoins);
        writer.Write(fatigue);
        writer.Write(weaknessPercent);
        return new GameServerPacket(NotificationPacketType, RequestPeerNotification, payload.ToArray());
    }

    /// <summary>命令通道的失败回包：body = [resultFlag=0, errorCode]（B 卷 §6.4 读出的失败码 switch）。</summary>
    public static GameServerPacket CreatePartyCommandError(byte commandId, byte errorCode) =>
        new(CommandPacketType, commandId, [0, errorCode]);

    /// <summary>CMD 26 结果：成功 [1]；失败 [0, errorCode]（1 = 无法继续交易，4 = 对方物品栏空间不足）。</summary>
    public static GameServerPacket CreateSetItemTradeStateResult(bool success, byte errorCode) =>
        success
            ? new GameServerPacket(CommandPacketType, SetItemTradeStateCommand, [1])
            : new GameServerPacket(CommandPacketType, SetItemTradeStateCommand, [0, errorCode]);

    /// <summary>NOTI 15：对方交易栏某格变化（itemId 0xFFFF = 清格）。</summary>
    public static GameServerPacket CreateChangeItemTradeItem(GameItemTradeEntry entry)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(entry.Slot);
        writer.Write(entry.ItemId);
        writer.Write(entry.CountOrValue);
        writer.Write(entry.State);
        writer.Write(entry.Durability);
        return new GameServerPacket(
            NotificationPacketType,
            ChangeItemTradeItemNotification,
            payload.ToArray());
    }

    /// <summary>NOTI 16：取消/超时/中止时把面板物品按原 (栏,格) 退回。</summary>
    public static GameServerPacket CreateCancelItemTrade(
        IReadOnlyList<GameItemTradeCancelEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((ushort)entries.Count);
        foreach (var entry in entries)
        {
            writer.Write(entry.PanelSlot);
            writer.Write(entry.DestinationListType);
            writer.Write(entry.DestinationSlot);
        }

        return new GameServerPacket(
            NotificationPacketType,
            CancelItemTradeNotification,
            payload.ToArray());
    }

    /// <summary>NOTI 17：锁定状态广播；接收端按 uid 是否等于自己决定写 myLock 还是 otherLock。</summary>
    public static GameServerPacket CreateStateItemTrade(ushort uid, bool locked) =>
        new(NotificationPacketType, StateItemTradeNotification, [.. BitConverter.GetBytes(uid), locked ? (byte)1 : (byte)0]);

    /// <summary>NOTI 18：完成交付，客户端自行并包并关窗。</summary>
    public static GameServerPacket CreateFinishItemTrade(
        IReadOnlyList<GameItemTradeDeliveryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((ushort)entries.Count);
        foreach (var entry in entries)
        {
            writer.Write(entry.OtherPanelSlot);
            writer.Write(entry.DestinationSlot);
        }

        return new GameServerPacket(
            NotificationPacketType,
            FinishItemTradeNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateChangeSkillSlotReply(
        byte sourceSlot,
        byte destinationSlot) =>
        new(
            CommandPacketType,
            ChangeSkillSlotCommand,
            [1, sourceSlot, destinationSlot]);

    public static GameServerPacket CreateBuySkillReply(
        ushort remainingSkillPoints,
        byte slot,
        byte skillId,
        byte level)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(remainingSkillPoints);
        writer.Write(slot);
        writer.Write(skillId);
        writer.Write(level);
        return new GameServerPacket(CommandPacketType, BuySkillCommand, payload.ToArray());
    }

    public static GameServerPacket CreateSkillInfo(
        ushort skillPoints,
        IReadOnlyList<GameSkillEntry> skills)
    {
        if (skills.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(skills));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(skillPoints);
        writer.Write((byte)skills.Count);
        for (var index = 0; index < skills.Count; index++)
        {
            var skill = skills[index];
            // DNF 1.0.1.9 protocol 19 records are slot/id/level triples.
            // When no stable UI slot is persisted yet, assign compact slots in
            // packet order. The learned-skill list in USERINFO remains id/level.
            writer.Write(skill.Slot ?? checked((byte)index));
            writer.Write(skill.SkillId);
            writer.Write(skill.Level);
        }

        writer.Write((byte)0); // no per-skill auxiliary values
        return new GameServerPacket(
            NotificationPacketType,
            SkillInfoNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateStamina(byte value) =>
        new(NotificationPacketType, StaminaNotification, [value]);

    public static GameServerPacket CreatePvpRecord(
        int wins,
        int losses,
        int pvpPoints,
        int currentRankPoint,
        int nextRankPoint,
        byte pvpGrade,
        byte pvpGradeExtension = 0)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(Math.Max(0, wins));
        writer.Write(Math.Max(0, losses));
        writer.Write(Math.Max(0, pvpPoints));
        writer.Write(Math.Max(0, currentRankPoint));
        writer.Write(Math.Max(0, nextRankPoint));
        writer.Write(pvpGrade);
        writer.Write(pvpGradeExtension);
        return new GameServerPacket(
            NotificationPacketType,
            PvpRecordNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateFailClearDungeon(byte stamina) =>
        new(NotificationPacketType, FailClearDungeonNotification, [stamina]);

    public static GameServerPacket CreateRecoverStaminaReply(int remainingGold)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write((uint)Math.Max(0, remainingGold));
        return new GameServerPacket(
            CommandPacketType,
            RecoverStaminaCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateRecoverStaminaError(byte errorCode) =>
        new(
            CommandPacketType,
            RecoverStaminaCommand,
            [0, errorCode]);

    public static GameServerPacket CreateDieState(ushort userId, bool isAlive) =>
        new(
            NotificationPacketType,
            DieStateNotification,
            [(byte)userId, (byte)(userId >> 8), isAlive ? (byte)1 : (byte)0]);

    public static GameServerPacket CreateFatigue(
        ushort usedFatigue = 0,
        ushort maximumFatigue = DefaultMaximumFatigue,
        ushort premiumFatigue = 0)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(usedFatigue);
        writer.Write(maximumFatigue);
        writer.Write(premiumFatigue);
        return new GameServerPacket(
            NotificationPacketType,
            FatigueNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateEventInfo(uint featureFlags = 0)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(featureFlags);
        return new GameServerPacket(
            NotificationPacketType,
            EventInfoNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateDungeonPermissions(
        IReadOnlyList<GameDungeonPermissionEntry> permissions)
    {
        if (permissions.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(permissions));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((ushort)permissions.Count);
        foreach (var permission in permissions)
        {
            writer.Write(permission.DungeonId);
            writer.Write(Math.Min((byte)3, permission.ClearState));
        }

        return new GameServerPacket(
            NotificationPacketType,
            DungeonPermissionNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateUserState(ushort userId, byte state) =>
        new(
            NotificationPacketType,
            UserStateNotification,
            [(byte)userId, (byte)(userId >> 8), state]);

    public static GameServerPacket CreateAcceptableQuestList(
        IReadOnlyList<ushort> questIds)
    {
        if (questIds.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(questIds));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)questIds.Count);
        foreach (var questId in questIds)
        {
            writer.Write(questId);
        }

        return new GameServerPacket(
            NotificationPacketType,
            AcceptableQuestListNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateEmptyAcceptableQuestList() =>
        CreateAcceptableQuestList([]);

    public static GameServerPacket CreateAcceptQuestReply(
        ushort questId,
        uint trigger = 0,
        IReadOnlyList<GameQuestInsertedItem>? insertedItems = null)
    {
        // DF2008 0x4133D1 reads quest:u16, trigger:u32, grants:u8.
        // 0x4EC960 hides the script's [delete npc index] when trigger != 0;
        // CMD 35 trigger 0 restores it through 0x4EBB60. No extra NPC packet.
        var inserted = insertedItems ?? [];
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(questId);
        writer.Write(trigger);
        writer.Write(checked((byte)Math.Min(byte.MaxValue, inserted.Count)));
        foreach (var item in inserted.Take(byte.MaxValue))
        {
            // DF2008 CMD 33 reads slot, item ID, then the inserted count/add_info.
            writer.Write(item.Slot);
            writer.Write(item.ItemId);
            writer.Write(item.CountOrSeed);
        }

        return new GameServerPacket(CommandPacketType, AcceptQuestCommand, payload.ToArray());
    }

    public static GameServerPacket CreateGiveupQuestReply(ushort questId)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(questId);
        return new GameServerPacket(CommandPacketType, GiveupQuestCommand, payload.ToArray());
    }

    public static GameServerPacket CreateSetQuestTriggerReply(
        ushort questId,
        uint trigger)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(questId);
        writer.Write(trigger);
        return new GameServerPacket(CommandPacketType, SetQuestTriggerCommand, payload.ToArray());
    }

    public static GameServerPacket CreateFinishQuestReply(
        ushort questId,
        IReadOnlyList<GameQuestConsumedItem>? consumedItems = null,
        IReadOnlyList<GameQuestInsertedItem>? insertedItems = null,
        byte chainType = 0,
        byte growNumber = 0,
        IReadOnlyList<GameQuestGrowthSkillChange>? growthSkillChanges = null)
    {
        if (chainType is not 0 and not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(chainType));
        }

        var consumed = consumedItems ?? [];
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(questId);
        writer.Write((byte)0); // completion type
        writer.Write(checked((byte)Math.Min(byte.MaxValue, consumed.Count)));
        foreach (var item in consumed.Take(byte.MaxValue))
        {
            writer.Write((byte)0); // inventory update type
            writer.Write(item.Slot);
            writer.Write(item.RemainingCount);
        }

        writer.Write(chainType);
        if (chainType == 0)
        {
            var inserted = insertedItems ?? [];
            writer.Write(checked((byte)Math.Min(byte.MaxValue, inserted.Count)));
            foreach (var item in inserted.Take(byte.MaxValue))
            {
                writer.Write(item.Slot);
                writer.Write(item.ItemId);
                writer.Write(item.CountOrSeed);
            }
        }
        else
        {
            writer.Write(growNumber);
            var changes = (growthSkillChanges ?? [])
                .Where(change => change.SkillClass < GrowthSkillClassCount)
                .Take(byte.MaxValue)
                .ToArray();
            writer.Write(checked((byte)changes.Length));
            foreach (var change in changes)
            {
                // DF2008 CMD 36 reads each change as class, skill ID, then level.
                writer.Write(change.SkillClass);
                writer.Write(change.SkillId);
                writer.Write(change.Level);
            }
        }

        return new GameServerPacket(CommandPacketType, FinishQuestCommand, payload.ToArray());
    }

    public static GameServerPacket CreateUserPosition(
        GameTownUser user,
        ushort movementValue = 100)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(user.UserId);
        writer.Write(user.X);
        writer.Write(user.Y);
        writer.Write(user.Direction);
        writer.Write(movementValue);
        return new GameServerPacket(
            NotificationPacketType,
            UserPositionNotification,
            payload.ToArray());
    }

    /// <summary>
    /// 离开信号用的 (town, area) 哨兵。安全性论证：客户端是拿这两个字节与**接收者自己
    /// 当前所在区域**（scene+0x88/+0x8C）比较，只要不相等就走销毁角色的分支；而本服
    /// 真实数据里不存在 town/area = 0xFF（城镇只有 1..5，区域取值远小于 0xFF），所以
    /// 任何收包者都必然判定为“不匹配”，不会误伤，也不需要按收件人现算不同城镇。
    /// </summary>
    public const byte DepartureSentinel = 0xFF;

    public static GameServerPacket CreateUserArea(
        byte townId,
        byte areaId,
        GameTownUser user,
        bool active = true)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(user.UserId);
        writer.Write(townId);
        writer.Write(areaId);
        writer.Write(user.X);
        writer.Write(user.Y);
        writer.Write(user.Direction);
        // 尾字节只是该 actor 的激活标记（creature+0x3E4），加入为 1、离开为 0；
        // 它**不销毁**角色（不移除地图实体与显示子对象），见 CreateUserAreaDeparture。
        writer.Write(active ? (byte)1 : (byte)0);
        return new GameServerPacket(
            NotificationPacketType,
            UserAreaNotification,
            payload.ToArray());
    }

    /// <summary>
    /// 离开区域的销毁信号（USER_AREA）：把 (town, area) 写成 0xFF/0xFF 哨兵，
    /// 收包客户端只有在包里的 (town, area) 与自己当前 scene 不一致时才会
    /// mapMgr->RemoveCreature（0x41EC1F，递归摘掉角色及其显示/特效子对象）。
    /// active 尾字节仍写 0，但那只是隐藏标记，单独发它不移除任何东西。
    /// </summary>
    public static GameServerPacket CreateUserAreaDeparture(GameTownUser user) =>
        CreateUserArea(DepartureSentinel, DepartureSentinel, user, active: false);

    public static GameServerPacket CreateAreaUsers(
        byte townId,
        byte areaId,
        IReadOnlyList<GameTownUser> users)
    {
        if (users.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(users));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(townId);
        writer.Write(areaId);
        writer.Write((ushort)users.Count);
        foreach (var user in users)
        {
            writer.Write(user.UserId);
            writer.Write(user.X);
            writer.Write(user.Y);
            writer.Write(user.Direction);
            writer.Write((byte)1); // active town actor
        }

        return new GameServerPacket(
            NotificationPacketType,
            AreaUsersNotification,
            payload.ToArray());
    }

    private const int FriendListNameFieldLength = 30;
    /// <summary>队伍成员槽位固定 4 个（客户端硬编码上限，A 卷 §7.1 / B 卷 §10.1）。</summary>
    public const int PartyMemberSlotCount = 4;
    private const int FriendListRecordLength = 42;
    // 客户端 NOTI 65 用 0x100 字节缓冲读写文本；CMD 17 的文本长度上限是 255。
    private const int MessengerTextMaximumLength = 0xFF;

    public static GameServerPacket CreateFriendList(IReadOnlyList<GameFriendEntry> friends)
    {
        ArgumentNullException.ThrowIfNull(friends);
        if (friends.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(friends));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)friends.Count);
        foreach (var friend in friends)
        {
            if (friend.NameBytes.Length is 0 or > FriendListNameFieldLength)
            {
                throw new ArgumentException(
                    "Friend names must contain 1-30 encoded bytes.",
                    nameof(friends));
            }

            // 记录布局（客户端 0x425668 逐字段读入；行绘制 0x7F7F30 在**没有本地 user
            // 记录**时就用下面这三组值画 `Lv.%d 职业名`，所以离线好友也必须真填）：
            // [0] 状态（0xFF = 保持原状态；离线由 BUDDY_OUT 置 -1），[1] 标志（BUDDY_OUT 置 1
            //     时行转灰=未连接样式），[2] 保留，
            // [3..32] 名字（定长 30、NUL 填充，匹配键只有它，大小写敏感逐字节），
            // [0x21] u16 等级（→ entry+0x1C，Lv.%d），
            // [0x23] u8 职业系（→ entry+0x20；客户端 `cmp arg3,5; jge` 会拒绝 >=5 的条目），
            // [0x24] u8 打包成长（低半字节 GrowType → entry+0x24，高半字节 AwakeningType
            //        → entry+0x28；两个半字节都要放对，否则“有记录/无记录”两种画法不一致），
            // [0x25] u32、[0x29] u8 —— 语义未考证，保持 0。
            writer.Write((byte)0);                 // state
            writer.Write((byte)0);                 // flag
            writer.Write((byte)0);                 // reserved
            var nameField = new byte[FriendListNameFieldLength];
            friend.NameBytes.CopyTo(nameField, 0);
            writer.Write(nameField);
            writer.Write(friend.Level);            // R+0x21：等级（不是 uid）
            writer.Write(ClampFriendJob(friend.Job)); // R+0x23：职业系，必须 <5
            writer.Write(PackGrowthType(friend.GrowType, friend.AwakeningType)); // R+0x24
            writer.Write(0u);                      // R+0x25（→ entry+0x34，语义未考证）
            writer.Write((byte)0);                 // R+0x29（→ entry+0x2C，职业查询 arg4）
        }

        return new GameServerPacket(
            NotificationPacketType,
            BuddyConnListNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateBuddyIn(byte state, byte[] nameBytes, byte flag = 0)
    {
        ArgumentNullException.ThrowIfNull(nameBytes);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(state);
        WriteLengthPrefixedBytes(writer, nameBytes);
        writer.Write(flag);
        return new GameServerPacket(
            NotificationPacketType,
            BuddyInNotification,
            payload.ToArray());
    }

    /// <summary>
    /// 好友下线（NOTI 55）。payload = [u8 0][u32 名字长度 + 名字][u8 flag]。
    /// </summary>
    /// <remarks>
    /// flag 的两个语义（两条都写 entry+0x30=-1，只有 +0xBC 与提示不同）：
    ///   flag==0 → 只打“[%s]已退出游戏”聊天栏提示（0x501140），行样式不动；
    ///   flag==1 → 行走“无记录”的灰色未连接画法并排到最后一组（0x7F8031 的分支与
    ///             0x7F75F0 排序的第四组），同时抑制自己的重复提示。
    /// 好友下线时 <see cref="FriendService"/> 先发 flag=0 再发 flag=1：既保留退出提示，
    /// 又让行转灰（顺序反了提示会被抑制）。默认值 1 用于“只想要灰色”的场合（登录快照
    /// 标记离线好友、加好友后的离线状态钉死）。BUDDY_IN 的 flag 保持 0（上线正常显示）。
    /// </remarks>
    public static GameServerPacket CreateBuddyOut(byte[] nameBytes, byte flag = 1)
    {
        ArgumentNullException.ThrowIfNull(nameBytes);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)0); // 客户端读入后忽略（SetOffline 只用名字与 flag）
        WriteLengthPrefixedBytes(writer, nameBytes);
        writer.Write(flag);
        return new GameServerPacket(
            NotificationPacketType,
            BuddyOutNotification,
            payload.ToArray());
    }

    /// <summary>
    /// ADD_FRIEND 成功结果（type=1 命令包，成功分支 handler 0x414986）。body 恰好
    /// 14+nameLen 字节：[非零标志][u32 名字长度 + 名字][A][B][C][u32 D][E][F]。
    /// </summary>
    /// <remarks>
    /// 客户端接收循环把 body[0] 当结果字节吃掉，再从 body+1 读名字，然后**无条件**读
    /// 尾部 6 个字段（1+1+1+4+1+1 共 9 字节）。读不到时整包剩余部分被 0x30 填充：
    /// 其中 B 落在记录偏移 0x23，而条目追加函数 0x500B00 的守卫是 B &lt; 5，0x30=48
    /// 会让条目被静默拒绝（聊天栏提示照打）——这正是“加好友不进列表”的根因，所以
    /// 尾部 9 字节必须发全。
    /// 尾部语义（与 NOTI 56 的记录同源同序，见 GameFriendEntry）：
    /// A=等级（u8）、B=职业系（&lt;5）、C=打包成长（低半字节 GrowType、高半字节 AwakeningType）、
    /// D=好友区用户 id（在线时填，客户端用它 FindByUid 给 user 对象打好友标记；离线填 0）、
    /// E=记录 0x29（保持 0）、F=状态（不用 0xFF；“保持原状态”对新条目无意义，状态由紧随
    /// 其后的 BUDDY_IN/BUDDY_OUT 决定）。新加的条目因此立刻带着真实 Lv./职业，不会再显示
    /// “Lv.0 鬼剑士”。
    /// </remarks>
    public static GameServerPacket CreateAddFriendResult(
        byte[] nameBytes,
        ushort areaUserId = 0,
        ushort level = 0,
        byte job = 0,
        byte growType = 0,
        byte awakeningType = 0)
    {
        ValidateFriendName(nameBytes);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);                              // 结果标志，非零即成功
        WriteLengthPrefixedBytes(writer, nameBytes);
        // A 是 u8（记录 0x21 是 u16，但 id63 的 A 只有一字节；本版本等级上限 60）。
        writer.Write((byte)Math.Clamp((int)level, 0, byte.MaxValue)); // A：等级
        writer.Write(ClampFriendJob(job));                       // B：职业系，必须 < 5
        writer.Write(PackGrowthType(growType, awakeningType));   // C：打包成长
        writer.Write((uint)areaUserId);                          // D：好友 uid（在线）/ 0（离线）
        writer.Write((byte)0);                                   // E：记录 0x29
        writer.Write((byte)0);                                   // F：0 = SetEntryState(状态 0) 由 BUDDY_* 纠正
        return new GameServerPacket(CommandPacketType, AddFriendCommand, payload.ToArray());
    }

    // 职业系必须 <5：客户端条目插入 0x500B00 的 `cmp arg3,5; jge` 守卫会拒绝整条记录
    // （聊天栏提示照打、列表里却没有）。坏存档里的越界值钳到 4，至少让好友行还在。
    private static byte ClampFriendJob(byte job) => (byte)Math.Clamp((int)job, 0, 4);

    /// <summary>
    /// REMOVE_FRIEND 成功结果（type=1 命令包）。客户端 handler 0x414BA7 凭它调用
    /// CNRDFriendManager::RemoveEntryByName → vector::erase 真正删除条目；通知通道
    /// （NOTI 56/54/55）只 InsertOrUpdate，永远删不掉条目。body = [非零标志][名字][0]。
    /// </summary>
    public static GameServerPacket CreateRemoveFriendResult(byte[] nameBytes)
    {
        ValidateFriendName(nameBytes);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);                              // 结果标志，非零即成功
        WriteLengthPrefixedBytes(writer, nameBytes);
        // handler 把它读作可选结果码（0x15 = 对方不在好友名单中），0 表示无错误。
        writer.Write((byte)0);
        return new GameServerPacket(CommandPacketType, RemoveFriendCommand, payload.ToArray());
    }

    /// <summary>
    /// CMD 128 成功结果（type=1 命令包）。客户端 handler 0x414C2B 从 body 第 1 字节读结果
    /// 标志，非零后读长度前缀名字，然后按**本机时间**给条目填年/月/日并追加，聊天栏打印
    /// “[%s]已被添加到黑名单”。失败必须走 <see cref="CreateBlacklistError"/> 的 0 标志。
    /// </summary>
    public static GameServerPacket CreateBlacklistAddResult(byte[] nameBytes)
    {
        ValidateBlacklistName(nameBytes);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);                              // 结果标志，非零即成功
        WriteLengthPrefixedBytes(writer, nameBytes);
        return new GameServerPacket(
            CommandPacketType,
            RegisterToBlacklistCommand,
            payload.ToArray());
    }

    /// <summary>
    /// CMD 129 成功结果（type=1 命令包）。客户端 handler 0x414D34 凭它按名字
    /// FindByName + vector::erase 删除条目（名字比较大小写敏感）。
    /// </summary>
    public static GameServerPacket CreateBlacklistRemoveResult(byte[] nameBytes)
    {
        ValidateBlacklistName(nameBytes);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        WriteLengthPrefixedBytes(writer, nameBytes);
        return new GameServerPacket(
            CommandPacketType,
            DeleteToBlacklistCommand,
            payload.ToArray());
    }

    /// <summary>
    /// CMD 128/129 失败结果：body = [0][u8 失败码]。客户端 128 认识 74/76/78、
    /// 129 认识 0x4B，其它码无动作（所以失败必须是这条包，不能发成功标志+名字）。
    /// </summary>
    public static GameServerPacket CreateBlacklistError(byte command, byte errorCode) =>
        new(CommandPacketType, command, [0, errorCode]);

    /// <summary>
    /// CMD 130 黑名单全量快照（type=1 命令包 id 130）：[非零标志][u8 条数] +
    /// 每条(u32 名字长度 + 名字, u8 年-1900, u8 月-1, u8 日)。客户端 handler 0x414D97
    /// 先 vector clear 再逐条追加，所以这就是“清空 + 全量追加”：任何时刻都可以重发，
    /// 重发前不需要任何会话状态（与好友名单的“每会话一次”快照相反）。
    /// </summary>
    /// <remarks>
    /// 日期只做**钳制**、不抛异常：条目来自存档（旧档/手改可能带越界值），130 是玩家
    /// 打开黑名单页就会发的回包，一个坏日期不能让整条会话被兜底逻辑断掉。钳制后写出的
    /// 一定是 u8 能表示的 (年-1900, 月-1, 日)。
    /// </remarks>
    public static GameServerPacket CreateBlacklistSnapshot(
        IReadOnlyList<GameBlacklistEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);                  // 标志必须非零，否则客户端整包忽略
        writer.Write((byte)entries.Count);
        foreach (var entry in entries)
        {
            ValidateBlacklistName(entry.NameBytes);
            var year = Math.Clamp(entry.Year, 1900, 2155);
            var month = Math.Clamp(entry.Month, 1, 12);
            var day = Math.Clamp(entry.Day, 1, 31);
            WriteLengthPrefixedBytes(writer, entry.NameBytes);
            // 客户端收包后做 +0x76C(1900) / +1 修正，所以线上存 (年-1900, 月-1, 日)。
            writer.Write((byte)(year - 1900));
            writer.Write((byte)(month - 1));
            writer.Write((byte)day);
        }

        return new GameServerPacket(
            CommandPacketType,
            RequestBlacklistCommand,
            payload.ToArray());
    }

    private static void ValidateFriendName(byte[] nameBytes) =>
        ValidateNameBytes(nameBytes, "Friend");

    private static void ValidateBlacklistName(byte[] nameBytes) =>
        ValidateNameBytes(nameBytes, "Blacklist");

    private static void ValidateNameBytes(byte[] nameBytes, string subject)
    {
        ArgumentNullException.ThrowIfNull(nameBytes);
        if (nameBytes.Length is 0 or > FriendListNameFieldLength)
        {
            throw new ArgumentException(
                $"{subject} names must contain 1-30 encoded bytes.",
                nameof(nameBytes));
        }

        if (nameBytes.Contains((byte)0))
        {
            throw new ArgumentException(
                $"{subject} names must not contain a NUL byte.",
                nameof(nameBytes));
        }
    }

    /// <summary>
    /// 1:1 窗口消息（NOTI 65）：type 0x12、flag 0，按名字把正文投递进 1:1 窗口。
    /// 只有 CMD 17 类型 18（真正的 1:1 频道）用它：悄悄话（类型 1/7）不应该开/写
    /// 1:1 窗口，普通悄悄话的接收端只收 NOTI 12。flag 必须 0——非 0 是状态码，
    /// 客户端只弹提示并丢弃正文。
    /// </summary>
    public static GameServerPacket CreateMessageOtherChannel(byte[] nameBytes, byte[] textBytes)
    {
        ArgumentNullException.ThrowIfNull(nameBytes);
        ArgumentNullException.ThrowIfNull(textBytes);
        if (nameBytes.Length is 0 or > FriendListNameFieldLength)
        {
            throw new ArgumentException(
                "Messenger peer names must contain 1-30 encoded bytes.",
                nameof(nameBytes));
        }

        if (nameBytes.Contains((byte)0))
        {
            throw new ArgumentException(
                "Messenger peer names must not contain a NUL byte.",
                nameof(nameBytes));
        }

        if (textBytes.Length is 0 or > MessengerTextMaximumLength || textBytes.Contains((byte)0))
        {
            throw new ArgumentException(
                "Messenger text must contain 1-255 non-NUL encoded bytes.",
                nameof(textBytes));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(MessengerMessageType);                 // 只有 0x12：进 1:1 窗口
        writer.Write(MessengerMessageFlag);                 // 0 = 正文；非 0 是状态码（提示并丢弃正文）
        WriteLengthPrefixedBytes(writer, nameBytes);
        WriteLengthPrefixedBytes(writer, textBytes);
        return new GameServerPacket(
            NotificationPacketType,
            MessageOtherChannelNotification,
            payload.ToArray());
    }

    /// <summary>解析 ADD_FRIEND/REMOVE_FRIEND 的载荷（Body 从序列号起算）。</summary>
    public static bool TryReadFriendName(byte[] body, out byte[] nameBytes) =>
        TryReadLengthPrefixedName(body, out nameBytes);

    /// <summary>
    /// 解析 CMD 128/129（黑名单增删）的载荷（Body 从序列号起算）：u32 名字长度 + 名字。
    /// 与好友命令同形，但名字在客户端是**大小写敏感**匹配的（见 FriendService 的说明）。
    /// </summary>
    public static bool TryReadBlacklistName(byte[] body, out byte[] nameBytes) =>
        TryReadLengthPrefixedName(body, out nameBytes);

    private static bool TryReadLengthPrefixedName(byte[] body, out byte[] nameBytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        const int payloadOffset = sizeof(ushort);
        nameBytes = [];
        if (body.Length < payloadOffset + sizeof(uint))
        {
            return false;
        }

        var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(
            body.AsSpan(payloadOffset, sizeof(uint)));
        if (nameLength is 0 or > FriendListNameFieldLength
            || nameLength != body.Length - payloadOffset - sizeof(uint))
        {
            return false;
        }

        var candidate = body.AsSpan(payloadOffset + sizeof(uint), (int)nameLength).ToArray();
        if (candidate.Contains((byte)0))
        {
            return false;
        }

        nameBytes = candidate;
        return true;
    }

    /// <summary>解析悄悄话（CMD 17 类型 1/7）正文之后追加的目标角色名。</summary>
    public static bool TryReadWhisperTarget(
        byte[] body,
        int messageLength,
        out byte[] targetNameBytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        const int payloadOffset = sizeof(ushort);
        var targetLengthOffset = payloadOffset
            + sizeof(byte)      // message type
            + sizeof(ushort)    // target
            + sizeof(uint)      // slot
            + sizeof(uint)      // message length
            + messageLength;
        targetNameBytes = [];
        if (body.Length < targetLengthOffset + sizeof(uint))
        {
            return false;
        }

        var targetLength = BinaryPrimitives.ReadUInt32LittleEndian(
            body.AsSpan(targetLengthOffset, sizeof(uint)));
        if (targetLength is 0 or > FriendListNameFieldLength
            || targetLength != body.Length - targetLengthOffset - sizeof(uint))
        {
            return false;
        }

        var candidate = body.AsSpan(
            targetLengthOffset + sizeof(uint),
            (int)targetLength).ToArray();
        if (candidate.Contains((byte)0))
        {
            return false;
        }

        targetNameBytes = candidate;
        return true;
    }

    /// <summary>
    /// CMD 10：u16 目标 uid + u8 peerType + u32 weightDelta；只有 type==1 才是交易请求。
    /// weightDelta = 客户端自算的「当前负重 − 负重上限」（克，正常为负，u32 二补码），
    /// 必须原样回传进 NOTI 7（trade-weight-check-recon §3.3/§5.1），客户端的锁定自检要用它。
    /// </summary>
    public static bool TryReadRequestPeer(
        byte[] body,
        out ushort targetUid,
        out byte peerType,
        out uint weightDelta)
    {
        ArgumentNullException.ThrowIfNull(body);
        targetUid = 0;
        peerType = 0;
        weightDelta = 0;
        // body = [u16 seq][u16 uid][u8 type][u32 weightDelta]，真机 CMD 10（0x4624F9）恒发后 4 字节。
        if (body.Length < 2 + 2 + 1 + 4)
        {
            return false;
        }

        targetUid = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2));
        peerType = body[4];
        weightDelta = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(5, 4));
        return peerType == 1;
    }

    /// <summary>
    /// CMD 11：与 CMD 10 同前缀（u16 uid + u8 peerType + u32 weightDelta），只有 type==1 是交易应答。
    /// weightDelta 语义与 CMD 10 完全一致，原样回传进 NOTI 8。
    /// **接受 vs 拒绝**由 <see cref="IsPeerResponseAccepted"/> 判定：真机（G 卷 §2.1/§2.3）的
    /// 交易应答窗 `CNRDResponsePeerWindow`，【是】（`0x821E46`）发 9 字节 `[u16 seq][u16 uid][u8 01][u32 X]`；
    /// 【否】（`0x821F77`）发同样的 9 字节再追加 `u16 0x0064`（字节 `64 00`），共 11 字节。
    /// 此前只看前 7 字节、长度放宽成 `>= 9`，把拒绝包当成了接受（真机实证：
    /// 用户点否的包 `0200 01 50730000 6400`）。
    /// </summary>
    public static bool TryReadResponsePeer(
        byte[] body,
        out ushort requesterUid,
        out byte peerType,
        out uint weightDelta,
        out bool accepted)
    {
        ArgumentNullException.ThrowIfNull(body);
        requesterUid = 0;
        peerType = 0;
        weightDelta = 0;
        accepted = false;
        // 真机 CMD 11（0x821E4E 起）同样以 u32 的「当前负重 − 负重上限」收尾。
        if (body.Length < 2 + 2 + 1 + 4)
        {
            return false;
        }

        requesterUid = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2));
        peerType = body[4];
        weightDelta = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(5, 4));
        accepted = IsPeerResponseAccepted(body);
        return peerType == 1;
    }

    /// <summary>
    /// CMD 10 的**组队**分支：与交易同前缀 `[u16 seq][u16 uid][u8 peerType][u32 X]`，
    /// 只有 peerType==0 才是组队（0=组队 / 1=交易 / 2,3,4=决斗场；A 卷 §2.1-2.2）。
    /// `X` 是请求者客户端自报的复活币个数（仅用于应答窗显示）。
    /// 与 <see cref="TryReadRequestPeer"/> 分开：交易路径的语义一个字都不动。
    /// </summary>
    public static bool TryReadPartyRequestPeer(byte[] body, out ushort targetUid, out uint reviveCoins)
    {
        ArgumentNullException.ThrowIfNull(body);
        targetUid = 0;
        reviveCoins = 0;
        if (body.Length < 2 + 2 + 1 + 4)
        {
            return false;
        }

        targetUid = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2));
        reviveCoins = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(5, 4));
        return body[4] == 0;
    }

    /// <summary>
    /// CMD 11 的**组队**分支：前缀同 CMD 10，只有 peerType==0 才是组队应答。
    /// **接受 vs 拒绝**同交易：真机（G 卷 §2.1/§2.2）里【是】（`0x4FEBC0`）发 9 字节
    /// `[u16 seq][u16 uid][u8 00][u32 0]`；【否】（`0x821FBA`）把同样 9 字节再追加 `u16 0x0064`
    /// （字节 `64 00`）成 11 字节。真机实证：用户点否的包 `0100 00 00000000 6400` 被旧实现当成了接受。
    /// </summary>
    public static bool TryReadPartyResponsePeer(byte[] body, out ushort requesterUid, out bool accepted)
    {
        ArgumentNullException.ThrowIfNull(body);
        requesterUid = 0;
        accepted = false;
        if (body.Length < 2 + 2 + 1 + 4)
        {
            return false;
        }

        requesterUid = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2));
        accepted = IsPeerResponseAccepted(body);
        return body[4] == 0;
    }

    /// <summary>
    /// 拒绝判据（G 卷真机 + 反汇编）：小窗【否】发的包与【是】同前缀，但**尾部多一个 u16 0x0064**。
    /// 即 9 字节 = 接受；11 字节且 body[9..10] == 64 00 = 拒绝。别的长度（异常）保守当作接受，
    /// 与改动前行为一致。
    /// </summary>
    private static bool IsPeerResponseAccepted(byte[] body) =>
        !(body.Length >= 11 && body[9] == 0x64 && body[10] == 0x00);

    /// <summary>
    /// CMD 12：`[u16 seq][u8 sel]` + （`sel == 0` 时 `u32 长度 + 名字`）+ `[u8 人数上限]`（B 卷 §5、C 卷 §7）。
    /// `sel` = 预置队名下标（1..6；0 = 自定义名，此时才带名字串）。名字不超过
    /// <see cref="FriendListNameFieldLength"/> 字节且不含 NUL（与好友名同规矩）。
    /// </summary>
    public static bool TryReadSetPartyInfo(
        byte[] body,
        out byte nameSelection,
        out byte[] nameBytes,
        out byte maxMembers)
    {
        ArgumentNullException.ThrowIfNull(body);
        nameSelection = 0;
        nameBytes = [];
        maxMembers = 0;
        const int payloadOffset = sizeof(ushort);
        if (body.Length < payloadOffset + 1)
        {
            return false;
        }

        nameSelection = body[payloadOffset];
        var cursor = payloadOffset + 1;
        if (nameSelection == 0)
        {
            if (body.Length < cursor + sizeof(uint))
            {
                return false;
            }

            var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(cursor, sizeof(uint)));
            cursor += sizeof(uint);
            if (nameLength is 0 or > FriendListNameFieldLength
                || body.Length < cursor + nameLength + 1)
            {
                return false;
            }

            var candidate = body.AsSpan(cursor, (int)nameLength).ToArray();
            if (candidate.Contains((byte)0))
            {
                return false;
            }

            nameBytes = candidate;
            cursor += (int)nameLength;
        }

        if (body.Length < cursor + 1)
        {
            return false;
        }

        maxMembers = body[cursor];
        return true;
    }

    /// <summary>CMD 26：单字节状态（0 解锁 / 1 锁定 / 2 取消 / 3 最终确认 / 4 未定）。</summary>
    public static bool TryReadItemTradeStateRequest(byte[] body, out byte state)
    {
        ArgumentNullException.ThrowIfNull(body);
        state = 0;
        if (body.Length < 3)
        {
            return false;
        }

        state = body[2];
        return true;
    }

    /// <summary>
    /// 解析 CMD 8 GET_USERINFO 的载荷（Body 从序列号起算）：u16 uid + u8 mode。
    /// 角色选择界面发的是 u16 0xFFFF + u8 2（账号角色卡列表），城镇内“查看信息”/“/info”
    /// 发的是 u16 uid + u8 3（该角色的资料，客户端用 mode 1/3 解析器读回包）。
    /// </summary>
    public static bool TryReadUserInfoRequest(byte[] body, out ushort userId, out byte mode)
    {
        ArgumentNullException.ThrowIfNull(body);
        userId = 0;
        mode = 0;
        const int payloadOffset = sizeof(ushort);
        if (body.Length < payloadOffset + sizeof(ushort) + sizeof(byte))
        {
            return false;
        }

        userId = BinaryPrimitives.ReadUInt16LittleEndian(
            body.AsSpan(payloadOffset, sizeof(ushort)));
        mode = body[payloadOffset + sizeof(ushort)];
        return true;
    }

    public static GameServerPacket CreateUdpPeerInfo(
        IReadOnlyList<GamePeerInfo> peers)
    {
        ArgumentNullException.ThrowIfNull(peers);
        if (peers.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(peers));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)peers.Count);
        Span<byte> networkOrder = stackalloc byte[4];
        foreach (var peer in peers)
        {
            writer.Write(peer.UserId);
            WriteIpv4Address(writer, peer.LocalAddress);
            WriteIpv4Address(writer, peer.PublicAddress);
            // The port is network order (big endian). Measured
            // 2026-10-04: the client dialled "192.168.3.177:23067" for a peer we
            // had written as 7002, and 23067 is exactly 0x1B5A byte-swapped, so
            // it was reading these two bytes big endian. The client's CMD 2
            // report is the corroborating evidence: it writes its own port 5063
            // as 0x13 0xC7, which is big endian, and our reader already agrees
            // (GameDatagramProtocol.TryParseNatReport reads it BigEndian).
            BinaryPrimitives.WriteUInt16BigEndian(networkOrder, peer.PublicPort);
            writer.Write(networkOrder[..2]);
            writer.Write(peer.AccountId);
            writer.Write(peer.NatType);
            // NOTI 11 reads this DWORD directly at 0x8F5160, stores it in
            // CNUser+0xC (0x417A90), then takes its unsigned minimum with the
            // local MTU for the reliable-buffer capacity (0x78E090). It is
            // little endian, as in CMD 2; the adjacent port uses network order.
            writer.Write(peer.Mtu);
        }

        return new GameServerPacket(
            NotificationPacketType,
            UdpPeerInfoNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateUdpHost(byte partyIndex = 0)
    {
        if (partyIndex >= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(partyIndex));
        }

        return new GameServerPacket(
            NotificationPacketType,
            UdpHostNotification,
            [partyIndex]);
    }

    public static GameServerPacket CreateEnterSelectDungeon(
        bool hellQuestsCompleted,
        IReadOnlyList<ushort> missingHellItemPartyIndices)
    {
        // DF2008 0x41F1CF reads quest eligibility (0 triggers string 5431).
        // 0x41F27A reads the count; 0x41F2BE reads u16 PARTY SLOTS for string
        // 5430, not user IDs. Do not use the newer server's NOTI 27 layout.
        if (missingHellItemPartyIndices.Count > 4
            || missingHellItemPartyIndices.Any(index => index >= 4))
        {
            throw new ArgumentOutOfRangeException(nameof(missingHellItemPartyIndices));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(hellQuestsCompleted);
        writer.Write((byte)missingHellItemPartyIndices.Count);
        foreach (var index in missingHellItemPartyIndices)
        {
            writer.Write(index);
        }

        return new(NotificationPacketType, EnterSelectDungeonNotification, payload.ToArray());
    }

    public static GameServerPacket CreateTutorialDungeonInfo(byte difficulty) =>
        CreateDungeonInfo(
            TutorialDungeonId,
            difficulty,
            mazeIndex: 0,
            bossX: byte.MaxValue,
            bossY: byte.MaxValue);

    public static GameServerPacket CreateDungeonInfo(
        ushort dungeonId,
        byte difficulty,
        byte mazeIndex,
        byte bossX,
        byte bossY,
        byte flagA = 0,
        byte flagB = 0)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(dungeonId);
        writer.Write(difficulty);
        writer.Write(mazeIndex);
        writer.Write(bossX);
        writer.Write(bossY);
        writer.Write(flagA);
        writer.Write(flagB);
        return new GameServerPacket(
            NotificationPacketType,
            DungeonInfoNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateStartMap(
        byte roomX,
        byte roomY,
        ushort mapId,
        IReadOnlyList<GameDungeonMonster> monsters,
        IReadOnlyList<GamePassiveObjectDrop>? passiveObjectDrops = null,
        uint randomSeed = 0,
        byte roomStateFlag = 1,
        byte hellPartyMode = 0)
    {
        passiveObjectDrops ??= [];
        if (monsters.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(monsters));
        }

        if (passiveObjectDrops.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(passiveObjectDrops));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(roomX);
        writer.Write(roomY);
        writer.Write(randomSeed);
        writer.Write(roomStateFlag is 1 or 2 ? roomStateFlag : (byte)1);
        writer.Write(mapId);
        writer.Write((byte)monsters.Count);
        foreach (var monster in monsters)
        {
            // DNF 1.0.1.9 START_MAP compresses a runtime monster to nine
            // bytes. The client indexes the selected .map file's [monster]
            // rows with the first byte, then uses the following fields as its
            // actor id, monster.lst index, level, type and box metadata.
            writer.Write(monster.MapListIndex);
            writer.Write(monster.UniqueId);
            writer.Write(monster.MonsterIndex);
            writer.Write(monster.Level);
            writer.Write(monster.Type);
            writer.Write(monster.IsBoxMonster);
            writer.Write(monster.BoxIndex);
        }
        writer.Write((byte)passiveObjectDrops.Count);
        foreach (var drop in passiveObjectDrops)
        {
            // Unlike a notification-38 death drop, a START_MAP passive drop
            // begins with the map parser's item-association slot and has no
            // item-state field.
            writer.Write(drop.AssociationSlot);
            writer.Write(drop.GroundId);
            writer.Write(drop.ItemId);
            writer.Write(drop.AddInfo);
            writer.Write(drop.OwnerUserId);
        }
        writer.Write(hellPartyMode);
        return new GameServerPacket(
            NotificationPacketType,
            StartMapNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCachedStartMap(
        byte roomX,
        byte roomY,
        uint randomSeed)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(roomX);
        writer.Write(roomY);
        writer.Write(randomSeed);
        // DNF 1.0.1.9 skips the map, monster and item sections when this
        // state byte is zero and reactivates its coordinate-scoped snapshot.
        writer.Write((byte)0);
        return new GameServerPacket(
            NotificationPacketType,
            StartMapNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateEmptyStartMap(
        byte roomX,
        byte roomY,
        ushort mapId,
        uint randomSeed = 0) =>
        CreateStartMap(roomX, roomY, mapId, [], randomSeed: randomSeed);

    public static GameDungeonMonster[] CreateTutorialMonsters() =>
    [
        new(0, 0, 1, 1, X: 1078, Y: 177),
        new(1, 1, 1, 1, X: 557, Y: 208),
        new(2, 2, 1, 1, X: 487, Y: 332),
        new(3, 3, 1, 1, X: 1026, Y: 334)
    ];

    public static GameServerPacket CreateTutorialStartMap() =>
        // The tutorial dungeon is id 10000, but START_MAP resolves its scene
        // through map.lst where Tutorial/tutorial.map is registered as 61000.
        CreateStartMap(
            roomX: 0,
            roomY: 0,
            mapId: TutorialMapId,
            monsters: CreateTutorialMonsters());

    public static bool IsTutorialCompletionModule(ReadOnlySpan<byte> body) =>
        body.Length >= 6
        && BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(2, sizeof(uint)))
            == TutorialCompletionModule;

    public static bool TryParseDieMonsterCommand(
        ReadOnlySpan<byte> body,
        out GameDieMonsterCommand command)
    {
        command = default;
        if (body.Length < 4)
        {
            return false;
        }

        var entityId = BinaryPrimitives.ReadUInt16LittleEndian(
            body.Slice(2, sizeof(ushort)));

        // Captured DFLegacy command-42 bodies are 23 bytes. The final byte is
        // the passive-object discriminator; ordinary monster deaths send 0.
        const int passiveObjectFlagOffset = 22;
        var isPassiveObject = body.Length > passiveObjectFlagOffset
            && body[passiveObjectFlagOffset] == 1;

        command = new GameDieMonsterCommand(entityId, isPassiveObject);
        return true;
    }

    public static bool TryParseGetItemCommand(ReadOnlySpan<byte> body, out GameGetItemCommand command)
    {
        command = default;
        // ACT1: sequence:u16, ground:u16, route:u8, client-rarity:u8, token:u16.
        // The later server reader's extra seed/position fields are not present.
        if (body.Length != 8) return false;
        command = new GameGetItemCommand(
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, 2)),
            body[4] == 1, body[5],
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, 2)));
        return true;
    }

    public static bool TryParseUseCoinCommand(
        ReadOnlySpan<byte> body,
        out GameUseCoinCommand command)
    {
        command = default;
        if (body.Length < 4)
        {
            return false;
        }

        command = new GameUseCoinCommand(
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, sizeof(ushort))));
        return true;
    }

    public static GameServerPacket CreateUseCoinReply(ushort targetUserId) =>
        new(
            CommandPacketType,
            UseCoinCommand,
            [1, (byte)targetUserId, (byte)(targetUserId >> 8)]);

    public static bool TryParseBossDieCheckCommand(
        ReadOnlySpan<byte> body,
        out GameBossDieCheckCommand command)
    {
        command = default;
        if (body.Length < 10)
        {
            return false;
        }

        command = new GameBossDieCheckCommand(
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(6, 4)));
        return true;
    }

    public static GameServerPacket CreateBossDieCheck(
        bool success,
        bool allBossesReported,
        ushort bossId = 0,
        byte errorCode = 0)
    {
        if (!success)
        {
            return new GameServerPacket(
                NotificationPacketType,
                BossDieCheckNotification,
                [0, errorCode]);
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(allBossesReported ? (byte)1 : (byte)0);
        writer.Write(bossId);
        return new GameServerPacket(
            NotificationPacketType,
            BossDieCheckNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateFinishLoading() =>
        new(NotificationPacketType, FinishLoadingNotification, []);

    public static GameServerPacket CreateEnterGameWorldComplete() =>
        new(NotificationPacketType, EnterGameWorldCompleteNotification, []);

    public static GameServerPacket CreateExperienceGained(
        byte level,
        uint cumulativeExperience,
        uint extraExperienceA,
        uint extraExperienceB,
        ushort skillPoints)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(level);
        writer.Write(cumulativeExperience);
        writer.Write(extraExperienceA);
        writer.Write(extraExperienceB);
        writer.Write(skillPoints);
        return new GameServerPacket(
            NotificationPacketType,
            ExperienceGainedNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateMonsterDie(
        ushort uniqueId,
        IReadOnlyList<GameDungeonDrop>? drops = null,
        sbyte championRewardOrdinal = NoChampionRewardOrdinal)
    {
        drops ??= [];
        if (drops.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(drops));
        }

        if (championRewardOrdinal != NoChampionRewardOrdinal
            && (championRewardOrdinal < 1
                || championRewardOrdinal > MaximumChampionRewardOrdinal))
        {
            throw new ArgumentOutOfRangeException(nameof(championRewardOrdinal));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(uniqueId);
        writer.Write((byte)drops.Count);
        foreach (var drop in drops)
        {
            // DFLegacy's compact death record is ground id, item id, add-info,
            // durability and the owning user id. Add-info is polymorphic: it
            // is the stack count for stackables and reinforcement for normal
            // equipment.
            writer.Write(drop.GroundId);
            writer.Write(drop.ItemId);
            writer.Write(drop.AddInfo);
            writer.Write(drop.Durability);
            writer.Write(drop.OwnerUserId);
        }

        // 13338's NOTI 38 is a compact six-byte no-drop record. The middle
        // trailer byte is not an owner index: Champion/SuperChampion kills
        // carry their 1..11 clear-reward animation ordinal here. Boss and
        // ordinary deaths use the no-reward sentinel (255).
        writer.Write((byte)0);
        writer.Write((byte)championRewardOrdinal);
        writer.Write((byte)0);
        return new GameServerPacket(
            NotificationPacketType,
            MonsterDieNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateDropItem(
        ushort userId,
        short x,
        short y,
        GameDungeonDrop drop)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(userId);
        writer.Write(x);
        writer.Write(y);
        writer.Write(drop.GroundId);
        writer.Write(drop.ItemId);
        // DFLegacy NOTI 40 does not reuse the five-field record embedded in
        // NOTI 38. The client reads item attr as a byte before add-info and
        // does not read an owner id at the end of this notification.
        writer.Write(drop.ItemAttr);
        writer.Write(drop.AddInfo);
        writer.Write(drop.Durability);
        return new GameServerPacket(
            NotificationPacketType,
            DropItemNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateGoldItemPickup(
        ushort groundId,
        ushort pickerUserId,
        uint goldAmount,
        byte pickerPartyIndex = 0)
    {
        if (pickerPartyIndex >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(pickerPartyIndex));
        }

        var amounts = new uint[4];
        amounts[pickerPartyIndex] = goldAmount;
        return CreateGoldItemPickup(groundId, pickerUserId, amounts);
    }

    public static GameServerPacket CreateGoldItemPickup(
        ushort groundId, ushort pickerUserId, IReadOnlyList<uint> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        if (amounts.Count != 4) throw new ArgumentException("Gold pickup requires four party slots.", nameof(amounts));
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(groundId);
        writer.Write(pickerUserId);
        for (var partyIndex = 0; partyIndex < 4; partyIndex++)
        {
            writer.Write(amounts[partyIndex]);
            writer.Write((byte)0); // no economic-control adjustment entries
        }

        return new GameServerPacket(
            NotificationPacketType,
            GetItemNotification,
            payload.ToArray());
    }

    /// <summary>
    /// `NOTI 86 SERVER_BROADCAST` 的子类型 5：官服拾取成功后向**全场**（`GameWorld::send_all`，含拾取者）
    /// 广播"地面物已被取走"。载荷 = `u8 5` + `u16 拾取者 uid` + `u32 物品 id`（7 字节）。
    ///
    /// `[静态]` 官服 `DisPatcher_GetItem::process@0x081C35C0(1832)`：`put_header(0, 86)`@`0x081c3a58`、
    /// `put_byte(5)`、`put_short(CUser::get_unique_id)`、`put_int(v31)`，末尾 `GameWorld::send_all`。
    /// 其中 `v31` = `CParty::get_item` 的第 5 个出参 = **物品 id**（`[ebp-0x28]`；`get_item` 里是 `v117+2`，
    /// 即喂给 `CDataManager::find_item` 的那个值），不是地面物 id。
    ///
    /// `[静态]` 官服的广播**带条件**：`*(dungeon+2140)` 为真、请求第 3 字段 ∈ {2,3,4}、且物品不可叠加。
    /// **本客户端那 8 字节 `CMD 46` 里读不出这三个条件对应的字段**（版本差，42 条真机包体已证）
    /// ⇒ 严格照抄条件等于永不广播。裁决（2026-10-03 用户确认）：**成功直取即广播**，条件差异记在此处与台账。
    ///
    /// `[静态]` 客户端可达性：`NOTI 86` → `0x427104`，只实现子类型 1/2；子类型 5 落
    /// `0x42740F → 0x42A069`（空操作）⇒ **照发但画面不变**（PD-3002）。
    /// </summary>
    public static GameServerPacket CreatePickupRemovedBroadcast(ushort pickerUserId, uint itemId)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(PickupRemovedSubtype);
        writer.Write(pickerUserId);
        writer.Write(itemId);
        return new GameServerPacket(
            NotificationPacketType,
            PickupRemovedNotification,
            payload.ToArray());
    }

    /// <summary>
    /// DFLegacy's client patch extends the unchanged official seven-byte prefix
    /// with the exact ground entity id. The stock subtype-5 handler is a no-op;
    /// the patch removes only that entity without replaying inventory rewards.
    /// </summary>
    public static GameServerPacket CreatePickupRemovedBroadcast(
        ushort pickerUserId, uint itemId, ushort groundId)
    {
        var original = CreatePickupRemovedBroadcast(pickerUserId, itemId);
        var payload = new byte[9];
        original.Payload.CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(7), groundId);
        return original with { Payload = payload };
    }

    /// <summary>官服 `put_byte(5)`：`NOTI 86` 里"地面物被取走"的子类型。</summary>
    public const byte PickupRemovedSubtype = 5;

    /// <summary>
    /// `CMD 46 GET_ITEM` 的**成功**回执。`[静态]` 官服 `DisPatcher_GetItem::send@0x081C3CE8(93)`：
    /// 结果码为 0 → `CUser::SendCmdOkPacket(user, 46)`，而
    /// `CUser::SendCmdOkPacket@0x0867BEA0(161)` = `put_header(class 1, cmd)` + `put_byte(1)` ⇒ **body = `[0x01]`**。
    ///
    /// `[静态]` 客户端可达性：`CMD 46` 的接收处理器 `0x414090`（remap 表 `0x417638` + 跳转表 `0x417480`）
    /// 第一条就是 `test al, al; jne 0x417467` —— `body[0]`（帧的 resultFlag）非 0 就**直接返回**。
    /// 官方成功包的 body[0] 正是 `1` ⇒ **本客户端对成功回执是空操作**。
    /// 裁决：按红线"官服有、客户端无反应 ⇒ 照发并标注"落地（PD-3005）。
    /// </summary>
    public static GameServerPacket CreateGetItemAcceptance(PacketFrame request)
    {
        if (request.ProtocolId != GetItemCommand)
        {
            throw new ArgumentException(
                "A GET_ITEM acceptance requires a GET_ITEM request.",
                nameof(request));
        }

        return new GameServerPacket(CommandPacketType, GetItemCommand, [1]);
    }

    /// <summary>
    /// `NOTI 39 GET_ITEM`（物品形态）：`u16 groundId` + `u16 拾取者 uid` + **4 个槽位字节** +
    /// `u16 归属者 uid` + `u16 目标背包槽位`。归属者可与拾取者不同；
    /// 客户端仅在归属者 uid 等于自己时执行本地入包，其他成员只显示骰子/获奖信息。
    ///
    /// `[静态]` 客户端 `0x421EA7` 的物品分支（`0x422275`）按 `u16, u16, 4×u8, u16, u16` 读，
    /// 并对**每个非 0 槽位**打印一行"[该槽成员名][数字]"（`0x422313-0x422420` 的循环 + 打印）——
    /// 所以这 4 个字节既不能乱写、也不能当成占位符：
    ///
    /// * **直取路径**（没有跑骰子）：官服 `CParty::get_item` 里的 `int v136[4]` 被函数入口的
    ///   `memset(v136, 0, 16)` 清零，且只有 routing 分支才会写它 ⇒ **全 0**
    ///   （`ida_s5_loot.txt:188` 与 `:973-978`）。这也正是"只有拾取者本人该看到获得行"的实现口径。
    /// * **routing 路径**：写 `get_item` 算出的**按槽位加权分**（`:817-832`）。
    ///
    /// routing 路径发送真实骰子分，并向全队发送同一结果；直取路径仍全 0。
    /// </summary>
    public static GameServerPacket CreateInventoryItemPickup(
        ushort groundId,
        ushort pickerUserId,
        ushort destinationSlot,
        IReadOnlyList<byte>? routingScores = null,
        ushort? winnerUserId = null)
    {
        var scores = routingScores ?? ZeroRoutingScores;
        if (scores.Count != RoutingScoreSlots)
        {
            throw new ArgumentException(
                $"A GET_ITEM pickup needs exactly {RoutingScoreSlots} routing score slots.",
                nameof(routingScores));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(groundId);
        writer.Write(pickerUserId);
        foreach (var score in scores)
        {
            writer.Write(score);
        }

        writer.Write(winnerUserId ?? pickerUserId);
        writer.Write(destinationSlot);
        return new GameServerPacket(
            NotificationPacketType,
            GetItemNotification,
            payload.ToArray());
    }

    /// <summary>`NOTI 39` 的槽位字节数（客户端按队伍槽位循环读取）。</summary>
    public const int RoutingScoreSlots = 4;

    private static readonly byte[] ZeroRoutingScores = new byte[RoutingScoreSlots];

    public static GameServerPacket CreateEnableClearDungeon() =>
        new(NotificationPacketType, EnableClearDungeonNotification, []);

    public static GameServerPacket CreatePlayResult(
        ushort userId,
        uint resultValue,
        ushort resultDetailA = 0,
        ushort resultDetailB = 0)
    {
        _ = resultValue;
        _ = resultDetailA;
        _ = resultDetailB;

        // DNF 1.0.1.9 reads five bytes: the party user id followed by
        // three score flags and one reserved result byte.
        var payload = new byte[]
        {
            checked((byte)userId),
            0,
            0,
            0,
            0,
        };
        return new GameServerPacket(
            NotificationPacketType,
            PlayResultNotification,
            payload);
    }

    public static GameServerPacket CreateClearDungeonReward(
        ushort userId,
        uint goldCardCost,
        uint freeGoldAmount = 0,
        ushort? freeItemId = null,
        bool goldCardEnabled = true,
        ushort? goldItemId = null,
        bool premiumCardEnabled = false,
        ushort? premiumItemId = null,
        byte localPartyIndex = 0,
        GameDungeonClearExperienceBreakdown? experience = null,
        IReadOnlyList<GamePremiumCardReward>? partyPremiumRewards = null)
    {
        if (localPartyIndex >= ClearRewardCardColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(localPartyIndex));
        }

        if (partyPremiumRewards is not null && partyPremiumRewards.Count != ClearRewardCardColumnCount)
            throw new ArgumentException("Party premium rewards must contain four slots.", nameof(partyPremiumRewards));

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(checked((byte)userId));

        experience ??= GameDungeonClearExperienceBreakdown.Empty;

        // DNF 1.0.1.9 reads nine uint32 values. Its NOTI 35 receiver derives
        // the displayed base value as wire[0] - wire[2], so wire[0] must carry
        // base plus the party bonus rather than base alone.
        writer.Write((uint)Math.Min(
            uint.MaxValue,
            (ulong)experience.BaseExperience + experience.PartyBonus));
        writer.Write(experience.RankBonus);
        writer.Write(experience.PartyBonus);
        writer.Write(experience.AvatarBonus);
        writer.Write(experience.EventBonus);
        // DF2008 setters: +0x130 black diamond, +0x134 matching channel,
        // +0x120 mentor. These are not the newer server's PC-room/material fields.
        writer.Write(experience.BlackDiamondBonus);
        writer.Write(experience.ChannelBonus);
        writer.Write(experience.MentorBonus);
        writer.Write(experience.CreatureBonus);

        // No immediate inventory rewards are attached to this notification.
        writer.Write((byte)0);

        // DFLegacy increments its visible-card count for every non-empty entry
        // in this first group. Keep all four columns present even in solo play.
        for (var cardIndex = 0;
             cardIndex < ClearRewardCardColumnCount;
             cardIndex++)
        {
            WriteFreeClearRewardPreview(
                writer,
                cardExists: true,
                freeItemId,
                freeGoldAmount);
        }

        writer.Write(goldCardEnabled ? goldCardCost : 0u);

        // The paid-card result remains hidden in NOTI 35. It is revealed by
        // SELECT_CARD's group-3 result list only after the player buys a card.
        for (var partyIndex = 0;
             partyIndex < ClearRewardCardColumnCount;
             partyIndex++)
        {
            writer.Write((byte)0);
        }

        for (var partyIndex = 0;
             partyIndex < ClearRewardCardColumnCount;
             partyIndex++)
        {
            WriteEquipmentRewardPreview(
                writer,
                partyPremiumRewards?[partyIndex].Enabled
                    ?? (premiumCardEnabled && partyIndex == localPartyIndex),
                partyPremiumRewards is null ? premiumItemId : partyPremiumRewards[partyIndex].ItemId);
        }

        return new GameServerPacket(
            NotificationPacketType,
            ClearDungeonRewardNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateScoreScrollStateReply() =>
        new(CommandPacketType, ScoreScrollStateCommand, [1]);

    public static GameServerPacket CreateCardSelectRightState(
        bool localUserCanSelect = true,
        byte localPartyIndex = 0)
    {
        if (localPartyIndex >= ClearRewardCardColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(localPartyIndex));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        for (var partyIndex = 0;
             partyIndex < ClearRewardCardColumnCount;
             partyIndex++)
        {
            writer.Write(partyIndex == localPartyIndex
                ? (ushort)(localUserCanSelect ? 1 : 0)
                : ushort.MaxValue);
        }

        return new GameServerPacket(
            CommandPacketType,
            CardSelectRightStateCommand,
            payload.ToArray());
    }

    public static GameServerPacket CreateCardSelectRightState(IReadOnlyList<ushort> partyRights)
    {
        if (partyRights.Count != ClearRewardCardColumnCount)
            throw new ArgumentException("Party card rights must contain four slots.", nameof(partyRights));
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        foreach (var right in partyRights)
        {
            if (right is not (0 or 1 or ushort.MaxValue))
                throw new ArgumentException("Invalid party card right.", nameof(partyRights));
            writer.Write(right);
        }
        return new(CommandPacketType, CardSelectRightStateCommand, payload.ToArray());
    }

    public static GameServerPacket CreateSelectCardReply(
        IReadOnlyList<byte> freeOwners,
        IReadOnlyList<byte> paidOwners,
        IReadOnlyList<ushort?> paidItems,
        IReadOnlyList<GameClearCardReward?>? freeRewards = null)
    {
        if (freeOwners.Count != ClearRewardCardColumnCount || paidOwners.Count != ClearRewardCardColumnCount || paidItems.Count != ClearRewardCardColumnCount)
            throw new ArgumentException("Party card snapshot must contain four columns.");
        if (freeRewards is not null && freeRewards.Count != ClearRewardCardColumnCount)
            throw new ArgumentException("Free card display must contain four columns.", nameof(freeRewards));
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        for (var i = 0; i < ClearRewardCardColumnCount; i++)
        {
            writer.Write(freeOwners[i]);
            writer.Write(paidOwners[i]);
            var assigned = paidOwners[i] != byte.MaxValue;
            writer.Write((byte)(assigned ? paidItems[i].HasValue ? 2 : 1 : 0));
            if (assigned)
            {
                writer.Write((ushort)0);
                writer.Write(0u);
                if (paidItems[i].HasValue)
                {
                    writer.Write(paidItems[i]!.Value);
                    writer.Write(0u);
                }
            }
        }
        if (freeRewards is not null)
        {
            // ACT1's native CMD74 only updates owners and paid contents. The
            // IJL15 receiver refreshes group-0 previews from this versioned
            // trailer before the original owner handler opens the cards.
            writer.Write("DFCR"u8);
            writer.Write((byte)1);
            writer.Write((byte)ClearRewardCardColumnCount);
            for (var i = 0; i < ClearRewardCardColumnCount; i++)
            {
                var assigned = freeOwners[i] != byte.MaxValue;
                var reward = assigned ? freeRewards[i] : null;
                writer.Write((byte)(assigned ? 1 : 0));
                writer.Write(reward?.GoldAmount ?? 0u);
                writer.Write(reward?.ItemId ?? (ushort)0);
            }
        }
        return new GameServerPacket(CommandPacketType, SelectCardCommand, payload.ToArray());
    }

    public static GameServerPacket CreateSelectCardReply(
        byte? freeCardIndex,
        byte? goldCardIndex,
        bool goldRewardRevealed = false,
        ushort? goldRewardItemId = null,
        byte localPartyIndex = 0)
    {
        if (freeCardIndex >= ClearRewardCardColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(freeCardIndex));
        }

        if (goldCardIndex >= ClearRewardCardColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(goldCardIndex));
        }

        if (localPartyIndex >= ClearRewardCardColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(localPartyIndex));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        for (byte cardIndex = 0;
             cardIndex < ClearRewardCardColumnCount;
             cardIndex++)
        {
            writer.Write(freeCardIndex == cardIndex
                ? localPartyIndex
                : byte.MaxValue);
            writer.Write(goldCardIndex == cardIndex
                ? localPartyIndex
                : byte.MaxValue);

            var writeGoldReward = goldRewardRevealed
                && goldCardIndex == cardIndex;
            writer.Write((byte)(writeGoldReward
                ? goldRewardItemId.HasValue ? 2 : 1
                : 0));
            if (writeGoldReward)
            {
                // DNF 1.0.1.9 treats the first record as the gold line and
                // renders equipment only from the second record. Paid cards
                // award no gold, but still require this zero-gold record.
                writer.Write((ushort)0);
                writer.Write(0u);

                if (goldRewardItemId.HasValue)
                {
                    writer.Write(goldRewardItemId.Value);
                    writer.Write(0u);
                }
            }
        }

        return new GameServerPacket(
            CommandPacketType,
            SelectCardCommand,
            payload.ToArray());
    }

    private static void WriteFreeClearRewardPreview(
        BinaryWriter writer,
        bool cardExists,
        ushort? itemId,
        uint goldAmount)
    {
        writer.Write((byte)(cardExists
            ? itemId.HasValue ? 2 : 1
            : 0));
        if (!cardExists)
        {
            return;
        }

        // A free card always contains its clear-gold record. Equipment is a
        // second, independently rolled record in the same card.
        writer.Write((ushort)0);
        writer.Write(goldAmount);

        if (itemId.HasValue)
        {
            writer.Write(itemId.Value);
            writer.Write(0u);
        }
    }

    private static void WriteEquipmentRewardPreview(
        BinaryWriter writer,
        bool rewardExists,
        ushort? itemId)
    {
        if (!rewardExists)
        {
            writer.Write((byte)0);
            return;
        }

        // The client always interprets the first record as the gold line and
        // renders an equipment icon only when a second record exists.
        writer.Write((byte)(itemId.HasValue ? 2 : 1));
        writer.Write((ushort)0);
        writer.Write(0u);

        if (itemId.HasValue)
        {
            writer.Write(itemId.Value);
            writer.Write(0u);
        }
    }

    public static GameServerPacket CreateEplpReply(byte state, byte option) =>
        new(CommandPacketType, EplpCommand, [1, state, option]);

    public static GameServerPacket CreateCheckConnection(ReadOnlySpan<byte> challenge)
    {
        if (challenge.Length != 16)
        {
            throw new ArgumentException("The check-connection challenge must be exactly 16 bytes.", nameof(challenge));
        }

        return new GameServerPacket(
            NotificationPacketType,
            CheckConnectionNotification,
            challenge.ToArray());
    }

    public static GameServerPacket CreateEmptyUserInfo()
    {
        return CreateUserInfo([]);
    }

    public static GameServerPacket CreateUserInfo(IReadOnlyList<GameCharacterSummary> characters)
    {
        if (characters.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(characters));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // Mode 2 is the character-selection representation. Unlike mode 0,
        // it constructs the visible character cards directly in this build.
        writer.Write((byte)2);
        writer.Write((ushort)characters.Count);

        foreach (var character in characters)
        {
            if (character.NameBytes.Length is 0 or >= 31)
            {
                throw new ArgumentException("Character names must contain 1-30 encoded bytes.", nameof(characters));
            }

            writer.Write(character.Slot);
            WriteLengthPrefixedBytes(writer, character.NameBytes);
            writer.Write(character.Job);
            writer.Write(PackGrowthType(character));
            writer.Write(character.Level);
            // The legacy client indexes the rank-name table from this byte
            // (0 = 10级, 1 = 9级, ...).  AwakeningType is already packed in
            // the high growth bits by PackGrowthType and must not occupy this
            // field; doing so makes an awakened character display as 9级.
            writer.Write(Math.Clamp(character.PvpGrade, (byte)0, (byte)30));
            writer.Write((byte)0); // character state
            var appearanceItems = (character.AppearanceItems ?? [])
                .Where(item => item.Slot <= 9 && item.ItemId != ushort.MaxValue)
                .GroupBy(item => item.Slot)
                .Select(group => group.First())
                .OrderBy(item => item.Slot)
                .ToArray();
            if (appearanceItems.Length > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(characters));
            }

            writer.Write((byte)appearanceItems.Length);
            foreach (var item in appearanceItems)
            {
                writer.Write((byte)item.Slot);
                writer.Write(item.ItemId);
                writer.Write(item.State);
            }

            writer.Write((byte)0); // character-list extension state
            writer.Write(0u);      // guild id
            writer.Write((byte)0); // guild/member flag
            writer.Write((byte)0); // character-list state
            writer.Write((byte)0); // character-list option
            writer.Write((byte)1); // visible/selectable character card
        }

        return new GameServerPacket(
            NotificationPacketType,
            UserInfoNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCurrentCharacterInfo(
        GameCharacterSummary character,
        ushort userId,
        IReadOnlyCollection<GameInventoryEntry>? equippedItems = null,
        GameCreatureAppearanceEntry? equippedCreature = null,
        bool hasBlackDiamond = false,
        bool hasPrivateStorePermission = false)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (character.NameBytes.Length is 0 or >= 31)
        {
            throw new ArgumentException(
                "Character names must contain 1-30 encoded bytes.",
                nameof(character));
        }

        if (equippedCreature is not null
            && (equippedCreature.ItemId == 0
                || equippedCreature.ItemId == ushort.MaxValue
                || equippedCreature.NameBytes.Length >= 31))
        {
            throw new ArgumentException(
                "Creature appearance requires a valid item id and at most 30 encoded name bytes.",
                nameof(equippedCreature));
        }

        var visibleItems = (equippedItems ?? [])
            // The mode-zero reader indexes a 12-entry compact appearance table
            // directly by slot. Slot 10 is the native [title name] endpoint.
            .Where(item => item.Slot <= 10 && item.ItemId != ushort.MaxValue)
            .GroupBy(item => item.Slot)
            .Select(group => group.First())
            .OrderBy(item => item.Slot)
            .ToArray();
        if (visibleItems.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(equippedItems));
        }

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // Mode 0 updates a player entity in the active town. The record's
        // leading id must match the server id returned by SELECT_CHARACTER so
        // the client recognizes it as the locally controlled character.
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write(userId);
        WriteLengthPrefixedBytes(writer, character.NameBytes);
        writer.Write(character.Job);
        writer.Write(PackGrowthType(character));
        writer.Write(character.Level);
        // Mode 0 uses the same legacy PVP-grade byte immediately after level.
        // Keep awakening in PackGrowthType; otherwise the town actor would
        // display the awakening stage as its PVP rank.
        writer.Write(Math.Clamp(character.PvpGrade, (byte)0, (byte)30));
        writer.Write((byte)0); // character state
        writer.Write((byte)0); // town-entity state
        writer.Write((byte)visibleItems.Length);
        foreach (var item in visibleItems)
        {
            // USERINFO mode 0 carries compact appearance entries. Unlike the
            // ten-byte mode-1 inventory object record, this is safe for the
            // locally controlled town actor.
            writer.Write((byte)item.Slot);
            writer.Write(item.ItemId);
            writer.Write(item.State);
        }

        writer.Write((byte)0); // town-entity extension state
        writer.Write(0u);      // guild id
        // 419F17 -> record+A0 -> 47B9B0 allocates the actor's shop component.
        // This is the private-store entitlement, independent of guild state.
        writer.Write(hasPrivateStorePermission ? (byte)1 : (byte)0);

        // DFLegacy mode zero keeps the legacy guild/title placeholders even
        // when they are empty. Omitting either length prefix shifts every
        // actor-state field and leaves the town scene black.
        // The old client converts this equipment id to [creature species]
        // through sub_50D8D0, then uses the adjacent name/state while its
        // mode-zero actor update creates the map Creature object.
        writer.Write(equippedCreature?.ItemId ?? (ushort)0);
        WriteLengthPrefixedBytes(writer, equippedCreature?.NameBytes ?? []);
        // The client stores !wireState on CNRDVirtualCreature+0x1A4 and only
        // registers the object in the scene when that internal value is zero.
        writer.Write(equippedCreature?.State ?? (byte)0);
        writer.Write((byte)0);   // PC-room name effect (record + 0xB0)
        // USERINFO mode zero stores this encrypted flag at record + 0xB8.
        // The name renderer uses value one for both the gold name color and
        // pcroompremiumreward.img index 3 (the black-diamond icon).
        writer.Write(hasBlackDiamond ? (byte)1 : (byte)0);
        // This is the town name-grade/reputation score, not an equipment id.
        // Values >= 100 select the client's generic GradeB/C/D title effects.
        // Equipped [title name] items belong in compact appearance slot 10.
        writer.Write(0u);
        // These following fields are independent guild-info state/name data.
        // Enabling them for a title makes the client construct a guild object.
        writer.Write((byte)0);
        WriteLengthPrefixedBytes(writer, []); // guild name
        writer.Write((byte)1);   // active entity
        writer.Write(0u);        // actor state A
        writer.Write(0u);        // actor state B
        writer.Write(0u);        // actor state C

        return new GameServerPacket(
            NotificationPacketType,
            UserInfoNotification,
            payload.ToArray());
    }

    public static GameServerPacket CreateCurrentCharacterDetails(
        ushort userId,
        uint experience = 0,
        IReadOnlyList<GameSkillEntry>? learnedSkills = null,
        GameCharacterCombatStats? combatStats = null,
        IReadOnlyCollection<GameInventoryEntry>? equippedItems = null,
        byte equippedCreatureLevel = 0,
        byte staminaRecoveryPercentage = 100)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (staminaRecoveryPercentage > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staminaRecoveryPercentage));
        }

        var statBytes = CreateCombatStatBlock(combatStats, staminaRecoveryPercentage);

        learnedSkills ??= [];
        if (learnedSkills.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(learnedSkills));
        }

        var equipment = ProjectWornEquipment(equippedItems, nameof(equippedItems));

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);       // full current-character subtype
        writer.Write((ushort)1);     // record count
        writer.Write(userId);
        writer.Write(experience);
        writer.Write((uint)statBytes.Length);
        writer.Write(statBytes);
        writer.Write((byte)equipment.Length);
        foreach (var item in equipment)
        {
            // USERINFO subtype 1 is the authoritative worn-equipment
            // projection in this client. Its equipment record is exactly:
            // slot:u8, item-id:u16, instance/value:u32, state:u8,
            // durability:u16. The handler also removes any of the 23 worn
            // slots omitted from this snapshot.
            writer.Write(checked((byte)item.Slot));
            writer.Write(item.ItemId);
            writer.Write(item.CountOrValue);
            writer.Write(item.State);
            writer.Write(item.Durability);
        }

        writer.Write((byte)learnedSkills.Count);
        foreach (var skill in learnedSkills)
        {
            // Subtype one uses compact skill-id/level pairs. The separate
            // SKILLINFO notification carries the third state byte.
            writer.Write(skill.SkillId);
            writer.Write(skill.Level);
        }
        writer.Write(equippedCreatureLevel);

        return new GameServerPacket(
            NotificationPacketType,
            UserInfoNotification,
            payload.ToArray());
    }

    /// <summary>
    /// “查看信息”（CMD 8 mode 3）的资料回包：NOTI 2 mode 3、count 1；
    /// 或 mode 1 的“只补模型”记录（给其他玩家补 <c>CNUser+0x110</c> 模型对象，见下）。
    /// </summary>
    /// <remarks>
    /// mode 1/3 共用客户端同一条记录解析器（0x41A764），但**只有 mode-3 记录尾部的
    /// 28 字节会打开资料卡窗口**（CNRDAnotherUserPopUpWindow，窗口 id 0x16）；
    /// 回 mode 1 只更新目标角色的模型/装备，卡片窗口根本不弹——“点了没反应”的根因。
    /// 回包记录的精确布局（勘察逐条 `call 0x8F5120/40/60` 得到，全部是硬证据）：
    /// [u8 mode][u16 count=1] 后接每条记录
    /// [u16 uid][u32 经验][u32 blobLen + blob][u8 nEq + n×10 字节装备]
    /// [u8 nPairs + n*(u8 skillId,u8 level)][u8 creature][28 字节 mode-3 专用尾]。
    /// uid 之后的 u32 是**累计总经验**（0x41A828 ReadU32 → 0x41A85A 调
    /// `actor->vtbl[0x490](值, 本地记录等级)`，即 0x4974E0 的经验 setter：本级已得 =
    /// 值−本级起始等级经验）；它**不是**名字槽位参数（旧注释按 “flag” 写 0，卡片经验才显示 0）。
    /// 记录里**没有名字字段**：名字来自客户端本地 user 注册表（mode-0 USERINFO
    /// 记录），所以回包前必须已经推过该 uid 的 mode-0 记录。装备列表是必需的：客户端会
    /// 删除列表未覆盖的 0..22 槽（0x41AC50），不发的槽在卡片里就是空的。
    /// <para>
    /// **mode 1 的用途（2026-09-30 真机定位的崩溃根因）**：客户端 `NOTI 2` 按 mode 分派到
    /// 两个不同解析器——mode 0 走 `0x419BEF`（“区域用户”，只登记区域 actor），
    /// **只有 mode 1/3 走 `0x41A764` 详情解析器**，而后者才是建 `CNUser+0x110` 模型对象的地方：
    /// `0x417AB0(record)` 取 `+0x110` → 为 0 则 `0x654E50(*(record+0x14))`（职业工厂）新建 →
    /// `0x4FD880(record, actor)` 挂上去。给**其他玩家**只发 mode 0 时，其 `CNUser` 会被
    /// 创建（`0x4FDF60`）但**模型对象永远是 NULL**，结算/奖励界面（状态机 state 8）解引用
    /// 该指针即空指针读 → 队员端客户端崩溃；同一原因也让副本内看不到队友。
    /// 因此“他人外观”必须 **mode 0（进名字与 actor）+ mode 1（补模型）成对、且 mode 0 在前**。
    /// mode 1 不带 28 字节尾（写了会被当成资料卡参数读走，窗口行为不可控）。
    /// </para>
    /// </remarks>
    public static GameServerPacket CreateRequestedUserInfoDetail(
        ushort userId,
        IReadOnlyCollection<GameInventoryEntry>? equippedItems = null,
        GameCharacterCombatStats? combatStats = null,
        byte equippedCreatureLevel = 0,
        byte staminaRecoveryPercentage = 100,
        uint experience = 0,
        byte mode = 3,
        IReadOnlyList<GameSkillEntry>? learnedSkills = null)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        if (mode is not (1 or 3))
        {
            // 0 是“区域用户”解析器、2 是账号角色列表，都不是这条记录的形状。
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "mode 只能是 1 或 3。");
        }

        if (staminaRecoveryPercentage > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(staminaRecoveryPercentage));
        }

        learnedSkills ??= [];
        if (learnedSkills.Count > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(learnedSkills));
        }

        var statBytes = CreateCombatStatBlock(combatStats, staminaRecoveryPercentage);
        var equipment = ProjectWornEquipment(equippedItems, nameof(equippedItems));

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // mode 3 = 资料卡（带 28 字节尾）；mode 1 = 只补模型（无尾）。两者共用解析器。
        writer.Write(mode);
        writer.Write((ushort)1);     // 记录条数
        writer.Write(userId);        // 客户端按它 FindByUid，必须原样回发请求里的 uid
        // 累计总经验（客户端 0x41A828 读出后交给 0x4974E0 经验 setter）。写 0 时卡片
        // 经验显示 0；目标角色的存档 Experience 与 mode-1 本地玩家用的是同一个字段。
        writer.Write(experience);
        // 81 字节 blob 是战斗数值块（HP/MP/攻防/抗性/速度/负重 + 体力恢复%），
        // 走 actor->vtbl[0x49C]（0x495D40）写 actor 的状态块，卡片**不从它读经验**。
        // 内容语义未证实（客户端只做原样拷贝），待真机确认。
        writer.Write((uint)statBytes.Length);
        writer.Write(statBytes);
        writer.Write((byte)equipment.Length);
        foreach (var item in equipment)
        {
            // 与 mode-1 相同的 10 字节装备记录：slot:u8, item-id:u16, 实例值:u32,
            // 状态:u8, 耐久:u16。客户端对未列出的槽执行删除，所以卡片要显示装备就必须
            // 把目标当前穿的槽逐条发全（按 slot 升序）。
            writer.Write(checked((byte)item.Slot));
            writer.Write(item.ItemId);
            writer.Write(item.CountOrValue);
            writer.Write(item.State);
            writer.Write(item.Durability);
        }

        // The shared mode-1/3 parser reads exactly u8/u8 pairs. Foreign
        // actors clear and rebuild their skill levels from this list before
        // simulating peer actions; an empty list is not a usable peer snapshot.
        // GameSkillEntry.SkillId is a byte, matching current-character mode 1.
        writer.Write((byte)learnedSkills.Count);
        foreach (var skill in learnedSkills)
        {
            writer.Write(skill.SkillId);
            writer.Write(skill.Level);
        }
        writer.Write(equippedCreatureLevel);

        if (mode == 3)
        {
            // mode-3 专用 28 字节尾 = 3×u32 + 4×u8 + 3×u32。勘察确认的落点与读数：
            // A/B → 资料卡窗口 +0x50/+0x54（卡片画 `%d胜 %d败` 与 A/(A+B) 百分比，新号 0 正确）；
            // C → +0x58，a..d → 栈上结构体 +0x10..+0x13（+0x11..+0x13 全二进制只写不读）；
            // X/Y → user 记录 +0xDC/+0xE0（卡片画 **Y*100/X 的百分比**，中等置信度），Z → +0xE4
            // （未找到读取点）。当前全 0；经验的主来源是上面的 u32，若真机卡片仍显示 0%，
            // 再试 X=本级所需经验、Y=本级已得经验（此项待真机确认）。
            // mode 1 绝不写这段：写了会被详情解析器当成资料卡参数读走。
            writer.Write(0u);   // A：资料卡窗口 +0x50（胜负/百分比）
            writer.Write(0u);   // B：资料卡窗口 +0x54
            writer.Write(0u);   // C：资料卡窗口 +0x58
            writer.Write((byte)0); // a：窗口 +0x5C（四个 u8 里唯一有读取点的）
            writer.Write((byte)0); // b：结构体 +0x11（只写不读）
            writer.Write((byte)0); // c：结构体 +0x12（只写不读）
            writer.Write((byte)0); // d：结构体 +0x13（只写不读）
            writer.Write(0u);   // X：user 记录 +0xDC（Y*100/X 的分母，待真机确认）
            writer.Write(0u);   // Y：user 记录 +0xE0（Y*100/X 的分子，待真机确认）
            writer.Write(0u);   // Z：user 记录 +0xE4（未找到读取点）
        }

        return new GameServerPacket(
            NotificationPacketType,
            UserInfoNotification,
            payload.ToArray());
    }

    private static readonly GameCharacterCombatStats DefaultCombatStats = new(
        MaximumHp: 11_600,
        MaximumMp: 11_900,
        PhysicalAttack: 75,
        PhysicalDefense: 75,
        MagicalAttack: 45,
        MagicalDefense: 45,
        FireResistance: 0,
        WaterResistance: 0,
        DarkResistance: 0,
        LightResistance: 0,
        InventoryLimit: 480_000,
        HpRegeneration: 0,
        MpRegeneration: 500,
        MovementSpeed: 8_500,
        AttackSpeed: 8_500,
        CastSpeed: 7_000,
        HitRecovery: 6_000,
        JumpPower: 4_300,
        Weight: 500_000);

    private static GameInventoryEntry[] ProjectWornEquipment(
        IReadOnlyCollection<GameInventoryEntry>? equippedItems,
        string parameterName)
    {
        var equipment = (equippedItems ?? [])
            .Where(item => item.Slot < 23)
            .OrderBy(item => item.Slot)
            .ToArray();
        if (equipment.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return equipment;
    }

    private static byte[] CreateCombatStatBlock(
        GameCharacterCombatStats? combatStats,
        byte staminaRecoveryPercentage)
    {
        var stats = combatStats ?? DefaultCombatStats;
        using var statsPayload = new MemoryStream();
        using (var writer = new BinaryWriter(statsPayload, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // DFLegacy reads a length-prefixed 81-byte combat-stat block.
            writer.Write(stats.MaximumHp);
            writer.Write(stats.MaximumMp);
            writer.Write(stats.PhysicalAttack);
            writer.Write(stats.PhysicalDefense);
            writer.Write(stats.MagicalAttack);
            writer.Write(stats.MagicalDefense);
            writer.Write(stats.FireResistance);
            writer.Write(stats.WaterResistance);
            writer.Write(stats.DarkResistance);
            writer.Write(stats.LightResistance);
            for (var index = 0; index < 17; index++)
            {
                writer.Write((ushort)0); // active-status resistances
            }

            writer.Write(stats.InventoryLimit);
            writer.Write(stats.HpRegeneration);
            writer.Write(stats.MpRegeneration);
            writer.Write(stats.MovementSpeed);
            writer.Write(stats.AttackSpeed);
            writer.Write(stats.CastSpeed);
            writer.Write(stats.HitRecovery);
            writer.Write(stats.JumpPower);
            writer.Write(stats.Weight);
            // The old client stores its current weakness/stamina recovery
            // percentage in the final byte of this exact 81-byte block.
            writer.Write(staminaRecoveryPercentage);
        }

        var statBytes = statsPayload.ToArray();
        if (statBytes.Length != 81)
        {
            throw new InvalidOperationException("The DFLegacy combat-stat block must contain exactly 81 bytes.");
        }

        return statBytes;
    }

    private static void WriteLengthPrefixedBytes(BinaryWriter writer, byte[] value)
    {
        writer.Write((uint)value.Length);
        writer.Write(value);
    }

    private static byte PackGrowthType(GameCharacterSummary character) =>
        PackGrowthType(character.GrowType, character.AwakeningType);

    private static byte PackGrowthType(byte growType, byte awakeningType)
    {
        // DF2008 stores the first growth branch in bits 0-3 and the awakening
        // stage in bits 4-6 of the same USERINFO byte; the friend-list record's
        // R+0x24 and the id-63 C byte use exactly the same packing.
        return (byte)((growType & 0x0F) | ((awakeningType & 0x07) << 4));
    }

    private static void WriteIpv4Address(BinaryWriter writer, System.Net.IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var bytes = address.MapToIPv4().GetAddressBytes();
        writer.Write(bytes);
    }

    public static GameServerPacket CreateChannelInfo(
        ReadOnlySpan<byte> displayName,
        uint channelType,
        uint result,
        byte serverId,
        byte channelNumber,
        uint seed,
        IReadOnlyList<byte[]> hosts,
        uint port1,
        uint port2)
    {
        if (displayName.Length >= 0x40)
        {
            throw new ArgumentException("The DFLegacy channel display name must be shorter than 64 bytes.", nameof(displayName));
        }

        ArgumentNullException.ThrowIfNull(hosts);

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        WriteLengthPrefixedBytes(writer, displayName.ToArray());
        writer.Write(channelType);
        writer.Write(result);
        writer.Write(serverId);
        writer.Write(channelNumber);
        writer.Write(seed);
        writer.Write((uint)hosts.Count);
        foreach (var host in hosts)
        {
            ArgumentNullException.ThrowIfNull(host);
            if (host.Length >= 0x104)
            {
                throw new ArgumentException("A DFLegacy channel host must be shorter than 260 bytes.", nameof(hosts));
            }

            WriteLengthPrefixedBytes(writer, host);
        }

        writer.Write(port1);
        writer.Write(port2);
        return new GameServerPacket(
            NotificationPacketType,
            ChannelInfoNotification,
            payload.ToArray());
    }
}
