namespace DFLegacy.Server;

/// <summary>
/// 一次拾取请求的裁决结果（官服 `DisPatcher_GetItem::process` 的三态 + `CParty::get_item` 的归属）。
/// </summary>
public enum LootPickupOutcome
{
    /// <summary>拾取者本人取得：官服 `get_item` 的直取分支（`v150 = a2`），**没有跑骰子**。</summary>
    Granted,

    /// <summary>
    /// 掷骰路由分支跑过了（官服 `v149` 为真且物品可路由）。
    /// <see cref="LootRoutingDecision.WinnerCharacterId"/> 才是归属——**可能不是拾取者本人**。
    /// </summary>
    Routed,

    /// <summary>
    /// 队伍正处于 routing 生命周期 ⇒ 官服 `process` 直接置结果 **216(0xD8)** 返回，不做普通拾取。
    /// </summary>
    RoutingActive,

    /// <summary>
    /// 地面物不在此房间或已被取走 ⇒ 官服 `get_item` 返回 false、`process` 置 **0x7FFFFFFF**
    /// （"不回包"哨兵）⇒ 调用方**什么都不发**。
    /// </summary>
    NotAvailable,
    /// <summary>官服没有活着且能容纳物品的候选：error 22，保留地面物。</summary>
    NoEligibleRecipient,
    /// <summary>异步得主尚在收件；实体仍在，第二请求须回滚本地拾取等待。</summary>
    PickupInProgress,
}

/// <summary>
/// routing 掷骰的候选成员（官服按**队伍槽位** 0..3 遍历 `_checkValidUser(k) &amp;&amp; *(party+k+896) == 1`）。
/// </summary>
public readonly record struct LootRoutingCandidate(
    byte PartySlot,
    Guid CharacterId,
    ushort UserId,
    bool JobMatchesItem = false,
    bool CanReceiveItem = true);

/// <summary>
/// 拾取裁决：归属（<see cref="WinnerCharacterId"/>）+ `NOTI 39` 要写的 4 个槽位字节
/// （<see cref="RoutingScores"/>）+ 失败时的错误码（<see cref="ErrorCode"/>）。
/// </summary>
public sealed record LootRoutingDecision(
    LootPickupOutcome Outcome,
    Guid WinnerCharacterId,
    byte[] RoutingScores,
    byte ErrorCode,
    Guid PickupId = default);

/// <summary>
/// 会话侧"把一件地面物收进**本会话角色**背包"的结果（`EntranceService.TryGrantGroundItemAsync`）。
/// 与 <see cref="LootPickupOutcome"/> 的分工：那个是**归属裁决**（谁该拿），这个是**交付结果**（拿成功没有）。
/// </summary>
public enum GroundItemGrantOutcome
{
    /// <summary>已落库，`NOTI 39` 与背包刷新都已发给本会话（= 得主本人）。</summary>
    Granted,

    /// <summary>
    /// 业务拒绝（超重 / 宠物栏或时装栏放不下 / 金币溢出）：**按既有口径已经回过包**，
    /// 调用方不必再补任何东西（超重那条回 `CMD 46` error 0x15 + 负重提示）。
    /// </summary>
    Rejected,

    /// <summary>抢输了（别的成员先摘走）或调用时地面物已不在：**什么都不发**。</summary>
    NotAvailable,

    /// <summary>落库失败：按原 GroundId 放回；调用方回 error4 完成原拾取请求。</summary>
    PersistFailed,
}

