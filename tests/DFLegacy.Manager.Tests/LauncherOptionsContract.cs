namespace DFLegacy.Launcher;

// The options model uses the executable's fallback only when ClientPath is
// absent. Exports supply ClientPath; link the real model without WPF startup.
internal static class Program
{
    internal const string DefaultClientPath = "DNF.exe";
}
