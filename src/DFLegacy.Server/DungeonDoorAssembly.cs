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

internal sealed record DungeonDoorOutcome(
    DungeonDoorDecision Decision,
    IReadOnlyList<DungeonDoorArrival> Arrivals,
    bool Forced,
    DateTimeOffset? CountdownDeadline);

/// <summary>
/// 「门口集结制」换房间（设计稿 §3.3，用户 2026-09-29 拍板）：第一个 CMD 48 只登记目标房间与到门口的人；
/// 全员到齐 → 立即推进；过半（&gt; 人数/2）→ 10 秒倒计时，到点由扫描循环强制推进。
/// 纯状态机：不碰锁、不碰 IO、时间由调用方传入（可单测）。调用方负责在实例锁内使用。
/// </summary>
internal sealed class DungeonDoorAssembly
{
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(10);

    private readonly List<DungeonDoorArrival> _arrivals = [];

    public DungeonRoomPosition? PendingTarget { get; private set; }

    public DateTimeOffset? CountdownDeadline { get; private set; }

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
        if (!_arrivals.Any(arrival => arrival.CharacterId == characterId))
        {
            _arrivals.Add(new DungeonDoorArrival(characterId, reply));
        }

        return Evaluate(memberCount, now, characterId);
    }

    /// <summary>倒计时到点 → 强制推进；未到点或没有集结时返回 null。</summary>
    public DungeonDoorOutcome? TryAdvanceOnCountdown(DateTimeOffset now, int memberCount)
    {
        if (PendingTarget is null
            || CountdownDeadline is not { } deadline
            || now < deadline)
        {
            return null;
        }

        var outcome = new DungeonDoorOutcome(
            DungeonDoorDecision.Advance,
            [.. _arrivals],
            Forced: true,
            CountdownDeadline);
        Reset();
        return outcome;
    }

    public void Reset()
    {
        _arrivals.Clear();
        PendingTarget = null;
        CountdownDeadline = null;
    }

    private DungeonDoorOutcome Evaluate(int memberCount, DateTimeOffset now, Guid lastArrival)
    {
        if (memberCount <= 1 || _arrivals.Count >= memberCount)
        {
            var outcome = new DungeonDoorOutcome(
                DungeonDoorDecision.Advance,
                [.. _arrivals],
                Forced: false,
                CountdownDeadline);
            Reset();
            return outcome;
        }

        // 过半（> 人数/2）→ 开倒计时；倒计时期间又有人到齐 → 上面的分支立即推进。
        if (_arrivals.Count * 2 > memberCount && CountdownDeadline is null)
        {
            CountdownDeadline = now + Countdown;
            return new DungeonDoorOutcome(
                DungeonDoorDecision.StartedCountdown,
                [.. _arrivals],
                Forced: false,
                CountdownDeadline);
        }

        _ = lastArrival;
        return new DungeonDoorOutcome(DungeonDoorDecision.Waiting, [.. _arrivals], false, CountdownDeadline);
    }
}
