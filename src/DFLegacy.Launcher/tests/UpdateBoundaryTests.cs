using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text.Json;
using DFLegacy.Launcher.Updates;
internal static class UpdateBoundaryTests
{
    private sealed class Progress(Action<UpdateProgress> report) : IProgress<UpdateProgress> { public void Report(UpdateProgress value)=>report(value); }
    internal static async Task Run(string root,Action<bool,string> check)
    {
        ExpansionLimit(root,check);
        var bytes=UpdateTransportTests.Package();
        var requests=new List<Uri>();
        using var redirect=new HttpClient(new UpdateTransportTests.Handler((request,_)=>
        {
            requests.Add(request.RequestUri!);
            check(request.Headers.Authorization==null && !request.Headers.Contains("Cookie"),"public requests carry no authentication");
            if(requests.Count==1) { var response=new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location=new Uri("https://release-assets.githubusercontent.com/fixture/package"); return Task.FromResult(response); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        }));
        await new GitHubUpdateClient(redirect,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes),root);
        check(requests.Count==2,"trusted release CDN redirect is followed");
        int evilRequests=0;
        using var evil=new HttpClient(new UpdateTransportTests.Handler((_,_)=>
        {
            evilRequests++; var response=new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location=new Uri("https://evil.example/secret"); return Task.FromResult(response);
        }));
        await UpdateTransportTests.Reject(async()=>await new GitHubUpdateClient(evil,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes),root),check,"untrusted redirect is rejected");
        check(evilRequests==1,"untrusted host is never contacted");
        using var cancel=new CancellationTokenSource(); using var http=UpdateTransportTests.Client(bytes);
        var before=Directory.GetDirectories(root,"dflegacy-update-*").Length;
        await UpdateTransportTests.Reject(async()=>await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes),root,new Progress(_=>cancel.Cancel()),cancel.Token),check,"download cancellation propagates");
        check(Directory.GetDirectories(root,"dflegacy-update-*").Length==before,"cancelled download cleans owned staging");
        using var timeoutHttp=new HttpClient(new UpdateTransportTests.Handler(async (_,ct)=>{ await Task.Delay(Timeout.Infinite,ct); return new(HttpStatusCode.OK); }));
        var clock=Stopwatch.StartNew();
        await UpdateTransportTests.Reject(async()=>await new GitHubUpdateClient(timeoutHttp,"firekula/DFLegacy").CheckAsync(),check,"startup check timeout is bounded");
        check(clock.Elapsed.TotalSeconds<7,"startup timeout is at most five seconds plus scheduling");
        await UpdateTransportTests.Reject(async()=>await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes) with { Size=256L*1024*1024+1 },root),check,"advertised ZIP limit is enforced");
        await UpdateTransportTests.Reject(async()=>await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes) with { Size=bytes.Length+1 },root),check,"outer size mismatch is rejected");
        var prepared=await UpdateInstallerTests.Prepare(root);
        var real=UpdateInstallerTests.Installation(root); var link=Path.Combine(root,"junction-install");
        var info=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Arguments=$"/d /c mklink /J \"{link}\" \"{real}\"";
        using(var child=Process.Start(info)!) { await child.WaitForExitAsync(); check(child.ExitCode==0,"owned junction fixture created"); }
        if(Directory.Exists(link))
        {
            try
            {
                var result=UpdateTransaction.Apply(link,prepared,_=>true);
                check(!result.Success && File.ReadAllText(Path.Combine(real,"ijl15.dll"))=="old dll","installation reparse path is rejected");
                await UpdateTransportTests.Reject(async()=>await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes),link),check,"staging reparse path is rejected");
            }
            finally { Directory.Delete(link); }
        }
        var interrupted=UpdateInstallerTests.Installation(root); var stateDir=Path.Combine(interrupted,".dflegacy"); Directory.CreateDirectory(stateDir);
        var id=Guid.NewGuid().ToString("N"); var backup=Path.Combine(interrupted,".dflegacy-backups",id); Directory.CreateDirectory(backup);
        var originals=new List<UpdateFile>();
        foreach(var name in new[]{"DFLegacy.Launcher.exe","ijl15.dll"})
        {
            var original=File.ReadAllBytes(Path.Combine(interrupted,name)); originals.Add(new(name,UpdateTransportTests.Hash(original),original.Length)); File.WriteAllBytes(Path.Combine(backup,name),original);
        }
        var receipt=new UpdateReceipt(id,null,new(7,"dev-test",prepared.Manifest.Files),originals.ToArray());
        UpdateTransaction.WriteJson(Path.Combine(stateDir,"pending.json"),receipt);
        File.WriteAllText(Path.Combine(interrupted,"DFLegacy.Launcher.exe"),"interrupted new exe");
        File.AppendAllText(Path.Combine(prepared.Directory,"ijl15.dll"),"tamper");
        var recovered=UpdateTransaction.Apply(interrupted,prepared,_=>true);
        check(!recovered.Success && File.ReadAllText(Path.Combine(interrupted,"DFLegacy.Launcher.exe"))=="old exe" && File.ReadAllText(Path.Combine(interrupted,"ijl15.dll"))=="old dll","interrupted transaction recovers originals before rejecting invalid next update");
        check(!File.Exists(Path.Combine(stateDir,"pending.json")),"completed recovery clears pending journal");
        Console.WriteLine("Update boundary tests completed");
    }
    private static void ExpansionLimit(string root,Action<bool,string> check)
    {
        byte[] exe=[1,2,3]; byte[] dll=new byte[1024*1024];
        var manifest=new UpdateManifest(1,"dev-test",new string('a',40),[new("DFLegacy.Launcher.exe",UpdateTransportTests.Hash(exe),3),new("ijl15.dll",UpdateTransportTests.Hash([0]),1)]);
        using var buffer=new MemoryStream();
        using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))
        {
            void Add(string name,byte[] bytes){using var output=zip.CreateEntry(name).Open();output.Write(bytes);}
            Add("DFLegacy.Launcher.exe",exe); Add("ijl15.dll",dll); Add("update-manifest.json",JsonSerializer.SerializeToUtf8Bytes(manifest,UpdateSafety.Json));
        }
        var bytes=buffer.ToArray();
        for(int offset=0;offset<bytes.Length-46;offset++)
        {
            if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))!=0x02014b50) continue;
            var length=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset+28));
            if(System.Text.Encoding.UTF8.GetString(bytes,offset+46,length)=="ijl15.dll") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset+24),1);
        }
        var directory=Path.Combine(root,"expansion-limit"); Directory.CreateDirectory(directory); var archive=Path.Combine(directory,"package.zip"); File.WriteAllBytes(archive,bytes);
        try { UpdatePackage.Extract(UpdateTransportTests.Release(bytes),directory,archive); check(false,"dishonest expansion length is rejected"); }
        catch(InvalidDataException) {}
        check(new FileInfo(Path.Combine(directory,"ijl15.dll")).Length<=1,"extractor never writes beyond manifest size even when ZIP headers lie");
    }
}
