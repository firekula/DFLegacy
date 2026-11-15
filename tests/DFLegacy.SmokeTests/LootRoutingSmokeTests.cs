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
        OfficialLootContractSmokeTests.Run(check);
        CheckRoutingGate(check);
        CheckEpicRoutingScript(check);
        CheckCorpseEligibility(check);
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
        CheckStaleLootPickupAttempt(check);
        CheckInventoryRejectionReplies(check);
        CheckDebugDropCommand(check);
        CheckDropDebugToggle(check);
    }

    /// <summary>
    /// "进不了背包"（planner 返回失败）时**必须有回包**——这是真机 run #49 的根因：
    /// 唯一一次 `Full` 拒绝之后，客户端**再也没发过拾取请求**（0 条 CMD 46），
    /// 而前面每次成功拾取都有完整的 `RX 46 → TX 39/13/86/46` 往返。
    ///
    /// 机制（官服 `PD-3005`）：`DisPatcher_GetItem::send` 是**三态**——结果 0 → `SendCmdOkPacket(46)`；
    /// 非 0 → `SendCmdErrorPacket(46,(u8)结果)`；**只有 `0x7FFFFFFF` 才是"什么都不发"**
    /// （那是 `CParty::get_item` 返回 false 的哨兵，即"这事没得做"，不是"装不下"）。
    /// 客户端接收面 `0x414090` 的错误分支（`0x15` 与任何非特殊值）会**重拉地面物列表**，
    /// 也就是把乐观拾取动画回滚掉；服务端沉默 ⇒ 客户端卡在"等应答"里，之后一个请求都不发。
    /// </summary>
    private static void CheckInventoryRejectionReplies(Action<bool, string> check)
    {
        foreach (var failure in new[]
                 {
                     InventoryPlacementFailure.Full,
                     InventoryPlacementFailure.Overweight,
                     InventoryPlacementFailure.InvalidItem,
                     InventoryPlacementFailure.UnsupportedCategory
                 })
        {
            var replies = EntranceService.CreateInventoryRejectionReplies(failure);
            check(replies.Count >= 1
                    && replies[0].Type == GameProtocolEngine.CommandPacketType
                    && replies[0].ProtocolId == GameProtocolEngine.GetItemCommand
                    && replies[0].Payload.SequenceEqual(
                        new byte[] { 0, GameProtocolEngine.GetItemSilentRejectionError }),
                $"a failed pickup placement ({failure}) is answered with the CMD 46 rollback that makes the client re-pull the ground list");
            check(replies.Skip(1).Any(packet =>
                    packet.Type == GameProtocolEngine.NotificationPacketType
                    && packet.ProtocolId == GameProtocolEngine.MessageNotification
                    && packet.Payload.Length > 0),
                $"a failed pickup placement ({failure}) also explains itself in the chat window");
        }

        check(EntranceService.CreateInventoryRejectionReplies(InventoryPlacementFailure.None).Count == 0,
            "no placement failure means no rejection reply at all");
    }

    /// <summary>
    /// 调试聊天命令 `掉落测试`（**真机测试专用**）：只认 <c>test</c> 账号、触发词精确匹配
    /// （账号大小写不敏感、正文首尾空白可容忍）。别的账号或别的文本一律不是命令。
    ///
    /// 触发词**不带斜杠**是硬约束（2026-10-03 真机实测）：客户端的聊天发送器把**任何**以 `/`
    /// 开头的输入当它自己的聊天命令吃掉（`/help` 打它自己的命令表，`/zzz` 与 `//掉落测试`
    /// 静默丢弃），一个包都不发出来 ⇒ 带斜杠的触发词永远到不了服务端。
    /// </summary>
    private static void CheckDebugDropCommand(Action<bool, string> check)
    {
        check(DebugChatCommands.IsDropEverythingCommand("掉落测试", "test")
                && DebugChatCommands.IsDropEverythingCommand("  掉落测试  ", "TEST"),
            "the drop-all chat command is accepted for the test account (trimmed, account case-insensitive)");
        check(!DebugChatCommands.IsDropEverythingCommand("掉落测试", "shizuku"),
            "the drop-all chat command is rejected for any other account");
        check(!DebugChatCommands.IsDropEverythingCommand("//掉落测试", "test"),
            "the slash form is rejected: the client swallows '/'-prefixed input and never sends it");
        check(!DebugChatCommands.IsDropEverythingCommand("掉落测试2", "test")
                && !DebugChatCommands.IsDropEverythingCommand(null, "test")
                && !DebugChatCommands.IsDropEverythingCommand("掉落测试", null),
            "anything but the exact trigger word (or a missing account) is not the command");
    }

    /// <summary>实例上的满掉落开关：同一把锁内翻转，且**只影响这一个实例**。</summary>
    private static void CheckDropDebugToggle(Action<bool, string> check)
    {
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        var other = new DungeonInstance(1, Guid.Empty, null!, null!);

        check(!instance.DropDebugAll, "the debug drop-all switch starts off");
        check(instance.ToggleDropDebugAll() && instance.DropDebugAll,
            "toggling turns the switch on and reports the new state");
        check(!instance.ToggleDropDebugAll() && !instance.DropDebugAll,
            "toggling again turns the switch back off");
        check(!other.DropDebugAll,
            "the switch is per instance and does not leak into another instance");
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

        check(LootRouting.IsRoutableForThisClient(rarity: 1)
                && LootRouting.IsRoutableForThisClient(rarity: 3)
                && LootRouting.IsRoutableForThisClient(rarity: 4),
            "ordinary nonwhite drops reach the dice path in production, including rarity 1");
        check(LootRouting.IsRoutableForThisClient(rarity: 0, isEpicRoutingItem: true),
            "the independent epic-routing flag also routes a white item");
    }

    private static void CheckCorpseEligibility(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        var instance = fixture.CreatePartyInstance(2600);
        var alive = Guid.NewGuid();
        var dead = Guid.NewGuid();
        instance.AddMember(fixture.Member(alive, 0, "活人"));
        instance.AddMember(fixture.Member(dead, 1, "尸体"));
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _), "the corpse routing fixture starts");
        var candidates = new[] { new LootRoutingCandidate(0, alive, 1), new LootRoutingCandidate(1, dead, 2) };
        instance.TrySetPartyMemberAlive(dead, false, DateTimeOffset.UtcNow);
        var sameRoom = instance.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero);
        check(sameRoom.Outcome == LootPickupOutcome.Routed && sameRoom.RoutingScores[1] == 0,
            "official ordinary item dice exclude even a current-room corpse");
        check(instance.TryMoveRoom(1, 0, out _, fixture.Random), "the party advances away from the corpse");
        var nextRoom = instance.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero);
        check(nextRoom.Outcome == LootPickupOutcome.Routed && nextRoom.RoutingScores[1] == 0,
            "a dead spectator whose corpse was left in the previous room cannot win new drops");
        instance.TrySetPartyMemberAlive(dead, false, DateTimeOffset.UtcNow);
        var duplicateDeath = instance.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero);
        check(duplicateDeath.RoutingScores[1] == 0,
            "a duplicate death report cannot create a corpse in the new room");
        check(instance.TryMoveRoom(0, 0, out _, fixture.Random), "the party revisits the death room");
        check(instance.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero).RoutingScores[1] == 0,
            "revisiting a map does not recreate a removed corpse's dice eligibility");
        instance.TrySetPartyMemberAlive(dead, true, DateTimeOffset.UtcNow);
        check(instance.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero).RoutingScores[1] != 0,
            "a revived member participates again");
        instance.RemoveMember(dead);
        check(instance.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero).Outcome == LootPickupOutcome.Granted,
            "a stale candidate snapshot cannot award an item to a member who has left");

        var reviving = fixture.CreatePartyInstance(2601);
        reviving.AddMember(fixture.Member(alive, 0, "活人"));
        reviving.AddMember(fixture.Member(dead, 1, "复活中"));
        reviving.TryStartRun(fixture.Layout, 0, 0, out _);
        reviving.TrySetPartyMemberAlive(dead, false, DateTimeOffset.UtcNow);
        check(reviving.TryBeginPartyRevival(dead, DateTimeOffset.UtcNow), "the member reserves a revival before inventory persistence");
        check(reviving.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero).RoutingScores[1] == 0,
            "a pending revival does not restore a dead member's dice eligibility before persistence");
        reviving.TryMoveRoom(1, 0, out _, fixture.Random);
        check(reviving.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero).RoutingScores[1] == 0,
            "an unfinished revival cannot make a previous-room corpse eligible in the next room");
        reviving.CancelPartyRevival(dead, DateTimeOffset.UtcNow);
        check(reviving.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero).RoutingScores[1] == 0,
            "a failed revival does not move the old corpse into the new room");
        reviving.TryBeginPartyRevival(dead, DateTimeOffset.UtcNow);
        reviving.TrySetPartyMemberAlive(dead, true, DateTimeOffset.UtcNow);
        check(reviving.DecidePickupRouting(alive, true, candidates, NextRandomAlwaysZero).RoutingScores[1] != 0,
            "successful revival persistence restores dice eligibility in the current room");
    }

    private static void CheckEpicRoutingScript(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dflegacy-routing-tags-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "equipment"));
        try
        {
            File.WriteAllText(Path.Combine(root, "equipment/equipment.lst"),
                "1000 `plain.equ`\n1001 `epic.equ`\n1002 `off.equ`\n");
            File.WriteAllText(Path.Combine(root, "equipment/plain.equ"), "[rarity] 0\n");
            File.WriteAllText(Path.Combine(root, "equipment/epic.equ"), "[rarity] 0\n[epic routing] 1\n[equipment type] `[weapon]`\n[usable job]\n`[priest]`\n[/usable job]\n");
            File.WriteAllText(Path.Combine(root, "equipment/off.equ"), "[rarity] 0\n[epic routing] 2\n[usable job]\n`[all]`\n[/usable job]\n");
            var scripts = new ScriptFileSystem(new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root },
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, Microsoft.Extensions.Logging.Abstractions.NullLogger<ItemCatalog>.Instance);
            items.Initialize();
            foreach (var (id, expected) in new[] { ((ushort)1000, false), ((ushort)1001, true), ((ushort)1002, false) })
            {
                check(items.TryGetDefinition(id, out var item)
                        && LootRouting.IsRoutableForThisClient(item.Rarity, item.IsEpicRoutingItem) == expected,
                    $"the production routing gate respects the independently parsed epic-routing tag for item {id}");
            }
            items.TryGetDefinition(1001, out var routableItem);
            items.TryGetDefinition(1000, out var unspecifiedJob);
            items.TryGetDefinition(1002, out var allJobs);
            check(LootRouting.HasJobBonus(routableItem, 4) && !LootRouting.HasJobBonus(routableItem, 0)
                    && unspecifiedJob.UsableJobMask == 0 && allJobs.UsableJobMask == 0x7FF,
                "PVF usable-job blocks preserve native specified, absent and all-job semantics");
            var ground = new DungeonGroundItemState();
            var generated = ground.Add(new DungeonGeneratedDrop(false, 1001, 1), 7, 10, 0, 0);
            var playerDropped = ground.AddPlayerDropped(new CharacterItemRecord(10, 1001, 1), 7, 0, 0);
            var passive = ground.AddPassive(new DungeonGeneratedDrop(false, 1001, 1), 7, 1, 0, 0, 0);
            check(LootRouting.IsRoutableGroundItem(generated, routableItem, true)
                    && LootRouting.IsRoutableGroundItem(passive, routableItem, true),
                "monster and passive drops retain their routing source flag");
            check(!LootRouting.IsRoutableGroundItem(playerDropped, routableItem),
                "player-discarded items cannot be rolled away to another member");
            var rewardOverflow = ground.Add(new DungeonGeneratedDrop(false, 1001, 1), 7, ushort.MaxValue, 0, 0, routingSource: 0);
            check(!LootRouting.IsRoutableGroundItem(rewardOverflow, routableItem),
                "an overflowed personal reward retains direct pickup instead of entering the dice");
            check(!LootRouting.IsRoutableGroundItem(generated, routableItem with { InventoryCategory = ItemInventoryCategory.Quest, IsEpicRoutingItem = false })
                    && !LootRouting.IsRoutableGroundItem(generated with { IsGold = true }, routableItem),
                "a white quest item without a routing flag and gold bypass ordinary item dice");
        }
        finally { Directory.Delete(root, recursive: true); }
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
        instance.AddMember(new DungeonInstanceMember(alpha, 1, 0, "A", _ => ValueTask.CompletedTask));
        instance.AddMember(new DungeonInstanceMember(bravo, 2, 1, "B", _ => ValueTask.CompletedTask));

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
            bravo, routable: true, [new LootRoutingCandidate(1, bravo, 2)], NextRandomAlwaysZero);
        check(solo.Outcome == LootPickupOutcome.Routed && solo.WinnerCharacterId == bravo,
            "a single eligible member still rolls when another party member is present");
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

    private static void CheckStaleLootPickupAttempt(Action<bool, string> check)
    {
        var picker = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        instance.AddMember(new DungeonInstanceMember(picker, 7, 0, "picker", _ => ValueTask.CompletedTask));
        instance.AddMember(new DungeonInstanceMember(winner, 9, 1, "winner", _ => ValueTask.CompletedTask));
        var original = instance.AddGroundItem(new DungeonGeneratedDrop(false, 3176, 1), 7, 2, 0, 0, 0, 0);
        instance.TryBeginLootPickup(picker, original, true,
            [new LootRoutingCandidate(0, picker, 7), new LootRoutingCandidate(1, winner, 9)], _ => 0, out var reservation);
        var attempt = new DungeonLootPickupAttempt(winner, original.GroundId, picker, 7, 0,
            [13, 81, 0, 0], Instance: instance, GroundItem: original, PickupId: reservation.PickupId);
        check(instance.TryGetGroundItemForLootAttempt(attempt, 0, 0, out var item) && ReferenceEquals(item, original),
            "a routed pickup mailbox entry can only read its original ground object in its original instance");
        check(!instance.TryGetGroundItemForLootAttempt(attempt, 1, 0, out _),
            "a routed pickup cannot follow its winner into another room");
        instance.ResetGroundItems();
        var replacement = instance.AddGroundItem(new DungeonGeneratedDrop(false, 3171, 1), 7, 3, 0, 0, 0, 0);
        check(replacement.GroundId == original.GroundId
                && !instance.TryGetGroundItemForLootAttempt(attempt, 0, 0, out _)
                && !instance.TryClaimGroundItem(replacement.GroundId, 0, 0, out _, expectedItem: original)
                && instance.TryGetGroundItem(replacement.GroundId, 0, 0, out _),
            "a delayed old-run award cannot claim a new drop that reuses the same ground id");
        var other = new DungeonInstance(1, Guid.Empty, null!, null!);
        other.AddMember(new DungeonInstanceMember(winner, 9, 1, "winner", _ => ValueTask.CompletedTask));
        other.AddGroundItem(new DungeonGeneratedDrop(false, 3176, 1), 7, 2, 0, 0, 0, 0);
        check(!other.TryGetGroundItemForLootAttempt(attempt, 0, 0, out _),
            "leaving and joining another instance cannot redirect a queued award");
    }
}
