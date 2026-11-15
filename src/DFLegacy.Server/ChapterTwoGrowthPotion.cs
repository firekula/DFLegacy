using System.Text.Json.Serialization;

namespace DFLegacy.Server;

public sealed record ChapterTwoGrowthPotionDefinition(
    int CooldownMilliseconds,
    int EffectMilliseconds,
    double ClearExperienceBonusRate)
{
    public const ushort ItemId = 7440;
    public const int MaximumWireSeconds = int.MaxValue / 1000;
}

public sealed record ChapterTwoGrowthPotionState(
    DateTimeOffset CooldownUntilUtc,
    DateTimeOffset EffectUntilUtc)
{
    public static uint RemainingSeconds(DateTimeOffset deadline, DateTimeOffset now) =>
        (uint)Math.Clamp(Math.Ceiling((deadline - now).TotalSeconds), 0,
            ChapterTwoGrowthPotionDefinition.MaximumWireSeconds);
}

public sealed partial record CharacterRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChapterTwoGrowthPotionState? ChapterTwoGrowthPotion { get; init; }
}
