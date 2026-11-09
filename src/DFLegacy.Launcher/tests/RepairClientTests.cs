using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using DFLegacy.Launcher.Updates;
using Core=DFLegacy.UpdateCore;

internal static class RepairClientTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if(args.Length!=2)return null;
        if(args[0]=="--repair-lock-fixture")
        {
            using var held=File.Open(args[1],FileMode.Open,FileAccess.Read,FileShare.Read);
            Console.WriteLine("ready");Console.Out.Flush();await Console.In.ReadLineAsync();return 0;
        }
        if(args[0]!="--repair-fixture-child")return null;
        var provider=FixtureProvider.Create();
        var result=await RepairClient.RunAsync(new(args[1]),provider);
        Console.WriteLine(result.Message);return result.Success?0:1;
    }
    internal static async Task Run(string scratch,Action<bool,string> check)
    {
        var root=Path.Combine(scratch,"independent-repair");Directory.CreateDirectory(root);
        var broken=Path.Combine(root,"DFLegacy.Launcher.exe");File.WriteAllText(broken,"not a valid executable");
        File.WriteAllText(Path.Combine(root,"client-package.json"),"broken prior metadata");
        const string config="{\"UpdateProvider\":\"gitee\",\"UpdateChannel\":\"repair-test\",\"Account\":\"private-account\",\"Password\":\"private-password\",\"UnknownSetting\":77}";
        File.WriteAllText(Path.Combine(root,"launcher.json"),config);
        var request=RepairClient.ParseArguments(["--repair-client",root,"--update-provider","gitee","--update-channel","repair-test","--no-dialog"]);
        check(request.Directory==root&&request.Provider=="gitee"&&request.NoDialog,"repair arguments use explicit installation/source without launching game");
        try { RepairClient.EnsureExternal(broken,root);check(false,"repair cannot run from its managed target"); }catch(InvalidOperationException){check(true,"self repair requires independent external executable");}
        var provider=FixtureProvider.Create();
        var phases=new List<string>();
        var result=await RepairClient.RunAsync(new(root),provider,new InlineProgress(p=>phases.Add(p.Phase)));
        check(result.Success&&!result.Offline&&File.ReadAllText(broken)=="repaired launcher bytes","independent repair replaces corrupt non-executable launcher");
        check(File.ReadAllText(Path.Combine(root,"launcher.json"))==config,"independent repair preserves player configuration bytes");
        check(phases.Contains("verify")&&phases.Contains("download")&&phases.Contains("install"),"repair reports actual validation/download/install phases");
        provider.Offline=true;result=await RepairClient.RunAsync(new(root),provider);
        check(result.Success&&result.Offline&&result.Message.Contains("未确认线上最新版本"),"offline repair outcome distinguishes verified cached installation");
        File.WriteAllText(broken,"corrupt");
        try { await RepairClient.RunAsync(new(root),provider);check(false,"offline corrupted installation cannot claim repair"); }catch(InvalidDataException){check(true,"offline corruption blocked");}
        File.WriteAllText(Path.Combine(root,"launcher.json"),config.Replace("77","77,\"DisableGitHubUpdates\":true"));provider.Offline=false;provider.FindCalls=0;
        try { await RepairClient.RunAsync(new(root),provider);check(false,"disabled-network corrupt repair cannot be reported as success"); }catch(InvalidDataException){check(provider.FindCalls==0,"disabled networking is respected without remote discovery");}
        File.WriteAllText(Path.Combine(root,"launcher.json"),config);
        // The separate fixture process executes the real repair orchestrator;
        // the installed launcher remains invalid PE until replacement finishes.
        var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        start.ArgumentList.Add("--repair-fixture-child");start.ArgumentList.Add(root);
        using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));await FileLockOwners.WaitForExitConfirmedAsync(child,deadline.Token);
        check(child.ExitCode==0&&File.ReadAllText(broken)=="repaired launcher bytes","separate process repairs without executing broken launcher: "+await output+await errors);
        var receipt=new Core.InstallationStore(root).ReadInstalled()!;
        var selected=RepairClient.ReadSettings(new(root));
        check(selected.Source==receipt.Release.Source,"repair source remains tied to installed receipt and matching architecture");
        File.WriteAllText(broken,"still corrupt before consent");
        var lockStart=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        lockStart.ArgumentList.Add("--repair-lock-fixture");lockStart.ArgumentList.Add(broken);
        using var holder=Process.Start(lockStart)!;_ = holder.SafeHandle;
        try
        {
            check(await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="ready","repair lock fixture is ready");
            var asked=false;
            try
            {
                await RepairClient.RunAsync(new(root),FixtureProvider.Create(),confirmClose:(owners,_)=>{asked=owners.Any(o=>o.ProcessId==holder.Id);return Task.FromResult(false);});
                check(false,"declined file-occupancy confirmation must prevent repair commit");
            }
            catch(OperationCanceledException ex)
            {check(asked&&!holder.HasExited&&File.ReadAllText(broken)=="still corrupt before consent"&&ex.Message.Contains(holder.Id.ToString()),"declining process closure keeps process and corrupt bytes, and names actual lock owner");}
        }
        finally {await holder.StandardInput.WriteLineAsync("release");await holder.StandardInput.FlushAsync();await FileLockOwners.WaitForExitConfirmedAsync(holder,deadline.Token);}
        var legacyPending=Path.Combine(root,".dflegacy","pending.json");File.WriteAllText(legacyPending,"old transaction must be recovered first");
        provider=FixtureProvider.Create();var priorBytes=File.ReadAllBytes(broken);
        try { await RepairClient.RunAsync(new(root),provider);check(false,"V4 repair must not install across an unresolved legacy V3 transaction"); }
        catch(InvalidDataException) {check(provider.FindCalls==0&&File.ReadAllBytes(broken).AsSpan().SequenceEqual(priorBytes)&&File.Exists(legacyPending),"legacy pending transaction blocks discovery and preserves recovery evidence");}
        finally {File.Delete(legacyPending);}
    }
    private sealed class InlineProgress(Action<Core.UpdateProgress> action):IProgress<Core.UpdateProgress>{public void Report(Core.UpdateProgress value)=>action(value);}
    private sealed class FixtureProvider(Core.VerifiedRelease release,Dictionary<string,byte[]> blobs):Core.IReleaseProvider
    {
        public Core.ReleaseSource Source=>release.Source;public string Identity=>Source.Identity;
        internal bool Offline;internal int FindCalls;
        public Task<Core.VerifiedRelease?> FindAsync(CancellationToken token=default){FindCalls++;token.ThrowIfCancellationRequested();return Offline?throw new HttpRequestException("offline fixture"):Task.FromResult<Core.VerifiedRelease?>(release);}
        public Task<HttpResponseMessage> OpenAsync(Core.ResourcePart part,long offset,string? etag,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(blobs[part.Sha256])});
        internal static FixtureProvider Create()
        {
            var source=new Core.ReleaseSource("gitee","firekula/DFLegacy","repair-test",System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.X86?"x86":"x64","normal");
            var blobs=new Dictionary<string,byte[]>();
            string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
            Core.ManagedFile File(string path,string text){var bytes=System.Text.Encoding.UTF8.GetBytes(text);var hash=Hash(bytes);blobs[hash]=bytes;return new(path,bytes.Length,hash,[new(hash,bytes.Length,"v4-repair-test",hash+".bin")]);}
            var manifest=new Core.ClientManifest(4,"v4-repair-test",new string('a',40),source.Architecture,"normal","repair-test",1,0,[File("DFLegacy.Launcher.exe","repaired launcher bytes"),File("Script.pvf","pvf fixture")],[],[]);
            var raw=Core.ManifestCodec.Serialize(manifest);return new(new(source,manifest.Version,Hash(raw),raw),blobs);
        }
    }
}
