using System.Net;

namespace DFLegacy.UpdateCore;

public sealed class ChapterTwoReleaseProvider : IReleaseProvider
{
    private readonly ReleaseProvider primary;
    private readonly ReleaseProvider fallback;
    private readonly string? alternateManifestName;
    private readonly Func<ClientManifest, bool>? includeManifest;
    public ReleaseSource Source { get; }
    public string Identity => Source.Identity;
    public string DiscoveryProvider { get; private set; } = "gitcode";

    public ChapterTwoReleaseProvider(HttpClient http, ReleaseSource source,
        bool includeVersionedStartup = false, string? fixedEntrySha256 = null, bool allowStartupMigration = false)
    {
        source.Validate();
        if (!ChapterTransitionSource.IsOfficialChapterTwo(source))
            throw new InvalidDataException("仅固定的第二章公开更新来源支持 GitCode 优先。");
        if (fixedEntrySha256 is not null && !ManifestCodec.IsHash(fixedEntrySha256))
            throw new InvalidDataException("固定启动入口的哈希格式无效。");
        if (allowStartupMigration && (!includeVersionedStartup || fixedEntrySha256 is null))
            throw new ArgumentException("启动入口迁移需要已验证的旧入口及新版更新清单。", nameof(allowStartupMigration));
        Source = source;
        if (includeVersionedStartup)
            alternateManifestName = (source with { Channel = "chapter-two-startup-v2" }).ManifestAssetName;
        if (fixedEntrySha256 is not null)
            includeManifest = manifest => manifest.Files.Any(file => file.Path == "DFLegacy.Launcher.exe"
                && file.Sha256 == fixedEntrySha256) || allowStartupMigration && IsStartupMigrationTarget(manifest);
        primary = new ReleaseProvider(http, source with { Provider = "gitcode" });
        fallback = new ReleaseProvider(http, source with { Provider = "github" });
    }

    // The bridge release and the final release are pinned as one approved pair:
    // only the fixed entry may differ, and only for these exact versions.
    public static bool IsStartupMigrationTarget(ClientManifest manifest) =>
        manifest is { Channel: "chapter-two", Version: "v1.0.12", Sequence: 18, MinimumSequence: 17 }
            or { Channel: "chapter-two", Version: "v1.0.13-r2", Sequence: 21, MinimumSequence: 17 }
            or { Channel: "chapter-two", Version: "v1.0.14", Sequence: 23, MinimumSequence: 17 };

    public async Task<VerifiedRelease?> FindAsync(CancellationToken token = default)
    {
        DiscoveryProvider = primary.Source.Provider;
        VerifiedRelease? release;
        try { release = await primary.FindAsync(alternateManifestName, includeManifest, token).ConfigureAwait(false); }
        catch (Exception error) when (IsUnavailable(error, token)) { release = null; }
        if (release is not null) return release with { Source = Source };

        DiscoveryProvider = fallback.Source.Provider;
        release = await fallback.FindAsync(alternateManifestName, includeManifest, token).ConfigureAwait(false);
        return release is null ? null : release with { Source = Source };
    }

    public async Task<HttpResponseMessage> OpenAsync(ResourcePart part, long offset, string? etag, CancellationToken token)
    {
        if (DiscoveryProvider == fallback.Source.Provider)
            return await fallback.OpenAsync(part, offset, etag, token).ConfigureAwait(false);

        try
        {
            var response = await primary.OpenAsync(part, offset, etag, token).ConfigureAwait(false);
            if (!IsUnavailable(response.StatusCode)) return response;
            response.Dispose();
        }
        catch (Exception error) when (IsUnavailable(error, token)) { }

        // A partial response from one CDN may carry a different ETag from the other.
        return await fallback.OpenAsync(part, 0, null, token).ConfigureAwait(false);
    }

    private static bool IsUnavailable(Exception error, CancellationToken token) =>
        error is HttpRequestException http && (http.StatusCode is null || IsUnavailable(http.StatusCode.Value))
        || error is TimeoutException || error is OperationCanceledException && !token.IsCancellationRequested;

    private static bool IsUnavailable(HttpStatusCode status) =>
        (int)status is 401 or 403 or 404 or 408 or 418 or 429 or >= 500;
}
