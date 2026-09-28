using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 队伍成员：角色 id + 显示名 + **当前**所在区域与区用户 id。
/// 区域字段会在换区时更新（设计稿 §5.6）——用 `with` **替换**名册里的元素而不是原地改写：
/// 成员对象不可变，`PartyRoster.Members` 才能给出"锁外可用"的快照（见 <see cref="PartyRoster.Members"/>）。
/// </summary>
internal sealed record PartyMember
{
    public required Guid CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public required ushort AreaUserId { get; init; }
    public required byte TownId { get; init; }
    public required byte AreaId { get; init; }

    public bool IsIn(byte townId, byte areaId) => TownId == townId && AreaId == areaId;
}

/// <summary>
/// 队伍纯模型（无 IO、无锁）：4 个槽位紧凑排列，**队长恒在 0 号槽**。
/// 槽位 uid 的生成规则见 <see cref="SlotUids"/>。
/// </summary>
internal sealed class PartyRoster
{
    /// <summary>队长固定占 0 号槽：换队长 = 把新队长移到 0 号再广播（NOTI 9 的 leaderSlot 因此恒为 0）。</summary>
    public const byte LeaderSlot = 0;

    private readonly List<PartyMember> _members = [];

    public PartyRoster(ushort partyId, PartyMember leader, byte maxMembers, byte nameSelection)
    {
        PartyId = partyId;
        MaxMembers = ClampMaxMembers(maxMembers);
        NameSelection = nameSelection;
        _members.Add(leader);
    }

    public ushort PartyId { get; }
    public byte NameSelection { get; private set; }
    public byte MaxMembers { get; private set; }
    public int Count => _members.Count;
    /// <summary>
    /// 名册快照：返回**新数组**。调用方（`PartyService`）的惯用法是"锁内取快照、出锁发包"，
    /// 直接交出去内部的 `List` 会让出锁后的枚举撞上并发 `TryAdd`/`Remove` 的版本检查异常；
    /// 成员对象本身也不可变，所以数组里的元素可以安全地在锁外读。
    /// </summary>
    public PartyMember[] Members => [.. _members];
    public PartyMember Leader => _members[0];
    public bool IsFull => _members.Count >= MaxMembers;

    public bool Contains(Guid characterId) => IndexOf(characterId) >= 0;

    public int IndexOf(Guid characterId)
    {
        for (var index = 0; index < _members.Count; index++)
        {
            if (_members[index].CharacterId == characterId)
            {
                return index;
            }
        }

        return -1;
    }

    public bool TryAdd(PartyMember member)
    {
        if (IsFull || Contains(member.CharacterId))
        {
            return false;
        }

        _members.Add(member);
        return true;
    }

    /// <summary>
    /// 移除成员；移除的是队长时把**最早加入的剩余成员**提到 0 号槽（设计稿 §4.2 的自动继任）。
    /// 返回 false 表示该角色本来就不在队里（不产生任何变化）。
    /// </summary>
    public bool Remove(Guid characterId)
    {
        var index = IndexOf(characterId);
        if (index < 0)
        {
            return false;
        }

        _members.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// 改队名与人数上限。上限**不会低于当前人数**：降到一个比现有成员还小的值会让队伍处在
    /// "3/2" 这种自相矛盾的状态，而客户端对这种状态的渲染未知（真机项），所以钳到
    /// <c>[成员数, 4]</c>（设计稿 §2.5）。
    /// </summary>
    public void SetInfo(byte nameSelection, byte maxMembers)
    {
        NameSelection = nameSelection;
        MaxMembers = (byte)Math.Clamp((int)maxMembers, _members.Count, GameProtocolEngine.PartyMemberSlotCount);
    }

    public bool UpdateLocation(Guid characterId, byte townId, byte areaId, ushort areaUserId)
    {
        var index = IndexOf(characterId);
        if (index < 0)
        {
            return false;
        }

        // 替换元素而不是原地改写：成员不可变，锁外读快照的人不会看到半个新位置（撕裂）。
        _members[index] = _members[index] with
        {
            TownId = townId,
            AreaId = areaId,
            AreaUserId = areaUserId
        };
        return true;
    }

    /// <summary>
    /// 生成 4 个槽位 uid。顺序**恒为加入顺序**（队长在 0 号）——踢人的 `CMD 14` 用的是槽位下标，
    /// 顺序全局一致时服务端可以直接把它映射到 <see cref="Members"/>。
    /// <paramref name="resolveUid"/> 返回"该收包者能解析到的 uid"（同区域 = 成员的真实 uid），
    /// 返回 null 表示该成员对这个收包者不可见（缺席）→ 填 **0**：填真实 uid 会在对方区域
    /// 解析成别人的名字（设计稿 §2.2.2）；超出成员数的槽填 0xFFFF。
    /// </summary>
    public ushort[] SlotUids(Func<PartyMember, ushort?> resolveUid)
    {
        ArgumentNullException.ThrowIfNull(resolveUid);
        var slots = new ushort[GameProtocolEngine.PartyMemberSlotCount];
        for (var index = 0; index < slots.Length; index++)
        {
            if (index >= _members.Count)
            {
                slots[index] = 0xFFFF;
                continue;
            }

            slots[index] = resolveUid(_members[index]) ?? 0;
        }

        return slots;
    }

    /// <summary>
    /// partyId 分配（设计稿 §2.2.1）：优先用 <paramref name="preferred"/>（建队时队长的 uid），
    /// 被现有队伍占用时扫描一个未占用的非 0 u16。
    /// </summary>
    public static bool TryAllocatePartyId(
        IReadOnlyCollection<ushort> takenIds,
        ushort preferred,
        out ushort partyId)
    {
        if (preferred != 0 && !takenIds.Contains(preferred))
        {
            partyId = preferred;
            return true;
        }

        for (var candidate = 1; candidate <= ushort.MaxValue; candidate++)
        {
            if (!takenIds.Contains((ushort)candidate))
            {
                partyId = (ushort)candidate;
                return true;
            }
        }

        partyId = 0;
        return false;
    }

    private static byte ClampMaxMembers(byte maxMembers) =>
        (byte)Math.Clamp((int)maxMembers, 1, GameProtocolEngine.PartyMemberSlotCount);
}
