using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;

internal static class HostedUpdateTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        const string origin = "https://games.example.test:18443";
        const long id = 1793665367000;
        var sha256 = new string('a', 64);
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new { id, version = "dev-test", publishedAt = DateTimeOffset.UtcNow, size = 12, sha256 });
        foreach (var endpoint in new[] { origin, "http://games.example.test:18083" })
        foreach (var arch in new[] { "x86", "x64" })
        foreach (var flavor in new[] { "normal", "test" })
        {
            Uri? requestUri = null;
            using var http = new HttpClient(new UpdateTransportTests.Handler((request, _) =>
            {
                requestUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(metadata) });
            }));
            var client = new HostedUpdateClient(http, HostedUpdateClient.ParseOrigin(endpoint), arch, flavor);
            var release = await client.CheckAsync(7, CancellationToken.None);
            check(requestUri?.AbsoluteUri == $"{endpoint}/v1/{arch}/{flavor}/latest"
                && release?.DownloadUrl.AbsoluteUri == $"{endpoint}/v1/{arch}/{flavor}/{sha256}.zip"
                && release.Id == id, "hosted feed selects exact architecture, flavor and configured scheme and custom port");
            check(await client.CheckAsync(id, CancellationToken.None) is null, "hosted feed does not repeat installed release");
            await UpdateTransportTests.Reject(() => client.DownloadAsync(release! with { DownloadUrl = new Uri("https://other.example.test/payload.zip") }, root, null, CancellationToken.None),
                check, "hosted updater rejects off-origin downloads");
        }
        using var absent = UpdateTransportTests.Client([], HttpStatusCode.NotFound);
        check(await new HostedUpdateClient(absent, HostedUpdateClient.ParseOrigin(origin), "x86", "normal").CheckAsync(0, CancellationToken.None) is null,
            "unpublished hosted slot has no available update");
        using var redirected = new HttpClient(new UpdateTransportTests.Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://other.example.test/");
            return Task.FromResult(response);
        }));
        await UpdateTransportTests.Reject(() => new HostedUpdateClient(redirected, HostedUpdateClient.ParseOrigin(origin), "x64", "normal").CheckAsync(0, CancellationToken.None),
            check, "hosted feed refuses redirects");
        var currentArchitecture = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64";
        var oppositeArchitecture = currentArchitecture == "x86" ? "x64" : "x86";
        var package = UpdateV2Tests.Package(root, launcher: File.ReadAllBytes(Environment.ProcessPath!));
        var hash = UpdateTransportTests.Hash(package);
        UpdateRelease Download(string arch) => new(id, "dev-test", DateTimeOffset.UtcNow, false,
            new Uri($"{origin}/v1/{arch}/normal/{hash}.zip"), package.Length, hash);
        using var download = UpdateTransportTests.Client(package);
        var approved = await new HostedUpdateClient(download, HostedUpdateClient.ParseOrigin(origin), currentArchitecture, "normal")
            .DownloadAsync(Download(currentArchitecture), root, null, CancellationToken.None);
        check(approved.Manifest.Version == "dev-test", "hosted download accepts verified ZIP and matching executable architecture");
        await UpdateTransportTests.Reject(() => new HostedUpdateClient(download, HostedUpdateClient.ParseOrigin(origin), oppositeArchitecture, "normal")
            .DownloadAsync(Download(oppositeArchitecture), root, null, CancellationToken.None), check, "hosted download rejects wrong-architecture executable in valid ZIP");
        await UpdateTransportTests.Reject(() => new HostedUpdateClient(redirected, HostedUpdateClient.ParseOrigin(origin), currentArchitecture, "normal")
            .DownloadAsync(Download(currentArchitecture), root, null, CancellationToken.None), check, "hosted package refuses redirects");
        const string plainOrigin = "http://games.example.test:18083";
        var plainRelease = Download(currentArchitecture) with
        {
            DownloadUrl = new Uri($"{plainOrigin}/v1/{currentArchitecture}/normal/{hash}.zip")
        };
        var plainClient = new HostedUpdateClient(download, HostedUpdateClient.ParseOrigin(plainOrigin), currentArchitecture, "normal");
        check((await plainClient.DownloadAsync(plainRelease, root, null, CancellationToken.None)).Manifest.Version == "dev-test",
            "explicit HTTP origin downloads and verifies a matching update");
        await UpdateTransportTests.Reject(() => plainClient.DownloadAsync(Download(currentArchitecture), root, null, CancellationToken.None),
            check, "HTTP update origin rejects a different scheme and port");
        foreach (var invalid in new[] { "ftp://games.example.test:18443", origin + "/updates", origin + "?feed=other", "https://user@games.example.test:18443" })
        {
            try { HostedUpdateClient.ParseOrigin(invalid); check(false, "hosted origin rejects unsupported or ambiguous URL"); }
            catch (ArgumentException) { check(true, "hosted origin rejects unsupported or ambiguous URL"); }
        }
        var installation = UpdateV2Tests.Installation(root);
        using var session = new UpdateSession(installation, new LauncherOptions { UpdateServiceUrl = origin, UpdateRepository = "unused" }, UpdateTransportTests.Client(metadata));
        check((await session.CheckAsync(7, CancellationToken.None))?.Id == id, "hosted update session ignores legacy GitHub repository and IDs");
        UpdateV2Tests.InstallMetadata(installation, version: "dev-test");
        check(await session.CheckAsync(7, CancellationToken.None) is null && session.ReadState()?.ReleaseId == id,
            "matching installed version adopts hosted release identity without download");
        using var optedOut = new UpdateSession(installation, new LauncherOptions { DisableGitHubUpdates = true, UpdateRepository = "unused" },
            new HttpClient(new UpdateTransportTests.Handler((_, _) => throw new InvalidOperationException("no network expected"))));
        check(await optedOut.CheckAsync(0, CancellationToken.None) is null, "generated client without configured origin does not fall back to GitHub");
    }
}
