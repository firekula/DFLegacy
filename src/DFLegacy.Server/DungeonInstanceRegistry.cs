using System.Collections.Concurrent;

namespace DFLegacy.Server;

/// <summary>
/// 服务端给某个队员会话投的"待入队请求"（设计稿 §3.2）：队长会话在锁外投递，
/// 队员会话在主循环顶部（与交易"待安装"同一位置）取走并自己执行入场。
/// </summary>
internal sealed record DungeonEntryRequest(
    ushort PartyId,
    ushort DungeonId,
    byte Difficulty,
    byte DungeonOption,
    byte LeaderTownId,
    byte LeaderAreaId,
    Guid LeaderCharacterId);

/// <summary>
/// 副本实例注册表（设计稿 §3.1）：队伍键 / 角色绑定 / 单人键三张表 + 待选图、待入队两个信箱。
/// 纯内存、不落盘；实例在"最后一个成员离开"时销毁（<see cref="UnbindCharacter"/> 返回值），
/// 服务端关停时由托管服务调 <see cref="Clear"/>。
/// 类型是 public 只是因为 <see cref="EntranceService"/>（public 祖先类的公开构造函数）要构造注入它；
/// 方法与属性一律 internal，模型（<see cref="DungeonInstance"/> 及其共享集合）不因此对外暴露。
/// </summary>
public sealed class DungeonInstanceRegistry
{
    private readonly ConcurrentDictionary<ushort, DungeonInstance> _byParty = new();
    private readonly ConcurrentDictionary<Guid, DungeonInstance> _byCharacter = new();

    /// <summary>
    /// 单人实例的**专属**键表（设计稿 §7 决策 1）：只有 <see cref="GetOrCreateForSolo"/>用它。
    /// 不能拿 <see cref="_byCharacter"/> 当单人表——那张表同时被 <see cref="BindCharacter"/> 用于队员绑定
    /// （指向队伍实例），一个作为队员绑定过队伍实例的角色进来开单人副本时会直接复用那支队伍的实例，
    /// 房间/掉落/成员广播被两队共用（Task 2 评审遗留项）。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DungeonInstance> _soloByCharacter = new();
    private readonly ConcurrentDictionary<Guid, DungeonEntryRequest> _pendingEntries = new();
    private readonly ConcurrentDictionary<Guid, Guid> _pendingSelections = new();

    internal DungeonInstance GetOrCreateForParty(ushort partyId, Func<DungeonInstance> create) =>
        _byParty.GetOrAdd(partyId, _ => create());

    internal DungeonInstance GetOrCreateForSolo(Guid characterId, Func<DungeonInstance> create) =>
        _soloByCharacter.GetOrAdd(characterId, _ => create());

    internal DungeonInstance? TryGetForParty(ushort partyId) =>
        _byParty.TryGetValue(partyId, out var instance) ? instance : null;

    internal DungeonInstance? TryGetForCharacter(Guid characterId) =>
        _byCharacter.TryGetValue(characterId, out var instance) ? instance : null;

    /// <summary>把角色映射到实例（入场时调用一次；同一角色重复绑定以第一次为准）。</summary>
    internal void BindCharacter(Guid characterId, DungeonInstance instance)
    {
        if (instance.IsParty)
        {
            _byParty[instance.PartyId] = instance;
        }

        _byCharacter[characterId] = instance;
    }

    /// <summary>
    /// 解绑一个角色（回城 / 掉线 / 换角色）；返回它所在的实例（调用方据此决定广播"某人离开"），
    /// **实例空了就地销毁**（设计稿 §3.4：最后一个成员离开 → 丢弃房间/掉落）。
    /// </summary>
    internal DungeonInstance? UnbindCharacter(Guid characterId)
    {
        _pendingEntries.TryRemove(characterId, out _);
        _pendingSelections.TryRemove(characterId, out _);
        if (!_byCharacter.TryRemove(characterId, out var instance))
        {
            return null;
        }

        if (instance.RemoveMember(characterId) && instance.MemberCount == 0)
        {
            Remove(instance);
        }

        return instance;
    }

    internal void Remove(DungeonInstance instance)
    {
        if (instance.IsParty)
        {
            _byParty.TryRemove(new KeyValuePair<ushort, DungeonInstance>(instance.PartyId, instance));
        }
        else
        {
            // 单人实例销毁后必须忘掉键：否则下一次 GetOrCreateForSolo 会拿到一个没有成员、
            // run 已停的尸体实例（键不匹配 / 从未登记过时是 no-op）。
            _soloByCharacter.TryRemove(
                new KeyValuePair<Guid, DungeonInstance>(instance.SoloCharacterId, instance));
        }

        foreach (var member in instance.Members)
        {
            _byCharacter.TryRemove(new KeyValuePair<Guid, DungeonInstance>(member.CharacterId, instance));
        }

        instance.StopRun();
    }

    internal void PostEntryRequest(Guid characterId, DungeonEntryRequest request) =>
        _pendingEntries[characterId] = request;

    internal bool TryTakeEntryRequest(Guid characterId, out DungeonEntryRequest request) =>
        _pendingEntries.TryRemove(characterId, out request!);

    /// <summary>
    /// 两个信箱任一有货（终审 P2）：会话主循环的读等待是"等包或超时"，而信箱只在**有包**的
    /// 路径被取——队员客户端停在选图界面时一个包都不发，待入队请求会一直躺着（真机检查点 2）。
    /// 只有真的有货时才把读等待缩短到一个短 tick；单人永远没有待处理邮箱。
    /// </summary>
    internal bool HasPendingMail(Guid characterId) =>
        _pendingEntries.ContainsKey(characterId) || _pendingSelections.ContainsKey(characterId);

    /// <summary>队长开选图界面时给队员投的"待选图"请求（只记队长角色 id，队员自己把状态发给自己客户端）。</summary>
    internal void PostSelectionRequest(Guid characterId, Guid leaderCharacterId) =>
        _pendingSelections[characterId] = leaderCharacterId;

    internal bool TryTakeSelectionRequest(Guid characterId, out Guid leaderCharacterId) =>
        _pendingSelections.TryRemove(characterId, out leaderCharacterId);

    /// <summary>倒计时扫描用：此刻还活着的实例（拷贝，出锁遍历）。</summary>
    internal DungeonInstance[] Instances =>
        _byParty.Values
            .Concat(_byCharacter.Values)
            .Concat(_soloByCharacter.Values)
            .Distinct()
            .ToArray();

    /// <summary>服务端关停：清空所有实例（设计稿 §3.1 生命周期最后一条）。</summary>
    internal void Clear()
    {
        foreach (var instance in Instances)
        {
            instance.StopRun();
        }

        _byParty.Clear();
        _byCharacter.Clear();
        _soloByCharacter.Clear();
        _pendingEntries.Clear();
        _pendingSelections.Clear();
    }
}
