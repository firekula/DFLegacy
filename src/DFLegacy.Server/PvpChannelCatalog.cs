using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record PvpChannelProfile(int Number, string Name, int Port, int MinimumGrade, int MaximumGrade)
{
    internal bool Allows(int grade, int pvfMaximum) =>
        grade >= MinimumGrade && grade <= Math.Min(MaximumGrade, pvfMaximum);
}

internal static class PvpChannelCatalog
{
    internal static PvpChannelProfile[] Profiles(PvpOptions options) => !options.Enabled ? [] :
    [
        new(51, options.Name, options.Port, 0, 9),
        new(52, "Duel Mid", Act1PvpPorts.Intermediate, 10, 19),
        new(53, "Duel High", Act1PvpPorts.Advanced, 20, byte.MaxValue),
        new(55, "Duel Free", Act1PvpPorts.Free, 0, byte.MaxValue)
    ];
}
