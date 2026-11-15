using System.Net;

namespace DFLegacy.UpdateCore.Tests;

internal static class LoopbackProviderTests
{
    internal static async Task Run()
    {
        var source = new ReleaseSource("local", "http://127.0.0.2:48123/c2", "gitee-test", "x86", "test");
        source.Validate();
        foreach (var repository in new[] { "http://127.0.0.1:48123/c2", "http://localhost:48123/c2", "https://127.0.0.2:48123/c2",
                     "http://127.0.0.2/c2", "http://127.0.0.2:48123/a/../b", "http://127.0.0.2:48123/c2?x=1", "http://user@127.0.0.2:48123/c2",
                     "http://example.com:48123/c2", "http://127.0.0.2:48123/c2#x", "http://127.0.0.2:48123/%2e%2e/b" })
            TestRun.Reject(() => (source with { Repository = repository }).Validate(), "unsafe local source " + repository);
        var manifest = ManifestTests.Sample();
        var raw = ManifestCodec.Serialize(manifest);
        var redirect = false; var html = false;
        using var http = new HttpClient(new Handler(request =>
        {
            TestRun.Check(request.RequestUri!.Host == "127.0.0.2" && request.RequestUri.Port == 48123, "local provider retains exact bound origin");
            TestRun.Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "local provider never sends login credentials");
            var result = new HttpResponseMessage(redirect ? HttpStatusCode.Redirect : HttpStatusCode.OK)
                { Content = new ByteArrayContent(raw), RequestMessage = request };
            if (redirect) result.Headers.Location = new("http://example.com/redirect");
            if (html) result.Content.Headers.ContentType = new("text/html");
            return result;
        }));
        using var provider = new LoopbackReleaseProvider(source, http);
        var release = await provider.FindAsync();
        TestRun.Check(release?.ManifestSha256 == TestRun.Hash(raw) && release.Source == source, "local manifest is bound to explicit source and content");
        redirect = true;
        await TestRun.RejectAsync(() => provider.FindAsync(), "redirect away from local source");
        redirect = false; html = true;
        await TestRun.RejectAsync(() => provider.FindAsync(), "local HTML response");
        html = false;
        await TestRun.RejectAsync(async () => { using var ignored = await provider.OpenAsync(new(new string('a', 64), 10, "../other", "a.bin"), 0, null, default); }, "resource path traversal");
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(run(request));
    }
}
