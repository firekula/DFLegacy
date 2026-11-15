using System.Net;
using System.Net.Http;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;

internal static class UpdateSessionVersionTests
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var target=UpdateInstallerTests.Installation(root);
        UpdateV2Tests.InstallMetadata(target,version:"dev-test");
        var tag="dev-test"; long releaseId=7; var requests=0;
        var assetName=Environment.Is64BitProcess?"DFLegacy-client-update-v2-win-x64.zip":"DFLegacy-client-update-v2-win-x86.zip";
        using var http=new HttpClient(new UpdateTransportTests.Handler((request,_)=>
        {
            requests++;
            if(request.RequestUri!.Host!="api.github.com") throw new Exception("Same-version startup must never download an archive");
            var feed=new[]{new {id=releaseId,tag_name=tag,published_at="2026-11-01T00:00:00Z",draft=false,prerelease=true,
                assets=new[]{new {name=assetName,size=100,digest="sha256:"+new string('a',64),browser_download_url="https://github.com/firekula/DFLegacy/releases/download/"+tag+"/"+assetName}}}};
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(feed))});
        }));
        using var session=new UpdateSession(target,new LauncherOptions(),http);
        check(await session.CheckAsync(0,CancellationToken.None)==null && requests==1,"fresh full package matching GitHub version goes straight to login without downloading to discover it is current");
        check(session.ReadState()?.ReleaseId==7,"recognizing installed version records a monotonic release floor without ZIP download");
        releaseId=6; tag="dev-old";
        check(await session.CheckAsync(session.ReadState()?.ReleaseId??0,CancellationToken.None)==null,"a later feed omitting the installed version cannot automatically downgrade it");
        tag="dev-new"; releaseId=8;
        check((await session.CheckAsync(0,CancellationToken.None))?.Version==tag,"different release remains available for verified update");
        var before=requests; File.AppendAllText(Path.Combine(target,"DFLegacy.Client.dll"),"tampered");
        await UpdateTransportTests.Reject(async()=>await session.CheckAsync(0,CancellationToken.None),check,"same-version optimization never skips installed file integrity validation");
        check(requests==before,"damaged installation fails before public HTTP requests");
        Console.WriteLine("Installed-version startup checks executed");
    }
}
