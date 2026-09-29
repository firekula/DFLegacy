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
/// 队伍纯模型（无 IO、无锁）：**固定 4 槽、槽位下标稳定**——成员一旦进队就终生占住它的槽位下标，
/// 任何情况下都不在槽位之间搬动；队长由 <see cref="LeaderSlot"/> 字段表达，允许非 0。
/// 槽位 uid 的生成规则见 <see cref="SlotUids"/>。
/// </summary>
/// <remarks>
/// **为什么槽位必须稳定（真机 bug 根因）**：客户端的槽位写入 `CNRDPartyManager::SetMember`
/// （`DNF.exe 0x4FE690`）是**逐槽 clear-then-set**——先清旧占用者的 `user+0xFC`（队伍指针），
/// 再给新占用者挂上。搬动成员（如队长退队后把队员紧凑到槽 0）会让被搬者先在槽 0 被挂上链接、
/// 又在处理他自己原先那个槽（已被写成 `0xFFFF`）时**被清成 0** → 客户端误判"我离队了"
/// （`wasBefore && !isAfter`）→ HUD 消失而服务端仍认为他在队里（"幽灵队伍"）。
/// 保持下标稳定后，他在自己的槽位上 uid 值没变，`SetMember` 的"值没变 → 什么都不做"分支
/// （`0x4FE6A2`）直接跳过，链接不受影响。
/// </remarks>
internal sealed class PartyRoster
{
    private readonly PartyMember?[] _slots = new PartyMember?[GameProtocolEngine.PartyMemberSlotCount];

    public PartyRoster(ushort partyId, PartyMember leader, byte maxMembers, byte nameSelection)
    {
        PartyId = partyId;
        MaxMembers = ClampMaxMembers(maxMembers);
        NameSelection = nameSelection;
        LeaderSlot = 0;
        _slots[0] = leader;
        Count = 1;
    }

    public ushort PartyId { get; }
    public byte NameSelection { get; private set; }
    public byte MaxMembers { get; private set; }

    /// <summary>
    /// 队长所在的**槽位下标**（0..3）。建队时恒为 0；队长离开时就地继任 → 变成剩余成员里
    /// 槽下标最小的那个（不搬动）。NOTI 9 的最后一个字节就是这个值。
    /// </summary>
    public byte LeaderSlot { get; private set; }

    public int Count { get; private set; }

    /// <summary>
    /// 名册快照：返回**新数组**，内容 = 按槽下标升序的已占用成员（元素数 = <see cref="Count"/>）。
    /// 返回新数组的理由是**快照一致性**：调用方（`PartyService`）的惯用法是"锁内取快照、出锁发包"，
    /// 交出去内部数组的话，出锁后的枚举会看到并发 `TryAdd`/`Remove` 改到一半的名册（成员数与内容
    /// 对不上的撕裂状态）；新数组在锁内复制完成，出锁后不再变化。成员对象本身也不可变，所以数组里的
    /// 元素可以安全地在锁外读。**注意 <c>Members[i]</c> 的槽下标不一定等于 i**（槽位不搬动、可有空洞）——
    /// 用槽位下标的地方必须走 <see cref="TryGetSlot(Guid, out byte)"/> / <see cref="TryGetSlot(byte, out PartyMember)"/>。
    /// </summary>
    public PartyMember[] Members
    {
        get
        {
            var members = new PartyMember[Count];
            var next = 0;
            foreach (var member in _slots)
            {
                if (member is not null)
                {
                    members[next++] = member;
                }
            }

            return members;
        }
    }

    public PartyMember Leader => _slots[LeaderSlot]!;
    public bool IsFull => Count >= MaxMembers;

    public bool Contains(Guid characterId) => TryGetSlot(characterId, out _);

    /// <summary>
    /// 成员 → 他的**槽位下标**（0..3）。**不是"第几个成员"**：CMD 14 踢人与 NOTI 10 要的都是槽下标。
    /// 角色不在队里时返回 false。
    /// </summary>
    public bool TryGetSlot(Guid characterId, out byte slot)
    {
        for (byte index = 0; index < _slots.Length; index++)
        {
            if (_slots[index]?.CharacterId == characterId)
            {
                slot = index;
                return true;
            }
        }

        slot = 0;
        return false;
    }

    /// <summary>槽位下标 → 成员（踢人要用）。空槽返回 false。</summary>
    public bool TryGetSlot(byte slot, out PartyMember member)
    {
        member = null!;
        if (slot >= _slots.Length || _slots[slot] is not { } found)
        {
            return false;
        }

        member = found;
        return true;
    }

