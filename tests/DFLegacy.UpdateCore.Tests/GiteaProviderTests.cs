using System.Net;
using System.Text.Json;

namespace DFLegacy.UpdateCore.Tests;

internal static class GiteaProviderTests
{
    internal static async Task Run()
    {
        const string origin="http://firekula.site:3000";
        var source=new ReleaseSource("gitea","firekula/DFLegacy","gitea-test","x86","test");
        source.Validate();
        TestRun.Reject(()=> (source with { Channel="stable" }).Validate(),"Gitea remains an explicit test channel");
        var manifest=ManifestTests.Sample() with { Channel="gitea-test" };
        var raw=ManifestCodec.Serialize(manifest);
        var name=source.ManifestAssetName;
        var url=$"{origin}/firekula/DFLegacy/releases/download/{manifest.Version}/{name}";
        var assetUrl=url;
        var resource=TestRun.Bytes("abc");
        var resourceUrl=origin+"/attachments/12345678-1234-1234-1234-123456789012";
        var requests=new List<Uri>();
        var olderPage=false; var endlessPages=false; var draft=false; var missing=false; string? redirect=null;
        using var http=new HttpClient(new Handler(request=>
        {
            var uri=request.RequestUri!; requests.Add(uri);
            TestRun.Check(request.Headers.Authorization==null && !request.Headers.Contains("Cookie"),"Gitea requests anonymous");
            TestRun.Check(uri.GetLeftPart(UriPartial.Authority)==origin,"Gitea requests stay on pinned origin");
            if(uri.AbsoluteUri==url)
            {
                if(redirect!=null) return new(HttpStatusCode.Redirect){Headers={Location=new Uri(redirect)}};
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(raw)};
            }
            if(uri.AbsolutePath=="/attachments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(raw)};
            if(uri.AbsoluteUri==resourceUrl)
            {
                TestRun.Check(request.Headers.Range?.Ranges.Single().From==1,"Gitea Range retained");
                TestRun.Check(request.Headers.IfRange?.EntityTag?.Tag=="\"fixture\"","Gitea ETag retained");
                var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(resource[1..])};
                response.Content.Headers.ContentRange=new(1,2,3); return response;
            }
            object Attachment(int id,string filename,string download,int size)=>new{id,name=filename,size,browser_download_url=download};
            var assets=missing?Array.Empty<object>():new[]{Attachment(10,name,assetUrl,raw.Length)};
            var row=new{id=1,tag_name=manifest.Version,draft,assets};
            if(uri.AbsolutePath.EndsWith("/releases"))
            {
                TestRun.Check(uri.Query.Contains("limit=50") && !uri.Query.Contains("per_page"),"Gitea uses its actual maximum page size 50");
                if(endlessPages) return Json(Enumerable.Repeat(row,50).ToArray());
                if(olderPage && uri.Query.Contains("page=1")) return Json(Enumerable.Range(0,50).Select(i=>new{id=100+i,tag_name="old-"+i,draft=false,assets=Array.Empty<object>()}).ToArray());
                return Json(new[]{row});
            }
            if(uri.AbsolutePath.EndsWith("/releases/tags/v4-test")) return Json(new{id=2,tag_name="v4-test",draft=false,assets=new[]{Attachment(11,"resource.bin",resourceUrl,3)}});
            throw new Exception("Unexpected Gitea API path "+uri.AbsolutePath);
        })){Timeout=Timeout.InfiniteTimeSpan};
        var provider=new ReleaseProvider(http,source);
        TestRun.Check((await provider.FindAsync())?.ManifestSha256==TestRun.Hash(raw),"Gitea manifest binds exact raw bytes");
        olderPage=true;
        TestRun.Check((await provider.FindAsync())?.ManifestSha256==TestRun.Hash(raw) && requests.Any(u=>u.Query.Contains("page=2")),"Gitea full 50-row page does not truncate discovery");
        olderPage=false; endlessPages=true;
        var incompleteRejected=false;
        try { await provider.FindAsync(); } catch(IOException) { incompleteRejected=true; }
        TestRun.Check(incompleteRejected,"A candidate on a full final page cannot certify latest version"); endlessPages=false;
        missing=true; TestRun.Check(await provider.FindAsync()==null,"Gitea absent manifest ignored"); missing=false;
        draft=true;TestRun.Check(await provider.FindAsync()==null,"Gitea draft invisible");draft=false;
        foreach(var bad in new[]{url.Replace(":3000",":3001"),url.Replace("firekula.site","evil.test"),url.Replace("/firekula/DFLegacy/","/other/repo/"),url+"?token=bad",url+"#bad",url.Replace("http://","http://user:secret@"),origin+"/api/v1/user",url.Replace("/releases/download/","/releases/download/other/")})
        { assetUrl=bad;await TestRun.RejectAsync(async()=>await provider.FindAsync(),"Gitea attachment boundary"); }
        assetUrl=url;
        redirect=origin+"/attachments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        TestRun.Check((await provider.FindAsync())?.ManifestSha256==TestRun.Hash(raw),"Gitea source-bound attachment UUID redirect");
        foreach(var bad in new[]{"http://evil.test/attachments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",origin+"/api/v1/user",origin+"/attachments/not-a-uuid",redirect+"?token=bad",redirect!.Replace(":3000",":3001")})
        { redirect=bad;await TestRun.RejectAsync(async()=>await provider.FindAsync(),"Gitea redirect boundary"); }
        redirect=null;
        var part=new ResourcePart(TestRun.Hash(resource),3,"v4-test","resource.bin");
        for(var i=0;i<2;i++) using(await provider.OpenAsync(part,1,"\"fixture\"",default)){}
        TestRun.Check(requests.Count(u=>u.AbsolutePath.EndsWith("/releases/tags/v4-test"))==1,"Gitea immutable resource metadata reused");
        http.DefaultRequestHeaders.Authorization=new("Bearer","fixture");
        await TestRun.RejectAsync(async()=>await provider.FindAsync(),"Gitea credentials must never reach players' resource requests");
    }
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value))};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(action(request));}
}
