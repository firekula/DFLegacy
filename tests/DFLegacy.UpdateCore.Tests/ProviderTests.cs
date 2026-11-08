using System.Net;
using System.Text.Json;

namespace DFLegacy.UpdateCore.Tests;

internal static class ProviderTests
{
    internal static async Task Run()
    {
        await GiteaProviderTests.Run();
        foreach (var provider in new[] { "github", "gitee" })
        {
            var m = ManifestTests.Sample(); var raw = ManifestCodec.Serialize(m); var body = TestRun.Bytes("abc");
            var source = new ReleaseSource(provider, "firekula/DFLegacy", "gitee-test", "x86", "test");
            var manifestName = $"DFLegacy-client-update-v4-gitee-test-test-win-x86.json";
            var uri = provider == "github" ? $"https://github.com/firekula/DFLegacy/releases/download/{m.Version}/{manifestName}" : "https://gitee.com/attach_files/10/download";
            var wrongOrigin = false; var badDigest = false; var draft = false; var missing = false; var redirect = false; var html = false; var expire = false; var cdn = false;
            var requests = new List<string>(); var cdnQuery="?attname=manifest.json&token=fixture-only&ts=1";
            using var http = new HttpClient(new Handler(request =>
            {
                TestRun.Check(request.Headers.Authorization == null && !request.Headers.Contains("Cookie"), "provider does not send credentials");
                var path = request.RequestUri!.AbsolutePath; requests.Add(path);
                if (request.RequestUri.Host == "foruda.gitee.com") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(raw) };
                if (path.Contains("attach_files/10/download") || path.EndsWith("/" + manifestName))
                {
                    if (redirect) return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.test/a") } };
                    if (cdn) return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://foruda.gitee.com/attach_file/1773140874168858731/manifest.json"+cdnQuery) } };
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(raw) };
                    if (html) response.Content.Headers.ContentType = new("text/html");
                    return response;
                }
                if (path.Contains("attach_files/11/download") || path.EndsWith("/resource.bin"))
                {
                    if (expire) { expire = false; return new(HttpStatusCode.Forbidden); }
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
                }
                object[] attachments = missing ? [] : [new { id = 10, name = manifestName, size = raw.Length, digest = "sha256:" + (badDigest ? new string('f', 64) : TestRun.Hash(raw)), browser_download_url = wrongOrigin ? "https://evil.test/manifest.json" : uri }];
                object release = new { id = 1, tag_name = m.Version, draft, prerelease = true, published_at = "2026-11-08T00:00:00Z", assets = provider == "github" ? attachments : [new { name = "Source code", browser_download_url = "https://gitee.com/archive.zip" }] };
                if (path.EndsWith("/releases/1/attach_files")) return Json(attachments);
                if (path.Contains("/releases/tags/v4-test"))
                {
                    object[] parts = [new { id = 11, name = "resource.bin", size = body.Length, browser_download_url = provider == "github" ? "https://github.com/firekula/DFLegacy/releases/download/v4-test/resource.bin" : "https://gitee.com/attach_files/11/download" }];
                    return Json(new { id = 2, tag_name = "v4-test", draft = false, assets = parts });
                }
                if (path.EndsWith("/releases/2/attach_files")) return Json(new[] { new { id = 11, name = "resource.bin", size = body.Length, browser_download_url = "https://gitee.com/attach_files/11/download" } });
                if (path.EndsWith("/releases")) return Json(new[] { release });
                throw new Exception("Unexpected provider request " + request.RequestUri);
            })) { Timeout = Timeout.InfiniteTimeSpan };
            var client = new ReleaseProvider(http, source);
            var found = await client.FindAsync();
            TestRun.Check(found?.ManifestSha256 == TestRun.Hash(raw) && found.Manifest.Sequence == 2, provider + " binds raw manifest to configured source");
            missing = true; TestRun.Check(await client.FindAsync() == null, provider + " incomplete release hidden"); missing = false;
            draft = true; TestRun.Check(await client.FindAsync() == null, provider + " draft ignored"); draft = false;
            wrongOrigin = true; await TestRun.RejectAsync(async () => await client.FindAsync(), provider + " rejects attachment origin escape"); wrongOrigin = false;
            redirect = true; await TestRun.RejectAsync(async () => await client.FindAsync(), provider + " rejects redirect origin escape"); redirect = false;
            if (provider == "gitee")
            {
                cdn = true; TestRun.Check((await client.FindAsync())?.ManifestSha256 == TestRun.Hash(raw), "Gitee source-bound attachment follows verified foruda CDN"); cdn = false;
                cdn=true; cdnQuery="?redirect=https%3A%2F%2Fevil.test";
                await TestRun.RejectAsync(async()=>await client.FindAsync(),"Gitee CDN unexpected query rejected");
                cdnQuery="?token=x&token=y";
                await TestRun.RejectAsync(async()=>await client.FindAsync(),"Gitee CDN duplicate signed query rejected"); cdn=false;
                TestRun.Check(requests.Any(p => p == "/api/v5/repos/firekula/DFLegacy/releases/1/attach_files/10/download"), "Gitee download uses source-bound API attachment identity");
            }
            html = true; await TestRun.RejectAsync(async () => await client.FindAsync(), provider + " rejects HTML manifest"); html = false;
            if (provider == "github") { badDigest = true; await TestRun.RejectAsync(async () => await client.FindAsync(), "GitHub raw manifest digest mismatch"); badDigest = false; }
            var originalRaw = raw; raw = ManifestCodec.Serialize(m with { Architecture = "x64" });
            await TestRun.RejectAsync(async () => await client.FindAsync(), provider + " refuses cross architecture manifest"); raw = originalRaw;
            var resource = new ResourcePart(TestRun.Hash(body), body.Length, "v4-test", "resource.bin");
            expire = true;
            using (var response = await client.OpenAsync(resource, 0, null, default)) TestRun.Check(response.StatusCode == HttpStatusCode.Forbidden, "expired temporary resource response surfaced for bounded retry");
            using (var response = await client.OpenAsync(resource, 0, null, default)) TestRun.Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(body), "retry resolves fixed asset not latest");
            TestRun.Check(requests.Any(p => p.Contains("/releases/tags/v4-test")), "part lookup uses immutable release tag");
            TestRun.Check(requests.Count(p=>p.Contains("/releases/tags/v4-test"))==1,"immutable release attachment metadata reused across resource attempts");
            http.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-real-token");
            await TestRun.RejectAsync(async () => await client.FindAsync(), "client with credential defaults rejected");
        }
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value)) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(action(request)); }
}