/// <summary>
/// 掉落与拾取的**归属规则**（纯函数、无状态；地面物账本仍在 <see cref="DungeonInstance"/>，
/// 会话层只读包/投递）。官服证据与地址：
///
/// * 门控 `CItem::check_item_routing@0x0850D756(41)`：
///   `return CItem::isEpicRoutingItem(this) || CItem::get_rarity(this) > 0;`
///   （`CItem::get_rarity@0x080F12D6(11)` = `*(int*)(this+56)`；`isEpicRoutingItem@0x0846996A(12)` = `*(u8*)(this+121)`）
///   ⇒ **"品级 > 0 就算可路由"**，不是"蓝装以上"。
/// * 加权分（`CParty::get_item@0x085A3B98`，`ida_s5_loot.txt:817-832`）：
///   `memset(v136, 0, 16)` → 对每个合法候选 `++v136[k]`（基础 1）→ 轮到轮转者 `+= 40`
///   （轮转下标在 `party+324`，`:784-787`）→ 职业匹配 `+= 30`（`:826-828`）→
///   `v136[k] = v136[k] + get_rand_int(100 - v136[k])`。
/// * 取最大分者（`:848-862`）；并列时 `if (v136[m] == v196 &amp;&amp; v136[m] &gt; 0) --v136[m];`
///   ⇒ **先遍历到的**（= 槽位小的）保住最大分。
/// * `NOTI 39` 的 4 个槽位字节就是这份 `v136`（`:977-978`）；直取路径是 `memset` 出来的全 0。
/// * `CParty::IsRoutingState@0x08145840(40)` = `*(int*)party != 0`；为真时
///   `DisPatcher_GetItem::process` 在调 `get_item` **之前**就置 216 返回（`:1209-1213`）。
///
/// 完整取证已闭合 kind10 是武器、usable-job 缺省位图0；生产已接独立容量预筛及武器职业加成。
/// 完整门控还受掉落来源与ACT1原生确认字节影响；不使用后期官服库存槽号与特殊确认协议编号。
/// </summary>
public static class LootRouting
{
    /// <summary>官服 `CParty::IsRoutingState` 为真时 `process` 写的结果码（216）。</summary>
    public const byte RoutingActiveErrorCode = 0xD8;

    /// <summary>官服 `++v136[k]`：每个合法候选的基础分。</summary>
    public const int BaseScore = 1;

    /// <summary>官服 `v136[k] += 40`：轮到轮转的那名成员。</summary>
    public const int RoundRobinBonus = 40;

    /// <summary>官服 `v136[k] += 30`：职业匹配（见类注释的缺口说明）。</summary>
    public const int JobMatchBonus = 30;

    /// <summary>官服 `get_rand_int(100 - v136[k])` 里的上界。</summary>
    public const int ScoreBound = 100;

    /// <summary>`NOTI 39` 的槽位字节数（客户端按队伍槽位循环读取并打印）。</summary>
    public const int RoutingScoreSlotCount = 4;

    /// <summary>
    /// 官服 `CItem::check_item_routing`：`isEpicRoutingItem || get_rarity() > 0`。
    /// 普通分配与特殊确认分配是不同路径；此谓词不代表启动特殊确认。
    /// </summary>
    public static bool IsRoutable(int? rarity, bool isEpicRoutingItem = false) =>
        isEpicRoutingItem || (rarity ?? 0) > 0;

    /// <summary>
    /// 普通骰子的稀有度下限。2026-10-26 恢复官方 rarity>0，
    /// 同时将原生 NOTI39 骰子结果发送给全队，替代旧的临时 rarity>3 限制。
    /// </summary>
    public const int RoutableRarityFloor = 0;

    /// <summary>
    /// 物品基础谓词；完整生产门控还需掉落来源和原生确认字节。
    /// </summary>
    public static bool IsRoutableForThisClient(int? rarity, bool isEpicRoutingItem = false) =>
        isEpicRoutingItem || (rarity ?? 0) > RoutableRarityFloor;

    /// <summary>
    /// 官服次序：来源1先启用；rarity>3用请求bool覆盖；非0来源epic再用请求bool覆盖。
    /// 最后检查rarity/epic或avatar。客户端报来的rarity不参与权威判定。
    /// </summary>
    public static bool IsRoutableGroundItem(DungeonGroundItem groundItem, ItemDefinition definition,
        bool routeRequested = false)
    {
        if (groundItem.IsGold) return false;
        var enabled = groundItem.RoutingSource == 1;
        if (definition.Rarity > 3) enabled = routeRequested;
        if (groundItem.RoutingSource != 0 && definition.IsEpicRoutingItem) enabled = routeRequested;
        return enabled && (IsRoutable(definition.Rarity, definition.IsEpicRoutingItem)
            || definition.InventoryCategory == ItemInventoryCategory.Avatar);
    }

    public static bool HasJobBonus(ItemDefinition definition, int job) =>
        definition.IsEquipment && definition.TypeTag == "weapon" && definition.IsUsableByJob(job);

