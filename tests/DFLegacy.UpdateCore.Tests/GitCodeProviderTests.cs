using System.Net;
using System.Text.Json;

namespace DFLegacy.UpdateCore.Tests;

internal static class GitCodeProviderTests
{
    internal static async Task Run()
    {
        var source = new ReleaseSource("gitcode", "firekula/DFLegacy", "chapter-two", "x86", "test");
        var manifest = ManifestTests.Sample() with { Channel = source.Channel };
        var manifestBytes = ManifestCodec.Serialize(manifest);
        var resourceBytes = "abc"u8.ToArray();
        var resource = new ResourcePart(TestRun.Hash(resourceBytes), resourceBytes.Length, manifest.Version, "resource.bin");
        const string cdn = "file-cdn.gitcode.com";
        const string gitcode = "api.gitcode.com";
        const string github = "api.github.com";
        var requested = new List<Uri>();
        var primaryListStatus = HttpStatusCode.OK;
        var primaryResourceStatus = HttpStatusCode.OK;
        var noGitCodeRelease = false;
        var untrustedRedirect = false;
        var manifestCorrupt = false;
        var privateDownload = false;
        using var http = new HttpClient(new FakeHttp(request =>
        {
            var uri = request.RequestUri!;
            requested.Add(uri);
            TestRun.Check(request.Headers.Authorization == null && !request.Headers.Contains("Cookie"),
                "both public sources use anonymous HTTP");
            if (uri.Host == gitcode && uri.AbsolutePath.EndsWith("/releases") && primaryListStatus != HttpStatusCode.OK)
                return new(primaryListStatus);
            if (uri.Host is gitcode or github)
            {
                var primary = uri.Host == gitcode;
                if (uri.AbsolutePath.EndsWith("/download"))
                {
                    if (privateDownload) return new(HttpStatusCode.Unauthorized);
                    if (untrustedRedirect) return Redirect("https://untrusted.invalid/a");
                    if (uri.AbsolutePath.EndsWith("/resource.bin/download") && primaryResourceStatus != HttpStatusCode.OK)
                        return new(primaryResourceStatus);
                    return Redirect($"https://{cdn}/attachment/{(uri.AbsolutePath.EndsWith("/resource.bin/download") ? "resource.bin" : source.ManifestAssetName)}?auth_key=fixture");
                }
                var tag = manifest.Version;
                var canonicalTag = $"/releases/tags/{tag}";
                var primaryOrigin = $"https://atomgit.com/{source.Repository}/releases/download/{tag}";
                var fallbackOrigin = $"https://github.com/{source.Repository}/releases/download/{tag}";
                object[] attachments = primary
                    ? [new {name = source.ManifestAssetName, browser_download_url = $"{primaryOrigin}/{source.ManifestAssetName}"},
                       new {name = resource.Asset, browser_download_url = $"{primaryOrigin}/{resource.Asset}"}]
                    : [new {name = source.ManifestAssetName, id = 10, size = manifestBytes.Length, browser_download_url = $"{fallbackOrigin}/{source.ManifestAssetName}"},
                       new {name = resource.Asset, id = 11, size = resourceBytes.Length, browser_download_url = $"{fallbackOrigin}/{resource.Asset}"}];
                var release = new { tag_name = tag, prerelease = false, draft = false, assets = attachments };
                if (uri.AbsolutePath.EndsWith(canonicalTag.TrimEnd('/'))) return Json(release);
                if (uri.AbsolutePath.EndsWith("/releases"))
                    return primary && noGitCodeRelease ? Json(Array.Empty<object>()) : Json(new[] { release });
            }
            if (uri.Host is cdn or "github.com")
            {
                if (uri.AbsolutePath.EndsWith("/resource.bin")) return Bytes(resourceBytes);
                return Bytes(manifestCorrupt && uri.Host == cdn ? "{}"u8.ToArray() : manifestBytes);
            }
            throw new InvalidOperationException($"Unexpected test URL {uri.Host}{uri.AbsolutePath}");
        }));
        var provider = new ChapterTwoReleaseProvider(http, source);
        var found = await provider.FindAsync();
        TestRun.Check(found?.Source == source && found.ManifestSha256 == TestRun.Hash(manifestBytes)
            && requested.All(uri => !uri.Host.Contains("github", StringComparison.Ordinal)),
            "GitCode manifest without a declared byte count is verified without checking GitHub");
        TestRun.Check(requested.Any(uri => uri.Host == gitcode && uri.AbsolutePath.EndsWith($"/attach_files/{source.ManifestAssetName}/download")),
            "GitCode resolves the canonical public GET download endpoint");
        using (var response = await provider.OpenAsync(resource, 0, null, default))
            TestRun.Check(response.IsSuccessStatusCode && requested[^1].Host == cdn, "GitCode downloads a resource through its pinned CDN");

        primaryResourceStatus = HttpStatusCode.ServiceUnavailable;
        using (var response = await provider.OpenAsync(resource, 2, "\"other-origin\"", default))
            TestRun.Check(response.IsSuccessStatusCode && requested[^1].Host == "github.com",
                "GitCode resource outage falls back for the same immutable resource");
        primaryResourceStatus = HttpStatusCode.OK;

        foreach (var fallback in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.OK })
        {
            requested.Clear();
            primaryListStatus = fallback;
            noGitCodeRelease = fallback == HttpStatusCode.OK;
            var alternate = await provider.FindAsync();
            TestRun.Check(alternate?.Source == source && alternate.ManifestSha256 == TestRun.Hash(manifestBytes)
                && requested[0].Host == gitcode && requested.Any(uri => uri.Host == github),
                $"GitHub fallback keeps the installation identity when GitCode is unavailable: {fallback}");
        }
        noGitCodeRelease = false;

