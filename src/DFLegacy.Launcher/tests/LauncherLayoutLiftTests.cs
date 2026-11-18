using DFLegacy.Launcher.Updates;

internal static class LauncherLayoutLiftTests
{
    internal static void Run(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "legacy-split-lift");
        var client = Path.Combine(root, "client");
        Directory.CreateDirectory(Path.Combine(client, ".dflegacy", "v4"));
        Directory.CreateDirectory(Path.Combine(client, "Logs", "DFLegacy"));
        Directory.CreateDirectory(Path.Combine(client, "ImagePacks2"));
        foreach (var name in new[] { "DFLegacy.Launcher.exe", "DFLegacy.Launcher.App.exe", "DFLegacy.Updater.exe",
                     "DFLegacy.Diagnostics.exe", "DFLegacy.CrashCapture.exe", "DFLegacy.Bootstrap.ini", "DFLegacy.Chapter.json",
                     "DNF.exe", "Script.pvf", "audio.xml" })
            File.WriteAllText(Path.Combine(client, name), "fixture:" + name);
        File.WriteAllText(Path.Combine(client, "ImagePacks2", "sprite.NPK"), "fixture:npk");
        File.WriteAllText(Path.Combine(client, ".dflegacy", "v4", "installed.json"), "{}");

        check(LauncherLayoutMigration.InstallationRoot(client) == root, "app base directory maps to the installation root");
        check(LauncherLayoutMigration.TryLift(root), "legacy split installation is lifted");
        check(File.Exists(Path.Combine(root, "DNF.exe")) && File.Exists(Path.Combine(root, "Script.pvf"))
            && Directory.Exists(Path.Combine(root, "ImagePacks2")), "game files move to the installation root");
        check(File.Exists(Path.Combine(client, "DFLegacy.Launcher.App.exe"))
            && File.Exists(Path.Combine(client, "DFLegacy.Bootstrap.ini"))
            && Directory.Exists(Path.Combine(client, ".dflegacy"))
            && Directory.Exists(Path.Combine(client, "Logs")), "launcher binaries and state stay in client");
        check(!LauncherLayoutMigration.TryLift(root), "a lifted installation is not lifted twice");

        var launcherOnly = Path.Combine(scratch, "launcher-only");
        Directory.CreateDirectory(Path.Combine(launcherOnly, "client"));
        File.WriteAllText(Path.Combine(launcherOnly, "client", "DFLegacy.Launcher.App.exe"), "fixture");
        check(!LauncherLayoutMigration.TryLift(launcherOnly), "a client without game files is left alone");
    }
}
