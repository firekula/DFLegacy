using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

internal static class StartupEntryMigrationTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static Core.ReleaseSource Source =>
        new("github", "firekula/DFLegacy", "chapter-two", BootstrapLayout.Architecture, "normal");

    internal static bool Dispatch(string[] args)
    {
        if (args is not ["--startup-entry-migration-cut", var root, var phase]) return false;
        var installed = new Core.InstallationStore(root).ReadInstalled()!;
        var target = ReadTarget(root);
        Core.InstallTransaction.ApplyStartupEntryMigration(root, target, installed.Release.ManifestSha256, new ExitAt(phase));
        throw new InvalidOperationException("Managed migration did not reach its interruption checkpoint.");
    }

    internal static int Probe(string bridgePath, string targetPath)
    {
        var failures = 0;
        void Check(bool condition, string message)
        {
            if (!condition) { Console.Error.WriteLine("FAIL: " + message); failures++; }
            else Console.WriteLine("ok: " + message);
        }
        var bridge = Load(bridgePath);
        var target = Load(targetPath);
        Check(bridge.Tag == "v1.0.13-bridge-r2" && bridge.Manifest.Sequence == 20, "bridge identity is v1.0.13-bridge-r2/20");
        Check(target.Tag == "v1.0.13-r2" && target.Manifest.Sequence == 21, "target identity is v1.0.13-r2/21");
        Check(Core.ChapterTwoReleaseProvider.IsStartupMigrationTarget(target.Manifest),
            "the target is the approved startup migration shape");
        Check(StartupEntryMigration.IsAuthorized(bridge, target),
            "the real bridge and target manifests authorize one entry replacement");
        Check(bridge.Manifest.Files.Length == target.Manifest.Files.Length,
            "both manifests carry the same file count");
        var previous = bridge.Manifest.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        Check(target.Manifest.Files.All(file => previous.TryGetValue(file.Path, out var old)
            && (file.Path == BootstrapLayout.EntryName
                ? file.Sha256 != old.Sha256
                : file.Sha256 == old.Sha256 && file.Size == old.Size)),
            "only the fixed entry differs between the bridge and the target");
        return failures == 0 ? 0 : 1;
    }

    private static Core.VerifiedRelease Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var manifest = Core.ManifestCodec.Parse(bytes);
        return new(Source, manifest.Version, Hash(bytes), bytes);
    }

    internal static async Task Run(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "entry-policy");
        var (baseline, staged) = Fixture(root);
        check(StartupEntryMigration.IsAuthorized(baseline, staged.Release), "the exact official bridge and final releases permit one entry replacement");
        foreach (var wrong in new[]
                 {
                     Modify(staged.Release, m => m with { Sequence = 19 }),
                     Modify(staged.Release, m => m with { Version = "v1.0.13" }),
                     Modify(staged.Release, m => m with { MinimumSequence = 16 }),
                     Modify(staged.Release, m => m with
                     {
                         Files = m.Files.Select(f => f.Path == BootstrapLayout.AppName ? Change(f) : f).ToArray()
                     }),
                     Modify(staged.Release, m => m with
                     {
                         Files = m.Files.Select(f => f.Path == BootstrapLayout.DescriptorName ? Change(f) : f).ToArray()
                     }),
                     Modify(staged.Release, m => m with
                     {
                         Files = m.Files.Select(f => f.Path == BootstrapLayout.EntryName
                             ? baseline.Manifest.Files.Single(old => old.Path == f.Path) : f).ToArray()
                     }),
                 })
            check(!StartupEntryMigration.IsAuthorized(baseline, wrong), "unrelated version or changed files do not authorize a detached entry update");
        check(!StartupEntryMigration.IsAuthorized(Modify(baseline, m => m with { Version = "v1.0.11", Sequence = 16 }), staged.Release),
            "v1.0.11 cannot skip the bridge");
        var local = Source with { Provider = "local", Repository = "http://127.0.0.2:48123/c2" };
        check(!StartupEntryMigration.IsAuthorized(baseline with { Source = local }, staged.Release with { Source = local }),
            "a custom release origin cannot authorize entry replacement");

        using (var session = new V4UpdateSession(root, new LauncherOptions(), new FixtureProvider(staged.Release)))
        {
            var update = await session.CheckAsync(17, CancellationToken.None);
            check(update?.Version == "v1.0.12" && session.RequiresDetachedInstall,
                "V4 gives the exact staged update to the detached helper");
        }
        var arbitrary = Modify(staged.Release, manifest => manifest with { Version = "v1.0.13", Sequence = 19 });
        using (var session = new V4UpdateSession(root, new LauncherOptions(), new FixtureProvider(arbitrary)))
        {
            var update = await session.CheckAsync(17, CancellationToken.None);
            check(update?.Version == "v1.0.13" && !session.RequiresDetachedInstall,
                "V4 keeps the stable-entry guard for other releases");
        }

        var r2Root = Path.Combine(scratch, "entry-policy-r2");
        var (bridgeR2, stagedR2) = Fixture(r2Root, "v1.0.13-bridge-r2", 20, 17, "v1.0.13-r2", 21, 17);
        check(Core.ChapterTwoReleaseProvider.IsStartupMigrationTarget(staged.Release.Manifest)
            && Core.ChapterTwoReleaseProvider.IsStartupMigrationTarget(stagedR2.Release.Manifest),
            "both approved bridge/final pairs are startup migration targets");
        check(StartupEntryMigration.IsAuthorized(bridgeR2, stagedR2.Release),
            "the v1.0.13 bridge and v1.0.13-r2 release permit one entry replacement");
        check(!StartupEntryMigration.IsAuthorized(baseline, stagedR2.Release)
            && !StartupEntryMigration.IsAuthorized(bridgeR2, staged.Release),
            "a bridge cannot migrate to another generation's final release");
        check(!StartupEntryMigration.IsAuthorized(bridgeR2, Modify(stagedR2.Release, m => m with { Sequence = 20 })),
            "a resequenced final release is not the paired migration target");
        check(!StartupEntryMigration.IsAuthorized(bridgeR2, Modify(stagedR2.Release, m => m with
        {
            Files = m.Files.Select(f => f.Path == BootstrapLayout.UpdaterName ? Change(f) : f).ToArray()
        })), "a changed updater is not an entry-only migration");
        using (var session = new V4UpdateSession(r2Root, new LauncherOptions(), new FixtureProvider(stagedR2.Release)))
        {
            var update = await session.CheckAsync(20, CancellationToken.None);
            check(update?.Version == "v1.0.13-r2" && session.RequiresDetachedInstall,
                "V4 gives the paired v1.0.13-r2 update to the detached helper");
        }
        var completedR2 = Path.Combine(scratch, "entry-success-r2");
        var (beforeR2, finalR2) = Fixture(completedR2, "v1.0.13-bridge-r2", 20, 17, "v1.0.13-r2", 21, 17);
        Core.InstallTransaction.ApplyStartupEntryMigration(completedR2, finalR2, beforeR2.ManifestSha256);
        var storeR2 = new Core.InstallationStore(completedR2);
        var receiptR2 = storeR2.ReadInstalled()!;
        check(receiptR2.Release.ManifestSha256 == finalR2.Release.ManifestSha256 && storeR2.HasValidatedReceipt(receiptR2)
            && !Core.InstallTransaction.HasPending(completedR2), "the v1.0.13-r2 entry and receipt commit atomically");

        var originalEntry = File.ReadAllBytes(Path.Combine(root, BootstrapLayout.EntryName));
        File.WriteAllText(Path.Combine(root, "Script.pvf"), "edited PVF");
        try
        {
            Core.InstallTransaction.ApplyStartupEntryMigration(root, staged, baseline.ManifestSha256);
            check(false, "unrelated disk damage cannot be included in the entry transaction");
        }
        catch (InvalidDataException)
        {
            check(File.ReadAllBytes(Path.Combine(root, BootstrapLayout.EntryName)).SequenceEqual(originalEntry)
                && !Core.InstallTransaction.HasPending(root), "unrelated disk damage stops before an entry change or journal");
        }

        foreach (var phase in new[] { "prepared", "install", "commit" })
        {
            var targetRoot = Path.Combine(scratch, "entry-cut-" + phase);
            var (old, target) = Fixture(targetRoot);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "--startup-entry-migration-cut", targetRoot, phase }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start) ?? throw new IOException("Could not start entry-cut helper.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await FileLockOwners.WaitForExitConfirmedAsync(child, deadline.Token);
            check(child.ExitCode == 29 && Core.InstallTransaction.HasPending(targetRoot), $"interruption after {phase} retains the pending journal");
            var pending = StartupEntryMigration.ReadRecovery(targetRoot);
            check(pending?.Previous.ManifestSha256 == old.ManifestSha256 && pending?.Target.ManifestSha256 == target.Release.ManifestSha256,
                $"{phase} recovery validates the backed-up bridge receipt and final release");
            var updater = old.Manifest.Files.Single(file => file.Path == BootstrapLayout.UpdaterName);
            var tempPath = Path.Combine(Path.GetTempPath(), $"DFLegacy-bootstrap-{Guid.NewGuid():N}", updater.Path);
            check(StartupEntryMigration.IsRecoverySupervisor(targetRoot, tempPath, updater.Sha256, pending!.JournalSha256),
                $"{phase} recovery can authorize the verified native temporary updater");
            check(!StartupEntryMigration.IsRecoverySupervisor(targetRoot, tempPath, new string('f', 64), pending.JournalSha256)
                && !StartupEntryMigration.IsRecoverySupervisor(targetRoot, Path.Combine(targetRoot, updater.Path), updater.Sha256, pending.JournalSha256),
                $"{phase} recovery refuses other updater paths or image digests");
            try
            {
                Core.InstallTransaction.RecoverStartupEntryMigration(targetRoot, new string('f', 64));
                check(false, "uncorrelated journal identity cannot authorize recovery");
            }
            catch (InvalidDataException)
            {
                check(Core.InstallTransaction.HasPending(targetRoot), "uncorrelated recovery preserves the original journal");
            }
            Core.InstallTransaction.RecoverStartupEntryMigration(targetRoot, pending.JournalSha256);
            var installed = new Core.InstallationStore(targetRoot).ReadInstalled()!;
            check(!Core.InstallTransaction.HasPending(targetRoot) && installed.Release.ManifestSha256 == old.ManifestSha256
                && (await Core.FileVerifier.VerifyAsync(targetRoot, old.Manifest)).IsValid, $"recovery restores the whole pre-update release after {phase}");
        }

        var completedRoot = Path.Combine(scratch, "entry-success");
        var (before, final) = Fixture(completedRoot);
        Core.InstallTransaction.ApplyStartupEntryMigration(completedRoot, final, before.ManifestSha256);
        var store = new Core.InstallationStore(completedRoot);
        var receipt = store.ReadInstalled()!;
        check(receipt.Release.ManifestSha256 == final.Release.ManifestSha256 && store.HasValidatedReceipt(receipt)
            && !Core.InstallTransaction.HasPending(completedRoot), "entry and final receipt commit atomically and remain validated");

        var entry = Path.Combine(completedRoot, BootstrapLayout.EntryName);
        using var held = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var unlocking = StartupEntryMigration.WaitForEntryAsync(completedRoot, waitLimit.Token);
        check(!unlocking.IsCompleted, "detached helper waits while an existing handle prevents entry replacement");
        held.Dispose();
        await unlocking;
        check(true, "existing handle closes before the helper proceeds");
    }

    private static (Core.VerifiedRelease Baseline, Core.StagedUpdate Target) Fixture(string root,
        string bridgeVersion = "v1.0.12-bridge", long bridgeSequence = 17, long bridgeMinimum = 16,
        string targetVersion = "v1.0.12", long targetSequence = 18, long targetMinimum = 17)
    {
        Directory.CreateDirectory(root);
        var files = new Dictionary<string, byte[]>
        {
            [BootstrapLayout.EntryName] = "published v1.0.11 entry"u8.ToArray(),
            [BootstrapLayout.AppName] = "same managed launcher for both releases"u8.ToArray(),
            [BootstrapLayout.UpdaterName] = "same managed updater for both releases"u8.ToArray(),
            ["DNF.exe"] = "unchanged game"u8.ToArray(),
            ["Script.pvf"] = "unchanged PVF"u8.ToArray()
        };
        files[BootstrapLayout.DescriptorName] = Encoding.ASCII.GetBytes($"[Bootstrap]\nSchemaVersion=1\nArchitecture={BootstrapLayout.Architecture}\nLauncherSha256={Hash(files[BootstrapLayout.AppName])}\nUpdaterSha256={Hash(files[BootstrapLayout.UpdaterName])}\n");
        files[Core.ClientChapterIdentity.FileName] = JsonSerializer.SerializeToUtf8Bytes(
            new Core.ClientChapterIdentity(1, Core.ClientChapterIdentity.ChapterTwo, BootstrapLayout.EntryName,
                [BootstrapLayout.EntryName, "DNF.exe", "Script.pvf"]), Core.ManifestCodec.Json);
        foreach (var (path, bytes) in files) File.WriteAllBytes(Path.Combine(root, path), bytes);
        var bridge = Release(files, bridgeVersion, bridgeSequence, bridgeMinimum);
        var store = new Core.InstallationStore(root);
        Directory.CreateDirectory(store.StateDirectory);
        File.WriteAllBytes(Path.Combine(store.StateDirectory, "installed.json"), JsonSerializer.SerializeToUtf8Bytes(
            new Core.InstalledReceipt(bridge, files.Keys.ToArray()), Core.ManifestCodec.Json));
        files[BootstrapLayout.EntryName] = "verified final entry"u8.ToArray();
        var final = Release(files, targetVersion, targetSequence, targetMinimum);
        File.WriteAllBytes(Path.Combine(root, "fixture-target.json"), final.ManifestBytes);
        var staged = ReadTarget(root);
        var cache = new Core.ContentCache(staged.CacheDirectory);
        foreach (var file in staged.Release.Manifest.Files)
        {
            var content = cache.ObjectPath(file.Sha256, file.Size);
            Directory.CreateDirectory(Path.GetDirectoryName(content)!);
            File.WriteAllBytes(content, files[file.Path]);
        }
        return (bridge, staged);
    }

    private static Core.StagedUpdate ReadTarget(string root)
    {
        var raw = File.ReadAllBytes(Path.Combine(root, "fixture-target.json"));
        var manifest = Core.ManifestCodec.Parse(raw);
        return new(new(Source, manifest.Version, Hash(raw), raw),
            Path.Combine(new Core.InstallationStore(root).StateDirectory, "cache"));
    }

    private static Core.VerifiedRelease Release(Dictionary<string, byte[]> content, string version, long sequence, long minimum)
    {
        var files = content.Select(pair =>
        {
            var hash = Hash(pair.Value);
            return new Core.ManagedFile(pair.Key, pair.Value.Length, hash, [new(hash, pair.Value.Length, version, hash + ".bin")]);
        }).ToArray();
        var manifest = new Core.ClientManifest(4, version, new string('a', 40), Source.Architecture, Source.Flavor,
            Source.Channel, sequence, minimum, files, [], []);
        var bytes = Core.ManifestCodec.Serialize(manifest);
        return new(Source, version, Hash(bytes), bytes);
    }

    private static Core.VerifiedRelease Modify(Core.VerifiedRelease release, Func<Core.ClientManifest, Core.ClientManifest> change)
    {
        var manifest = change(release.Manifest);
        var bytes = Core.ManifestCodec.Serialize(manifest);
        return release with { Tag = manifest.Version, ManifestBytes = bytes, ManifestSha256 = Hash(bytes) };
    }

    private static Core.ManagedFile Change(Core.ManagedFile old)
    {
        var hash = new string('f', 64);
        return old with { Sha256 = hash, Parts = [new(hash, old.Size, "v1.0.12", hash + ".bin")] };
    }

    private sealed class ExitAt(string phase) : IProgress<Core.UpdateProgress>
    {
        public void Report(Core.UpdateProgress value)
        {
            if (value.Phase == phase) Environment.Exit(29);
        }
    }

    private sealed class FixtureProvider(Core.VerifiedRelease release) : Core.IReleaseProvider
    {
        public Core.ReleaseSource Source => release.Source;
        public string Identity => Source.Identity;
        public Task<Core.VerifiedRelease?> FindAsync(CancellationToken token = default) => Task.FromResult<Core.VerifiedRelease?>(release);
        public Task<HttpResponseMessage> OpenAsync(Core.ResourcePart part, long offset, string? etag, CancellationToken token) =>
            throw new InvalidOperationException("Discovery must not download any file.");
    }
}
