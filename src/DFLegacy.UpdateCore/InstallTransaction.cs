using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DFLegacy.UpdateCore;

public sealed record StagedUpdate(VerifiedRelease Release, string CacheDirectory);

/// <summary>Write-ahead, rollback-only recovery. The receipt is committed after all content postconditions pass.</summary>
public static class InstallTransaction
{
    private sealed record Change(string Path, bool Existed, long OldSize, string? OldHash, long NewSize, string? NewHash);
    private sealed record ChapterJournal(string BaselineManifestSha256, string ConfigurationSha256, string? PreviousConfigurationSha256);
    private sealed record Journal(int Schema, string Root, string Id, VerifiedRelease Target, Change[] Changes,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ChapterJournal? Chapter = null);
    private sealed record PendingWrite(string Path, string? Source, byte[]? Bytes);
    private const string ReceiptName = ".dflegacy/v4/installed.json";
    private const string FirstConfiguration = "launcher.json";
    private const string SecondConfiguration = "launcher.chapter2.json";
    private const int JournalLimit = 64 * 1024 * 1024;

    public static bool HasPending(string root)
    {
        var path = new InstallationStore(root).JournalPath; PathPolicy.NoLinks(path);
        return File.Exists(path) || Directory.Exists(path);
    }
    public static bool Recover(string root, IProgress<UpdateProgress>? progress = null)
    {
        using var lease = InstallationLease.Acquire(root);
        RequireNoChapterRollback(root);
        return RecoverLocked(new InstallationStore(root), progress);
    }
    public static void Apply(string root, StagedUpdate staged, IProgress<UpdateProgress>? progress = null) =>
        ApplyCore(root, staged, null, false, progress);

    public static void ApplyChapterUpgrade(string root, StagedUpdate staged, ReleaseSource authorizedSource,
        bool explicitSelection, IProgress<UpdateProgress>? progress = null, ChapterDisplayPreference? firstDisplay = null,
        bool deferReadiness = false)
    {
        if (!explicitSelection || authorizedSource == null)
            throw new InvalidDataException("必须主动选择跨章节升级来源。");
        ApplyCore(root, staged, authorizedSource, true, progress, firstDisplay, deferReadiness);
    }
    public static void ValidateChapterReadyProbe(string root, string targetManifestSha256)
    {
        using var lease = InstallationLease.Acquire(root);
        _ = ReadCommittedChapterJournal(new InstallationStore(root), targetManifestSha256, verifyResources: true);
    }

