namespace DFLegacy.Server;

public static class DungeonWeaknessPolicy
{
    public const int MinimumLevel = 18;
    public const byte MinimumRecovery = 10;
    public const byte InitialRecovery = MinimumRecovery;
    public const byte RecoveryPerMinute = 9;
    public const byte FullStamina = 100;

    public static byte ProjectStaminaRecovery(
        CharacterRecord? character,
        bool hasBlackDiamond)
    {
        if (character is null || !character.IsWeakened || hasBlackDiamond)
        {
            return FullStamina;
        }

        return (byte)Math.Clamp(character.WeaknessRecovery, MinimumRecovery, FullStamina);
    }

    public static bool ShouldApply(
        int characterLevel,
        bool forcedDungeonExit,
        bool dungeonActive,
        bool dungeonCleared,
        bool hasBlackDiamond) =>
        forcedDungeonExit
        && dungeonActive
        && !dungeonCleared
        && !hasBlackDiamond
        && characterLevel >= MinimumLevel;
}
