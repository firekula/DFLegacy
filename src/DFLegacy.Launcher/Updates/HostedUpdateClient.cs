using System.Net;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace DFLegacy.Launcher.Updates;

internal sealed class HostedUpdateClient(HttpClient http, Uri origin, string architecture, string flavor)
{
    private Uri Slot(string leaf) => new(origin, $"/v1/{architecture}/{flavor}/{leaf}");

    internal static Uri ParseOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("更新服务器必须使用 HTTP 或 HTTPS 域名或地址，可指定独立端口。");
        return uri;
    }

    internal async Task<UpdateRelease?> CheckAsync(long installedId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await GetAsync(Slot("latest"), timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var data = new MemoryStream();
        await UpdateDownload.CopyBounded(body, data, 16 * 1024, null, timeout.Token).ConfigureAwait(false);
        try
        {
            using var json = JsonDocument.Parse(data.ToArray());
            var record = json.RootElement;
            ClientPackageManifest.ExactKeys(record, "id", "version", "publishedAt", "size", "sha256");
            var id = record.GetProperty("id").GetInt64();
            var version = record.GetProperty("version").GetString();
            var publishedAt = record.GetProperty("publishedAt").GetDateTimeOffset();
            var size = record.GetProperty("size").GetInt64();
            var digest = record.GetProperty("sha256").GetString();
            if (id <= 0 || version is null || version.Length is < 1 or > 128 || version.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(version) || size is <= 0 or > UpdateSafety.MaxZip
                || !ClientPackageManifest.Hex(digest, 64))
                throw new InvalidDataException("更新服务器返回的版本信息无效。");
            return id <= installedId ? null : new(id, version, publishedAt, false, Slot(digest + ".zip"), size, digest!);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("更新服务器返回的版本信息无效。", error);
        }
    }

    internal Task<PreparedUpdate> DownloadAsync(UpdateRelease release, string stagingRoot,
        IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        if (!ClientPackageManifest.Hex(release.Sha256, 64)
            || release.DownloadUrl.AbsoluteUri != Slot(release.Sha256 + ".zip").AbsoluteUri)
            throw new InvalidDataException("更新下载地址无效。");
        return UpdateDownload.FetchAsync(release, stagingRoot, progress, token, flavor, GetAsync, ValidateArchitecture);
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        UpdateDownload.RequireNoCredentials(http);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("DFLegacy-Updater/1.0");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400 || response.RequestMessage?.RequestUri is { } actual && actual != uri)
        {
            response.Dispose();
            throw new InvalidDataException("更新服务不允许重定向。");
        }
        return response;
    }

    private void ValidateArchitecture(PreparedUpdate update)
    {
        using var file = File.OpenRead(Path.Combine(update.Directory, "DFLegacy.Launcher.exe"));
        try
        {
            using var pe = new PEReader(file);
            var headers = pe.PEHeaders;
            var valid = architecture == "x86"
                ? headers.CoffHeader.Machine == Machine.I386 && headers.PEHeader?.Magic == PEMagic.PE32
                : headers.CoffHeader.Machine == Machine.Amd64 && headers.PEHeader?.Magic == PEMagic.PE32Plus;
            if (!valid) throw new InvalidDataException("更新登录器架构与当前客户端不匹配。");
        }
        catch (BadImageFormatException error)
        {
            throw new InvalidDataException("更新登录器文件格式无效。", error);
        }
    }
}
