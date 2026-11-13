using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.UpdateCore;

internal sealed record ChapterUpgradeAuthorization(string BaselineManifestSha256, string TargetManifestSha256,
    ReleaseSource TargetSource, byte[] ChapterTwoConfiguration, byte[]? PreviousChapterTwoConfiguration,
    byte[]? PreviousChapterOneConfiguration);

public static partial class ChapterTransition
{
    private const string FirstConfig = "launcher.json";
    private const string SecondConfig = "launcher.chapter2.json";
    private const int RecordLimit = 64 * 1024 * 1024;

    private sealed record Identity(long Size, string Hash);
    private sealed record Checkpoint(int Schema, string Root, InstalledReceipt Baseline, ReleaseSource SecondSource,
        Identity? FirstConfiguration);
    private sealed record RollbackJournal(int Schema, string Root, string BaselineManifestSha256,
        InstalledReceipt Current, Identity? PreviousConfiguration, Identity Configuration);

    private static string Area(InstallationStore store) => Path.Combine(store.Root, ".dflegacy", "chapter-transition");
    private static string Record(InstallationStore store, string name) => Path.Combine(Area(store), name);
    private static string Baseline(InstallationStore store, ManagedFile file) =>
        Path.Combine(Area(store), "baseline", file.Sha256 + "-" + file.Size + ".blob");

    // Preview/prepare is useful to tests and tools. Forward installation must
    // invoke the locked variant and commit these bytes in the same V4 transaction.
    public static byte[] PrepareUpgrade(string root, StagedUpdate staged, ReleaseSource authorizedSource, bool explicitSelection)
    {
        var store = new InstallationStore(root);
        using var lease = InstallationLease.Acquire(store.Root);
        return PrepareUpgradeLocked(store, staged, authorizedSource, explicitSelection).ChapterTwoConfiguration;
    }

    internal static ChapterUpgradeAuthorization PrepareUpgradeLocked(InstallationStore store, StagedUpdate staged,
        ReleaseSource authorizedSource, bool explicitSelection, ChapterDisplayPreference? firstDisplay = null)
    {
        if (!explicitSelection) throw new InvalidDataException("必须主动选择跨章节升级。");
        if (HasPending(store.Root) || InstallTransaction.HasPending(store.Root))
            throw new InvalidDataException("请先恢复未完成的更新。");
        staged.Release.Validate(authorizedSource);
        var current = store.ReadInstalled() ?? throw new InvalidDataException("缺少已校验的第一章桥接安装。");
        var first = ClientChapterIdentity.Read(store.Root, current.Release);
        var target = staged.Release.Manifest;
        var targetFiles = FileMap(target);
        if (!targetFiles.TryGetValue(ClientChapterIdentity.FileName, out var descriptor))
            throw new InvalidDataException("第二章资源缺少章别描述。");
        var cache = new ContentCache(staged.CacheDirectory);
        var descriptorPath = Matches(PathPolicy.Resolve(store.Root, descriptor.Path), Info(descriptor))
            ? PathPolicy.Resolve(store.Root, descriptor.Path) : cache.ObjectPath(descriptor.Sha256, descriptor.Size);
        var second = ClientChapterIdentity.ReadFile(staged.Release, descriptorPath);
        if (first.Chapter != ClientChapterIdentity.ChapterOne || second.Chapter != ClientChapterIdentity.ChapterTwo
            || first.Entry != second.Entry || current.Release.Source.Architecture != authorizedSource.Architecture
            || current.Release.Source == authorizedSource || current.Release.Manifest.Sequence < target.MinimumSequence
            || target.Migrations.Length != 0)
            throw new InvalidDataException("跨章来源、启动入口、桥接版本或配置迁移不兼容。");

        var originals = FileMap(current.Release.Manifest);
        var retirements = target.Retired.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var name in ClientChapterIdentity.FirstChapterModules)
        {
            var path = PathPolicy.Resolve(store.Root, name);
            if (Exists(path) && (!originals.ContainsKey(name) || !retirements.TryGetValue(name, out var retired) || retired.Action != "delete"))
                throw new InvalidDataException("无法确认旧模块归属，无法跨章升级：" + name);
        }

