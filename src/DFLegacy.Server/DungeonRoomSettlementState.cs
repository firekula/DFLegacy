namespace DFLegacy.Server;

/// <summary>
/// 会话级的「本房间结算」标志（结果屏 / 卡片布局 / 选卡权 / 选卡截止 / 结算奖励 / 已选卡片）。
/// 换房间必须整组清零——单人的 <c>CMD 48</c> 立即推进一直这么做。
///
/// Task 5 的门口集结制让房间可以由**别的会话**（队友的 <c>CMD 48</c> 或倒计时扫描）推进，
/// 于是"换房就要重置"这件事必须在三条路径上一致执行：单人立即推进、组队自己推进
/// （`AdvancePartyRoomAsync`）、被强制推进后的镜像追赶。原本的七个会话局部量无法从冒烟测试驱动，
/// 因此搬进这个可测对象：<see cref="Reset"/> 的有效性由
/// `DungeonPartySmokeTests.CheckRoomSettlementReset` 钉住（含变异验证）。
/// 语义与原局部量逐条一致（默认值、赋值点、读取点全部不变，报文一条不变）。
/// </summary>
internal sealed class DungeonRoomSettlementState
{
    public Guid SettlementId { get; private set; } = Guid.NewGuid();
    /// <summary>本房间的结算（<c>SET_PLAY_RESULT</c> → 结果屏）是否已经发出。</summary>
    public bool ResultSent { get; set; }

    /// <summary>本房间的结算卡片布局是否已经发出。</summary>
    public bool CardLayoutSent { get; set; }

    /// <summary>本房间的"选卡权"是否已经发出。</summary>
    public bool CardRightsSent { get; set; }

    /// <summary>本房间的卡片选择截止时刻（主循环到点会替客户端选卡）。</summary>
    public DateTimeOffset? CardSelectionDeadline { get; set; }

    /// <summary>本房间掷出的结算奖励（免费卡/金币卡的金额与道具）。</summary>
    public DungeonClearRewardRoll? ClearReward { get; set; }

    /// <summary>本房间免费卡已选的下标（null = 还没选）。</summary>
    public byte? FreeCardIndex { get; set; }

    /// <summary>本房间金币卡已选的下标（null = 还没选）。</summary>
    public byte? GoldCardIndex { get; set; }

    /// <summary>
    /// 换房间：整组清零。逐条对应改造前单人 `CMD 48` 分支里的七句赋值
    /// （`EntranceService.cs` 的 `dungeonResultSent = false;` 那一组）。
    /// </summary>
    public void Reset()
    {
        SettlementId = Guid.NewGuid();
        CardLayoutSent = false;
        ResultSent = false;
        CardRightsSent = false;
        CardSelectionDeadline = null;
        ClearReward = null;
        FreeCardIndex = null;
        GoldCardIndex = null;
    }
}
