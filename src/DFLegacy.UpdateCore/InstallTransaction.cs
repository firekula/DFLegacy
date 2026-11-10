using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.UpdateCore;

public sealed record StagedUpdate(VerifiedRelease Release, string CacheDirectory);

/// <summary>Write-ahead, rollback-only recovery. The receipt is committed after all content postconditions pass.</summary>
public static class InstallTransaction
{
    private sealed record Change(string Path, bool Existed, long OldSize, string? OldHash, long NewSize, string? NewHash);
    private sealed record Journal(int Schema, string Root, string Id, VerifiedRelease Target, Change[] Changes);
    private sealed record PendingWrite(string Path, string? Source, byte[]? Bytes);
    private const string ReceiptName = ".dflegacy/v4/installed.json";
    private const int JournalLimit = 64 * 1024 * 1024;

    public static bool HasPending(string root)
    {
        var path = new InstallationStore(root).JournalPath; PathPolicy.NoLinks(path);
        return File.Exists(path) || Directory.Exists(path);
    }
    public static bool Recover(string root, IProgress<UpdateProgress>? progress = null)
    {
        using var lease = InstallationLease.Acquire(root);
        return RecoverLocked(new InstallationStore(root), progress);
    }
    public static void Apply(string root, StagedUpdate staged, IProgress<UpdateProgress>? progress = null)
    {
        if (staged?.Release?.Source == null) throw new InvalidDataException("更新目标来源缺失。");
        staged.Release.Validate(staged.Release.Source);
        using var lease = InstallationLease.Acquire(root);
        var store = new InstallationStore(root);
        RecoverLocked(store, progress);
        InstalledReceipt? installed;
        try { installed = store.ReadInstalled(); } catch (InvalidDataException) { installed = null; }
        var target = staged.Release.Manifest;
        if (installed != null)
        {
            staged.Release.Validate(installed.Release.Source);
            var old = installed.Release.Manifest;
            if (target.Sequence < old.Sequence || target.Sequence == old.Sequence && staged.Release.ManifestSha256 != installed.Release.ManifestSha256)
                throw new InvalidDataException("更新序号回退或同序号内容发生改变。");
        }
        if ((installed?.Release.Manifest.Sequence ?? 0) < target.MinimumSequence) throw new InvalidDataException("当前基线不受支持，请使用桥接修复包。");
        var history = new HashSet<string>(installed?.ManagedHistory ?? [], StringComparer.OrdinalIgnoreCase);
        var current = target.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retirement = target.Retired.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        if (history.Any(p => !current.Contains(p) && !retirement.ContainsKey(p))) throw new InvalidDataException("历史受管理文件被遗漏，缺少明确退役声明。");
        using var pins = new DirectoryPin(); pins.Ensure(store.StateDirectory);
        var writes = new List<PendingWrite>();
        var cache = new ContentCache(staged.CacheDirectory);
        // Observe current disk contents under the lease; the earlier download plan is not installation evidence.
        foreach (var file in target.Files)
        {
            var destination = PathPolicy.Resolve(store.Root, file.Path); pins.Ensure(Path.GetDirectoryName(destination)!);
            if (Matches(destination, file.Size, file.Sha256)) continue;
            var source = cache.ObjectPath(file.Sha256, file.Size);
            if (!Matches(source, file.Size, file.Sha256)) throw new InvalidDataException("所需缓存缺失或损坏：" + file.Path);
            writes.Add(new(file.Path, source, null));
        }
        foreach (var retired in target.Retired)
        {
            if (!history.Contains(retired.Path)) continue;
            if (retired.Action == "unmanage") { history.Remove(retired.Path); continue; }
            var path = PathPolicy.Resolve(store.Root, retired.Path); pins.Ensure(Path.GetDirectoryName(path)!);
            if (File.Exists(path)) writes.Add(new(retired.Path, null, null));
            else if (Directory.Exists(path)) throw new InvalidDataException("退役路径不是文件：" + retired.Path);
        }
        if (target.Migrations.Length > 0)
        {
            var path = PathPolicy.Resolve(store.Root, "launcher.json", true);
            var original = File.Exists(path) ? InstallationStore.ReadBounded(path, ConfigurationMigration.MaxBytes) : "{}"u8.ToArray();
            var migrated = ConfigurationMigration.Apply(original, target.Migrations);
            if (!original.AsSpan().SequenceEqual(migrated) || !File.Exists(path)) writes.Add(new("launcher.json", null, migrated));
        }
        history.UnionWith(current);
        var receipt = new InstalledReceipt(staged.Release, history.Order(StringComparer.OrdinalIgnoreCase).ToArray()); InstallationStore.Validate(receipt);
        writes.Add(new(ReceiptName, null, JsonSerializer.SerializeToUtf8Bytes(receipt, ManifestCodec.Json)));
        PreflightSpace(store.Root, writes);
        var id = Guid.NewGuid().ToString("N"); var workspace = Path.Combine(store.StateDirectory, "transactions", id); pins.Ensure(workspace);
        var changes = new List<Change>();
        // All originals are copied as actual bytes, even if the previous manifest would reject them.
        for (var index = 0; index < writes.Count; index++)
        {
            var write = writes[index]; var destination = Destination(store, write.Path); pins.Ensure(Path.GetDirectoryName(destination)!);
            var existed = File.Exists(destination);
            if (!existed && Directory.Exists(destination)) throw new InvalidDataException("安装目标不是文件：" + write.Path);
            var oldSize = 0L; string? oldHash = null;
            if (existed)
            {
                var old = Path.Combine(workspace, index + ".old"); CopyFlushed(destination, old);
                (oldSize, oldHash) = Identity(old);
            }
            var newSize = 0L; string? newHash = null;
            if (write.Source != null || write.Bytes != null)
            {
                var fresh = Path.Combine(workspace, index + ".new");
                if (write.Bytes != null) WriteFlushed(fresh, write.Bytes); else CopyFlushed(write.Source!, fresh);
                (newSize, newHash) = Identity(fresh);
                var expected = target.Files.FirstOrDefault(f => f.Path == write.Path);
                if (expected != null && (newSize != expected.Size || newHash != expected.Sha256)) throw new InvalidDataException("暂存文件在准备期间发生改变。");
            }
            changes.Add(new(write.Path, existed, oldSize, oldHash, newSize, newHash));
        }
        var journal = new Journal(4, store.Root, id, staged.Release, changes.ToArray());
        WriteAtomic(store.JournalPath, JsonSerializer.SerializeToUtf8Bytes(journal, ManifestCodec.Json));
        try
        {
            ValidateJournal(store, journal);
            using var evidence = OpenEvidence(workspace, journal);
            progress?.Report(new("prepared", "", 0, changes.Count));
            for (var index = 0; index < changes.Count - 1; index++)
            {
                CommitChange(store, workspace, changes[index], index);
                progress?.Report(new("install", changes[index].Path, index + 1, changes.Count));
            }
            var verification = FileVerifier.VerifyAsync(store.Root, target, progress).GetAwaiter().GetResult();
            if (!verification.IsValid) throw new InvalidDataException("安装后完整校验失败。");
            if (target.Migrations.Length > 0 && !ConfigurationMigration.IsSatisfied(InstallationStore.ReadBounded(Path.Combine(store.Root, "launcher.json"), ConfigurationMigration.MaxBytes), target.Migrations))
                throw new InvalidDataException("安装后配置迁移校验失败。");
            CommitChange(store, workspace, changes[^1], changes.Count - 1);
            if (store.ReadInstalled()?.Release.ManifestSha256 != staged.Release.ManifestSha256) throw new InvalidDataException("安装记录提交后校验失败。");
            store.RecordValidation(receipt);
            progress?.Report(new("commit", "installed.json", changes.Count, changes.Count));
            PathPolicy.NoLinks(store.JournalPath); File.Delete(store.JournalPath);
        }
        catch
        {
            // A failed rollback keeps its journal and evidence. Next startup must retry recovery before launch.
            RecoverLocked(store, progress); throw;
        }
        pins.Dispose(); CleanupWorkspace(store, journal);
    }
    private static bool RecoverLocked(InstallationStore store, IProgress<UpdateProgress>? progress)
    {
        if (!HasPending(store.Root)) { CleanupAbandoned(store); return false; }
        if (Directory.Exists(store.JournalPath)) throw new InvalidDataException("未决事务记录不是文件。");
        var journal = InstallationStore.ReadJson<Journal>(store.JournalPath, JournalLimit);
        ValidateJournal(store, journal);
        store.InvalidateValidation();
        using var pins = new DirectoryPin(); pins.Ensure(store.StateDirectory);
        var workspace = Path.Combine(store.StateDirectory, "transactions", journal.Id); pins.Ensure(workspace);
        foreach (var change in journal.Changes) pins.Ensure(Path.GetDirectoryName(Destination(store, change.Path))!);
        using var evidence = OpenEvidence(workspace, journal);
        // Validate every backup/stage first; never begin a partial rollback with missing recovery data.
        for (var index = journal.Changes.Length - 1; index >= 0; index--)
        {
            var change = journal.Changes[index]; var destination = Destination(store, change.Path);
            if (change.Existed) Replace(Path.Combine(workspace, index + ".old"), destination);
            else if (File.Exists(destination)) File.Delete(destination);
            else if (Directory.Exists(destination)) throw new InvalidDataException("恢复目标不是文件。");
            progress?.Report(new("recover", change.Path, journal.Changes.Length - index, journal.Changes.Length));
        }
        foreach (var change in journal.Changes)
        {
            var destination = Destination(store, change.Path);
            if (change.Existed ? !Matches(destination, change.OldSize, change.OldHash!) : File.Exists(destination) || Directory.Exists(destination))
                throw new InvalidDataException("回滚验证失败；恢复资料已保留。");
        }
        PathPolicy.NoLinks(store.JournalPath); File.Delete(store.JournalPath);
        evidence.Dispose(); pins.Dispose(); CleanupWorkspace(store, journal); return true;
    }
    private static void ValidateJournal(InstallationStore store, Journal j)
    {
        if (j.Schema != 4 || !string.Equals(j.Root, store.Root, StringComparison.OrdinalIgnoreCase) || j.Id is not { Length: 32 } || !Guid.TryParseExact(j.Id, "N", out _)
            || j.Target?.Source == null || j.Changes is not { Length: > 0 and <= 200002 }) throw new InvalidDataException("未决事务身份或结构无效。");
        j.Target.Validate(j.Target.Source);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var target = j.Target.Manifest;
        foreach (var change in j.Changes)
        {
            if (change == null || !paths.Add(change.Path) || change.OldSize < 0 || change.NewSize < 0
                || change.Existed != (change.OldHash != null) || change.OldHash != null && !ManifestCodec.IsHash(change.OldHash)
                || change.NewHash != null && !ManifestCodec.IsHash(change.NewHash) || !change.Existed && change.OldSize != 0 || change.NewHash == null && change.NewSize != 0)
                throw new InvalidDataException("未决事务操作无效。");
            _ = Destination(store, change.Path);
            if (change.Path == ReceiptName) { if (change.NewHash == null) throw new InvalidDataException("事务缺少新安装记录。"); continue; }
            if (change.Path == "launcher.json") { if (target.Migrations.Length == 0 || change.NewHash == null) throw new InvalidDataException("事务配置操作不符。"); continue; }
            var file = target.Files.FirstOrDefault(f => f.Path == change.Path);
            if (change.NewHash != null ? file == null || file.Sha256 != change.NewHash || file.Size != change.NewSize : !target.Retired.Any(r => r.Path == change.Path && r.Action == "delete"))
                throw new InvalidDataException("事务资源与目标清单不符。");
        }
        if (j.Changes[^1].Path != ReceiptName) throw new InvalidDataException("安装记录必须最后提交。");
    }
    private sealed class Evidence : IDisposable
    {
        internal List<FileStream> Streams { get; } = [];
        public void Dispose() { foreach (var stream in Streams) stream.Dispose(); }
    }
    private static Evidence OpenEvidence(string workspace, Journal j)
    {
        var result = new Evidence();
        try
        {
            for (var index = 0; index < j.Changes.Length; index++)
            {
                var change = j.Changes[index];
                foreach (var (suffix, size, hash) in new[] { (".old", change.OldSize, change.OldHash), (".new", change.NewSize, change.NewHash) })
                {
                    if (hash == null) continue;
                    var path = Path.Combine(workspace, index + suffix); PathPolicy.NoLinks(path);
                    FileStream stream;
                    try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new InvalidDataException("恢复资料缺失。", ex); }
                    result.Streams.Add(stream);
                    if (stream.Length != size || Convert.ToHexStringLower(SHA256.HashData(stream)) != hash) throw new InvalidDataException("恢复资料校验失败；事务保留。");
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private static void CommitChange(InstallationStore store, string workspace, Change change, int index)
    {
        var destination = Destination(store, change.Path);
        // Detect writers which do not participate in the lease. Never silently clobber their newer data.
        if (change.Existed ? !Matches(destination, change.OldSize, change.OldHash!) : File.Exists(destination) || Directory.Exists(destination))
            throw new InvalidDataException("安装目标在提交前发生改变：" + change.Path);
        if (change.NewHash == null) File.Delete(destination); else Replace(Path.Combine(workspace, index + ".new"), destination);
    }
    private static string Destination(InstallationStore store, string relative)
    {
        if (relative != ReceiptName) return PathPolicy.Resolve(store.Root, relative, relative == "launcher.json");
        PathPolicy.NoLinks(store.ReceiptPath); return store.ReceiptPath;
    }
    private static bool Matches(string path, long size, string hash) => FileVerifier.MatchesAsync(path, size, hash).GetAwaiter().GetResult();
    private static (long, string) Identity(string path)
    {
        PathPolicy.NoLinks(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }
    private static void CopyFlushed(string source, string target)
    {
        PathPolicy.NoLinks(source); PathPolicy.NoLinks(target);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024);
        input.CopyTo(output); output.Flush(true);
    }
    private static void WriteFlushed(string path, byte[] bytes)
    {
        PathPolicy.NoLinks(path); using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
    private static void Replace(string source, string destination)
    {
        var temporary = destination + ".v4-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { CopyFlushed(source, temporary); PathPolicy.NoLinks(destination); File.Move(temporary, destination, true); }
        finally { PathPolicy.NoLinks(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void WriteAtomic(string destination, byte[] bytes)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteFlushed(temporary, bytes); PathPolicy.NoLinks(destination); File.Move(temporary, destination, true); }
        finally { PathPolicy.NoLinks(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void PreflightSpace(string root, List<PendingWrite> writes)
    {
        long stagedBytes = 0, backupBytes = 0, largest = 0;
        foreach (var write in writes)
        {
            var destination = Path.Combine(root, write.Path.Replace('/', Path.DirectorySeparatorChar)); PathPolicy.NoLinks(destination);
            var backup = File.Exists(destination) ? new FileInfo(destination).Length : 0;
            var staged = write.Source != null ? new FileInfo(write.Source).Length : write.Bytes?.LongLength ?? 0;
            stagedBytes = checked(stagedBytes + staged); backupBytes = checked(backupBytes + backup); largest = Math.Max(largest, Math.Max(staged, backup));
        }
        var needed = EstimateRequiredSpace(stagedBytes, backupBytes, largest);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.AvailableFreeSpace < needed) throw new IOException("磁盘空间不足以保存暂存、备份和同盘替换文件。");
    }
    internal static long EstimateRequiredSpace(long stagedBytes, long backupBytes, long largestTemporary) => checked(1024 * 1024 + stagedBytes + backupBytes + largestTemporary);
    private static void CleanupWorkspace(InstallationStore store, Journal journal)
    {
        // No recursive deletion: only this validated transaction's known backup/stage files.
        if (HasPending(store.Root)) return;
        var parent = Path.GetFullPath(Path.Combine(store.StateDirectory, "transactions"));
        var directory = Path.GetFullPath(Path.Combine(parent, journal.Id));
        if (!Guid.TryParseExact(journal.Id, "N", out _) || Path.GetDirectoryName(directory) != parent) throw new InvalidDataException("事务清理路径无效。");
        using (var pin = new DirectoryPin())
        {
            pin.Ensure(directory);
            for (var index = 0; index < journal.Changes.Length; index++)
                foreach (var suffix in new[] { ".old", ".new" })
                { var path = Path.Combine(directory, index + suffix); PathPolicy.NoLinks(path); if (File.Exists(path)) File.Delete(path); }
        }
        // An unexpected file is kept for inspection, never swept up by a recursive delete.
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) { PathPolicy.NoLinks(directory); Directory.Delete(directory); }
    }
    private static void CleanupAbandoned(InstallationStore store)
    {
        var parent = Path.GetFullPath(Path.Combine(store.StateDirectory, "transactions")); PathPolicy.NoLinks(parent);
        if (!Directory.Exists(parent)) return;
        using var parentPin = new DirectoryPin(); parentPin.Ensure(parent);
        foreach (var directory in Directory.EnumerateDirectories(parent))
        {
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) != parent || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            // Only updater-shaped files are disposable; unexpected files/subdirectories are preserved.
            using (var pin = new DirectoryPin())
            {
                pin.Ensure(directory);
                var files = Directory.EnumerateFileSystemEntries(directory).ToArray();
                if (files.Any(p => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"\A[0-9]+\.(?:old|new)\z") || Directory.Exists(p))) continue;
                foreach (var file in files) PathPolicy.NoLinks(file);
                foreach (var file in files) File.Delete(file);
            }
            PathPolicy.NoLinks(directory); Directory.Delete(directory);
        }
    }
}
