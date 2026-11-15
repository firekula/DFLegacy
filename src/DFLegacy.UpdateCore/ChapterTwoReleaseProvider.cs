using System.Net;

namespace DFLegacy.UpdateCore;

public sealed class ChapterTwoReleaseProvider : IReleaseProvider
{
    private readonly ReleaseProvider primary;
    private readonly ReleaseProvider fallback;
    public ReleaseSource Source { get; }
    public string Identity => Source.Identity;
    public string DiscoveryProvider { get; private set; } = "gitcode";

    public ChapterTwoReleaseProvider(HttpClient http, ReleaseSource source)
    {
        source.Validate();
        if (!ChapterTransitionSource.IsOfficialChapterTwo(source))
            throw new InvalidDataException("仅固定的第二章公开更新来源支持 GitCode 优先。");
        Source = source;
        primary = new ReleaseProvider(http, source with { Provider = "gitcode" });
        fallback = new ReleaseProvider(http, source with { Provider = "github" });
    }

    public async Task<VerifiedRelease?> FindAsync(CancellationToken token = default)
    {
        DiscoveryProvider = primary.Source.Provider;
        VerifiedRelease? release;
        try { release = await primary.FindAsync(token).ConfigureAwait(false); }
        catch (Exception error) when (IsUnavailable(error, token)) { release = null; }
        if (release is not null) return release with { Source = Source };

        DiscoveryProvider = fallback.Source.Provider;
        release = await fallback.FindAsync(token).ConfigureAwait(false);
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