        requested.Clear();
        untrustedRedirect = true;
        await TestRun.RejectAsync(() => provider.FindAsync(), "GitCode external CDN redirect");
        TestRun.Check(requested.All(uri => uri.Host != github && uri.Host != "untrusted.invalid"),
            "untrusted redirect aborts before contacting an unrelated origin");
        untrustedRedirect = false;
        manifestCorrupt = true;
        await TestRun.RejectAsync(() => provider.FindAsync(), "malformed primary manifest");
        manifestCorrupt = false;

        privateDownload = true;
        TestRun.Check((await provider.FindAsync())?.Source == source, "blocked anonymous GitCode attachment falls back");
        var former = source with { Provider = "github" };
        var oldReceiptProvider = new ChapterTwoReleaseProvider(http, former);
        TestRun.Check((await oldReceiptProvider.FindAsync())?.Source == former, "legacy Chapter Two receipts stay valid during mirror discovery");
        await CheckVersionedStartupAsync(source);
    }

    private static async Task CheckVersionedStartupAsync(ReleaseSource source)
    {
        const string finalName = "DFLegacy-client-update-v4-chapter-two-startup-v2-test-win-x86.json";
        var oldEntry = ManifestTests.Entry("DFLegacy.Launcher.exe", "original fixed entry");
        var newEntry = ManifestTests.Entry("DFLegacy.Launcher.exe", "versioned fixed entry");
        var bridge = ManifestTests.Sample() with
        {
            Version = "v1.0.12-bridge", Channel = source.Channel, Sequence = 17, Files = [oldEntry]
        };
        var final = bridge with { Version = "v1.0.12", Sequence = 18, MinimumSequence = 17, Files = [newEntry] };
        var later = final with { Version = "v1.0.13", Sequence = 19 };
        var laterBytes = ManifestCodec.Serialize(later);
        var bridgeBytes = ManifestCodec.Serialize(bridge);
        var finalBytes = ManifestCodec.Serialize(final);
        var gitCodeHasFinal = true;
        var includeLater = false;
        var downloads = new List<string>();
        object Release(ClientManifest version, string name, int size, bool gitCode)
        {
            var host = gitCode ? "gitcode.com" : "github.com";
            return new { tag_name = version.Version, draft = false, assets = new[]
            {
                new { name, size, id = 1, browser_download_url = $"https://{host}/{source.Repository}/releases/download/{version.Version}/{name}" }
            }};
        }
        using var http = new HttpClient(new FakeHttp(request =>
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            if (path.EndsWith("/releases", StringComparison.Ordinal))
            {
                var gitCode = uri.Host == "api.gitcode.com";
                var releases = new List<object> { Release(bridge, source.ManifestAssetName, bridgeBytes.Length, gitCode) };
                if (!gitCode || gitCodeHasFinal) releases.Add(Release(final, finalName, finalBytes.Length, gitCode));
                if (includeLater) releases.Add(Release(later, finalName, laterBytes.Length, gitCode));
                return Json(releases);
            }
            var isFinal = path.EndsWith("/" + finalName, StringComparison.Ordinal)
                || path.EndsWith("/" + finalName + "/download", StringComparison.Ordinal);
            downloads.Add(isFinal ? final.Version : bridge.Version);
            var bytes = path.Contains("/v1.0.13/", StringComparison.Ordinal) ? laterBytes : isFinal ? finalBytes : bridgeBytes;
            return Bytes(bytes);
        }));
        var legacy = new ChapterTwoReleaseProvider(http, source);
        TestRun.Check((await legacy.FindAsync())?.Tag == bridge.Version && !downloads.Contains(final.Version),
            "the legacy asset name discovers the bridge without opening the incompatible final manifest");
        var upgraded = new ChapterTwoReleaseProvider(http, source, includeVersionedStartup: true);
        TestRun.Check((await upgraded.FindAsync())?.Tag == final.Version,
            "the upgraded app discovers the final version without changing its recorded update source");
        var oldSupervisor = new ChapterTwoReleaseProvider(http, source, includeVersionedStartup: true,
            fixedEntrySha256: oldEntry.Sha256);
        TestRun.Check((await oldSupervisor.FindAsync())?.Tag == bridge.Version,
            "a supervisor with the old startup executable accepts only a compatible target");
        gitCodeHasFinal = false;
        var newSupervisor = new ChapterTwoReleaseProvider(http, source, includeVersionedStartup: true,
            fixedEntrySha256: newEntry.Sha256);
        TestRun.Check((await newSupervisor.FindAsync()) is { Tag: "v1.0.12", Source: var foundSource }
            && foundSource == source && newSupervisor.DiscoveryProvider == "github",
            "repair with the new startup executable can fall back to the compatible GitHub manifest");
        gitCodeHasFinal = true;
        includeLater = true;
        var migration = new ChapterTwoReleaseProvider(http, source, includeVersionedStartup: true,
            fixedEntrySha256: oldEntry.Sha256, allowStartupMigration: true);
        TestRun.Check((await migration.FindAsync())?.Tag == final.Version,
            "the bridge selects the approved migration even when a newer version is published");
        TestRun.Check((await newSupervisor.FindAsync())?.Tag == later.Version,
            "the new startup generation accepts subsequent updates with matching entry bytes");
    }

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Json(object value) => Bytes(JsonSerializer.SerializeToUtf8Bytes(value));
    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(location) } };

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
