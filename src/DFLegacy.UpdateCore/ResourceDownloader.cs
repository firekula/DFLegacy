using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace DFLegacy.UpdateCore;

public interface IResourceTransport
{
    string Identity { get; }
    Task<HttpResponseMessage> OpenAsync(ResourcePart part, long offset, string? etag, CancellationToken token);
}
public sealed record DownloadOptions(int MaxAttempts = 4, TimeSpan? IdleTimeout = null, TimeSpan? RetryDelay = null);

public sealed class ResourceDownloader(IResourceTransport transport, DownloadOptions? options = null)
{
    private static readonly SemaphoreSlim Slots = new(2);
    private readonly DownloadOptions settings = options ?? new();
    private sealed record PartialIdentity(string Sha256, long Size, string Source, string? ETag);
    public async Task<string> FetchAsync(ResourcePart part, ContentCache cache, IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        if (!ManifestCodec.IsHash(part.Sha256) || part.Size < 0 || part.Size > ManifestCodec.PartLimit || settings.MaxAttempts is < 1 or > 10)
            throw new InvalidDataException("下载参数无效。");
        var target = cache.ObjectPath(part.Sha256, part.Size);
        using var lease = await cache.LockAsync(target + ".lock", token).ConfigureAwait(false);
        if (await FileVerifier.MatchesAsync(target, part.Size, part.Sha256, token).ConfigureAwait(false)) return target;
        await Slots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var partial = cache.PartialPath(part); var identityPath = partial + ".json";
            var source = transport.Identity + "/" + part.Release + "/" + part.Asset;
            PathPolicy.NoLinks(partial); PathPolicy.NoLinks(identityPath);
            PartialIdentity? identity = null;
            try
            {
                if (File.Exists(identityPath) && new FileInfo(identityPath).Length < 8192)
                    identity = JsonSerializer.Deserialize<PartialIdentity>(File.ReadAllBytes(identityPath), ManifestCodec.Json);
            }
            catch (JsonException) { }
            if (identity == null || identity.Sha256 != part.Sha256 || identity.Size != part.Size || identity.Source != source
                || File.Exists(partial) && new FileInfo(partial).Length > part.Size)
            {
                if (File.Exists(partial)) File.Delete(partial);
                identity = new(part.Sha256, part.Size, source, null);
            }
            for (var attempt = 1; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var retryAfter = settings.RetryDelay ?? TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                try
                {
                    if (await FileVerifier.MatchesAsync(partial, part.Size, part.Sha256, token).ConfigureAwait(false))
                    { File.Move(partial, target, true); if (File.Exists(identityPath)) File.Delete(identityPath); return target; }
                    var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                    if (offset == part.Size && part.Size != 0) { File.Delete(partial); offset = 0; }
                    cache.EnsureAvailableBytes(checked(part.Size + 65536));
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                    idle.CancelAfter(settings.IdleTimeout ?? TimeSpan.FromSeconds(30));
                    using var response = await transport.OpenAsync(part, offset, identity.ETag, idle.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        if (File.Exists(partial)) File.Delete(partial);
                        throw new IOException("服务器拒绝续传范围，已重置该文件。");
                    }
                    if ((int)response.StatusCode is 429 or >= 500 or 403 or 404)
                    {
                        var retry = response.Headers.RetryAfter;
                        if (retry?.Delta is { } delta) retryAfter = delta;
                        else if (retry?.Date is { } when) retryAfter = when - DateTimeOffset.UtcNow;
                        throw new HttpRequestException("资源请求暂时失败（HTTP " + (int)response.StatusCode + "）。", null, response.StatusCode);
                    }
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                        throw new InvalidDataException("更新资源返回了网页，可能需要登录或被拦截。");
                    var returnedTag = response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null;
                    if (response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        var range = response.Content.Headers.ContentRange;
                        if (range?.Unit != "bytes" || range.From != offset || range.To != part.Size - 1 || range.Length != part.Size
                            || offset > 0 && identity.ETag != null && returnedTag != identity.ETag)
                            throw new InvalidDataException("续传响应范围或文件身份不一致。");
                    }
                    else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                    else throw new InvalidDataException("资源响应状态不支持。");
                    if (response.Content.Headers.ContentLength is long length && length != part.Size - offset)
                        throw new InvalidDataException("资源响应长度与清单不符。");
                    identity = identity with { ETag = returnedTag };
                    WriteIdentity(identityPath, identity);
                    using var input = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
                    PathPolicy.NoLinks(partial);
                    using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Open, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                    {
                        if (output.Length != offset) throw new InvalidDataException("续传缓存已被修改。");
                        output.Position = offset;
                        var buffer = new byte[128 * 1024]; var count = offset; var timer = Stopwatch.StartNew();
                        try
                        {
                            while (true)
                            {
                                idle.CancelAfter(settings.IdleTimeout ?? TimeSpan.FromSeconds(30));
                                var read = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                                if (read == 0) break;
                                if (read > part.Size - count) throw new InvalidDataException("资源超过清单长度。");
                                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); count += read;
                                progress?.Report(new("download", part.Asset, 0, 1, count, part.Size, (count - offset) / Math.Max(.001, timer.Elapsed.TotalSeconds)));
                            }
                            if (count != part.Size) throw new IOException("下载连接提前结束，已保留续传数据。");
                        }
                        finally { output.Flush(true); }
                    }
                    if (!await FileVerifier.MatchesAsync(partial, part.Size, part.Sha256, token).ConfigureAwait(false))
                    { File.Delete(partial); throw new InvalidDataException("资源 SHA256 不匹配，将重新下载该文件。"); }
                    PathPolicy.NoLinks(target); File.Move(partial, target, true);
                    File.Delete(identityPath);
                    return target;
                }
                catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                {
                    if (attempt >= settings.MaxAttempts) throw new TimeoutException("更新下载长时间没有进展，续传数据已保留。", ex);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException)
                {
                    if (attempt >= settings.MaxAttempts) throw;
                }
                retryAfter = TimeSpan.FromMilliseconds(Math.Clamp(retryAfter.TotalMilliseconds, 0, 30000));
                progress?.Report(new("retry", part.Asset, 0, 1, Detail: $"第 {attempt} 次下载失败，保留已完成内容并重试。"));
                await Task.Delay(retryAfter, token).ConfigureAwait(false);
            }
        }
        finally { Slots.Release(); }
    }
    private static void WriteIdentity(string path, PartialIdentity value)
    {
        var next = path + ".next"; PathPolicy.NoLinks(next); PathPolicy.NoLinks(path);
        using (var stream = new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value, ManifestCodec.Json); stream.Flush(true); }
        File.Move(next, path, true);
    }
}
