using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class TownSyncSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        CheckAreaUserIdPool(check);
        CheckOptionsAndPackets(check);
        await Task.CompletedTask;
    }

    private static void CheckOptionsAndPackets(Action<bool, string> check)
    {
        var defaults = new ServerOptions();
        check(defaults.TownSync.Enabled, "town sync is enabled by default");
        check(!defaults.TownSync.ForeignAppearanceBeforeRoster,
            "the roster is sent before foreign appearances by default");
        check(defaults.TownSync.DepartureSignal == TownDepartureSignal.UserAreaInactive,
            "the default departure signal is USER_AREA with active zero");
        check(defaults.TownSync.MaxPositionForwardsPerSecond == 10,
            "position forwarding is capped per member and second");
        check(!defaults.Debug.TownGhost.Enabled, "the debug town ghost is off by default");

        var townUser = new GameTownUser(7, 474, 234, 5);
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser).Payload[9] == 1,
            "USER_AREA marks a joining actor as active");
        check(GameProtocolEngine.CreateUserArea(2, 5, townUser, active: false).Payload[9] == 0,
            "USER_AREA marks a departing actor as inactive");
    }

    private static void CheckAreaUserIdPool(Action<bool, string> check)
    {
        var pool = new AreaUserIdPool(3);
        check(pool.Capacity == 3, "area user id pool capacity matches the channel limit");
        check(pool.TryAllocate(out var first) && first == 1,
            "the first area user id is still 1 so solo play stays byte-identical");
        check(pool.TryAllocate(out var second) && second == 2,
            "the second area user id is 2");
        check(pool.TryAllocate(out var third) && third == 3,
            "the third area user id is 3");
        check(!pool.TryAllocate(out _),
            "an exhausted pool refuses to hand out a duplicate id");
        check(pool.InUseCount == 3, "the pool reports three ids in use");
        check(pool.Release(2) && pool.TryAllocate(out var reused) && reused == 2,
            "a released area user id can be reused");
        check(!pool.Release(0) && !pool.Release(9),
            "releasing an out-of-range id is rejected");
        check(pool.Release(3) && pool.TryAllocateReverse(out var ghost) && ghost == 3,
            "the debug ghost allocates from the back of the pool");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new AreaUserIdPool(0),
            check,
            "a pool without capacity is rejected");
    }

    private static void CheckThrows<TException>(
        Action action,
        Action<bool, string> check,
        string name)
        where TException : Exception
    {
        try
        {
            action();
            check(false, name);
        }
        catch (TException)
        {
            check(true, name);
        }
    }
}
