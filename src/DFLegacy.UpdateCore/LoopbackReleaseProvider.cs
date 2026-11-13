using System.Net;
using System.Security.Cryptography;

namespace DFLegacy.UpdateCore;

/// <summary>Explicit isolated acceptance source. Never follows redirects or sends account data.</summary>
public sealed class LoopbackReleaseProvider : IReleaseProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    public ReleaseSource Source { get; }
    public string Identity => Source.Identity;

    public LoopbackReleaseProvider(ReleaseSource source) : this(source, ReleaseProvider.CreateHttpClient(), true) { }
    internal LoopbackReleaseProvider(ReleaseSource source, HttpClient client) : this(source, client, false) { }
    private LoopbackReleaseProvider(ReleaseSource source, HttpClient client, bool owns)
    {
        source.Validate();
        if (source.Provider != "local") throw new InvalidDataException("本机更新入口只接受显式本机来源。");
        Source = source; http = client; ownsHttp = owns;
    }

    public async Task<VerifiedRelease?> FindAsync(CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await SendAsync(Source.ManifestAssetName, 0, null, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > ManifestCodec.MaxBytes) throw new InvalidDataException("本机更新清单过大。");
        using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        for (var read = await input.ReadAsync(buffer, deadline.Token); read > 0; read = await input.ReadAsync(buffer, deadline.Token))
        {
            if (output.Length + read > ManifestCodec.MaxBytes) throw new InvalidDataException("本机更新清单过大。");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        var manifest = ManifestCodec.Parse(bytes);
        var result = new VerifiedRelease(Source, manifest.Version, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes);
        result.Validate(Source);
        return result;
    }

    public Task<HttpResponseMessage> OpenAsync(ResourcePart part, long offset, string? etag, CancellationToken token)
    {
        if (!ManifestCodec.IsName(part.Release) || !ManifestCodec.IsName(part.Asset)
            || !ManifestCodec.IsHash(part.Sha256) || part.Size < 0 || part.Size > ManifestCodec.PartLimit
            || offset < 0 || offset > part.Size)
            throw new InvalidDataException("本机更新资源路径或大小无效。");
        return SendAsync("releases/" + part.Release + "/" + part.Asset, offset, etag, token);
    }

    private async Task<HttpResponseMessage> SendAsync(string relative, long offset, string? etag, CancellationToken token)
    {
        if (http.DefaultRequestHeaders.Authorization is not null || http.DefaultRequestHeaders.Contains("Cookie"))
            throw new InvalidDataException("本机更新下载不能携带账号凭据。");
        var uri = new Uri(Source.Repository + "/" + relative);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset > 0)
        {
            request.Headers.Range = new(offset, null);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Range", etag);
        }
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400
            || response.RequestMessage?.RequestUri is { } actual && actual != uri
            || response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
        {
            response.Dispose();
            throw new InvalidDataException("本机更新来源返回了重定向或无效内容。");
        }
        return response;
    }

    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