        foreach (var path in current.ManagedHistory)
        {
            if (targetFiles.ContainsKey(path)) continue;
            if (!retirements.TryGetValue(path, out var retired))
                throw new InvalidDataException("第二章清单没有声明旧文件去向：" + path);
            if (Exists(PathPolicy.Resolve(store.Root, path))
                && (retired.Action != "delete" || !originals.ContainsKey(path)))
                throw new InvalidDataException("旧文件没有可核验的退役摘要：" + path);
        }

        using var pins = new DirectoryPin();
        pins.Ensure(Area(store));
        VerifyManaged(store.Root, originals.Values, pins);
        long forwardStage = 0, forwardBackups = 0, largest = 0;
        foreach (var file in target.Files)
        {
            var path = PathPolicy.Resolve(store.Root, file.Path);
            if (Exists(path) && !originals.ContainsKey(file.Path))
                throw new InvalidDataException("第二章资源与非受管理文件冲突：" + file.Path);
            if (Matches(path, Info(file))) continue;
            if (!Matches(cache.ObjectPath(file.Sha256, file.Size), Info(file)))
                throw new InvalidDataException("第二章暂存资源缺失或损坏：" + file.Path);
            forwardStage = checked(forwardStage + file.Size);
            largest = Math.Max(largest, file.Size);
            if (originals.TryGetValue(file.Path, out var old))
            {
                forwardBackups = checked(forwardBackups + old.Size);
                largest = Math.Max(largest, old.Size);
                RequireWritable(path);
            }
        }
        foreach (var old in originals.Values.Where(file => !targetFiles.ContainsKey(file.Path)))
        {
            forwardBackups = checked(forwardBackups + old.Size);
            largest = Math.Max(largest, old.Size);
            RequireWritable(PathPolicy.Resolve(store.Root, old.Path));
        }
        var oldConfig = ReadConfig(store, FirstConfig);
        var priorSecondConfig = ReadConfig(store, SecondConfig);
        if (priorSecondConfig != null) RequireWritable(ConfigurationPath(store, SecondConfig));
        var config = MergeConfiguration(oldConfig, priorSecondConfig, includeDisplay: priorSecondConfig == null, firstDisplay);
        var checkpointPath = Record(store, "checkpoint.json");
        if (Exists(checkpointPath))
        {
            var checkpoint = ReadCheckpoint(store);
            if (checkpoint.Baseline.Release.ManifestSha256 != current.Release.ManifestSha256
                || checkpoint.Baseline.Release.Source != current.Release.Source || checkpoint.SecondSource != authorizedSource)
                throw new InvalidDataException("当前第一章来源与已有跨章备份不一致。");
            VerifyCheckpoint(store, checkpoint, pins);
        }
        else
        {
            var backupBytes = originals.Values.GroupBy(file => (file.Sha256, file.Size))
                .Aggregate(0L, (sum, files) => checked(sum + files.Key.Size));
            CheckSpace(store, checked(backupBytes + (oldConfig?.LongLength ?? 0)));
            pins.Ensure(Record(store, "baseline"));
            foreach (var file in originals.Values)
            {
                var backup = Baseline(store, file);
                if (!Matches(backup, Info(file)))
                    ReplaceFrom(PathPolicy.Resolve(store.Root, file.Path), backup, Info(file));
            }
            if (oldConfig != null) WriteAtomic(Record(store, "first-configuration.json"), oldConfig);
            var checkpoint = new Checkpoint(1, store.Root, current, authorizedSource, Info(oldConfig));
            VerifyCheckpoint(store, checkpoint, pins);
            WriteRecord(checkpointPath, checkpoint);
        }

