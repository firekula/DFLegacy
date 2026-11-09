using System.Diagnostics;
using System.Net.Http;

namespace DFLegacy.Launcher.Updates;

internal static class UpdateDownload
{
    internal static void RequireNoCredentials(HttpClient client)
    {
        if (client.DefaultRequestHeaders.Any(header => !header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
            && !header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("公开更新客户端不能携带凭据或自定义请求头。");
    }

    internal static async Task<PreparedUpdate> FetchAsync(UpdateRelease release, string stagingRoot,
        IProgress<UpdateProgress>? progress, CancellationToken cancellationToken, string flavor,
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> get, Action<PreparedUpdate>? verify = null)
    {
        if (release.Id <= 0 || release.Size <= 0 || release.Size > UpdateSafety.MaxZip || !UpdateSafety.IsHash(release.Sha256))
            throw new InvalidDataException("更新附件信息无效。");
        UpdateSafety.NoLinks(stagingRoot);
        Directory.CreateDirectory(stagingRoot);
        using var rootPin = UpdateSafety.PinDirectory(stagingRoot);
        var directory = Path.Combine(Path.GetFullPath(stagingRoot), "dflegacy-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var pin = UpdateSafety.PinDirectory(directory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var response = await get(release.DownloadUrl, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("更新附件长度不匹配。");
            var archive = Path.Combine(directory, "package.zip");
            await using (var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var clock = Stopwatch.StartNew();
                var copied = await CopyBounded(body, output, release.Size,
                    bytes => progress?.Report(new(bytes, release.Size, bytes / Math.Max(clock.Elapsed.TotalSeconds, 0.001))), timeout.Token).ConfigureAwait(false);
                if (copied != release.Size) throw new InvalidDataException("更新附件未完整下载。");
            }
            timeout.Token.ThrowIfCancellationRequested();
            var update = UpdatePackage.Extract(release, directory, archive, timeout.Token, flavor);
            verify?.Invoke(update);
            return update;
        }
        catch
        {
            UpdateArtifacts.DeleteStage(directory);
            throw;
        }
    }

    internal static async Task<long> CopyBounded(Stream input, Stream output, long limit, Action<long>? progress, CancellationToken token)
    {
        var bytes = new byte[65536];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("更新数据超出大小限制。");
            await output.WriteAsync(bytes.AsMemory(0, count), token).ConfigureAwait(false);
            progress?.Invoke(total);
        }
        return total;
    }
}
