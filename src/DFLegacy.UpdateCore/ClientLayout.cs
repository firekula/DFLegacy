namespace DFLegacy.UpdateCore;

/// <summary>
/// Resolves the physical location of a manifest file for the split layout:
/// launcher binaries live in <c>client\</c> while the original game files
/// (DNF.exe, DLLs, ImagePacks, Script.pvf, ...) stay in the installation root.
/// The flat layout is the default and remains fully supported.
/// </summary>
public static class ClientLayout
{
    private static readonly HashSet<string> LauncherFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "DFLegacy.Launcher.exe", "DFLegacy.Launcher.App.exe", "DFLegacy.Updater.exe",
        "DFLegacy.Diagnostics.exe", "DFLegacy.CrashCapture.exe", "DFLegacy.Bootstrap.ini",
        "DFLegacy.Chapter.json",
    };

    private static string? _launcherRoot;
    private static string? _gameRoot;

    /// <summary>The directory that holds the managed launcher and its .dflegacy state.</summary>
    public static string LauncherRoot => _launcherRoot ?? AppContext.BaseDirectory;

    /// <summary>The directory that holds the original game files.</summary>
    public static string GameRoot => _gameRoot ?? LauncherRoot;

    /// <summary>True when the launcher runs from client\ next to the original game files.</summary>
    public static bool IsSplit => !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(LauncherRoot)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(GameRoot)), StringComparison.OrdinalIgnoreCase);

    public static void Configure(string launcherRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(launcherRoot));
        _launcherRoot = root;
        _gameRoot = DetachedGameRoot(root);
    }

    private static string DetachedGameRoot(string launcherRoot)
    {
        if (!string.Equals(Path.GetFileName(launcherRoot), "client", StringComparison.OrdinalIgnoreCase)) return launcherRoot;
        var parent = Path.GetDirectoryName(launcherRoot);
        if (string.IsNullOrEmpty(parent)) return launcherRoot;
        return File.Exists(Path.Combine(parent, "DNF.exe")) ? parent : launcherRoot;
    }

    /// <summary>True when <paramref name="relative"/> is a managed launcher file.</summary>
    public static bool IsLauncherFile(string relative)
    {
        var name = relative.Replace('\\', '/');
        return !name.Contains('/') && LauncherFiles.Contains(name);
    }

    /// <summary>
    /// Maps a manifest-relative path to the physical file. Launcher files and
    /// .dflegacy state stay beside this process; everything else belongs to the game root.
    /// </summary>
    public static string Resolve(string installRoot, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (!IsSplit || !SameRoot(installRoot, LauncherRoot)) return relative;
        if (IsLauncherFile(normalized) || normalized.StartsWith(".dflegacy/", StringComparison.OrdinalIgnoreCase)) return relative;
        var combined = Path.GetFullPath(Path.Combine(GameRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        return combined;
    }

    private static bool SameRoot(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