        CheckSpace(store, InstallTransaction.EstimateRequiredSpace(
            checked(forwardStage + config.LongLength + InstallationStore.MaximumReceiptBytes),
            checked(forwardBackups + (priorSecondConfig?.LongLength ?? 0) + InstallationStore.MaximumReceiptBytes),
            Math.Max(largest, InstallationStore.MaximumReceiptBytes)));
        return new(current.Release.ManifestSha256, staged.Release.ManifestSha256, authorizedSource, config, priorSecondConfig, oldConfig);
    }

    internal static InstalledReceipt RequireCheckpointBinding(InstallationStore store, string baselineSha256, ReleaseSource target)
    {
        var checkpoint = ReadCheckpoint(store);
        if (checkpoint.Baseline.Release.ManifestSha256 != baselineSha256 || checkpoint.SecondSource != target)
            throw new InvalidDataException("跨章事务与第一章检查点或目标来源不匹配。");
        return checkpoint.Baseline;
    }

    public static bool HasPending(string root) => Exists(Record(new InstallationStore(root), "rollback.json"));

    public static bool RequiresExplicitUpgrade(string root) => Exists(Record(new InstallationStore(root), "hold"));

    public static void Rollback(string root, ReleaseSource authorizedSource, IProgress<UpdateProgress>? progress = null)
    {
        var store = new InstallationStore(root);
        using var lease = InstallationLease.Acquire(store.Root);
        if (InstallTransaction.HasPending(store.Root)) throw new InvalidDataException("请先恢复未完成的普通更新。");
        var checkpoint = ReadCheckpoint(store);
        if (checkpoint.SecondSource != authorizedSource) throw new InvalidDataException("第二章更新来源与回退备份不一致。");
        if (RecoverLocked(store, checkpoint, progress)) return;

        var current = store.ReadInstalled() ?? throw new InvalidDataException("缺少第二章安装记录。");
        current.Release.Validate(authorizedSource);
        var descriptor=ClientChapterIdentity.Descriptor(current.Release)
            ??throw new InvalidDataException("第二章安装记录没有章别描述。");
        var originalChapter = ReadBaselineIdentity(store, checkpoint);
        if (Exists(PathPolicy.Resolve(store.Root, descriptor.Path)))
        {
            var chapter = ClientChapterIdentity.Read(store.Root, current.Release);
            if (chapter.Chapter != ClientChapterIdentity.ChapterTwo || chapter.Entry != originalChapter.Entry)
                throw new InvalidDataException("第二章安装与回退备份不匹配。");
        }
        else
        {
            var files = FileMap(current.Release.Manifest);
            ClientChapterIdentity.RequireNoFirstChapterModules(store.Root);
            if (!files.ContainsKey(originalChapter.Entry) || !files.ContainsKey("DNF.exe") ||
                ClientChapterIdentity.FirstChapterModules.Any(files.ContainsKey))
                throw new InvalidDataException("第二章安装与回退备份不匹配。");
        }

        using var pins = new DirectoryPin();
        VerifyCheckpoint(store, checkpoint, pins);
        CheckRollbackDestinations(store, checkpoint, current, pins, recovering: false);
        var previousConfig = ReadConfig(store, FirstConfig);
        if (previousConfig != null) RequireWritable(ConfigurationPath(store, FirstConfig));
        var secondConfig = ReadConfig(store, SecondConfig);
        var restoredConfig = MergeConfiguration(secondConfig,
            previousConfig ?? ReadSavedConfiguration(store, checkpoint), includeDisplay: false);

        // The rollback can add every baseline resource before deleting any C2 file.
        var resourceSpace = checkpoint.Baseline.Release.Manifest.Files.Aggregate(0L, (sum, file) => checked(sum + file.Size));
        CheckSpace(store, checked(resourceSpace + restoredConfig.LongLength * 2 + InstallationStore.MaximumReceiptBytes));
        WriteAtomic(Record(store, "rollback-configuration.json"), restoredConfig);
        var journal = new RollbackJournal(1, store.Root, checkpoint.Baseline.Release.ManifestSha256,
            current, Info(previousConfig), Info(restoredConfig)!);
        WriteRecord(Record(store, "rollback.json"), journal);
        RecoverLocked(store, checkpoint, progress);
    }

    public static bool Recover(string root)
    {
        var store = new InstallationStore(root);
        using var lease = InstallationLease.Acquire(store.Root);
        if (!HasPending(store.Root)) return false;
        if (InstallTransaction.HasPending(store.Root)) throw new InvalidDataException("不能同时恢复两个未完成的更新。");
        return RecoverLocked(store, ReadCheckpoint(store), null);
    }

    private static bool RecoverLocked(InstallationStore store, Checkpoint checkpoint, IProgress<UpdateProgress>? progress)
    {
        if (!HasPending(store.Root)) return false;
        var pendingPath = Record(store, "rollback.json");
        var journal = InstallationStore.ReadJson<RollbackJournal>(pendingPath, RecordLimit);
        if (journal.Schema != 1 || !SameRoot(store, journal.Root) || journal.Current == null
            || journal.BaselineManifestSha256 != checkpoint.Baseline.Release.ManifestSha256 || !ValidIdentity(journal.Configuration)
            || journal.PreviousConfiguration != null && !ValidIdentity(journal.PreviousConfiguration))
            throw new InvalidDataException("跨章回退记录不完整。");
        InstallationStore.Validate(journal.Current);
        journal.Current.Release.Validate(checkpoint.SecondSource);
        var installed = store.ReadInstalled() ?? throw new InvalidDataException("回退期间缺少受验证的安装记录。");
        if (!SameReceipt(installed, journal.Current) && !SameReceipt(installed, checkpoint.Baseline))
            throw new InvalidDataException("回退期间安装记录已改变。");

        using var pins = new DirectoryPin();
        VerifyCheckpoint(store, checkpoint, pins);
        CheckRollbackDestinations(store, checkpoint, journal.Current, pins, recovering: true);
        var configuration = ReadVerified(Record(store, "rollback-configuration.json"), journal.Configuration);
        var configPath = ConfigurationPath(store, FirstConfig);
        if (!Matches(configPath, journal.Configuration) && !Matches(configPath, journal.PreviousConfiguration))
            throw new InvalidDataException("第一章配置在回退期间发生改变。");

        store.InvalidateValidation();
        var baseline = FileMap(checkpoint.Baseline.Release.Manifest);
        var current = FileMap(journal.Current.Release.Manifest);
        var done = 0;
        foreach (var file in baseline.Values)
        {
            var path = PathPolicy.Resolve(store.Root, file.Path);
            if (Matches(path, Info(file))) continue;
            if (current.TryGetValue(file.Path, out var prior))
            {
                if (Exists(path)) DemandMatch(path, Info(prior));
            }
            else DemandMatch(path, null);
            ReplaceFrom(Baseline(store, file), path, Info(file));
            progress?.Report(new("chapter-rollback", file.Path, ++done, baseline.Count + current.Count));
        }
        foreach (var file in current.Values)
        {
            if (baseline.ContainsKey(file.Path)) continue;
            var path = PathPolicy.Resolve(store.Root, file.Path);
            if (!Exists(path)) continue;
            DemandMatch(path, Info(file));
            File.Delete(path);
            progress?.Report(new("chapter-rollback", file.Path, ++done, baseline.Count + current.Count));
        }
        if (!Matches(configPath, journal.Configuration))
        {
            DemandMatch(configPath, journal.PreviousConfiguration);
            WriteAtomic(configPath, configuration);
        }
        VerifyManaged(store.Root, baseline.Values, pins);
        foreach (var path in current.Keys.Where(path => !baseline.ContainsKey(path)))
            DemandMatch(PathPolicy.Resolve(store.Root, path), null);

        WriteAtomic(Record(store, "hold"), "explicit upgrade required"u8.ToArray());
        WriteRecord(store.ReceiptPath, checkpoint.Baseline, InstallationStore.MaximumReceiptBytes);
        store.RecordValidation(checkpoint.Baseline);
        PathPolicy.NoLinks(pendingPath);
        File.Delete(pendingPath);
        return true;
    }

    private static void CheckRollbackDestinations(InstallationStore store, Checkpoint checkpoint, InstalledReceipt current,
        DirectoryPin pins, bool recovering)
    {
        var baselineFiles = FileMap(checkpoint.Baseline.Release.Manifest);
        var currentFiles = FileMap(current.Release.Manifest);
        foreach (var name in baselineFiles.Keys.Union(currentFiles.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var path = PathPolicy.Resolve(store.Root, name);
            baselineFiles.TryGetValue(name, out var old);
            currentFiles.TryGetValue(name, out var currentFile);
            if (Exists(path) && !Matches(path, Info(currentFile)) && (!recovering || !Matches(path, Info(old))))
                throw new InvalidDataException("回退路径已改变或文件归属不明：" + name);
            pins.Ensure(Path.GetDirectoryName(path)!);
            if (Exists(path) && !Matches(path, Info(old))) RequireWritable(path);
        }
        foreach (var path in current.ManagedHistory)
            if (!currentFiles.ContainsKey(path) && !baselineFiles.ContainsKey(path) && Exists(PathPolicy.Resolve(store.Root, path)))
                throw new InvalidDataException("历史受管理文件没有可核验的回退摘要：" + path);
    }

    private static Checkpoint ReadCheckpoint(InstallationStore store)
    {
        var path = Record(store, "checkpoint.json");
        if (!File.Exists(path)) throw new InvalidDataException("没有可用的第一章检查点。");
        var checkpoint = InstallationStore.ReadJson<Checkpoint>(path, RecordLimit);
        if (checkpoint.Schema != 1 || !SameRoot(store, checkpoint.Root) || checkpoint.Baseline == null
            || checkpoint.SecondSource == null || checkpoint.FirstConfiguration != null && !ValidIdentity(checkpoint.FirstConfiguration))
            throw new InvalidDataException("第一章检查点记录无效。");
        InstallationStore.Validate(checkpoint.Baseline);
        checkpoint.SecondSource.Validate();
        if (checkpoint.SecondSource == checkpoint.Baseline.Release.Source
            || checkpoint.SecondSource.Architecture != checkpoint.Baseline.Release.Source.Architecture)
            throw new InvalidDataException("第一章检查点的更新来源无效。");
        return checkpoint;
    }

    private static void VerifyCheckpoint(InstallationStore store, Checkpoint checkpoint, DirectoryPin pins)
    {
        pins.Ensure(Record(store, "baseline"));
        foreach (var file in checkpoint.Baseline.Release.Manifest.Files)
            DemandMatch(Baseline(store, file), Info(file));
        _ = ReadBaselineIdentity(store, checkpoint);
        _ = ReadSavedConfiguration(store, checkpoint);
    }

    private static ClientChapterIdentity ReadBaselineIdentity(InstallationStore store, Checkpoint checkpoint)
    {
        var release = checkpoint.Baseline.Release;
        var descriptor = release.Manifest.Files.SingleOrDefault(file => file.Path == ClientChapterIdentity.FileName)
            ?? throw new InvalidDataException("第一章检查点没有受管理的章别描述。");
        var identity = ClientChapterIdentity.ReadFile(release, Baseline(store, descriptor));
        if (identity.Chapter != ClientChapterIdentity.ChapterOne) throw new InvalidDataException("检查点不是第一章。");
        return identity;
    }

    private static byte[]? ReadSavedConfiguration(InstallationStore store, Checkpoint checkpoint) =>
        checkpoint.FirstConfiguration == null ? null : ReadVerified(Record(store, "first-configuration.json"), checkpoint.FirstConfiguration);

    private static bool SameRoot(InstallationStore store, string root) =>
        string.Equals(store.Root, root, StringComparison.OrdinalIgnoreCase);

    private static bool SameReceipt(InstalledReceipt a, InstalledReceipt b) =>
        a.Release.Source == b.Release.Source && a.Release.ManifestSha256 == b.Release.ManifestSha256
        && a.ManagedHistory.SequenceEqual(b.ManagedHistory, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, ManagedFile> FileMap(ClientManifest manifest) =>
        manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);

    private static void VerifyManaged(string root, IEnumerable<ManagedFile> files, DirectoryPin pins)
    {
        foreach (var file in files)
        {
            var path = PathPolicy.Resolve(root, file.Path);
            pins.Ensure(Path.GetDirectoryName(path)!);
            DemandMatch(path, Info(file));
        }
    }

    private static Identity? Info(ManagedFile? file) => file == null ? null : new(file.Size, file.Sha256);
    private static Identity? Info(byte[]? bytes) => bytes == null ? null : new(bytes.LongLength, Hash(bytes));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool ValidIdentity(Identity? identity) =>
        identity is { Size: > 0 and <= ConfigurationMigration.MaxBytes } && ManifestCodec.IsHash(identity.Hash);
    private static bool Matches(string path, Identity? identity) =>
        identity == null ? !Exists(path) : !Directory.Exists(path) &&
            FileVerifier.MatchesAsync(path, identity.Size, identity.Hash).GetAwaiter().GetResult();
    private static bool Exists(string path)
    {
        PathPolicy.NoLinks(path);
        return File.Exists(path) || Directory.Exists(path);
    }

    private static void DemandMatch(string path, Identity? identity)
    {
        if (!Matches(path, identity)) throw new InvalidDataException("回退或备份文件与摘要不符：" + Path.GetFileName(path));
    }

    private static void RequireWritable(string path)
    {
        PathPolicy.NoLinks(path);
        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void CheckSpace(InstallationStore store, long required)
    {
        if (new DriveInfo(Path.GetPathRoot(store.Root)!).AvailableFreeSpace < required)
            throw new IOException("磁盘空间不足以保存跨章备份和完成恢复。");
    }

    private static byte[] ReadVerified(string path, Identity identity)
    {
        var bytes = InstallationStore.ReadBounded(path, ConfigurationMigration.MaxBytes);
        if (Info(bytes) != identity) throw new InvalidDataException("跨章配置备份校验失败。");
        return bytes;
    }

    private static void WriteRecord<T>(string path, T record, int limit = RecordLimit)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, ManifestCodec.Json);
        if (bytes.Length > limit) throw new InvalidDataException("跨章记录超过大小限制。");
        WriteAtomic(path, bytes);
    }

    private static void WriteAtomic(string path, byte[] bytes) => AtomicFile(path, output => output.Write(bytes));

    private static void ReplaceFrom(string source, string path, Identity? identity) =>
        AtomicFile(path, output =>
        {
            PathPolicy.NoLinks(source);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (identity == null || input.Length != identity.Size
                || Convert.ToHexStringLower(SHA256.HashData(input)) != identity.Hash)
                throw new InvalidDataException("跨章资源在写入前发生改变：" + Path.GetFileName(source));
            input.Position = 0;
            input.CopyTo(output);
        });

    private static void AtomicFile(string path, Action<FileStream> write)
    {
        PathPolicy.NoLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(output);
                output.Flush(true);
            }
            PathPolicy.NoLinks(path);
            File.Move(temporary, path, true);
        }
        finally
        {
            PathPolicy.NoLinks(temporary);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
