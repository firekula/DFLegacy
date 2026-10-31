namespace DFLegacy.Server;

/// <summary>
/// 一个会话（一个角色）自己的翻牌奖励代理：把两件事交回**该成员自己的会话**去做（R1′/R4）——
/// ①用它自己那份掷取（<c>roomSettlement.ClearReward</c>，含它自己的黑钻标志）产出归属列内容；
/// ②用它**自己角色的精确基线**把奖励提交进存档。
/// <para>
/// 实例只按 <c>characterId</c> 持有它，并在调用**之前**校验归属（列→槽位）。
/// 会话在入队/入场时注册、离队/掉线时随 <see cref="DungeonInstance.RemoveMember"/> 解绑。
/// </para>
/// </summary>
internal sealed class SessionPartyCardRewardAgent(
    Guid characterId,
    Func<Guid, PartyCardType, byte, long, PartyCardContent> rollOwnColumn,
    Func<Guid, PartyCardType, byte, long, PartyCardContent, CancellationToken, Task<PartyCardCommitOutcome>> commitOwnColumn)
    : IPartyCardRewardAgent
{
    internal Guid CharacterId { get; } = characterId;

    public PartyCardContent RollOwnColumn(Guid settlementId, PartyCardType cardType, byte column, long generation) =>
        rollOwnColumn(settlementId, cardType, column, generation);

    public Task<PartyCardCommitOutcome> CommitOwnColumnAsync(
        Guid settlementId,
        PartyCardType cardType,
        byte column,
        long generation,
        PartyCardContent content,
        CancellationToken cancellationToken) =>
        commitOwnColumn(settlementId, cardType, column, generation, content, cancellationToken);
}