    public static void ConfirmChapterReady(string root, string targetManifestSha256)
    {
        using var lease = InstallationLease.Acquire(root);
        var store = new InstallationStore(root);
        var journal = ReadCommittedChapterJournal(store, targetManifestSha256, verifyResources: false);
        File.Delete(store.JournalPath);
        try { CleanupWorkspace(store, journal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The readiness commit is complete. A later transaction can clean
            // the abandoned workspace once the transient file owner releases it.
        }
    }

    private static Journal ReadCommittedChapterJournal(InstallationStore store, string targetManifestSha256, bool verifyResources)
    {
        RequireNoChapterRollback(store.Root);
        if (!HasPending(store.Root)) throw new InvalidDataException("没有待确认的跨章安装。");
        var journal = InstallationStore.ReadJson<Journal>(store.JournalPath, JournalLimit);
        ValidateJournal(store, journal);
        if (journal.Chapter == null || journal.Target.ManifestSha256 != targetManifestSha256)
            throw new InvalidDataException("跨章就绪确认与当前事务不匹配。");
        var receipt = store.ReadInstalled();
        if (receipt?.Release.ManifestSha256 != targetManifestSha256 || receipt.Release.Source != journal.Target.Source)
            throw new InvalidDataException("跨章安装尚未提交。");
        // Full verification below hashes every managed file once. Check the
        // config, receipt and retired paths here because they are outside that manifest.
        foreach (var change in journal.Changes)
            if ((change.Path == ReceiptName || change.Path == SecondConfiguration || verifyResources && change.NewHash == null)
                && !MatchesState(Destination(store, change.Path), change.NewSize, change.NewHash))
                throw new InvalidDataException("待确认的跨章安装文件已改变：" + change.Path);
        var identity = ClientChapterIdentity.Read(store.Root, journal.Target);
        if (identity.Chapter != ClientChapterIdentity.ChapterTwo
            || verifyResources && !FileVerifier.VerifyAsync(store.Root, journal.Target.Manifest).GetAwaiter().GetResult().IsValid)
            throw new InvalidDataException("待确认的第二章未通过完整校验。");
        return journal;
    }

    private static void ApplyCore(string root, StagedUpdate staged, ReleaseSource? authorizedSource,
        bool explicitSelection, IProgress<UpdateProgress>? progress, ChapterDisplayPreference? firstDisplay = null, bool deferReadiness = false)
    {
        if (staged?.Release?.Source == null) throw new InvalidDataException("更新目标来源缺失。");
        staged.Release.Validate(staged.Release.Source);
        using var lease = InstallationLease.Acquire(root);
        var store = new InstallationStore(root);
        RequireNoChapterRollback(root);
        RecoverLocked(store, progress);
        InstalledReceipt? installed;
        try { installed = store.ReadInstalled(); }
        catch (InvalidDataException) when (authorizedSource == null && ClientChapterIdentity.Descriptor(staged.Release) == null
            && !File.Exists(PathPolicy.Resolve(store.Root, ClientChapterIdentity.FileName))) { installed = null; }
        var chapter = authorizedSource == null ? null : ChapterTransition.PrepareUpgradeLocked(store, staged, authorizedSource, explicitSelection, firstDisplay, progress);
        var target = staged.Release.Manifest;
        if (installed != null && chapter == null)
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
        var identity = ClientChapterIdentity.ReadStaged(store.Root, staged, cache);
        if (chapter == null) RequireSameChapter(store, installed, identity, staged.Release);
        if (identity?.Chapter == ClientChapterIdentity.ChapterTwo)
        {
            if (target.Migrations.Length != 0) throw new InvalidDataException("第二章资源不能修改第一章配置。");
            if (chapter == null) ClientChapterIdentity.RequireNoFirstChapterModules(store.Root);
        }
        var chapterBaselineFiles = chapter == null ? null :
            installed!.Release.Manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var chapterTwoPriorFiles = chapter == null && identity?.Chapter == ClientChapterIdentity.ChapterTwo
            ? installed?.Release.Manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase) : null;
        var targetFiles = target.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        // Observe current disk contents under the lease; the earlier download plan is not installation evidence.
        foreach (var file in target.Files)
        {
            var destination = PathPolicy.Resolve(store.Root, file.Path); pins.Ensure(Path.GetDirectoryName(destination)!);
            if (chapterTwoPriorFiles != null && !chapterTwoPriorFiles.ContainsKey(file.Path)
                && (File.Exists(destination) || Directory.Exists(destination)))
                throw new InvalidDataException("第二章资源与非受管理文件冲突：" + file.Path);
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
            if (File.Exists(path))
            {
                if (chapterTwoPriorFiles != null && (!chapterTwoPriorFiles.TryGetValue(retired.Path, out var owned)
                    || !Matches(path, owned.Size, owned.Sha256)))
                    throw new InvalidDataException("退役文件没有当前版本的可验证归属：" + retired.Path);
                writes.Add(new(retired.Path, null, null));
            }
            else if (Directory.Exists(path)) throw new InvalidDataException("退役路径不是文件：" + retired.Path);
        }
        if (target.Migrations.Length > 0)
        {
            var path = PathPolicy.Resolve(store.Root, "launcher.json", true);
            var original = File.Exists(path) ? InstallationStore.ReadBounded(path, ConfigurationMigration.MaxBytes) : "{}"u8.ToArray();
            var migrated = ConfigurationMigration.Apply(original, target.Migrations);
            if (!original.AsSpan().SequenceEqual(migrated) || !File.Exists(path)) writes.Add(new("launcher.json", null, migrated));
        }
        if (chapter != null) writes.Add(new(SecondConfiguration, null, chapter.ChapterTwoConfiguration));
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
                progress?.Report(new("backup", write.Path, index, writes.Count));
                var old = Path.Combine(workspace, index + ".old"); CopyFlushed(destination, old);
                (oldSize, oldHash) = Identity(old);
            }
            progress?.Report(new("stage", write.Path, index, writes.Count));
            if (chapter != null && write.Path != ReceiptName)
            {
                if (write.Path == SecondConfiguration)
                {
                    var original = chapter.PreviousChapterTwoConfiguration;
                    if (existed != (original != null) || original != null && (oldSize != original.LongLength || oldHash != Hash(original)))
                        throw new InvalidDataException("第二章配置在准备期间发生改变。");
                }
                else if (chapterBaselineFiles!.TryGetValue(write.Path, out var baseline))
                {
                    if (!existed || oldSize != baseline.Size || oldHash != baseline.Sha256)
                        throw new InvalidDataException("第一章资源在准备期间发生改变：" + write.Path);
                }
                else if (existed) throw new InvalidDataException("第二章资源与非受管理文件冲突：" + write.Path);
            }
            if (chapterTwoPriorFiles != null && write.Source == null && write.Bytes == null
                && (!existed || !chapterTwoPriorFiles.TryGetValue(write.Path, out var prior) || oldSize != prior.Size || oldHash != prior.Sha256))
                throw new InvalidDataException("第二章退役文件在提交前发生改变：" + write.Path);
            if (chapterTwoPriorFiles != null && existed && write.Source != null && !chapterTwoPriorFiles.ContainsKey(write.Path))
                throw new InvalidDataException("第二章资源与非受管理文件冲突：" + write.Path);
            var newSize = 0L; string? newHash = null;
            if (write.Source != null || write.Bytes != null)
            {
                var fresh = Path.Combine(workspace, index + ".new");
                if (write.Bytes != null) WriteFlushed(fresh, write.Bytes); else CopyFlushed(write.Source!, fresh);
                (newSize, newHash) = Identity(fresh);
                if (targetFiles.TryGetValue(write.Path, out var expected) && (newSize != expected.Size || newHash != expected.Sha256))
                    throw new InvalidDataException("暂存文件在准备期间发生改变。");
            }
            changes.Add(new(write.Path, existed, oldSize, oldHash, newSize, newHash));
        }
        var journal = new Journal(4, store.Root, id, staged.Release, changes.ToArray(), chapter == null ? null :
            new(chapter.BaselineManifestSha256, Hash(chapter.ChapterTwoConfiguration),
                chapter.PreviousChapterTwoConfiguration == null ? null : Hash(chapter.PreviousChapterTwoConfiguration)));
        var journalBytes = JsonSerializer.SerializeToUtf8Bytes(journal, ManifestCodec.Json);
        if (journalBytes.Length > JournalLimit) throw new InvalidDataException("未决事务记录超过大小限制。");
        ValidateJournal(store, journal);
        WriteAtomic(store.JournalPath, journalBytes);
        try
        {
            using var evidence = OpenEvidence(workspace, journal);
            progress?.Report(new("prepared", "", 0, changes.Count));
            for (var index = 0; index < changes.Count - 1; index++)
            {
                if (chapter != null && changes[index].Path == SecondConfiguration)
                    RequireUnchangedFirstConfiguration(store, chapter);
                progress?.Report(new("replace", changes[index].Path, index, changes.Count));
                CommitChange(store, workspace, changes[index], index);
                progress?.Report(new("install", changes[index].Path, index + 1, changes.Count));
            }
            var verification = FileVerifier.VerifyAsync(store.Root, target, progress).GetAwaiter().GetResult();
            if (!verification.IsValid) throw new InvalidDataException("安装后完整校验失败。");
            if (identity != null) _ = ClientChapterIdentity.Read(store.Root, staged.Release);
            if (chapter != null)
            {
                RequireUnchangedFirstConfiguration(store, chapter);
                if (!Matches(Destination(store, SecondConfiguration), chapter.ChapterTwoConfiguration.LongLength, Hash(chapter.ChapterTwoConfiguration)))
                    throw new InvalidDataException("第二章迁移配置在提交前发生改变。");
            }
            if (target.Migrations.Length > 0 && !ConfigurationMigration.IsSatisfied(InstallationStore.ReadBounded(Path.Combine(store.Root, "launcher.json"), ConfigurationMigration.MaxBytes), target.Migrations))
                throw new InvalidDataException("安装后配置迁移校验失败。");
            CommitChange(store, workspace, changes[^1], changes.Count - 1);
            if (store.ReadInstalled()?.Release.ManifestSha256 != staged.Release.ManifestSha256) throw new InvalidDataException("安装记录提交后校验失败。");
            store.RecordValidation(receipt);
            progress?.Report(new("commit", "installed.json", changes.Count, changes.Count));
            if (!deferReadiness)
            {
                PathPolicy.NoLinks(store.JournalPath);
                File.Delete(store.JournalPath);
            }
        }
        catch
        {
            // A failed rollback keeps its journal and evidence. Next startup must retry recovery before launch.
            pins.Dispose();
            RecoverLocked(store, progress); throw;
        }
        pins.Dispose();
        if (!deferReadiness) CleanupWorkspace(store, journal);
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
        if (journal.Chapter != null)
        {
            ValidateChapterReceipts(store, workspace, journal);
            foreach (var change in journal.Changes)
            {
                var destination = Destination(store, change.Path);
                if (!MatchesState(destination, change.OldSize, change.OldHash)
                    && !MatchesState(destination, change.NewSize, change.NewHash))
                    throw new InvalidDataException("跨章恢复目标发生外部改变：" + change.Path);
            }
        }
        for (var index = journal.Changes.Length - 1; index >= 0; index--)
        {
            var change = journal.Changes[index]; var destination = Destination(store, change.Path);
            if (journal.Chapter != null && MatchesState(destination, change.OldSize, change.OldHash)) continue;
            progress?.Report(new("restore", change.Path, journal.Changes.Length - index - 1, journal.Changes.Length));
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
        var files = target.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var retired = target.Retired.Where(file => file.Action == "delete").Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ManagedFile>? baselineFiles = null;
        if (j.Chapter != null)
        {
            if (!ManifestCodec.IsHash(j.Chapter.BaselineManifestSha256) || !ManifestCodec.IsHash(j.Chapter.ConfigurationSha256)
                || j.Chapter.PreviousConfigurationSha256 != null && !ManifestCodec.IsHash(j.Chapter.PreviousConfigurationSha256)
                || target.Migrations.Length != 0 || ClientChapterIdentity.Descriptor(j.Target) == null)
                throw new InvalidDataException("跨章未决事务记录无效。");
            var baseline = ChapterTransition.RequireCheckpointBinding(store, j.Chapter.BaselineManifestSha256, j.Target.Source);
            baselineFiles = baseline.Release.Manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        }
        foreach (var change in j.Changes)
        {
            if (change == null || !paths.Add(change.Path) || change.OldSize < 0 || change.NewSize < 0
                || change.Existed != (change.OldHash != null) || change.OldHash != null && !ManifestCodec.IsHash(change.OldHash)
                || change.NewHash != null && !ManifestCodec.IsHash(change.NewHash) || !change.Existed && change.OldSize != 0 || change.NewHash == null && change.NewSize != 0)
                throw new InvalidDataException("未决事务操作无效。");
            _ = Destination(store, change.Path);
            if (change.Path == ReceiptName) { if (change.NewHash == null) throw new InvalidDataException("事务缺少新安装记录。"); continue; }
            if (change.Path == FirstConfiguration)
            {
                if (target.Migrations.Length == 0 || change.NewHash == null) throw new InvalidDataException("事务配置操作不符。");
                continue;
            }
            if (change.Path == SecondConfiguration)
            {
                if (j.Chapter == null || change.NewHash != j.Chapter.ConfigurationSha256 || change.OldHash != j.Chapter.PreviousConfigurationSha256
                    || change.NewSize is <= 0 or > ConfigurationMigration.MaxBytes || change.OldSize > ConfigurationMigration.MaxBytes)
                    throw new InvalidDataException("跨章事务配置操作不符。");
                continue;
            }
            files.TryGetValue(change.Path, out var file);
            if (change.NewHash != null ? file == null || file.Path != change.Path || file.Sha256 != change.NewHash || file.Size != change.NewSize
                : !retired.Contains(change.Path))
                throw new InvalidDataException("事务资源与目标清单不符。");
            if (baselineFiles != null)
            {
                baselineFiles.TryGetValue(change.Path, out var prior);
                if (prior == null ? change.Existed : !change.Existed || prior.Sha256 != change.OldHash || prior.Size != change.OldSize)
                    throw new InvalidDataException("跨章事务原资源与第一章检查点不符。");
            }
        }
        if (j.Changes[^1].Path != ReceiptName) throw new InvalidDataException("安装记录必须最后提交。");
        if (j.Chapter != null && !paths.Contains(SecondConfiguration))
            throw new InvalidDataException("跨章事务缺少第二章配置写入。");
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
    private static void ValidateChapterReceipts(InstallationStore store, string workspace, Journal journal)
    {
        var baseline = ChapterTransition.RequireCheckpointBinding(store, journal.Chapter!.BaselineManifestSha256, journal.Target.Source);
        var index = journal.Changes.Length - 1;
        if (!journal.Changes[index].Existed) throw new InvalidDataException("跨章事务缺少原安装记录。");
        var original = InstallationStore.ReadJson<InstalledReceipt>(Path.Combine(workspace, index + ".old"), InstallationStore.MaximumReceiptBytes);
        var target = InstallationStore.ReadJson<InstalledReceipt>(Path.Combine(workspace, index + ".new"), InstallationStore.MaximumReceiptBytes);
        InstallationStore.Validate(original);
        InstallationStore.Validate(target);
        if (original.Release.Source != baseline.Release.Source || original.Release.ManifestSha256 != baseline.Release.ManifestSha256
            || !original.ManagedHistory.SequenceEqual(baseline.ManagedHistory, StringComparer.OrdinalIgnoreCase)
            || target.Release.Source != journal.Target.Source || target.Release.ManifestSha256 != journal.Target.ManifestSha256)
            throw new InvalidDataException("跨章恢复安装记录与来源证据不一致。");
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
        if (relative is not (ReceiptName or SecondConfiguration))
            return PathPolicy.Resolve(store.Root, relative, relative == FirstConfiguration);
        var path = relative == ReceiptName ? store.ReceiptPath : Path.Combine(store.Root, SecondConfiguration);
        PathPolicy.NoLinks(path);
        return path;
    }
    private static void RequireNoChapterRollback(string root)
    {
        if (ChapterTransition.HasPending(root)) throw new InvalidDataException("请先恢复未完成的跨章回退。");
    }
    private static void RequireSameChapter(InstallationStore store, InstalledReceipt? installed, ClientChapterIdentity? target, VerifiedRelease release)
    {
        var prior = installed == null ? null : ClientChapterIdentity.Descriptor(installed.Release);
        ClientChapterIdentity? current = null;
        if (prior != null)
            current = prior.Sha256 == ClientChapterIdentity.Descriptor(release)?.Sha256
                ? target : ClientChapterIdentity.Read(store.Root, installed!.Release);
        if (current?.Chapter == ClientChapterIdentity.ChapterTwo && target == null
            || (current?.Chapter ?? ClientChapterIdentity.ChapterOne) != (target?.Chapter ?? ClientChapterIdentity.ChapterOne)
            || current != null && target != null && current.Entry != target.Entry)
            throw new InvalidDataException("跨章切换必须使用明确选择的升级或回退操作。");
    }
    private static void RequireUnchangedFirstConfiguration(InstallationStore store, ChapterUpgradeAuthorization chapter)
    {
        var path = Destination(store, FirstConfiguration);
        var original = chapter.PreviousChapterOneConfiguration;
        if (!MatchesState(path, original?.LongLength ?? 0, original == null ? null : Hash(original)))
            throw new InvalidDataException("第一章配置在迁移期间发生改变。");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool MatchesState(string path, long size, string? hash) =>
        hash == null ? !File.Exists(path) && !Directory.Exists(path) : !Directory.Exists(path) && Matches(path, size, hash);
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
