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
/// 副本实例注册表（设计稿 §3.1）：队伍键 / 角色键两张表 + 待入队信箱。
/// 纯内存、不落盘；实例在"最后一个成员离开"时销毁（<see cref="UnbindCharacter"/> 返回值），
/// 服务端关停时由托管服务调 <see cref="Clear"/>。
/// 类型是 public 只是因为 <see cref="EntranceService"/>（public 祖先类的公开构造函数）要构造注入它；
/// 方法与属性一律 internal，模型（<see cref="DungeonInstance"/> 及其共享集合）不因此对外暴露。
/// </summary>
public sealed class DungeonInstanceRegistry
{
    private readonly ConcurrentDictionary<ushort, DungeonInstance> _byParty = new();
    private readonly ConcurrentDictionary<Guid, DungeonInstance> _byCharacter = new();
    private readonly ConcurrentDictionary<Guid, DungeonEntryRequest> _pendingEntries = new();

    internal DungeonInstance GetOrCreateForParty(ushort partyId, Func<DungeonInstance> create) =>
        _byParty.GetOrAdd(partyId, _ => create());

    internal DungeonInstance GetOrCreateForSolo(Guid characterId, Func<DungeonInstance> create) =>
        _byCharacter.GetOrAdd(characterId, _ => create());

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

    /// <summary>倒计时扫描用：此刻还活着的实例（拷贝，出锁遍历）。</summary>
    internal DungeonInstance[] Instances =>
        _byParty.Values.Concat(_byCharacter.Values).Distinct().ToArray();

    /// <summary>服务端关停：清空所有实例（设计稿 §3.1 生命周期最后一条）。</summary>
    internal void Clear()
    {
        foreach (var instance in Instances)
        {
            instance.StopRun();
        }

        _byParty.Clear();
        _byCharacter.Clear();
        _pendingEntries.Clear();
    }
}
