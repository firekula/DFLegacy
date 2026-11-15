using System.Text.Json.Serialization;

namespace DFLegacy.Server;

public sealed partial record CharacterRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public byte ExpertJob { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public uint EnchantingProficiency { get; init; }
}
