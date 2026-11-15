using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal static class GuildProgression
{
    internal const byte StatusSkill = 200;
    internal const byte ExperienceSkill = 201;
    internal const int SkillGoldCost = 100000;
    internal const byte MaximumSkillLevel = 5;

    // Chapter Two guild growth table. Requirements are cumulative.
    private static readonly (uint Experience, int Gold)[] Upgrades =
    [
        (1350, 300000), (4894, 500000), (12150, 700000),
        (25175, 1000000), (46738, 1400000), (80547, 1800000),
        (131545, 2400000), (206299, 3100000), (313498, 3900000)
    ];

    internal static uint MaximumExperience => Upgrades[^1].Experience;

    internal static (uint Experience, int Gold)? GetUpgrade(byte level) =>
        level > 0 && level <= Upgrades.Length ? Upgrades[level - 1] : null;

    internal static bool IsSkill(byte id) => id is StatusSkill or ExperienceSkill;

    internal static bool IsPurchase(PacketFrame request) =>
        request.ProtocolId == GameProtocolEngine.BuySkillCommand
        && request.Body.Length > 2 && IsSkill(request.Body[2]);

    internal static byte Slot(byte skill) => (byte)(SkillCatalog.GuildSkillStart + skill - StatusSkill);

    internal static uint DungeonContribution(int characterLevel, int dungeonLevel, uint fatigue) =>
        characterLevel - dungeonLevel is >= -5 and <= 3 ? fatigue : 0;
}

internal readonly record struct GuildBenefits(byte StatusLevel, byte ExperienceLevel)
{
    internal static GuildBenefits For(GuildRecord? guild, byte rank) =>
        guild is not null && rank is >= 1 and <= 4
            ? new(Math.Min(GuildProgression.MaximumSkillLevel, guild.StatusSkillLevel),
                Math.Min(GuildProgression.MaximumSkillLevel, guild.ExperienceSkillLevel))
            : default;

    internal uint BonusExperience(uint experience) => (uint)((ulong)experience * ExperienceLevel / 100);

    internal IEnumerable<GameSkillEntry> Skills()
    {
        if (StatusLevel > 0)
            yield return new(GuildProgression.StatusSkill, StatusLevel, GuildProgression.Slot(GuildProgression.StatusSkill));
        if (ExperienceLevel > 0)
            yield return new(GuildProgression.ExperienceSkill, ExperienceLevel, GuildProgression.Slot(GuildProgression.ExperienceSkill));
    }
}
