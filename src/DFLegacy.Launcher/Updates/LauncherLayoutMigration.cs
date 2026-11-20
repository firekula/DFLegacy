using System.Diagnostics;
using System.Text.Json;
using DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

/// <summary>
/// Rewrites a flat managed client into the single-launcher layout:
/// <c>DFOLauncher.exe</c> outside, every managed and game file inside <c>client\</c>.
/// The receipt moves with the client so startup validation keeps working.
/// </summary>
internal static class LauncherLayoutMigration
{
    internal const string ClientDirectory = "client";
    private static readonly string[] Managed =
    {
        "DFLegacy.Launcher.App.exe", "DFLegacy.Updater.exe", "DFLegacy.Diagnostics.exe",
        "DFLegacy.CrashCapture.exe", "DFLegacy.Bootstrap.ini", "DFLegacy.Launcher.exe",
        "DFLegacy.Chapter.json",
    };
    // Game binaries, DLLs and data packs stay in the installation root.

    internal static bool IsFlat(string root) =>
        File.Exists(Path.Combine(root, Managed[0])) && !Directory.Exists(Path.Combine(root, ClientDirectory));

    /// <summary>
    /// The installation root for a launcher base directory: the app runs from <c>client\</c>,
    /// while a flat launcher runs from the root itself.
    /// </summary>
    internal static string InstallationRoot(string baseDirectory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (!string.Equals(Path.GetFileName(trimmed), ClientDirectory, StringComparison.OrdinalIgnoreCase)) return trimmed;
        return Path.GetDirectoryName(trimmed) ?? trimmed;
    }

    /// <summary>
    /// Lifts the game files out of <c>client\</c> for installations made by the previous
    /// single-launcher package. Launcher files, <c>.dflegacy</c> and Logs stay in place.
    /// Idempotent; on failure every moved entry returns to <c>client\</c>.
    /// </summary>
    internal static bool TryLift(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var client = Path.Combine(root, ClientDirectory);
        if (!File.Exists(Path.Combine(client, "DNF.exe")) || File.Exists(Path.Combine(root, "DNF.exe"))) return false;
        var keep = new HashSet<string>(Managed, StringComparer.OrdinalIgnoreCase) { ".dflegacy", "Logs" };
        var entries = Directory.EnumerateFileSystemEntries(client)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !keep.Contains(name) && !name.StartsWith(ClientDirectory + ".", StringComparison.Ordinal);
            })
            .OrderBy(path => Path.GetFileName(path).Equals("DNF.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();
        var moved = new List<string>();
        try
        {
            foreach (var source in entries)
            {
                var name = Path.GetFileName(source);
                var target = Path.Combine(root, name);
                if (File.Exists(target) || Directory.Exists(target)) continue;
                PathPolicy.NoLinks(source);
                if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
                moved.Add(source);
            }
            if (!File.Exists(Path.Combine(root, "DNF.exe")))
                throw new InvalidDataException("游戏文件未能移回安装根目录。");
            return true;
        }
        catch
        {
            for (var index = moved.Count - 1; index >= 0; --index)
            {
                var source = moved[index];
                var name = Path.GetFileName(source);
                var target = Path.Combine(root, name);
                try
                {
                    if (Directory.Exists(target)) Directory.Move(target, source);
                    else if (File.Exists(target)) File.Move(target, source);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    /// <summary>
    /// Moves the flat installation under <c>client\</c> and starts the launcher stub from
    /// the original root. Returns the stub process when the migration started.
    /// </summary>
    internal static async Task<bool> TryMigrateAsync(string root, string launcher, string[] arguments)
    {
        if (!IsFlat(root) || !File.Exists(launcher)) return false;
        Program.WriteLauncherLog("Launcher layout migration: starting.");
        var client = Path.Combine(root, ClientDirectory);
        var staging = client + ".migrating";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var name in Managed)
            {
                var source = Path.Combine(root, name);
                if (!File.Exists(source)) continue;
                PathPolicy.NoLinks(source);
                File.Move(source, Path.Combine(staging, name));
            }
            foreach (var path in Directory.EnumerateFileSystemEntries(root))
            {
                var name = Path.GetFileName(path);
                if (name is ClientDirectory or "DFOLauncher.exe" || name.StartsWith(ClientDirectory + ".", StringComparison.Ordinal)
                    || Path.GetFileName(path) is "DFLegacy.Launcher.exe") continue;
                if (name.StartsWith(".dflegacy", StringComparison.OrdinalIgnoreCase))
                {
                    PathPolicy.NoLinks(path);
                    var target = Path.Combine(staging, name);
                    if (Directory.Exists(path)) Directory.Move(path, target); else File.Move(path, target);
                }
                // Game files and player configuration stay in the installation root.
            }
            Directory.Move(staging, client);
            Program.WriteLauncherLog("Launcher layout migration: client directory ready.");
        }
        catch
        {
            if (Directory.Exists(staging)) { try { Directory.Delete(staging, recursive: true); } catch (IOException) { } }
            throw;
        }
        // The launcher stub resolves client\ itself; it must outlive this process.
        var start = new ProcessStartInfo(launcher) { WorkingDirectory = root, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("无法启动单启动器入口。");
        Program.WriteLauncherLog("Launcher layout migrated to DFOLauncher.exe + client.");
        await Task.CompletedTask;
        return true;
    }
}
