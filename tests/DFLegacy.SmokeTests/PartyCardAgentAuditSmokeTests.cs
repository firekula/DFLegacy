using System.Reflection;
using DFLegacy.Server;

/// <summary>
/// R1′ 的强制证据 + 代理失败路径的审计：归属列的内容只能由**该列归属成员自己的代理**上报，
/// 代理缺失 / 抛异常 / 取消 / 超时 / 提交非 Committed 一律**不重试、不发布、留审计**（R4/R5）。
/// 覆盖范围：DungeonInstance 的归属-内容-发布模型；会话侧那几行（把代理注册进去、读审计写日志）
/// 不在本文件的自动化范围内（见 task-2-report.md 的"未覆盖范围"）。
/// </summary>
internal static class PartyCardAgentAuditSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await CheckMissingAgentAuditedAsync(check);
        await CheckAgentExceptionAuditedAsync(check);
        await CheckAgentCancellationAuditedAsync(check);
        await CheckAgentTimeoutAuditedAsync(check);
        await CheckAgentTimeoutLateResultIsReportedAsync(check);
        await CheckContentMismatchAuditedAsync(check);
        await CheckCommitOutcomeAuditedAsync(check);
        await CheckAgentReceivesSettlementIdAsync(check);
        await CheckForgedReportRejectedAsync(check);
    }

    /// <summary>
    /// 代理拿到的结算标识必须是实例在当前这把锁里认下的那一份：会话用它算幂等键，
    /// 拿错了（例如"局 A 的代次 + 局 B 的标识"）会让本局奖励撞上别的键而永久发不出去。
    /// </summary>
    private static async Task CheckAgentReceivesSettlementIdAsync(Action<bool, string> check)
    {
        var (instance, a, _, generation) = CreateSettlement();
        var agent = new Agent(0, DungeonRewardLedgerStatus.Committed);
        instance.RegisterPartyCardRewardAgent(a, agent);
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        check(agent.LastSettlementId == instance.PartyCardSettlementId,
            "the agent is handed the settlement id the instance validated the owner against");
        check(agent.LastSettlementId != Guid.Empty, "the settlement id is never empty");
    }

    private static (DungeonInstance Instance, Guid A, Guid B, long Generation) CreateSettlement(
        TimeSpan? agentTimeout = null)
    {
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!, agentTimeout);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        instance.AddMember(new(a, 1, 0, "A", _ => ValueTask.CompletedTask));
        instance.AddMember(new(b, 2, 1, "B", _ => ValueTask.CompletedTask));
        var generation = instance.BeginPartyCardSettlement();
        instance.CompletePartyCardSelection(generation);
        return (instance, a, b, generation);
    }

    /// <summary>缺代理：A 有代理、B 没有。B 那一列必须保持未归属、没有内容、有审计。</summary>
    private static async Task CheckMissingAgentAuditedAsync(Action<bool, string> check)
    {
        var (instance, a, b, generation) = CreateSettlement();
        var agentA = new Agent(0, DungeonRewardLedgerStatus.Committed);
        instance.RegisterPartyCardRewardAgent(a, agentA);
        await instance.SettleOwnPartyCardAsync(b, PartyCardType.Free, 1, generation, CancellationToken.None);
        var audit = instance.DrainPartyCardAudit();
        check(audit.Any(entry => entry.Reason == "agent-missing"
                && entry.CharacterId == b
                && entry.Column == 1
                && entry.Generation == generation),
            "missing member agent is audited for that column");
        check(agentA.Commits == 0, "missing agent never triggers another member's agent");
        // B 那一列永远没有内容 → 不可发布；等 A（有代理）落地后，发布表里 B 的列仍是 0xFF。
        check(!instance.TryTakePartyCardPublication(generation, out _),
            "a column without an agent never becomes publishable on its own");
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        check(instance.TryTakePartyCardPublication(generation, out var snapshot)
            && snapshot!.Selection.FreeOwners[0] == 0
            && snapshot.Selection.FreeOwners[1] == PartyCardSelectionState.Unassigned,
            "missing-agent column stays 0xFF while the known column publishes");
        check(instance.DrainPartyCardAudit().Count == 0, "audit drain empties the buffer");
        // 评审 F6：条目自带产生它那一刻的结算标识（会话写日志时不能拿"当前"值）。
        var (second, c, _, secondGeneration) = CreateSettlement();
        second.RegisterPartyCardRewardAgent(c, new Agent(0, DungeonRewardLedgerStatus.Committed));
        await second.SettleOwnPartyCardAsync(Guid.NewGuid(), PartyCardType.Free, 0, secondGeneration, CancellationToken.None);
        check(second.DrainPartyCardAudit().Count == 0, "a foreign character never produces an audit entry");
        var (third, d, _, thirdGeneration) = CreateSettlement();
        await third.SettleOwnPartyCardAsync(d, PartyCardType.Free, 0, thirdGeneration, CancellationToken.None);
        var entries = third.DrainPartyCardAudit();
        check(entries.Count > 0 && entries.All(entry => entry.SettlementId == third.PartyCardSettlementId),
            "each audit entry carries the settlement id of the settlement it was produced in");
    }

    /// <summary>代理抛异常：该列屏蔽、不重试（第二次 settle 不再调代理）、审计一次。</summary>
    private static async Task CheckAgentExceptionAuditedAsync(Action<bool, string> check)
    {
        var (instance, a, _, generation) = CreateSettlement();
        var agent = new Agent(0, DungeonRewardLedgerStatus.Committed) { ThrowOnCommit = true };
        instance.RegisterPartyCardRewardAgent(a, agent);
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        var audit = instance.DrainPartyCardAudit();
        check(audit.Count(entry => entry.Reason == "agent-exception") == 1,
            "agent exception is audited exactly once for the reservation");
        check(agent.Commits == 1, "failed agent attempt is never retried for the same reservation");
        check(instance.TryTakePartyCardPublication(generation, out _) == false,
            "failed agent content never reaches the shared table");
    }

    /// <summary>代理响应取消：审计 "agent-cancelled"，同样不重试。</summary>
    private static async Task CheckAgentCancellationAuditedAsync(Action<bool, string> check)
    {
        var (instance, a, _, generation) = CreateSettlement();
        var agent = new Agent(0, DungeonRewardLedgerStatus.Committed) { ThrowCancellation = true };
        instance.RegisterPartyCardRewardAgent(a, agent);
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        check(instance.DrainPartyCardAudit().Any(entry => entry.Reason == "agent-cancelled"),
            "cancelled agent commit is audited distinctly");
    }

    /// <summary>代理一直不返回：有界等待后放弃该列，会话不被钉死，审计 "agent-timeout"。</summary>
    private static async Task CheckAgentTimeoutAuditedAsync(Action<bool, string> check)
    {
        var (instance, a, _, generation) = CreateSettlement(TimeSpan.FromMilliseconds(40));
        var agent = new Agent(0, DungeonRewardLedgerStatus.Committed) { HangOnCommit = true };
        instance.RegisterPartyCardRewardAgent(a, agent);
        var settle = instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        var finished = await Task.WhenAny(settle, Task.Delay(TimeSpan.FromSeconds(5)));
        check(finished == settle, "hung agent cannot pin the settling session forever");
        var audit = instance.DrainPartyCardAudit();
        check(audit.Any(entry => entry.Reason == "agent-timeout"), "hung agent is audited as a timeout");
        check(agent.Commits == 1, "timed-out agent attempt is not restarted");
        check(!instance.TryTakePartyCardPublication(generation, out _),
            "timed-out column stays masked in the shared table");
    }

    /// <summary>
    /// 超时**不等于没落库**：实例有界等待后放弃，但代理那次提交仍会在会话侧跑完并把奖励写进存档。
    /// 因此迟到完成必须**如实上报**（内容按真实结果进表 → 该列重新可发布）并留一条"迟到结果"审计；
    /// 否则审计写着"没提交"、存档里却已经发了奖励，运维照审计补发就会发出第二份（评审 F1）。
    /// </summary>
    private static async Task CheckAgentTimeoutLateResultIsReportedAsync(Action<bool, string> check)
    {
        // 迟到且结算在等待期间已经重开：审计与上报必须仍然属于**旧那一局**（评审 N2）。
        var (stale, staleMember, _, staleGeneration) = CreateSettlement(TimeSpan.FromMilliseconds(40));
        var staleSettlementId = stale.PartyCardSettlementId;
        var staleCommit = new Agent(0, DungeonRewardLedgerStatus.Committed) { HangOnCommit = true };
        stale.RegisterPartyCardRewardAgent(staleMember, staleCommit);
        var staleSettle = stale.SettleOwnPartyCardAsync(staleMember, PartyCardType.Free, 0, staleGeneration, CancellationToken.None);
        check(await Task.WhenAny(staleSettle, Task.Delay(TimeSpan.FromSeconds(5))) == staleSettle,
            "the reopened-settlement case releases the settling session at the timeout");
        var reopenedGeneration = stale.BeginPartyCardSettlement();
        check(stale.PartyCardSettlementId != staleSettlementId,
            "reopening the settlement issues a new settlement id");
        check(reopenedGeneration != staleGeneration, "reopening the settlement issues a new generation");
        staleCommit.CompleteHanging(DungeonRewardLedgerStatus.Committed);
        var staleAudit = await WaitForAuditAsync(stale, "agent-timeout-late-committed");
        check(staleAudit.Any(entry => entry.Reason == "agent-timeout-late-committed"),
            "a late commit landing after a settlement reopen is still audited");
        check(staleAudit.Where(entry => entry.Reason.StartsWith("agent-timeout", StringComparison.Ordinal))
                .All(entry => entry.SettlementId == staleSettlementId),
            "late audit entries keep the settlement id of the settlement they belong to");
        check(!stale.TryTakePartyCardPublication(staleGeneration, out _),
            "a stale late report never publishes into the reopened settlement");

        // 迟到且成功：内容进表，该列重新可发布。
        var (instance, a, _, generation) = CreateSettlement(TimeSpan.FromMilliseconds(40));
        var committed = new Agent(0, DungeonRewardLedgerStatus.Committed) { HangOnCommit = true };
        instance.RegisterPartyCardRewardAgent(a, committed);
        var settle = instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        check(await Task.WhenAny(settle, Task.Delay(TimeSpan.FromSeconds(5))) == settle,
            "late-result case still releases the settling session at the timeout");
        committed.CompleteHanging(DungeonRewardLedgerStatus.Committed);
        var audit = await WaitForAuditAsync(instance, "agent-timeout-late-committed");
        check(audit.Any(entry => entry.Reason == "agent-timeout"),
            "the timeout itself is still audited");
        check(audit.Any(entry => entry.Reason == "agent-timeout-late-committed"),
            "a late successful commit is audited as a late result instead of a bare timeout");
        check(instance.TryTakePartyCardPublication(generation, out var snapshot)
            && snapshot!.Selection.FreeOwners[0] == 0,
            "a late committed column becomes publishable for the next publication");

        // 迟到且失败：同样如实上报，该列保持屏蔽。
        var (second, _, b, secondGeneration) = CreateSettlement(TimeSpan.FromMilliseconds(40));
        var unknown = new Agent(1, DungeonRewardLedgerStatus.Committed) { HangOnCommit = true };
        second.RegisterPartyCardRewardAgent(b, unknown);
        var secondSettle = second.SettleOwnPartyCardAsync(b, PartyCardType.Free, 1, secondGeneration, CancellationToken.None);
        check(await Task.WhenAny(secondSettle, Task.Delay(TimeSpan.FromSeconds(5))) == secondSettle,
            "late-failure case releases the settling session at the timeout");
        unknown.CompleteHanging(DungeonRewardLedgerStatus.Unknown);
        var secondAudit = await WaitForAuditAsync(second, "agent-timeout-late-unknown");
        check(secondAudit.Any(entry => entry.Reason == "agent-timeout-late-unknown"),
            "a late failed commit is audited with its real outcome");
        check(!second.TryTakePartyCardPublication(secondGeneration, out _),
            "a late Unknown column stays masked");
    }

    /// <summary>轮询审计（每次排空并累积），等到出现目标原因或超时。</summary>
    private static async Task<List<PartyCardAuditEntry>> WaitForAuditAsync(DungeonInstance instance, string reason)
    {
        var drained = new List<PartyCardAuditEntry>();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            drained.AddRange(instance.DrainPartyCardAudit());
            if (drained.Any(entry => entry.Reason == reason))
            {
                return drained;
            }

            await Task.Delay(10);
        }

        return drained;
    }

    /// <summary>代理上报的内容不是它自己的列/槽位：拒收 + 审计，绝不进入共享表。</summary>
    private static async Task CheckContentMismatchAuditedAsync(Action<bool, string> check)
    {
        var (instance, a, _, generation) = CreateSettlement();
        var agent = new Agent(0, DungeonRewardLedgerStatus.Committed) { RollColumnOffset = 1 };
        instance.RegisterPartyCardRewardAgent(a, agent);
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        check(instance.DrainPartyCardAudit().Any(entry => entry.Reason == "agent-content-mismatch"),
            "content for another column is refused and audited");
        check(!instance.TryTakePartyCardPublication(generation, out _),
            "mismatched content never publishes");
    }

    /// <summary>
    /// R4/R5：提交结果都留审计；Rejected 是**确定性终态**（该列可以带着归属发布，玩家看到"这列是他的、
    /// 他没拿到东西"），Unknown 才是必须屏蔽、不重试、不发布的那一种（同代次里别人照常发布）。
    /// </summary>
    private static async Task CheckCommitOutcomeAuditedAsync(Action<bool, string> check)
    {
        foreach (var (status, reason, owner) in new[]
                 {
                     (DungeonRewardLedgerStatus.Rejected, "commit-rejected", (byte)0),
                     (DungeonRewardLedgerStatus.Unknown, "commit-unknown", PartyCardSelectionState.Unassigned)
                 })
        {
            var (instance, a, b, generation) = CreateSettlement();
            instance.RegisterPartyCardRewardAgent(a, new Agent(0, status));
            instance.RegisterPartyCardRewardAgent(b, new Agent(1, DungeonRewardLedgerStatus.Committed));
            await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
            check(instance.DrainPartyCardAudit().Any(entry => entry.Reason == reason),
                $"commit {status} is audited as {reason}");
            await instance.SettleOwnPartyCardAsync(b, PartyCardType.Free, 1, generation, CancellationToken.None);
            check(instance.TryTakePartyCardPublication(generation, out var snapshot)
                && snapshot!.Selection.FreeOwners[0] == owner
                && snapshot.Selection.FreeOwners[1] == 1,
                $"commit {status} leaves its own column at owner={owner} while the committed column publishes");
        }
    }

    /// <summary>
    /// 伪造上报：别的成员（或任何非归属者）把内容塞进**不属于自己**的列必须被拒（R1′）。
    /// 走反射是为了模拟"绕过 SettleOwn 的上报入口"；生产代码只能经私有
    /// <c>ReportPartyCardCommitFromAgent</c>（由 SettleOwn 在**校验 owner 之后**调用）。
    /// 变异（删掉 owner/槽位强制）会让本项变红——见 task-2-report.md 的变异记录。
    /// </summary>
    private static Task CheckForgedReportRejectedAsync(Action<bool, string> check)
    {
        var (instance, a, b, generation) = CreateSettlement();
        var slotA = instance.SnapshotPartyCards().MemberSlots[0];
        var slotB = instance.SnapshotPartyCards().MemberSlots[1];
        var roll = new DungeonClearRewardRoll(10, true, 50, 100, 101, 102);
        var report = typeof(DungeonInstance).GetMethod(
            "ReportPartyCardCommitFromAgent",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Report(Guid characterId, PartyCardType type, byte column, PartyCardContent content) =>
            (bool)report.Invoke(instance, [instance.PartyCardSettlementId, characterId, type, column, generation, content, new PartyCardCommitOutcome(DungeonRewardLedgerStatus.Committed, null)])!;

        // B 上报"自己的列"但内容是 A 掷的（PartySlot = A 的槽位）→ 拒。
        check(!Report(b, PartyCardType.Free, 1, new PartyCardContent(PartyCardType.Free, 1, slotA, roll, PartyCardCommitStatus.Pending)),
            "content rolled for another member's slot is refused");
        // A 上报 B 那一列 → 拒（列归属不是 A）。
        check(!Report(a, PartyCardType.Free, 1, new PartyCardContent(PartyCardType.Free, 1, slotB, roll, PartyCardCommitStatus.Pending)),
            "report for a column owned by another member is refused");
        // 没人归属的第 3 列 → 拒。
        check(!Report(a, PartyCardType.Free, 3, new PartyCardContent(PartyCardType.Free, 3, slotA, roll, PartyCardCommitStatus.Pending)),
            "report for an unassigned column is refused");
        // 类型串了（拿 Paid 内容塞 Free 列）→ 拒。
        check(!Report(a, PartyCardType.Free, 0, new PartyCardContent(PartyCardType.Paid, 0, slotA, roll, PartyCardCommitStatus.Pending)),
            "content whose type disagrees with the report is refused");
        // 旧代次 → 拒。
        check(!(bool)report.Invoke(instance, [instance.PartyCardSettlementId, a, PartyCardType.Free, (byte)0, generation + 1,
                new PartyCardContent(PartyCardType.Free, 0, slotA, roll, PartyCardCommitStatus.Pending),
                new PartyCardCommitOutcome(DungeonRewardLedgerStatus.Committed, null)])!,
            "stale generation report is refused");
        check(instance.DrainPartyCardAudit().Count(entry => entry.Reason == "report-rejected") == 5,
            "every refused report leaves an audit entry");
        // 正主上报自己的列 → 接受，且成为唯一发布来源。
        check(Report(a, PartyCardType.Free, 0, new PartyCardContent(PartyCardType.Free, 0, slotA, roll, PartyCardCommitStatus.Pending)),
            "owner report for its own column is accepted");
        instance.CompletePartyCardSelection(generation);
        return Task.CompletedTask;
    }

    private sealed class Agent(byte slot, DungeonRewardLedgerStatus status) : IPartyCardRewardAgent
    {
        private TaskCompletionSource<PartyCardCommitOutcome>? _hangingCommit;

        public int Commits { get; private set; }
        public Guid LastSettlementId { get; private set; }
        public bool ThrowOnCommit { get; init; }
        public bool ThrowCancellation { get; init; }
        public bool HangOnCommit { get; init; }
        public byte RollColumnOffset { get; init; }

        /// <summary>让"一直不返回"的那次提交在测试控制的时刻完成（模拟实例超时之后才落库）。</summary>
        public void CompleteHanging(DungeonRewardLedgerStatus lateStatus) =>
            (_hangingCommit ?? throw new InvalidOperationException("this agent is not hanging"))
                .TrySetResult(new PartyCardCommitOutcome(lateStatus, "late"));

        public PartyCardContent RollOwnColumn(Guid settlementId, PartyCardType type, byte column, long generation)
        {
            LastSettlementId = settlementId;
            return new(type, (byte)(column + RollColumnOffset), slot, new(10, true, 20, 30, null, 40), PartyCardCommitStatus.Pending);
        }

        public Task<PartyCardCommitOutcome> CommitOwnColumnAsync(
            Guid settlementId,
            PartyCardType type,
            byte column,
            long generation,
            PartyCardContent content,
            CancellationToken cancellationToken)
        {
            Commits++;
            if (ThrowOnCommit)
            {
                throw new InvalidOperationException("agent failure");
            }

            if (ThrowCancellation)
            {
                throw new OperationCanceledException("agent cancelled");
            }

            if (HangOnCommit)
            {
                _hangingCommit = new TaskCompletionSource<PartyCardCommitOutcome>();
                return _hangingCommit.Task;
            }

            return Task.FromResult(new PartyCardCommitOutcome(status, null));
        }
    }
}
