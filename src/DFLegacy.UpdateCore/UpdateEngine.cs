namespace DFLegacy.UpdateCore;

public sealed record LaunchValidation(bool CanLaunch, string Version, string Message, VerificationResult? Verification = null);
public sealed record UpdateCheck(VerifiedRelease? Target, InstalledReceipt? Installed, VerificationResult? Verification,
    bool NeedsInstall, bool CanLaunch, bool IsOffline, string Message);

/// <summary>Discovery never replaces the receipt of the last committed installation.</summary>
public sealed class UpdateEngine
{
    private readonly string root;
    private readonly IReleaseProvider provider;
    private readonly InstallationStore store;
    public UpdateEngine(string root, IReleaseProvider provider)
    {
        this.root = Path.GetFullPath(root); this.provider = provider; provider.Source.Validate(); store = new(this.root);
    }
    public async Task<UpdateCheck> CheckAsync(IProgress<UpdateProgress>? progress = null, CancellationToken token = default, bool forceVerification = false)
    {
        RequireNoPending();
        VerifiedRelease? target = null; Exception? unavailable = null;
        try { target = await provider.FindAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        { unavailable = ex; }
        using var lease = InstallationLease.Acquire(root);
        RequireNoPending();
        InstalledReceipt? installed;
        try { installed = store.ReadInstalled(); }
        catch (InvalidDataException) when (target != null) { installed = null; }
        if (installed != null) installed.Release.Validate(provider.Source);
        if (target == null)
        {
            if (installed == null)
            {
                if (unavailable != null) throw new IOException("无法连接更新源，也没有可完整校验的可信安装记录。", unavailable);
                return new(null, null, null, false, false, false, "当前源尚未提供 V4 清单。");
            }
            var local = await VerifyInstalled(installed, progress, token, forceVerification).ConfigureAwait(false);
            return new(null, installed, local.Verification, false, local.CanLaunch, true,
                local.CanLaunch ? $"文件符合缓存版本 {local.Version}，未确认线上最新版本。" : local.Message);
        }
        target.Validate(provider.Source); var manifest = target.Manifest;
        if (installed != null)
        {
            var previous = installed.Release.Manifest;
            if (manifest.Sequence < previous.Sequence)
            {
                // A standalone distribution can precede its online publication.
                // Verify its own receipt, and never expose the older release as an install target.
                var local = await VerifyInstalled(installed, progress, token, true).ConfigureAwait(false);
                return new(null, installed, local.Verification, false, local.CanLaunch, false,
                    local.CanLaunch ? $"更新源版本 {manifest.Version} 早于本地版本 {local.Version}；已校验并保留本地版本，继续等待后续更新。" : local.Message);
            }
            if (manifest.Sequence == previous.Sequence && target.ManifestSha256 != installed.Release.ManifestSha256)
                throw new InvalidDataException("线上清单的同序号内容被替换，已停止更新。");
            if (previous.Sequence < manifest.MinimumSequence) throw new InvalidDataException("当前版本不在支持的升级范围，请先安装桥接包。");
            var targets = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var retired = new HashSet<string>(manifest.Retired.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            if (installed.ManagedHistory.Any(path => !targets.Contains(path) && !retired.Contains(path)))
                throw new InvalidDataException("新清单遗漏了旧受管理文件，且没有声明删除或解除管理。");
        }
        else if (manifest.MinimumSequence != 0) throw new InvalidDataException("此更新需要已验证的旧版本，请先安装桥接包。");
        if (installed != null && installed.Release.ManifestSha256 == target.ManifestSha256)
        {
            var local = await VerifyInstalled(installed, progress, token, forceVerification).ConfigureAwait(false);
            return new(target, installed, local.Verification, !local.CanLaunch, local.CanLaunch, false,
                local.CanLaunch ? $"已确认当前版本 {manifest.Version}；使用已完成校验的安装记录。" : local.Message);
        }
        var verification = await FileVerifier.VerifyAsync(root, manifest, progress, token).ConfigureAwait(false);
        var configReady = ConfigurationReady(manifest);
        var needsInstall = installed == null || installed.Release.ManifestSha256 != target.ManifestSha256 || !verification.IsValid || !configReady;
        var canLaunch = installed != null && (await VerifyInstalled(installed, progress, token).ConfigureAwait(false)).CanLaunch;
        return new(target, installed, verification, needsInstall, canLaunch, false,
            needsInstall ? $"已确认目标 {manifest.Version}；需要修复 {verification.Problems.Count} 个文件并核验配置。" : $"已完整校验 {manifest.Files.Length} 个文件，当前版本 {manifest.Version}。");
    }
    public async Task<UpdateCheck> CheckChapterUpgradeAsync(bool explicitSelection, IProgress<UpdateProgress>? progress = null,
        CancellationToken token = default)
    {
        if (!explicitSelection) throw new InvalidDataException("必须主动选择跨章节升级。");
        RequireNoPending();
        _ = ReadChapterUpgradeBaseline();
        var target = await provider.FindAsync(token).ConfigureAwait(false);
        using var lease = InstallationLease.Acquire(root);
        RequireNoPending();
        var installed = ReadChapterUpgradeBaseline();
        var baseline = await VerifyInstalled(installed, progress, token, true).ConfigureAwait(false);
        if (!baseline.CanLaunch) throw new InvalidDataException("第一章桥接安装未通过完整校验。");
        if (target == null) return new(null, installed, baseline.Verification, false, true, false, "所选来源未提供跨章更新。");
        target.Validate(provider.Source);
        var manifest = target.Manifest;
        if (ClientChapterIdentity.Descriptor(target) == null || installed.Release.Manifest.Sequence < manifest.MinimumSequence
            || manifest.Migrations.Length != 0)
            throw new InvalidDataException("所选来源缺少可用的跨章更新。");
        var declared = manifest.Files.Select(file => file.Path).Concat(manifest.Retired.Select(file => file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (installed.ManagedHistory.Any(path => !declared.Contains(path)))
            throw new InvalidDataException("第二章清单没有声明旧文件去向。");
        var verification = await FileVerifier.VerifyAsync(root, manifest, progress, token).ConfigureAwait(false);
        return new(target, installed, verification, true, true, false, $"已确认跨章目标 {manifest.Version}。");
    }

    private InstalledReceipt ReadChapterUpgradeBaseline()
    {
        var installed = store.ReadInstalled() ?? throw new InvalidDataException("缺少已校验的第一章桥接安装。");
        ChapterTransitionSource.RequireUpgrade(installed.Release.Source, provider.Source);
        if (ClientChapterIdentity.Read(root, installed.Release).Chapter != ClientChapterIdentity.ChapterOne)
            throw new InvalidDataException("当前章别不支持跨章升级。");
        return installed;
    }
    public async Task<LaunchValidation> ValidateInstalledAsync(IProgress<UpdateProgress>? progress = null, CancellationToken token = default, bool forceVerification = false)
    {
        using var lease = InstallationLease.Acquire(root);
        if (HasPending()) return new(false, "未知", "存在未完成安装，必须先恢复。");
        var installed = store.ReadInstalled();
        if (installed == null) return new(false, "未知", "没有已提交的 V4 安装记录。");
        installed.Release.Validate(provider.Source);
        return await VerifyInstalled(installed, progress, token, forceVerification).ConfigureAwait(false);
    }
    private async Task<LaunchValidation> VerifyInstalled(InstalledReceipt installed, IProgress<UpdateProgress>? progress, CancellationToken token, bool forceVerification = false)
    {
        var manifest = installed.Release.Manifest;
        token.ThrowIfCancellationRequested();
        var chapterReady = IsChapterReady(installed);
        if (!forceVerification && chapterReady && store.HasValidatedReceipt(installed) && ConfigurationReady(manifest))
            return new(true, manifest.Version, $"文件符合已校验安装记录 {manifest.Version}，未确认线上最新版本。");
        var verification = await FileVerifier.VerifyAsync(root, manifest, progress, token).ConfigureAwait(false);
        var good = verification.IsValid && chapterReady && ConfigurationReady(manifest);
        if (good) store.RecordValidation(installed); else store.InvalidateValidation();
        return new(good, manifest.Version, good ? $"文件符合缓存版本 {manifest.Version}，未确认线上最新版本。" : "受管理文件或配置未通过完整校验，请修复后启动。", verification);
    }
    private bool IsChapterReady(InstalledReceipt installed)
    {
        if (ClientChapterIdentity.Descriptor(installed.Release) == null) return true;
        try { _ = ClientChapterIdentity.Read(root, installed.Release); return true; }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
    private bool HasPending() => InstallTransaction.HasPending(root) || ChapterTransition.HasPending(root);
    private void RequireNoPending()
    {
        if (HasPending()) throw new InvalidDataException("存在未完成安装，请先恢复后再启动。");
    }
    private bool ConfigurationReady(ClientManifest manifest)
    {
        if (manifest.Migrations.Length == 0) return true;
        var path = PathPolicy.Resolve(root, "launcher.json", true);
        if (!File.Exists(path)) return ConfigurationMigration.IsSatisfied("{}"u8.ToArray(), manifest.Migrations);
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("配置文件过大。");
        return ConfigurationMigration.IsSatisfied(File.ReadAllBytes(path), manifest.Migrations);
    }
    public async Task<StagedUpdate> DownloadAsync(UpdateCheck check, IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        var target = check.Target ?? throw new InvalidDataException("没有已验证的线上修复目标。"); target.Validate(provider.Source);
        token.ThrowIfCancellationRequested(); var manifest = target.Manifest;
        VerificationResult current;
        var cache = new ContentCache(Path.Combine(store.StateDirectory, "cache"));
        ManagedFile[] changed;
        using (InstallationLease.Acquire(root))
        {
            RequireNoPending();
            current = await FileVerifier.VerifyAsync(root, manifest, progress, token).ConfigureAwait(false);
            var needed = new HashSet<string>(current.Problems.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
            changed = manifest.Files.Where(f => needed.Contains(f.Path)).ToArray();
            await cache.PrepareAsync(manifest,changed,token:token).ConfigureAwait(false);
        }
        var downloader = new ResourceDownloader(provider); var completed = 0;
        foreach (var file in changed)
        {
            token.ThrowIfCancellationRequested();
            await cache.MaterializeAsync(file, downloader, new ForwardProgress(value => progress?.Report(value with
            { Path = file.Path, CompletedFiles = completed, TotalFiles = changed.Length })), token).ConfigureAwait(false);
            progress?.Report(new("download", file.Path, ++completed, changed.Length));
        }
        return new(target, cache.DirectoryPath);
    }
    private sealed class ForwardProgress(Action<UpdateProgress> report) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => report(value); }
}
