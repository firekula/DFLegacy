namespace DFLegacy.Server;

public sealed partial record CharacterRecord
{
    public DateTimeOffset? GuildDepartedAtUtc { get; init; }
}
