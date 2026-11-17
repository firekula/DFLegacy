using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Launcher.Updates;

internal static class FullStarterMigrationTests
{
    internal static async Task Run(string scratch, Action<bool, string> check)
    {
        var old = Path.Combine(scratch, "starter-old");
        Directory.CreateDirectory(old);
        var oldConfig = Path.Combine(old, "launcher.chapter2.json");
        var options = """{"Accounts":[{"Account":"test001","Password":"test-only"}],"ChapterTwoDisplay":{"Width":800},"FutureSetting":[7]}""";
        File.WriteAllText(oldConfig, options);
        File.WriteAllText(Path.Combine(old, "Config.ini"), "local video options");
        var oldNative = Path.Combine(old, ".dflegacy", "chapter-two", "settings", "DNF_CHINA.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(oldNative)!);
        File.WriteAllText(oldNative, "[DNF COMMON]\r\nCONFIG VERSION=1\r\nKEYBOARD=A\r\n");
        using var zip = Archive("DFLegacy.Launcher.exe", "new entry");
        var pinned = Hash(zip);

        var damaged = await Rejected(async () =>
        {
            zip.Position = 0;
            await FullStarterMigration.ImportAsync(zip, Path.Combine(scratch, "damaged"), zip.Length, new string('0', 64));
        });
        check(damaged && !Directory.Exists(Path.Combine(scratch, "damaged", "client")),
            "an incorrect full archive hash leaves no staged installation");
        using var unsafeZip = Archive("../outside.exe", "unexpected");
        check(await Rejected(() => FullStarterMigration.ImportAsync(unsafeZip, Path.Combine(scratch, "unsafe"),
            unsafeZip.Length, Hash(unsafeZip))) && !File.Exists(Path.Combine(scratch, "outside.exe")),
            "a ZIP member cannot escape the staged installation");

        zip.Position = 0;
        var workspace = FullStarterMigration.CreateAttempt(old);
        var next = await FullStarterMigration.ImportAsync(zip, workspace, zip.Length, pinned);
        File.WriteAllText(Path.Combine(next, "launcher.chapter2.json"), "{}");
        FullStarterMigration.CopySettings(old, next);
        check(File.ReadAllText(Path.Combine(next, "DFLegacy.Launcher.exe")) == "new entry",
            "verified archive staged the new entry");
        check(File.ReadAllText(Path.Combine(next, "launcher.chapter2.json")) == options
            && File.ReadAllText(Path.Combine(next, "Config.ini")) == "local video options",
            "the new installation retains saved accounts, settings and unknown fields");
        var newNative = Path.Combine(next, ".dflegacy", "chapter-two", "settings", "DNF_CHINA.cfg");
        check(File.Exists(newNative) && File.ReadAllBytes(newNative).SequenceEqual(File.ReadAllBytes(oldNative)),
            "the game key and display configuration survives the move to the new installation");

        var refused = await Rejected(() => FullStarterMigration.ActivateAsync(old, next,
            (_, _) => throw new InvalidDataException("new launcher readiness failed")));
        check(refused && FullStarterMigration.ReadActive(old) is null && File.ReadAllText(oldConfig) == options,
            "failed readiness keeps the original path and configuration");
        check(await Rejected(() => FullStarterMigration.ActivateAsync(old, next, (_, _) => Task.CompletedTask))
            && FullStarterMigration.ReadActive(old) is null,
            "a launcher returning without readiness cannot activate the new installation");
        await FullStarterMigration.ActivateAsync(old, next, (ready, _) =>
        {
            check(FullStarterMigration.ReadActive(old) is null, "the destination cannot become active before readiness");
            ready();
            return Task.CompletedTask;
        });
        check(FullStarterMigration.ReadActive(old) == next, "a confirmed destination survives a later launch");
        FullStarterMigration.Deactivate(old);
        check(FullStarterMigration.ReadActive(old) is null && File.ReadAllText(oldConfig) == options,
            "rollback removes only the activation, retaining the original settings");

        var active = Path.Combine(old, ".dflegacy", "c2-upgrade", "active.json");
        File.WriteAllText(active, JsonSerializer.Serialize("../foreign"));
        check(await Rejected(() => Task.FromResult(FullStarterMigration.ReadActive(old))),
            "a replaced activation record cannot resolve an arbitrary path");
    }

    private static MemoryStream Archive(string entry, string value)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var output = new StreamWriter(zip.CreateEntry(entry).Open()))
            output.Write(value);
        stream.Position = 0;
        return stream;
    }

    private static string Hash(MemoryStream stream) => Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));

    private static async Task<bool> Rejected(Func<Task> operation)
    {
        try { await operation(); return false; }
        catch (InvalidDataException) { return true; }
        catch (IOException) { return true; }
    }
}
