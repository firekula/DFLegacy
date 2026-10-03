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
}

/// <summary>
/// routing 掷骰的候选成员（官服按**队伍槽位** 0..3 遍历 `_checkValidUser(k) &amp;&amp; *(party+k+896) == 1`）。
/// </summary>
public readonly record struct LootRoutingCandidate(
    byte PartySlot,
    Guid CharacterId,
    ushort UserId,
    bool JobMatchesItem = false);

/// <summary>
/// 拾取裁决：归属（<see cref="WinnerCharacterId"/>）+ `NOTI 39` 要写的 4 个槽位字节
/// （<see cref="RoutingScores"/>）+ 失败时的错误码（<see cref="ErrorCode"/>）。
/// </summary>
public sealed record LootRoutingDecision(
    LootPickupOutcome Outcome,
    Guid WinnerCharacterId,
    byte[] RoutingScores,
    byte ErrorCode);

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

    /// <summary>落库失败：已把实体按原 GroundId 放回原地，**什么都不发**。</summary>
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
/// 已知缺口（`[未验证]`，见 S5-brief §1.7）：官服的第 3 项加成条件还带
/// `is_stackable() != 1 &amp;&amp; item_kind == 10 &amp;&amp; check_job_type(item, job)`，
/// `check_job_type` 与 "item kind == 10" 的语义尚未取证 ⇒ 这里由调用方经
/// <see cref="LootRoutingCandidate.JobMatchesItem"/> 传入，当前生产接线传 false。
/// 同样地 `isEpicRoutingItem`（`CItem+121`）在我们的物品表里**没有对应字段**，
/// 只能由调用方给（当前恒 false）⇒ 我们的可路由判据实际退化为 `rarity > 0`。
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
    /// **这是语义层证据，不要直接拿它当生效门控**——见 <see cref="RoutableRarityFloor"/>。
    /// </summary>
    public static bool IsRoutable(int? rarity, bool isEpicRoutingItem = false) =>
        isEpicRoutingItem || (rarity ?? 0) > 0;

    /// <summary>
    /// **本客户端生效的**可路由品级下限（`rarity > 3` 才算可路由）。
    ///
    /// 官服的 `check_item_routing` 是 `rarity > 0`（≈任何非最低品级），但本客户端**没有骰子 UI**：
    /// "掷骰判给别人"在玩家侧等于**点了没反应、东西还留在地上**——真机 run #46 里神枪手
    /// 唯一一次有效拾取就这么被判给了队友，用户随即报"捡不起东西"。
    /// 2026-10-03 用户裁决：**先收窄到 `rarity > 3`**（回到 2026-10-01 的"蓝装以上"口径），
    /// 把普通掉落的体感恢复正常；等有客户端反馈手段（或确认玩家能接受静默转移）再放开。
    /// ⇒ 这是一处**有意偏离**：规则、单测、跨会话交付都保留，只改这一条门控；
    /// 台账与 `STATUS.md` 已按"部分对齐"记录，放开时把 3 改成 0 即可。
    /// </summary>
    public const int RoutableRarityFloor = 3;

    /// <summary>
    /// 生产接线用的门控：`isEpicRoutingItem || rarity > `<see cref="RoutableRarityFloor"/>。
    /// </summary>
    public static bool IsRoutableForThisClient(int? rarity, bool isEpicRoutingItem = false) =>
        isEpicRoutingItem || (rarity ?? 0) > RoutableRarityFloor;

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
        out int nextRoundRobinIndex)
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

        // 官服 get_member_count <= 1 → v150 = a2；v149 为假（不可路由）也走直取。
        if (!routable || candidates.Count <= 1)
        {
            nextRoundRobinIndex = roundRobinIndex;
            return new LootRoutingDecision(
                LootPickupOutcome.Granted, pickerCharacterId, scores, 0);
        }

        var bonusOrdinal = (roundRobinIndex + 1) % candidates.Count;
        nextRoundRobinIndex = bonusOrdinal;

        for (var ordinal = 0; ordinal < candidates.Count; ordinal++)
        {
            var candidate = candidates[ordinal];
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

            scores[candidate.PartySlot] = (byte)Math.Min(score, byte.MaxValue);
        }

        // 官服按槽位 0..3 遍历取最大分；并列时后来者自减 1（先到者保住最大分）。
        var best = 0;
        var winnerSlot = -1;
        foreach (var candidate in candidates.OrderBy(candidate => candidate.PartySlot))
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

        var winner = candidates.First(candidate => candidate.PartySlot == winnerSlot);
        return new LootRoutingDecision(LootPickupOutcome.Routed, winner.CharacterId, scores, 0);
    }
}
