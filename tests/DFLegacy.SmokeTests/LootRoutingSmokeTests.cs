using DFLegacy.Protocol;
using DFLegacy.Server;

/// <summary>
/// S5 掉落与拾取的**归属规则**（官服 `CParty::get_item` 的 routing 分支）。
///
/// 官服证据（Hex-Rays 单函数反编译，产物 `work/out/ida_s5_loot.txt` + `ida_s5_routing.txt`）：
/// * 门控：`CItem::check_item_routing(item)` = `isEpicRoutingItem(item) || get_rarity(item) > 0`
///   （`CItem::get_rarity` = `*(int*)(this+56)`；`isEpicRoutingItem` = `*(u8*)(this+121)`）。
/// * 加权分（每个**合法候选槽位**）：`++v136[k]`（基础 1）；
///   轮到轮转者 `+= 40`（轮转下标在 `party+324`）；职业匹配 `+= 30`；
///   最后 `v136[k] += rand(100 - v136[k])`。
/// * 取最大分者（`v150`）；并列时后来者自减 1（先到者保住最大分）。
/// * `CParty::IsRoutingState()` 为真 ⇒ `process` 直接置结果 **216(0xD8)** 返回，不做普通拾取。
/// * `CParty::get_item` 返回 false ⇒ `process` 置 **0x7FFFFFFF**（"不回包"哨兵）。
///
/// 本文件只测**纯规则**（模型层、可注入随机源）；会话接线与跨会话投递由其它用例覆盖。
/// </summary>
internal static class LootRoutingSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        CheckRoutingGate(check);
        CheckSingleMemberAndNonRoutableStayWithThePicker(check);
        CheckRoundRobinBonusDecidesTheWinner(check);
        CheckRoundRobinIndexAdvances(check);
        CheckTieKeepsTheEarlierCandidate(check);
        CheckJobMatchBonus(check);
        CheckRoutingStateRejectsDirectPickup(check);
        CheckUnavailableItemIsSilent(check);
        CheckOneAwardPerGroundItem(check);
        CheckInstanceRoutingDecisionKeepsRotation(check);
        CheckLootPickupMailbox(check);
    }

    /// <summary>`check_item_routing = isEpicRoutingItem || rarity > 0`（官服 @0x0850d756）。</summary>
    private static void CheckRoutingGate(Action<bool, string> check)
    {
        check(!LootRouting.IsRoutable(rarity: 0) && !LootRouting.IsRoutable(rarity: null),
            "a rarity-0 (or unknown-rarity) item is not routable");
        check(LootRouting.IsRoutable(rarity: 1) && LootRouting.IsRoutable(rarity: 4),
            "any item with rarity above zero is routable (official check_item_routing)");
        check(LootRouting.IsRoutable(rarity: 0, isEpicRoutingItem: true),
            "the epic-routing flag routes an item even at rarity zero");

        // 生效门控（2026-10-03 用户裁决）：官服语义是 `rarity > 0`，但本客户端**没有骰子 UI**，
        // "判给别人"在玩家侧等于"点了没反应、东西还在地上"（真机 run #46 观测）⇒ 先收窄到
        // `rarity > 3`。放开时把 `RoutableRarityFloor` 改回 0 即可，本组断言要跟着改。
        check(LootRouting.RoutableRarityFloor == 3,
            "the effective routing floor is narrowed to rarity > 3 for this client");
        check(!LootRouting.IsRoutableForThisClient(rarity: 1)
                && !LootRouting.IsRoutableForThisClient(rarity: 3)
                && LootRouting.IsRoutableForThisClient(rarity: 4),
            "only items above the narrowed floor are routed in production");
        check(LootRouting.IsRoutableForThisClient(rarity: 0, isEpicRoutingItem: true),
            "the epic-routing flag still routes an item regardless of the narrowed floor");
    }

    /// <summary>官服 `get_member_count &lt;= 1 → v150 = a2`：单人/不可路由 = 拾取者本人取得，骰子字节全 0。</summary>
    private static void CheckSingleMemberAndNonRoutableStayWithThePicker(
        Action<bool, string> check)
    {
        var picker = Guid.NewGuid();
        var solo = LootRouting.Decide(
            itemAvailable: true,
            routingActive: false,
            routable: true,
            pickerCharacterId: picker,
            candidates: [new LootRoutingCandidate(0, picker, 1)],
            roundRobinIndex: 0,
            nextRandom: NextRandomAlwaysZero,
            out var soloNext);
        check(solo.Outcome == LootPickupOutcome.Granted
                && solo.WinnerCharacterId == picker
                && solo.RoutingScores.All(score => score == 0)
                && soloNext == 0,
            "a solo pickup is granted to the picker with zero routing scores");

        var peer = Guid.NewGuid();
        var notRoutable = LootRouting.Decide(
            itemAvailable: true,
            routingActive: false,
            routable: false,
            pickerCharacterId: picker,
            candidates: [new LootRoutingCandidate(0, picker, 1), new LootRoutingCandidate(1, peer, 2)],
            roundRobinIndex: 0,
            nextRandom: NextRandomAlwaysZero,
            out _);
        check(notRoutable.Outcome == LootPickupOutcome.Granted
                && notRoutable.WinnerCharacterId == picker
                && notRoutable.RoutingScores.All(score => score == 0),
            "a non-routable item in a party still goes to the picker (official v149 == false branch)");
    }

    /// <summary>
    /// 轮转者 +40（官服 `if (v194 == v193) v136[k] += 40`）。候选按**槽位**落到 4 字节里，
    /// 客户端就是这么按槽位打印的（PD-3004 的 `" %s[%d]"` 循环）。
    /// </summary>
    private static void CheckRoundRobinBonusDecidesTheWinner(Action<bool, string> check)
    {
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        // 轮转下标 0 → 下一次轮到候选序 1（槽位 3）：它拿 +40，另一个只有基础 1。
        var decision = LootRouting.Decide(
            itemAvailable: true,
            routingActive: false,
            routable: true,
            pickerCharacterId: alpha,
            candidates:
            [
                new LootRoutingCandidate(1, alpha, 10),
                new LootRoutingCandidate(3, bravo, 20),
            ],
            roundRobinIndex: 0,
            nextRandom: NextRandomAlwaysZero,
            out var nextRoundRobin);

        check(decision.Outcome == LootPickupOutcome.Routed
                && decision.WinnerCharacterId == bravo,
            "the round-robin member wins when it carries the +40 bonus");
        check(decision.RoutingScores.Length == LootRouting.RoutingScoreSlotCount
                && decision.RoutingScores[1] == LootRouting.BaseScore
                && decision.RoutingScores[3] == LootRouting.BaseScore + LootRouting.RoundRobinBonus
                && decision.RoutingScores[0] == 0
                && decision.RoutingScores[2] == 0,
            "routing scores are slot-indexed and only candidates receive a score");
        check(nextRoundRobin == 1, "the round-robin pointer advances to the bonused candidate");
    }

    /// <summary>轮转确实在转：存 1 → 下一次给候选序 0（槽位 1）。</summary>
    private static void CheckRoundRobinIndexAdvances(Action<bool, string> check)
    {
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var decision = LootRouting.Decide(
            itemAvailable: true,
            routingActive: false,
            routable: true,
            pickerCharacterId: alpha,
            candidates:
            [
                new LootRoutingCandidate(1, alpha, 10),
                new LootRoutingCandidate(3, bravo, 20),
            ],
            roundRobinIndex: 1,
            nextRandom: NextRandomAlwaysZero,
            out var nextRoundRobin);

        check(decision.WinnerCharacterId == alpha
                && decision.RoutingScores[1] == LootRouting.BaseScore + LootRouting.RoundRobinBonus
                && nextRoundRobin == 0,
            "the +40 rotates to the next candidate slot on every roll");
    }

    /// <summary>
    /// 并列处理（官服 `if (v136[m] == v196 &amp;&amp; v136[m] &gt; 0) --v136[m];`）：
    /// **先遍历到的**候选保住最大分，后来者自减 1。
    /// </summary>
    private static void CheckTieKeepsTheEarlierCandidate(Action<bool, string> check)
    {
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        // 让 bravo（候选序 1，无轮转加成）也拿到 41：基础 1 + rand(99) = 41 ⇒ rand 返回 40。
        var rolls = new Queue<int>([40, 0]);
        var decision = LootRouting.Decide(
            itemAvailable: true,
            routingActive: false,
            routable: true,
            pickerCharacterId: alpha,
            candidates:
            [
                new LootRoutingCandidate(1, alpha, 10),
                new LootRoutingCandidate(3, bravo, 20),
            ],
            roundRobinIndex: 0,
            nextRandom: bound => rolls.Count > 0 ? rolls.Dequeue() : 0,
            out _);

        check(decision.WinnerCharacterId == alpha,
            "an exact tie keeps the first candidate in slot order as the winner");
        check(decision.RoutingScores[1] == 41 && decision.RoutingScores[3] == 40,
            "the later tied candidate is decremented so the maximum stays unique");
    }

    /// <summary>职业匹配 +30（官服 `if (!is_stackable &amp;&amp; kind == 10 &amp;&amp; check_job_type(...)) v136[k] += 30;`）。</summary>
    private static void CheckJobMatchBonus(Action<bool, string> check)
    {
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var decision = LootRouting.Decide(
            itemAvailable: true,
            routingActive: false,
            routable: true,
            pickerCharacterId: alpha,
            candidates:
            [
                new LootRoutingCandidate(0, alpha, 10),
                new LootRoutingCandidate(2, bravo, 20, JobMatchesItem: true),
            ],
            roundRobinIndex: 0,
            nextRandom: NextRandomAlwaysZero,
            out _);

        // 轮转加成给候选序 1（=bravo，槽位 2）⇒ 它同时拿 +40 与 +30。
        check(decision.RoutingScores[2]
                == LootRouting.BaseScore + LootRouting.RoundRobinBonus + LootRouting.JobMatchBonus
                && decision.WinnerCharacterId == bravo,
            "a job-matching member carries the extra +30 weight");
    }

    /// <summary>官服 `CParty::IsRoutingState()` 为真 ⇒ 结果 216(0xD8)，且**不产生任何归属**。</summary>
    private static void CheckRoutingStateRejectsDirectPickup(Action<bool, string> check)
    {
        var picker = Guid.NewGuid();
        var decision = LootRouting.Decide(
            itemAvailable: true,
            routingActive: true,
            routable: true,
            pickerCharacterId: picker,
            candidates: [new LootRoutingCandidate(0, picker, 1)],
            roundRobinIndex: 0,
            nextRandom: NextRandomAlwaysZero,
            out _);

        check(decision.Outcome == LootPickupOutcome.RoutingActive
                && decision.ErrorCode == LootRouting.RoutingActiveErrorCode
                && LootRouting.RoutingActiveErrorCode == 0xD8
                && decision.WinnerCharacterId == Guid.Empty
                && decision.RoutingScores.All(score => score == 0),
            "a live routing lifecycle rejects the direct pickup with error 0xD8 and awards nothing");
    }

    /// <summary>官服 `get_item == false → 0x7FFFFFFF`（不回包）：拾取失败必须**静默**，且不产生归属。</summary>
    private static void CheckUnavailableItemIsSilent(Action<bool, string> check)
    {
        var picker = Guid.NewGuid();
        var decision = LootRouting.Decide(
            itemAvailable: false,
            routingActive: false,
            routable: true,
            pickerCharacterId: picker,
            candidates: [new LootRoutingCandidate(0, picker, 1)],
            roundRobinIndex: 0,
            nextRandom: NextRandomAlwaysZero,
            out _);

        check(decision.Outcome == LootPickupOutcome.NotAvailable
                && decision.WinnerCharacterId == Guid.Empty
                && decision.ErrorCode == 0,
            "an unavailable ground item yields no award and no reply (official 0x7FFFFFFF sentinel)");
    }

    /// <summary>
    /// **每件地面物只产生一次归属**：两个成员同时抢同一件 → 只有一个能赢下实体，
    /// 另一个得到 `NotAvailable`（不得产生第二份归属/第二次广播）。
    /// </summary>
    private static void CheckOneAwardPerGroundItem(Action<bool, string> check)
    {
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var instance = new DungeonInstance(0, alpha, null!, null!);
        var groundItem = instance.AddGroundItem(
            new DungeonGeneratedDrop(false, 3176, 1),
            ownerUserId: 1,
            sourceMonsterUniqueId: 2,
            roomX: 0,
            roomY: 0,
            x: 0,
            y: 0);
        var candidates = new[]
        {
            new LootRoutingCandidate(1, alpha, 10),
            new LootRoutingCandidate(3, bravo, 20),
        };

        var awards = 0;
        Guid? winner = null;
        foreach (var picker in new[] { alpha, bravo })
        {
            var claimed = instance.TryClaimGroundItem(groundItem.GroundId, 0, 0, out _);
            var decision = LootRouting.Decide(
                itemAvailable: claimed,
                routingActive: false,
                routable: true,
                pickerCharacterId: picker,
                candidates: candidates,
                roundRobinIndex: 0,
                nextRandom: NextRandomAlwaysZero,
                out _);
            if (decision.Outcome is LootPickupOutcome.Granted or LootPickupOutcome.Routed)
            {
                awards++;
                winner = decision.WinnerCharacterId;
            }
        }

        check(awards == 1 && winner == bravo,
            "only one of two concurrent pickers produces an award, and it follows the routing rule");
    }

    /// <summary>官服 `get_rand_int(bound)` 的确定性替身：永远返回 0（把随机项消掉）。</summary>
    private static int NextRandomAlwaysZero(int bound) => 0;

    /// <summary>
    /// 实例侧入口：纯规则之外还要**在锁内推进轮转下标**（官服 `party+324`），
    /// 否则两个成员同时点拾取会拿到同一个 +40 顺位。
    /// </summary>
    private static void CheckInstanceRoutingDecisionKeepsRotation(Action<bool, string> check)
    {
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        var candidates = new[]
        {
            new LootRoutingCandidate(0, alpha, 1),
            new LootRoutingCandidate(1, bravo, 2),
        };

        var first = instance.DecidePickupRouting(
            alpha, routable: true, candidates, NextRandomAlwaysZero);
        check(first.Outcome == LootPickupOutcome.Routed
                && first.WinnerCharacterId == bravo
                && first.RoutingScores[1] == LootRouting.BaseScore + LootRouting.RoundRobinBonus,
            "the first routed pickup hands the +40 to the next slot in the rotation");
        check(instance.RoutingRoundRobinIndex == 1,
            "the rotation index (official party+324) is stored on the instance");

        var second = instance.DecidePickupRouting(
            alpha, routable: true, candidates, NextRandomAlwaysZero);
        check(second.WinnerCharacterId == alpha && instance.RoutingRoundRobinIndex == 0,
            "the next pickup rotates the +40 back to the first candidate");

        var notRoutable = instance.DecidePickupRouting(
            alpha, routable: false, candidates, NextRandomAlwaysZero);
        check(notRoutable.Outcome == LootPickupOutcome.Granted
                && notRoutable.WinnerCharacterId == alpha
                && notRoutable.RoutingScores.All(score => score == 0),
            "a non-routable item skips the dice and stays with the picker");

        var solo = instance.DecidePickupRouting(
            bravo, routable: true, [new LootRoutingCandidate(3, bravo, 2)], NextRandomAlwaysZero);
        check(solo.Outcome == LootPickupOutcome.Granted && solo.WinnerCharacterId == bravo,
            "a single eligible member skips the dice");
    }

    /// <summary>
    /// 跨会话拾取信箱（拾取者投、**得主本人**取）。队列语义与"有待处理邮件"的判定都要钉住：
    /// 后者决定得主会话的读等待会不会被唤醒（客户端静止不发包时请求会一直躺着）。
    /// </summary>
    private static void CheckLootPickupMailbox(Action<bool, string> check)
    {
        var registry = new DungeonInstanceRegistry();
        var winner = Guid.NewGuid();
        var first = new DungeonLootPickupAttempt(
            winner, GroundId: 1, Guid.NewGuid(), PickerUserId: 1, PickerPartySlot: 0, [0, 0, 3, 41]);
        var second = new DungeonLootPickupAttempt(
            winner, GroundId: 2, Guid.NewGuid(), PickerUserId: 2, PickerPartySlot: 1, [0, 0, 0, 42]);

        check(!registry.HasPendingLootPickup(winner) && !registry.HasPendingMail(winner),
            "an empty pickup mailbox reports nothing pending");
        check(!registry.TryTakeLootPickupAttempt(winner, out _),
            "taking from an empty pickup mailbox fails");

        registry.PostLootPickupAttempt(first);
        registry.PostLootPickupAttempt(second);
        check(registry.HasPendingLootPickup(winner) && registry.HasPendingMail(winner),
            "a posted attempt shows up both in the drain tick and in HasPendingMail");

        check(registry.TryTakeLootPickupAttempt(winner, out var takenFirst)
                && takenFirst.GroundId == 1
                && takenFirst.RoutingScores.SequenceEqual(new byte[] { 0, 0, 3, 41 }),
            "pickup attempts are delivered in FIFO order with their dice scores");
        check(registry.TryTakeLootPickupAttempt(winner, out var takenSecond)
                && takenSecond.GroundId == 2,
            "two attempts for the same winner both queue instead of overwriting each other");
        check(!registry.HasPendingLootPickup(winner)
                && !registry.HasPendingMail(winner)
                && !registry.TryTakeLootPickupAttempt(winner, out _),
            "the mailbox is empty again once drained");
    }
}
