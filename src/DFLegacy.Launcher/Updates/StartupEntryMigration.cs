using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal static class StartupEntryMigration
{
    internal static bool IsBridge(Core.VerifiedRelease? release) =>
        release is { Tag: "v1.0.12-bridge" }
        && Core.ChapterTransitionSource.IsOfficialChapterTwo(release.Source)
        && release.Manifest.Sequence == 17;

    internal static bool IsInstalledBridge(string root)
    {
        try { return IsBridge(new Core.InstallationStore(root).ReadInstalled()?.Release); }
        catch(InvalidDataException) { return false; }
    }

    internal static bool IsAuthorized(Core.VerifiedRelease previous, Core.VerifiedRelease target)
    {
        if (!IsBridge(previous) || target.Tag != "v1.0.12" || previous.Source != target.Source) return false;
        previous.Validate(target.Source);
        target.Validate(previous.Source);
        var old = previous.Manifest;
        var next = target.Manifest;
        if (!Core.ChapterTwoReleaseProvider.IsStartupMigrationTarget(next) || old.Files.Length != next.Files.Length
            || old.Migrations.Length != 0 || next.Migrations.Length != 0 || !old.Retired.SequenceEqual(next.Retired)
            || !old.Files.Any(file => file.Path == Core.ClientChapterIdentity.FileName)) return false;
        BootstrapLayout.RequireManifest(old);
        BootstrapLayout.RequireManifest(next);

        var previousFiles = old.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        return next.Files.All(file => previousFiles.TryGetValue(file.Path, out var prior)
            && (file.Path == BootstrapLayout.EntryName
                ? file.Sha256 != prior.Sha256
                : file.Sha256 == prior.Sha256 && file.Size == prior.Size));
    }

    internal static Core.InstalledReceipt RequireReady(string root, Core.VerifiedRelease release)
    {
        var store = new Core.InstallationStore(root);
        var installed = store.ReadInstalled();
        if (installed is null || !IsAuthorized(installed.Release, release) || !store.HasValidatedReceipt(installed))
            throw new InvalidDataException("当前安装未通过启动入口迁移的桥接版本校验。");
        BootstrapLayout.RequireReceiptLayout(root, BootstrapLayout.Architecture, installed);
        if (ChapterUpdatePolicy.Validate(root, installed.Release)?.Chapter != Core.ClientChapterIdentity.ChapterTwo)
            throw new InvalidDataException("仅第二章桥接版本支持此次启动入口迁移。");
        BootstrapLayout.RequireStableEntry(root, installed.Release.Manifest);
        return installed;
    }

    internal static Core.PendingStartupEntryMigration? ReadRecovery(string root)
    {
        var pending = Core.InstallTransaction.ReadPendingStartupEntryMigration(root);
        return pending != null && IsAuthorized(pending.Previous, pending.Target) ? pending : null;
    }

    internal static bool IsRecoverySupervisor(string root, string path, string hash, string journalHash)
    {
        if (Path.GetFileName(path) != BootstrapLayout.UpdaterName) return false;
        var directory = Path.GetDirectoryName(path)!;
        var folder = Path.GetFileName(directory);
        const string prefix = "DFLegacy-bootstrap-";
        if (!folder.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(folder[prefix.Length..], "N", out _)
            || !string.Equals(Path.GetDirectoryName(directory), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)) return false;
        var pending = ReadRecovery(root);
        return pending is not null && pending.JournalSha256 == journalHash
            && pending.Previous.Manifest.Files.Single(file => file.Path == BootstrapLayout.UpdaterName).Sha256 == hash;
    }

    internal static Task<FileStream> WaitForBootstrapLockAsync(string root, string name, CancellationToken token)
    {
        var directory = Path.Combine(root, ".dflegacy", "bootstrap");
        Core.PathPolicy.NoLinks(directory);
        Directory.CreateDirectory(directory);
        return WaitForExclusiveFileAsync(Path.Combine(directory, name), FileMode.OpenOrCreate, token);
    }

    internal static async Task WaitForEntryAsync(string root, CancellationToken token)
    {
        var entry = Core.PathPolicy.Resolve(root, BootstrapLayout.EntryName);
        using var handle = await WaitForExclusiveFileAsync(entry, FileMode.Open, token).ConfigureAwait(false);
    }

    private static async Task<FileStream> WaitForExclusiveFileAsync(string path, FileMode mode, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Core.PathPolicy.NoLinks(path);
            try
            {
                return new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None);
            }
            // A running native image or another outstanding handle can block a rename.
            // Never close an unrelated process to replace the startup entry.
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 or 1224)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }
}
