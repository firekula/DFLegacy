using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>Original member_exp.tbl uses open lower / closed upper intervals.</summary>
public static class MentorGrowthPolicy
{
    private static readonly uint[] Thresholds = [0, 3, 6, 11, 20, 37, 70, 132, 249, 468, 880];
    public const uint MaximumExperience = 880;
    public static (byte Level, uint Experience, uint NextExperience) Project(uint experience)
    {
        experience = Math.Min(experience, MaximumExperience);
        var level = 1;
        while (level < 10 && experience > Thresholds[level]) level++;
        var lower = Thresholds[level - 1];
        return ((byte)level, experience - lower, Thresholds[level] - lower);
    }
    public static bool IsLevelUp(uint experience) => experience == MaximumExperience
        || (experience > 1 && Thresholds.Contains(experience - 1));
    // The reference stores the product to float32 before widening for the EXP multiplication.
    public static double Rate(byte level) => (double)(Math.Min(level, (byte)10) * 0.01f);
    public static uint Bonus(uint basis, double rate) => (uint)Math.Min(uint.MaxValue, Math.Truncate(basis * rate));
    public static uint Request(uint basis, uint bonus) => (uint)Math.Min(uint.MaxValue, (ulong)basis + bonus);
    public static uint CreditedBonus(uint basis, uint nominalBonus, uint actualGrant) =>
        Math.Min(nominalBonus, actualGrant - Math.Min(basis, actualGrant));

    /// <summary>Allocate the emulator's uint capacity to established components before mentor.</summary>
    public static GameDungeonClearExperienceBreakdown ClipClear(GameDungeonClearExperienceBreakdown value, uint actual)
    {
        uint Take(uint amount) { var taken = Math.Min(amount, actual); actual -= taken; return taken; }
        return new()
        {
            BaseExperience = Take(value.BaseExperience), PartyBonus = Take(value.PartyBonus),
            RankBonus = Take(value.RankBonus), AvatarBonus = Take(value.AvatarBonus),
            CreatureBonus = Take(value.CreatureBonus), EventBonus = Take(value.EventBonus),
            BlackDiamondBonus = Take(value.BlackDiamondBonus), ChannelBonus = Take(value.ChannelBonus),
            MentorBonus = Take(value.MentorBonus)
        };
    }
}
