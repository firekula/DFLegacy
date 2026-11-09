using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.UpdateCore;

/// <summary>The trust root is the configured public repository's HTTPS API, never release-body links.</summary>
public sealed class ReleaseProvider : IReleaseProvider
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim metadataGate = new(1,1);
    private readonly Dictionary<string,Attachment[]> fixedAttachments = new(StringComparer.Ordinal);
    public ReleaseSource Source { get; }
    public string Identity => Source.Identity;
    private string Api => Source.Provider switch
    { "github" => "https://api.github.com", "gitea" => ReleaseSource.GiteaOrigin + "/api/v1", _ => "https://gitee.com/api/v5" };
    private int PageSize => Source.Provider == "gitea" ? 50 : 100;
    private sealed record Attachment(string Name, long Size, Uri Url, string? Digest, long Id, long ReleaseId = 0);
    public ReleaseProvider(HttpClient http, ReleaseSource source) { source.Validate(); this.http = http; Source = source; }
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, Credentials = null }) { Timeout = Timeout.InfiniteTimeSpan };
    private void AnonymousOnly()
    {
        if (http.DefaultRequestHeaders.Any(h => h.Key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || h.Key.Contains("cookie", StringComparison.OrdinalIgnoreCase) || h.Key.Contains("token", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("公开更新请求不得携带登录凭据。");
    }
    public async Task<VerifiedRelease?> FindAsync(CancellationToken token = default)
    {
        var candidates = new List<VerifiedRelease>();
        for (var page = 1; page <= 3; page++)
        {
            var paging = Source.Provider == "gitea" ? "limit=50" : "per_page=100";
            using var releases = await JsonAsync($"/repos/{Source.Repository}/releases?{paging}&page={page}", token).ConfigureAwait(false);
            if (releases.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("发行版列表不是数组。");
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                if (IsDraft(release)) continue;
                var tag = release.GetProperty("tag_name").GetString();
                if (!ManifestCodec.IsName(tag)) continue;
                var assets = await AttachmentsAsync(release, token).ConfigureAwait(false);
                var manifests = assets.Where(a => a.Name == Source.ManifestAssetName).ToArray();
                if (manifests.Length == 0) continue; // Gitee makes a release visible before the last manifest upload.
                if (manifests.Length != 1) throw new InvalidDataException("发行版中有重复更新清单。");
                var asset = manifests[0]; ValidateAttachment(asset, tag!);
                if (asset.Size <= 0 || asset.Size > ManifestCodec.MaxBytes) throw new InvalidDataException("更新清单附件大小无效。");
                using var response = await AssetAsync(asset, tag!, 0, null, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode(); RejectHtml(response);
                var bytes = await ReadBoundedAsync(response, ManifestCodec.MaxBytes, token).ConfigureAwait(false);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (bytes.LongLength != asset.Size || asset.Digest != null && asset.Digest != "sha256:" + hash)
                    throw new InvalidDataException("更新清单附件摘要或长度不符。");
                var found = new VerifiedRelease(Source, tag!, hash, bytes); found.Validate(Source); candidates.Add(found);
            }
            if (releases.RootElement.GetArrayLength() < PageSize) break;
            if (page == 3) throw new IOException("尚未查完发行版，无法确认线上最新版本。");
        }
        var newest = candidates.OrderByDescending(r => r.Manifest.Sequence).FirstOrDefault();
        if (newest != null && candidates.Any(c => c.Manifest.Sequence == newest.Manifest.Sequence && c.ManifestSha256 != newest.ManifestSha256))
            throw new InvalidDataException("同一更新序号对应多个不同清单。");
        return newest;
    }
    private static bool IsDraft(JsonElement release) => release.TryGetProperty("draft", out var value) && value.ValueKind == JsonValueKind.True;
    private async Task<List<Attachment>> AttachmentsAsync(JsonElement release, CancellationToken token)
    {
        // Gitea's Release embeds its complete attachment array, like GitHub.
        // Avoid one extra API request for every historical non-V4 release.
        if (Source.Provider is "github" or "gitea") return ParseAttachments(release.GetProperty("assets"));
        var id = release.GetProperty("id").GetInt64();
        if (id <= 0) throw new InvalidDataException("发行版 ID 无效。");
        var result = new List<Attachment>();
        for (var page = 1; page <= 11; page++)
        {
            using var assets = await JsonAsync($"/repos/{Source.Repository}/releases/{id}/attach_files?per_page=100&page={page}", token).ConfigureAwait(false);
            var part = ParseAttachments(assets.RootElement).Select(a => a with { ReleaseId = id }).ToList(); result.AddRange(part);
            if (part.Count < 100) return result;
        }
        throw new InvalidDataException("发行版附件列表过大或分页未结束。");
    }
    private static List<Attachment> ParseAttachments(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new InvalidDataException("附件列表格式无效。");
        var result = new List<Attachment>();
        foreach (var item in array.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? "";
            if (!ManifestCodec.IsName(name)) continue;
            if (!Uri.TryCreate(item.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri)) throw new InvalidDataException("附件地址无效。");
            var size = item.GetProperty("size").GetInt64();
            var id = item.GetProperty("id").GetInt64();
            var digest = item.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            result.Add(new(name, size, uri, digest, id));
        }
        return result;
    }
    public async Task<HttpResponseMessage> OpenAsync(ResourcePart part, long offset, string? etag, CancellationToken token)
    {
        if (!ManifestCodec.IsName(part.Release) || !ManifestCodec.IsName(part.Asset)) throw new InvalidDataException("附件身份无效。");
        var assets = (await FixedAttachmentsAsync(part.Release,token).ConfigureAwait(false)).Where(a => a.Name == part.Asset).ToArray();
        if (assets.Length != 1 || assets[0].Size != part.Size || assets[0].Digest != null && assets[0].Digest != "sha256:" + part.Sha256)
            throw new InvalidDataException("更新资源附件缺失、重复或内容身份不符。");
        ValidateAttachment(assets[0], part.Release);
        return await AssetAsync(assets[0], part.Release, offset, etag, token).ConfigureAwait(false);
    }
    private async Task<Attachment[]> FixedAttachmentsAsync(string tag,CancellationToken token)
    {
        // Cache canonical attachment identities, never expiring CDN redirect URLs or credentials.
        await metadataGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if(fixedAttachments.TryGetValue(tag,out var cached)) return cached;
            using var release=await JsonAsync($"/repos/{Source.Repository}/releases/tags/{tag}",token).ConfigureAwait(false);
            if(release.RootElement.GetProperty("tag_name").GetString()!=tag || IsDraft(release.RootElement)) throw new InvalidDataException("资源发行版身份不符。");
            var assets=(await AttachmentsAsync(release.RootElement,token).ConfigureAwait(false)).ToArray();
            if(fixedAttachments.Count>=128) fixedAttachments.Remove(fixedAttachments.Keys.First());
            fixedAttachments.Add(tag,assets); return assets;
        }
        finally {metadataGate.Release();}
    }
    private void ValidateAttachment(Attachment asset, string tag)
    {
        var uri = asset.Url; RequireTransport(uri);
        var valid = Source.Provider == "github"
            ? uri.Host == "github.com" && uri.AbsolutePath == $"/{Source.Repository}/releases/download/{tag}/{asset.Name}"
            : Source.Provider == "gitea"
            ? asset.Id > 0 && IsGiteaOrigin(uri) && (uri.AbsolutePath == $"/{Source.Repository}/releases/download/{tag}/{asset.Name}" || IsGiteaAttachment(uri))
            : uri.Host == "gitee.com" && asset.Id > 0 && asset.ReleaseId > 0 &&
                (uri.AbsolutePath == $"/attach_files/{asset.Id}/download"
                 || uri.AbsolutePath == $"/{Source.Repository}/attach_files/{asset.Id}/download/{asset.Name}"
                 || uri.AbsolutePath == $"/{Source.Repository}/releases/download/{tag}/{asset.Name}"
                 || uri.AbsolutePath == $"/api/v5/repos/{Source.Repository}/releases/{asset.ReleaseId}/attach_files/{asset.Id}/download");
        if (!valid || uri.Query.Length != 0) throw new InvalidDataException("附件地址不属于所配置发行版。");
    }
    private static bool IsGiteaOrigin(Uri uri) => uri.Scheme == "http" && uri.Host == "firekula.site" && uri.Port == 3000;
    private static bool IsGiteaAttachment(Uri uri) => System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,
        @"\A/attachments/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z");
    private void RequireTransport(Uri uri)
    {
        if (Source.Provider == "gitea")
        {
            if (!IsGiteaOrigin(uri) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidDataException("Gitea 测试更新请求不属于配置的实例。");
            return;
        }
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("更新请求必须使用不含凭据的 HTTPS 地址。");
    }
    private async Task<HttpResponseMessage> AssetAsync(Attachment asset, string tag, long offset, string? etag, CancellationToken token)
    {
        ValidateAttachment(asset, tag);
        var canonical = Source.Provider == "gitee"
            ? new Uri($"{Api}/repos/{Source.Repository}/releases/{asset.ReleaseId}/attach_files/{asset.Id}/download") : asset.Url;
        var uri = canonical;
        for (var hop = 0; hop < 6; hop++)
        {
            RequireTransport(uri);
            if (hop > 0)
            {
                var valid = Source.Provider == "github"
                    ? uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com" || uri == asset.Url
                    : Source.Provider == "gitea"
                    ? IsGiteaOrigin(uri) && uri.Query.Length == 0 && (uri == asset.Url || IsGiteaAttachment(uri))
                    : uri.Host == "gitee.com" && uri.AbsolutePath == canonical.AbsolutePath && uri.Query.Length == 0
                        || uri.Host == "foruda.gitee.com" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"\A/attach_file/[0-9]+/[A-Za-z0-9._-]+\z") && ValidGiteeCdnQuery(uri);
                if (!valid) throw new InvalidDataException("更新重定向域名不受信任。");
            }
            using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            headersTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            var response = await SendAsync(uri, offset, etag, false, headersTimeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var next = response.Headers.Location; response.Dispose();
                if (next == null) throw new InvalidDataException("更新重定向缺少地址。");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue;
            }
            return response;
        }
        throw new InvalidDataException("更新重定向过多。");
    }
    private static bool ValidGiteeCdnQuery(Uri uri)
    {
        if(uri.Query.Length==0) return true;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var pair in uri.Query[1..].Split('&'))
        {
            var split=pair.IndexOf('='); if(split<=0) return false;
            var key=pair[..split];
            if(key is not ("attname" or "token" or "ts") || !seen.Add(key) || split==pair.Length-1) return false;
        }
        return true;
    }
    private async Task<JsonDocument> JsonAsync(string relative, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await SendAsync(new Uri(Api + relative), 0, null, true, idle.Token).ConfigureAwait(false);
            if (((int)response.StatusCode is 429 or >= 500) && attempt < 3)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0, 30000)), token).ConfigureAwait(false); continue;
            }
            response.EnsureSuccessStatusCode(); RejectHtml(response);
            var bytes = await ReadBoundedAsync(response, ManifestCodec.MaxBytes, token).ConfigureAwait(false);
            try { var doc = JsonDocument.Parse(bytes); ManifestCodec.RejectDuplicateKeys(doc.RootElement); return doc; }
            catch (JsonException ex) { throw new InvalidDataException("更新服务器返回了无效元数据。", ex); }
        }
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri, long offset, string? etag, bool json, CancellationToken token)
    {
        RequireTransport(uri);
        AnonymousOnly();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("DFLegacy-Updater/4.0"); request.Headers.Accept.ParseAdd(json ? "application/json" : "application/octet-stream");
        if (offset > 0)
        {
            request.Headers.Range = new(offset, null);
            if (etag != null) request.Headers.TryAddWithoutValidation("If-Range", etag);
        }
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri is { } actual && actual != uri)
        { response.Dispose(); throw new InvalidDataException("更新 HTTP 客户端必须关闭自动重定向。"); }
        return response;
    }
    private static void RejectHtml(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml") throw new InvalidDataException("更新服务器返回了登录页或拦截页面。");
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("元数据超过长度限制。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[65536];
        while (true)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false); if (count == 0) break;
            if (output.Length + count > maximum) throw new InvalidDataException("元数据超过长度限制。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
