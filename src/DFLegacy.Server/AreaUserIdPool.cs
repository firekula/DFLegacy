namespace DFLegacy.Server;

/// <summary>
/// 区用户 id 池：协议里 SELECT_CHARACTER 应答下发的 server id / 区域成员 id。
/// 客户端靠它认定「哪个 actor 是我」，因此同一时刻在线的角色必须拿到互不相同的 id。
/// 正向分配从 1 开始（第一个玩家仍是 1，单人行为与改造前逐字节一致）；
/// 反向分配从容量尾部开始，留给调试幽灵，避免与真人抢占低段 id。
/// </summary>
public sealed class AreaUserIdPool(int capacity)
{
    private readonly object _gate = new();
    private readonly bool[] _inUse = new bool[ValidateCapacity(capacity)];

    public int Capacity => _inUse.Length;

    public int InUseCount
    {
        get
        {
            lock (_gate)
            {
                var count = 0;
                foreach (var used in _inUse)
                {
                    if (used)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    public bool TryAllocate(out ushort id) => TryAllocateCore(fromBack: false, out id);

    public bool TryAllocateReverse(out ushort id) => TryAllocateCore(fromBack: true, out id);

    public bool Release(ushort id)
    {
        if (id == 0 || id > _inUse.Length)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_inUse[id - 1])
            {
                return false;
            }

            _inUse[id - 1] = false;
            return true;
        }
    }

    private bool TryAllocateCore(bool fromBack, out ushort id)
    {
        lock (_gate)
        {
            for (var offset = 0; offset < _inUse.Length; offset++)
            {
                var index = fromBack ? _inUse.Length - 1 - offset : offset;
                if (_inUse[index])
                {
                    continue;
                }

                _inUse[index] = true;
                id = checked((ushort)(index + 1));
                return true;
            }
        }

        id = 0;
        return false;
    }

    private static int ValidateCapacity(int value) =>
        value is >= 1 and <= ushort.MaxValue
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(capacity),
                value,
                "Area user id capacity must be between 1 and 65535.");
}
