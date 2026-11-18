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
                    continue;
                }
                if (File.Exists(path)) File.Move(path, Path.Combine(staging, name));
                else if (Directory.Exists(path)) Directory.Move(path, Path.Combine(staging, name));
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