    /// <summary>加入**最小的空闲槽下标**（不做紧凑排列：新成员可能填进比现有成员更小的下标）。</summary>
    public bool TryAdd(PartyMember member)
    {
        if (IsFull || Contains(member.CharacterId))
        {
            return false;
        }

        for (byte index = 0; index < _slots.Length; index++)
        {
            if (_slots[index] is null)
            {
                _slots[index] = member;
                Count++;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 移除成员：**只清空他自己那个槽**，其余成员原地不动（槽位不搬动）。移除的是队长时**就地继任**——
    /// 队长身份交给槽下标最小的剩余成员（只更新 <see cref="LeaderSlot"/>，成员不动）。
    /// 返回 false 表示该角色本来就不在队里（不产生任何变化）。
    /// </summary>
    public bool Remove(Guid characterId)
    {
        if (!TryGetSlot(characterId, out var slot))
        {
            return false;
        }

        _slots[slot] = null;
        Count--;
        if (slot == LeaderSlot)
        {
            PromoteLowestSlot();
        }

        return true;
    }

    /// <summary>就地继任：把队长身份交给槽下标最小的剩余成员；队伍空时保持原值（调用方随即解散，不再读 Leader）。</summary>
    private void PromoteLowestSlot()
    {
        for (byte index = 0; index < _slots.Length; index++)
        {
            if (_slots[index] is not null)
            {
                LeaderSlot = index;
                return;
            }
        }
    }

    /// <summary>
    /// 转交队长：把队长身份交给 <paramref name="slot"/> 上的成员（B 卷 §4.3 的 CMD 131）。
    /// **就地**改 LeaderSlot，不移动任何成员（槽位下标稳定是本服务的硬约束——搬动槽位会让客户端
    /// 误判退队，见 README/设计稿里的"幽灵队伍"根因）。返回 false 表示该槽位没有人或本来就是队长。
    /// </summary>
    public bool TryPromoteLeader(byte slot)
    {
        // 空槽与越界都走 TryGetSlot（它同时负责边界检查）；"本来就是队长"不产生任何变化。
        if (slot == LeaderSlot || !TryGetSlot(slot, out _))
        {
            return false;
        }

        LeaderSlot = slot;
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
        MaxMembers = (byte)Math.Clamp((int)maxMembers, Count, GameProtocolEngine.PartyMemberSlotCount);
    }

    public bool UpdateLocation(Guid characterId, byte townId, byte areaId, ushort areaUserId)
    {
        if (!TryGetSlot(characterId, out var slot))
        {
            return false;
        }

        // 替换元素而不是原地改写：成员不可变，锁外读快照的人不会看到半个新位置（撕裂）。
        _slots[slot] = _slots[slot]! with
        {
            TownId = townId,
            AreaId = areaId,
            AreaUserId = areaUserId
        };
        return true;
    }

    /// <summary>
    /// 生成 4 个槽位 uid，**按槽位下标 0..3 遍历**：该槽有成员 → `resolve(member) ?? 该成员自己的 uid`；
    /// **空槽只能是 `0xFFFF`**。顺序语义与旧实现一致（所有收包者看到的槽位下标相同），只是"哪些下标被占用"不再连续。
    /// 踢人的 `CMD 14` 用的就是这些下标，所以服务端可以用 <see cref="TryGetSlot(Guid, out byte)"/> 直接映射。
    /// <paramref name="resolveUid"/> 返回"该收包者能解析到的 uid"（同区域 = 成员的真实 uid），
    /// 返回 null 表示该成员对这个收包者不可见（跨区域 / 目录里查不到）→ 填**该成员自己的 uid**，
    /// **绝不填 0**（真机证据：把 0 填给"与收包者不在同一区域的成员"后，客户端把 0 当成"这个槽没有人/
    /// 该成员已离开"——换区后两端的名册里对方那一格都是 0，**两端同时**弹"队伍中只剩下[X]。要继续保留队伍吗？"
    /// 并各自把对方摘掉，队伍退化成两个单人队；填一个本地解析不到的 uid 只是那一行显示空白，成员仍在队伍里）。
    /// 填成员自己的 uid 在对方区域**可能撞号**、解析成同区另一位玩家的名字——次要的正确性问题，
    /// 本轮不改 uid 池，取舍与已知限制见设计稿 §2.2.2/§11。
    /// </summary>
    public ushort[] SlotUids(Func<PartyMember, ushort?> resolveUid)
    {
        ArgumentNullException.ThrowIfNull(resolveUid);
        var slots = new ushort[GameProtocolEngine.PartyMemberSlotCount];
        for (byte index = 0; index < slots.Length; index++)
        {
            slots[index] = _slots[index] is { } member
                ? resolveUid(member) ?? member.AreaUserId
                : (ushort)0xFFFF;
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
