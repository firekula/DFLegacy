using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>门口集结的判定结果（设计稿 §3.3）。</summary>
internal enum DungeonDoorDecision
{
    /// <summary>已登记，留在原地等其他人。</summary>
    Waiting,

    /// <summary>过半到齐，10 秒倒计时开始（本次调用不再改房间）。</summary>
    StartedCountdown,

    /// <summary>推进房间：调用方出锁后给全员发 NOTI 29（并给已登记的人补命令回包）。</summary>
    Advance,

    /// <summary>与当前集结方向不一致（不同门）→ 记日志忽略。</summary>
    Ignored
}

/// <summary>已到门口的成员：推进时要给他补发 CMD 48 的成功回包（他的客户端还在等）。</summary>
internal sealed record DungeonDoorArrival(Guid CharacterId, GameServerPacket Reply);

/// <summary>
/// 一次集结判定的结果。<see cref="GraceExpired"/> 只用于日志区分"宽限窗口到点"与"倒计时到点"
/// 这两条都带 <see cref="Forced"/>=true 的路径（行为一致，证据要分得清）。
/// </summary>
internal sealed record DungeonDoorOutcome(
    DungeonDoorDecision Decision,
    IReadOnlyList<DungeonDoorArrival> Arrivals,
    bool Forced,
    DateTimeOffset? CountdownDeadline,
    bool GraceExpired = false);

/// <summary>
/// 「门口集结制」换房间（设计稿 §3.3，用户 2026-09-29 拍板）：第一个 CMD 48 只登记目标房间与到门口的人；
/// 全员到齐 → 立即推进；过半（&gt; 人数/2）→ 10 秒倒计时，到点由扫描循环强制推进。
/// 人数缩水（有人退队/回城）由扫描循环 <see cref="TryAdvanceOnCountdown"/> 带着新的 memberCount 重新评估。
/// 纯状态机：不碰锁、不碰 IO、时间由调用方传入（可单测）。调用方负责在实例锁内使用。
///
/// **宽限窗口**（<see cref="Grace"/>，2026-10-01）：真机证据（`server-party28.log`）表明 2008 客户端
/// **只有一方会每房上报 CMD 48**（7 次换房全部由同一个会话上报，另一个 0 次；party26/27 同形），
/// 所以"全员到齐"永远不成立；改造前调用方按 `memberCount: 1` 登记，第一个上报者**立刻**推进，
/// 把还在加载的队友拽进下一房（疑似 `Eip=0x009019C1` 硬崩的诱因）。现在按**真实人数**登记，
/// 并在只有部分人到门口时开一个短窗口：窗口内全员到齐 → 立即推进；窗口到点仍有人没报 →
/// 照样强制推进兜底（门绝不能永远打不开）。
///
/// 窗口与过半倒计时**不叠加**：过半（严格 &gt; 人数/2）时**根本不开**窗口，交给既有的 10 秒倒计时，
/// 于是"过半 → 倒计时"的行为逐字不变；只有"有人在门口但还没过半"的等待期才有窗口兜底。
/// </summary>
internal sealed class DungeonDoorAssembly
{
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(10);

    /// <summary>未显式配置时的宽限窗口（生产由 <c>ServerOptions.Dungeon.DoorGraceSeconds</c> 注入）。</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(2);

    private readonly List<DungeonDoorArrival> _arrivals = [];

    public DungeonDoorAssembly(TimeSpan? grace = null) => Grace = grace ?? DefaultGrace;

    /// <summary>本实例的门口宽限窗口（&lt;= 0 表示不要窗口，退回"立即推进"）。</summary>
    public TimeSpan Grace { get; }

    public DungeonRoomPosition? PendingTarget { get; private set; }

    public DateTimeOffset? CountdownDeadline { get; private set; }

    /// <summary>宽限窗口的到点时间：只有"有人在门口、又没到过半"时才有值。</summary>
    public DateTimeOffset? GraceDeadline { get; private set; }

    public bool IsWaiting => PendingTarget is not null;

    public DungeonDoorOutcome Register(
        DungeonRoomPosition target,
        Guid characterId,
        int memberCount,
        DateTimeOffset now,
        GameServerPacket reply)
    {
        if (PendingTarget is { } pending && pending != target)
        {
            // 最小实现：只认一个门；换门要先等本次集结结束（设计稿 §3.3 第 6 条）。
            return new DungeonDoorOutcome(DungeonDoorDecision.Ignored, [.. _arrivals], false, CountdownDeadline);
        }

        PendingTarget ??= target;
        var existingIndex = _arrivals.FindIndex(arrival => arrival.CharacterId == characterId);
        if (existingIndex < 0)
        {
            _arrivals.Add(new DungeonDoorArrival(characterId, reply));
        }
        else
        {
            // 客户端在等回包期间又发了一次 CMD 48（真机第一检查点就是"客户端能否接受延迟回包"）：
            // 推进时补发的必须是**最新**那一份回包（旧序列号客户端对不上号）；同一角色仍只算
            // 一个到门口者，顺序不变。
            _arrivals[existingIndex] = new DungeonDoorArrival(characterId, reply);
        }

        return Evaluate(memberCount, now, characterId);
    }