    /// <summary>
    /// 裁决一次拾取。判定顺序照官服 `DisPatcher_GetItem::process`：
    /// routing 生命周期 → 地面物是否可拿 → 是否走掷骰。
    /// </summary>
    /// <param name="nextRoundRobinIndex">
    /// 本轮**拿到 +40** 的候选序（官服把 `party+324` 置成该序）；调用方须把它存回实例，
    /// 下一轮从 `(它+1) % 候选数` 继续轮转。
    /// </param>
    public static LootRoutingDecision Decide(
        bool itemAvailable,
        bool routingActive,
        bool routable,
        Guid pickerCharacterId,
        IReadOnlyList<LootRoutingCandidate> candidates,
        int roundRobinIndex,
        Func<int, int> nextRandom,
        out int nextRoundRobinIndex,
        int? partyMemberCount = null,
        Guid forcedOwnerCharacterId = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(nextRandom);

        var scores = new byte[RoutingScoreSlotCount];

        // 官服先问 IsRoutingState（:1209），为真直接 216 返回——此时连 get_item 都不调。
        if (routingActive)
        {
            nextRoundRobinIndex = roundRobinIndex;
            return new LootRoutingDecision(
                LootPickupOutcome.RoutingActive, Guid.Empty, scores, RoutingActiveErrorCode);
        }

        // 官服 get_item 返回 false ⇒ process 置 0x7FFFFFFF（不回包）。
        if (!itemAvailable)
        {
            nextRoundRobinIndex = roundRobinIndex;
            return new LootRoutingDecision(
                LootPickupOutcome.NotAvailable, Guid.Empty, scores, 0);
        }

        var totalMembers = partyMemberCount ?? candidates.Count;
        if (!routable || totalMembers <= 1)
        {
            nextRoundRobinIndex = roundRobinIndex;
            return new LootRoutingDecision(
                LootPickupOutcome.Granted, pickerCharacterId, scores, 0);
        }

        var eligible = candidates.Where(candidate => candidate.CanReceiveItem).OrderBy(candidate => candidate.PartySlot).ToArray();
        nextRoundRobinIndex = roundRobinIndex;
        if (eligible.Length == 0)
            return new LootRoutingDecision(LootPickupOutcome.NoEligibleRecipient, Guid.Empty, scores, 22);
        var bonusOrdinal = roundRobinIndex + 1;
        if (bonusOrdinal >= totalMembers) bonusOrdinal = 0;
        var ownerScore = forcedOwnerCharacterId == Guid.Empty ? 0 : 89 + nextRandom(11);

        for (var ordinal = 0; ordinal < eligible.Length; ordinal++)
        {
            var candidate = eligible[ordinal];
            if (candidate.PartySlot >= RoutingScoreSlotCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(candidates),
                    candidate.PartySlot,
                    "A routing candidate must live in one of the four party slots.");
            }

            var score = BaseScore;
            if (ordinal == bonusOrdinal)
            {
                score += RoundRobinBonus;
                nextRoundRobinIndex = ordinal;
            }

            if (candidate.JobMatchesItem)
            {
                score += JobMatchBonus;
            }

            var remaining = ScoreBound - score;
            if (remaining > 0)
            {
                score += nextRandom(remaining);
            }

            if (ownerScore != 0)
                score = candidate.CharacterId == forcedOwnerCharacterId ? ownerScore : Math.Min(score, ownerScore - 1);
            scores[candidate.PartySlot] = (byte)score;
        }

        // 官服按槽位 0..3 遍历取最大分；并列时后来者自减 1（先到者保住最大分）。
        var best = 0;
        var winnerSlot = -1;
        foreach (var candidate in eligible)
        {
            var score = scores[candidate.PartySlot];
            if (score <= best)
            {
                if (score == best && score > 0)
                {
                    scores[candidate.PartySlot] = (byte)(score - 1);
                }

                continue;
            }

            best = score;
            winnerSlot = candidate.PartySlot;
        }

        if (winnerSlot < 0)
        {
            // 候选分全为 0 在当前公式下不可能（基础分 1）；保守退回拾取者并保留全 0 骰子字节。
            return new LootRoutingDecision(
                LootPickupOutcome.Granted, pickerCharacterId, scores, 0);
        }

        var winner = eligible.First(candidate => candidate.PartySlot == winnerSlot);
        return new LootRoutingDecision(LootPickupOutcome.Routed, winner.CharacterId, scores, 0);
    }
}
