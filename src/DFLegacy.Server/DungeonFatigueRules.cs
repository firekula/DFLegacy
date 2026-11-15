using System.Globalization;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public readonly record struct FatigueLimits(ushort Maximum, ushort Premium);

public static class DungeonFatigueRules
{
    internal static FatigueLimits GetLimits(
        int fatigueDayKey,
        bool hasBlackDiamond,
        bool weekendDoubleEnabled)
    {
        var multiplier = weekendDoubleEnabled
            && DateOnly.TryParseExact(fatigueDayKey.ToString(CultureInfo.InvariantCulture),
                "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? 2 : 1;
        var premium = hasBlackDiamond ? GameProtocolEngine.BlackDiamondBonusFatigue * multiplier : 0;
        var maximum = GameProtocolEngine.DefaultMaximumFatigue * multiplier + premium;
        return new(checked((ushort)maximum), checked((ushort)premium));
    }

    public static bool CanEnterNewDungeon(
        ushort usedFatigue,
        ushort maximumFatigue) =>
        usedFatigue < maximumFatigue;
}