    /// <summary>
    /// 扫描循环用（人数会在等待期间缩水）：① 到齐 → 立即推进；② 过半且还没起倒计时 → 起倒计时；
    /// ③ 倒计时到点 → 强制推进；④ 其余返回 null（调用方只记日志、不改房间）。
    /// 没有 ①② 两次重估，人一走少剩下的人就永远卡在门口：既拿不到 CMD 48 回包，也不会过半。
    /// **⑤ 宽限窗口到点**（没人过半、但有人在门口）同样强制推进：这是"客户端不每人都报 CMD 48"
    /// 时的兜底，位置就在过半判定之后——过了半就该让 10 秒倒计时说话。
    /// </summary>
    public DungeonDoorOutcome? TryAdvanceOnCountdown(DateTimeOffset now, int memberCount)
    {
        if (PendingTarget is null)
        {
            return null;
        }

        // 严格大于 1：单人没有"集结"这回事，别把 memberCount 缩到 1 之后的等待当成到齐。
        if (memberCount > 1 && AllArrived(memberCount))
        {
            return TakeAllArrivedAdvance();
        }

        // 人数缩水可能让"过半"第一次成立（4 人时 2 人 Waiting → 剩 3 人时 2/3 已过半）。
        if (CountdownDeadline is null && _arrivals.Count * 2 > memberCount)
        {
            CountdownDeadline = now + Countdown;
            // 过半由倒计时接手：宽限窗口让位，否则它会在 2 秒时把还没到齐的人提前推进去。
            GraceDeadline = null;
            return new DungeonDoorOutcome(
                DungeonDoorDecision.StartedCountdown,
                [.. _arrivals],
                Forced: false,
                CountdownDeadline);
        }

        if (CountdownDeadline is { } deadline)
        {
            if (now < deadline)
            {
                return null;
            }

            var forced = new DungeonDoorOutcome(
                DungeonDoorDecision.Advance,
                [.. _arrivals],
                Forced: true,
                CountdownDeadline);
            Reset();
            return forced;
        }

        if (GraceDeadline is not { } grace || now < grace)
        {
            return null;
        }

        // 宽限窗口到点：仍按"立即推进 + 其余人被强制"的兜底口径推进（绝不留下开不了的门）。
        var graceExpired = new DungeonDoorOutcome(
            DungeonDoorDecision.Advance,
            [.. _arrivals],
            Forced: true,
            CountdownDeadline,
            GraceExpired: true);
        Reset();
        return graceExpired;
    }

    public void Reset()
    {
        _arrivals.Clear();
        PendingTarget = null;
        CountdownDeadline = null;
        GraceDeadline = null;
    }

    private DungeonDoorOutcome Evaluate(int memberCount, DateTimeOffset now, Guid lastArrival)
    {
        if (memberCount <= 1 || AllArrived(memberCount))
        {
            return TakeAllArrivedAdvance();
        }

        // 过半（> 人数/2）→ 开倒计时；倒计时期间又有人到齐 → 上面的分支立即推进。
        if (_arrivals.Count * 2 > memberCount && CountdownDeadline is null)
        {
            CountdownDeadline = now + Countdown;
            // 倒计时接手，宽限窗口让位（见 TryAdvanceOnCountdown 的同一处理）。
            GraceDeadline = null;
            return new DungeonDoorOutcome(
                DungeonDoorDecision.StartedCountdown,
                [.. _arrivals],
                Forced: false,
                CountdownDeadline);
        }

        // 还没过半：开一次宽限窗口等其他人（只开一次，不因后来的人重复登记而顺延）。
        // 过半时**不开**窗口（上面那条分支已经把 deadline 清掉了）：那种情形交给 10 秒倒计时，
        // 别让 2 秒的窗口把还没到门口的人提前推走。
        GraceDeadline = _arrivals.Count * 2 > memberCount || Grace <= TimeSpan.Zero
            ? null
            : GraceDeadline ?? now + Grace;

        _ = lastArrival;
        return new DungeonDoorOutcome(DungeonDoorDecision.Waiting, [.. _arrivals], false, CountdownDeadline);
    }

    /// <summary>到门口的人数已达到当前人数（人数缩水后同样成立）。</summary>
    private bool AllArrived(int memberCount) => _arrivals.Count >= memberCount;

    /// <summary>全员到齐的推进结果：逐字复用（不再等倒计时），并清空本次集结。</summary>
    private DungeonDoorOutcome TakeAllArrivedAdvance()
    {
        var outcome = new DungeonDoorOutcome(
            DungeonDoorDecision.Advance,
            [.. _arrivals],
            Forced: false,
            CountdownDeadline);
        Reset();
        return outcome;
    }
}
