using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DFLegacy.Launcher.Updates;

internal static class UpdateTransportTests
{
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static byte[] Package(string variant = "valid")
    {
        var binary = Encoding.UTF8.GetBytes("new executable"); var dll = Encoding.UTF8.GetBytes("new dll");
        var files = new[] { new { path = "DFLegacy.Launcher.exe", sha256 = Hash(binary), size = binary.Length }, new { path = "ijl15.dll", sha256 = variant == "hash" ? new string('0',64) : Hash(dll), size = dll.Length + (variant == "size" ? 1 : 0) } };
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string name, byte[] bytes, bool link = false) { var entry = zip.CreateEntry(name); if (link) entry.ExternalAttributes = (0xA000 | 0x1FF) << 16; using var output = entry.Open(); output.Write(bytes); }
            Add("update-manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = variant == "schema" ? 2 : 1, version = "dev-test", commit = new string('a',40), files }));
            Add("DFLegacy.Launcher.exe", binary, variant == "symlink");
            if (variant != "missing") Add("ijl15.dll", dll);
            if (variant == "extra") Add("Config.ini", [1]);
            if (variant == "duplicate") Add("IJL15.DLL", dll);
            if (variant == "traversal") Add("../escape", [1]);
        }
        return stream.ToArray();
    }
    internal static UpdateRelease Release(byte[] bytes, long id = 7) => new(id,"dev-test",DateTimeOffset.UtcNow,true,new Uri("https://github.com/firekula/DFLegacy/releases/download/dev-test/DFLegacy-client-update-win-x64.zip"),bytes.Length,Hash(bytes));
    internal sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request,token); }
    internal static HttpClient Client(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler((_,_) => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) })));
    internal static HttpClient PagedPreviews(Action<int> requested, int fullPages=3) => new(new Handler((request,_) =>
    {
        var page=int.Parse(request.RequestUri!.Query.Split("page=").Last()); requested(page);
        var entries=Enumerable.Range(0,page<=fullPages?100:page==4?1:0).Select(i=>new {
            id=page==4?700:1400-page*100-i,tag_name=page==4?"stable":"dev-"+(page*100+i),
            published_at="2026-01-01T00:00:00Z",draft=false,prerelease=page!=4,
            assets=new[]{new {name=GitHubUpdateClient.AssetName,size=12,browser_download_url="https://github.com/firekula/DFLegacy/releases/download/stable/"+GitHubUpdateClient.AssetName,digest="sha256:"+new string('a',64)}}
        });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(entries)) });
    }));
    internal static async Task Reject(Func<Task> action, Action<bool,string> check, string name)
    { try { await action(); check(false,name); } catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or OperationCanceledException or IOException or InvalidOperationException) {} }
    internal static async Task Run(string root, Action<bool,string> check)
    {
        byte[] Feed() => JsonSerializer.SerializeToUtf8Bytes(new[] {
            new { id=5,tag_name="zzz",published_at="2026-01-01T00:00:00Z",draft=false,prerelease=false,assets=new[]{new {name="DFLegacy-client-update-win-x64.zip",size=12,browser_download_url="https://github.com/firekula/DFLegacy/releases/download/zzz/DFLegacy-client-update-win-x64.zip",digest="sha256:"+new string('a',64)}} },
            new { id=7,tag_name="aaa",published_at="2026-02-01T00:00:00Z",draft=false,prerelease=true,assets=new[]{new {name="DFLegacy-client-update-win-x64.zip",size=12,browser_download_url="https://github.com/firekula/DFLegacy/releases/download/aaa/DFLegacy-client-update-win-x64.zip",digest="sha256:"+new string('a',64)}} },
            new { id=9,tag_name="draft",published_at="2026-03-01T00:00:00Z",draft=true,prerelease=false,assets=new[]{new {name="DFLegacy-client-update-win-x64.zip",size=12,browser_download_url="https://github.com/firekula/DFLegacy/releases/download/draft/DFLegacy-client-update-win-x64.zip",digest="sha256:"+new string('a',64)}} }
        });
        using var http = Client(Feed());
        var client = new GitHubUpdateClient(http,"firekula/DFLegacy");
        check((await client.CheckAsync())?.Id == 7,"release selection uses publication time and ignores drafts");
        check((await new GitHubUpdateClient(http,"firekula/DFLegacy",false).CheckAsync())?.Id == 5,"stable feed excludes preview");
        check(await client.CheckAsync(7) is null,"recorded release prevents downgrade/repeat");
        var pages=new List<int>();
        using(var previews=PagedPreviews(pages.Add))
        {
            await Reject(async()=>await new GitHubUpdateClient(previews,"firekula/DFLegacy",false).CheckAsync(),check,"300 previews before eligible stable must produce explicit incomplete search, never false current");
            check(pages.SequenceEqual(new[]{1,2,3}),"incomplete release search retains three-request bound");
            try { check((await new GitHubUpdateClient(previews,"firekula/DFLegacy",true).CheckAsync())?.Id==1300,"bounded full feed retains available eligible update selection"); }
            catch(IOException) { check(false,"bounded full feed must not suppress an available eligible update"); }
        }
        pages.Clear();
        using(var exhausted=PagedPreviews(pages.Add,2))
        {
            check(await new GitHubUpdateClient(exhausted,"firekula/DFLegacy",false).CheckAsync() is null,"two full preview pages followed by empty page definitively has no stable update");
            check(pages.SequenceEqual(new[]{1,2,3}),"definitive exhaustion reads the final empty page");
        }
        using var missing = Client([],HttpStatusCode.NotFound);
        check(await new GitHubUpdateClient(missing,"firekula/DFLegacy").CheckAsync() is null,"empty repository 404 is no update");
        using var limited = Client([],HttpStatusCode.TooManyRequests);
        await Reject(async()=>await new GitHubUpdateClient(limited,"firekula/DFLegacy").CheckAsync(),check,"429 is recoverable error");
        using var stalled = new HttpClient(new Handler(async (_,ct)=>{ await Task.Delay(Timeout.Infinite,ct); return new(HttpStatusCode.OK); }));
        using var cancel = new CancellationTokenSource(60);
        await Reject(async()=>await new GitHubUpdateClient(stalled,"firekula/DFLegacy").CheckAsync(cancellationToken:cancel.Token),check,"cancel terminates stalled HTTP");
        foreach (var variant in new[]{"valid","hash","size","schema","symlink","missing","extra","duplicate","traversal"})
        {
            var bytes=Package(variant); using var download=Client(bytes);
            var transport=new GitHubUpdateClient(download,"firekula/DFLegacy");
            if(variant=="valid") { var prepared=await transport.DownloadAsync(Release(bytes),root); check(File.ReadAllText(Path.Combine(prepared.Directory,"ijl15.dll"))=="new dll","valid package fully extracted"); }
            else await Reject(async()=>await transport.DownloadAsync(Release(bytes),root),check,"reject package "+variant);
        }
        var valid=Package(); using var data=Client(valid);
        await Reject(async()=>await new GitHubUpdateClient(data,"firekula/DFLegacy").DownloadAsync(Release(valid) with { Sha256=new string('0',64)},root),check,"outer digest checked");
        await Reject(async()=>await new GitHubUpdateClient(data,"firekula/DFLegacy").DownloadAsync(Release(valid) with { DownloadUrl=new Uri("https://evil.example/file")},root),check,"untrusted asset origin rejected");
        using var authenticated=Client(valid); authenticated.DefaultRequestHeaders.Authorization=new("Bearer","fixture-secret");
        await Reject(async()=>await new GitHubUpdateClient(authenticated,"firekula/DFLegacy").DownloadAsync(Release(valid),root),check,"credentialed public transport rejected");
        Console.WriteLine("Update transport tests completed");
    }
}
