using System.Net;
using System.Security.Cryptography;
using System.Text;
using DFLegacy.Launcher.Updates;
using Core=DFLegacy.UpdateCore;

internal static class BootstrapUpdateTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if(args.Length<2||args[0]!="--bootstrap-ready-fixture")return null;
        if(args[1]=="fail")return 23;
        if(args[1]=="wait-cancel")
        {
            var directory=Environment.GetEnvironmentVariable("DFLEGACY_TEST_CHILD_ROOT")!;
            using var held=BootstrapLayout.AcquireAppLock(directory);
            File.WriteAllText(Path.Combine(directory,"child-ready"),Environment.ProcessId.ToString());
            await Task.Delay(Timeout.Infinite);return 0;
        }
        if(args[1]=="slow-fail"){BootstrapUpdateClient.SignalReady();await Task.Delay(400);return 23;}
        BootstrapUpdateClient.SignalReady();if(args[1]!="immediate")await Task.Delay(2000);return 0;
    }
    internal static async Task Run(string scratch,Action<bool,string> check)
    {
        var handshake=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        handshake.ArgumentList.Add("--bootstrap-ready-fixture");handshake.ArgumentList.Add("ready");
        await BootstrapUpdateClient.LaunchAndConfirmAsync(handshake,default);
        check(true,"real child confirms readiness through a unique event");
        handshake.ArgumentList[1]="immediate";
        await BootstrapUpdateClient.LaunchAndConfirmAsync(handshake,default);
        check(true,"immediate successful headless completion acknowledges readiness exactly once");
        handshake.ArgumentList[1]="fail";
        try{await BootstrapUpdateClient.LaunchAndConfirmAsync(handshake,default);check(false,"failed UI child cannot claim successful handoff");}
        catch(InvalidOperationException ex){check(ex.Message.Contains("23"),"premature UI exit preserves failure code for recovery fallback");}
        handshake.ArgumentList[1]="slow-fail";handshake.ArgumentList.Add("--headless");
        try{await BootstrapUpdateClient.LaunchAndConfirmAsync(handshake,default);check(false,"headless result waits for actual completion");}
        catch(InvalidOperationException ex){check(ex.Message.Contains("23"),"delayed headless failure is not readiness success");}
        handshake.ArgumentList.RemoveAt(2);handshake.ArgumentList[1]="wait-cancel";
        var cancelRoot=Path.Combine(scratch,"cancel-owned-ui");Directory.CreateDirectory(cancelRoot);handshake.Environment["DFLEGACY_TEST_CHILD_ROOT"]=cancelRoot;
        using(var cancelled=new CancellationTokenSource())
        {
            var pending=BootstrapUpdateClient.LaunchAndConfirmAsync(handshake,cancelled.Token);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(!File.Exists(Path.Combine(cancelRoot,"child-ready"))&&timer.Elapsed<TimeSpan.FromSeconds(15))await Task.Delay(25);
            if(!File.Exists(Path.Combine(cancelRoot,"child-ready")))throw new Exception("Owned child fixture failed to become ready");
            using var owned=System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(cancelRoot,"child-ready"))));_ = owned.SafeHandle;
            cancelled.Cancel();
            try{await pending;check(false,"cancel interrupts UI readiness");}catch(OperationCanceledException){check(true,"cancel interrupts UI readiness");}
            try{check(owned.HasExited,"cancelled unconfirmed child exits before a fallback may start");}
            finally{if(!owned.HasExited)owned.Kill();await FileLockOwners.WaitForExitConfirmedAsync(owned,default);}
            using var unlocked=BootstrapLayout.AcquireAppLock(cancelRoot);check(true,"cancelled child cannot retain UI lock");
        }
        string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
        var arch=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.X86?"x86":"x64";
        var hash=new string('a',64);
        var text=$"[Bootstrap]\r\nSchemaVersion=1\r\nArchitecture={arch}\r\nLauncherSha256={hash}\r\nUpdaterSha256={hash}\r\n";
        check(BootstrapLayout.Parse(Encoding.ASCII.GetBytes(text),arch).LauncherSha256==hash,"strict bootstrap descriptor parses CRLF");
        foreach(var bad in new[]{text.Replace("SchemaVersion=1","SchemaVersion=2"),text+"Architecture="+arch+"\n",text+"[Other]\n",text+"Source=evil\n",text.Replace(hash,hash.ToUpperInvariant()),text.Replace(arch,arch=="x64"?"x86":"x64"),"\ufeff"+text,text.Replace("[Bootstrap]","[bootstrap]"),new string('x',4097)})
        {try{BootstrapLayout.Parse(Encoding.UTF8.GetBytes(bad),arch);check(false,"reject malformed bootstrap metadata");}catch(InvalidDataException){check(true,"reject malformed bootstrap metadata");}}
        try{BootstrapLayout.Parse(Encoding.ASCII.GetBytes(text.Replace("Architecture=","\r\nArchitecture=")),arch);check(false,"blank lines rejected consistently with native grammar");}catch(InvalidDataException){check(true,"blank lines rejected consistently with native grammar");}
        var request=BootstrapUpdateClient.ParseArguments(["--bootstrap-client",scratch,"--","--account","a space \" quote","尾部\\"]);
        check(request.Directory==Path.GetFullPath(scratch)&&request.LauncherArguments.SequenceEqual(new[]{"--account","a space \" quote","尾部\\"}),"bootstrap preserves original arguments without reinterpretation");
        foreach(var bad in new[]{new[]{"--bootstrap-client",scratch},new[]{"--bootstrap-client",scratch,"--bad"},new[]{"--bootstrap-client",scratch,"--","--bootstrap-verified"}})
        {try{BootstrapUpdateClient.ParseArguments(bad);check(false,"reject ambiguous internal flags");}catch(ArgumentException){check(true,"reject ambiguous internal flags");}}
        var root=Path.Combine(scratch,"自动修复 client");Directory.CreateDirectory(root);
        const string config="{\"UpdateProvider\":\"gitea\",\"UpdateChannel\":\"gitea-test\",\"Account\":\"private\",\"Password\":\"private\",\"Other\":42}";
        File.WriteAllText(Path.Combine(root,"launcher.json"),config);
        var files=new Dictionary<string,byte[]>{[BootstrapLayout.EntryName]="entry bytes"u8.ToArray(),[BootstrapLayout.AppName]="launcher app"u8.ToArray(),[BootstrapLayout.UpdaterName]="updater bytes"u8.ToArray(),["DFLegacy.Client.dll"]="client dll"u8.ToArray(),["resource.bin"]="resource content"u8.ToArray()};
        files[BootstrapLayout.DescriptorName]=Encoding.ASCII.GetBytes(text.Replace("LauncherSha256="+hash,"LauncherSha256="+Hash(files[BootstrapLayout.AppName])).Replace("UpdaterSha256="+hash,"UpdaterSha256="+Hash(files[BootstrapLayout.UpdaterName])));
        var provider=new FixtureProvider(arch,files);
        var legacy=new FixtureProvider(arch,files.Where(f=>f.Key!=BootstrapLayout.AppName).ToDictionary());
        var old=Encoding.ASCII.GetBytes("original fixed entry");File.WriteAllBytes(Path.Combine(root,BootstrapLayout.EntryName),old);
        try{await RepairClient.RunAsync(new(root),legacy,validateManifest:BootstrapLayout.RequireManifest);check(false,"legacy target cannot destroy fixed entry");}
        catch(InvalidDataException){check(File.ReadAllBytes(Path.Combine(root,BootstrapLayout.EntryName)).SequenceEqual(old)&&legacy.OpenCalls==0&&!Core.InstallTransaction.HasPending(root),"layout guard rejects legacy manifest before downloads or writes");}
        await RepairClient.RunAsync(new(root),provider,validateManifest:BootstrapLayout.RequireManifest);
        check(File.ReadAllText(Path.Combine(root,"launcher.json"))==config,"bootstrap repair preserves unknown/user config bytes");
        await BootstrapLayout.ValidateInstalledAsync(root,arch);
        var validationPath=Path.Combine(root,".dflegacy","v4","validated.json");
        File.Delete(validationPath);
        try
        {
            await BootstrapLayout.ValidateInstalledAsync(root,arch);
            check(File.Exists(validationPath),"first UI handoff from the v0.1.8 updater creates a fully verified validation record");
        }
        catch(InvalidDataException){check(false,"first UI handoff accepts the old updater's complete installation without a validation record");}
        // Keep later cases independent even when the preceding RED case failed.
        await RepairClient.RunAsync(new(root),provider,validateManifest:BootstrapLayout.RequireManifest);
        File.WriteAllText(validationPath,"{");
        try
        {
            await BootstrapLayout.ValidateInstalledAsync(root,arch);
            check(new Core.InstallationStore(root).HasValidatedReceipt(new Core.InstallationStore(root).ReadInstalled()!),"corrupt validation record is replaced only after complete verification");
        }
        catch(InvalidDataException){check(false,"corrupt validation record can be rebuilt from the complete trusted installation");}
        await RepairClient.RunAsync(new(root),provider,validateManifest:BootstrapLayout.RequireManifest);
        using(var assetLock=new FileStream(Path.Combine(root,"resource.bin"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            await BootstrapLayout.ValidateInstalledAsync(root,arch);
            check(true,"managed UI startup reads completed record without opening asset content");
            var quickLaunches=0;
            var ordinary=BootstrapUpdateClient.ParseArguments(["--bootstrap-client",root,"--"]);
            try
            {
                await BootstrapUpdateClient.RunAsync(ordinary,provider,launch:(_,_)=>{quickLaunches++;return Task.CompletedTask;});
                check(quickLaunches==1,"unchanged v0.1.8 native entry can open the UI without re-reading resource contents");
            }
            catch(Exception ex) when(ex is IOException or InvalidOperationException){check(false,"ordinary bootstrap must reuse the completed receipt while resource contents are locked");}
            var manual=BootstrapUpdateClient.ParseArguments(["--bootstrap-client",root,"--","--bootstrap-wait-parent",int.MaxValue.ToString(),"1"]);
            try{await BootstrapUpdateClient.RunAsync(manual,provider,launch:(_,_)=>{check(false,"manual check cannot execute UI before full verification");return Task.CompletedTask;});check(false,"manual check must read locked resource contents");}
            catch(Exception ex) when(ex is IOException or InvalidOperationException){check(true,"manual check propagated by the old native entry retains full verification");}
            try{await RepairClient.RunAsync(new(root),provider);check(false,"explicit repair must still read locked resource contents");}
            catch(Exception ex) when(ex is IOException or InvalidOperationException){check(true,"explicit repair cannot bless unreadable resource contents");}
        }
        await RepairClient.RunAsync(new(root),provider,validateManifest:BootstrapLayout.RequireManifest);
        File.Delete(validationPath);File.WriteAllText(Path.Combine(root,"resource.bin"),"corrupt resource");
        try{await BootstrapLayout.ValidateInstalledAsync(root,arch);check(false,"old updater handoff cannot bless corrupt resources without a validation record");}
        catch(InvalidDataException){check(!File.Exists(validationPath),"failed first handoff leaves no validation record");}
        await RepairClient.RunAsync(new(root),provider,validateManifest:BootstrapLayout.RequireManifest);
        var validConfig=File.ReadAllText(Path.Combine(root,"launcher.json"));
        File.WriteAllText(Path.Combine(root,"launcher.json"),"{\"UpdateProvider\":\"github\",\"UpdateChannel\":\"stable\"}");
        try{await BootstrapLayout.ValidateInstalledAsync(root,arch);check(false,"record fast path cannot authorize a mismatched configured source");}
        catch(InvalidDataException){check(true,"UI handoff validates configured source even with a completed record");}
        File.WriteAllText(Path.Combine(root,"launcher.json"),validConfig);
        provider.InvalidManifest=true;
        try{await BootstrapUpdateClient.RunAsync(new(root,[]),provider,launch:(_,_)=>{check(false,"invalid manifest cannot launch UI through record fast path");return Task.CompletedTask;});check(false,"invalid online manifest must fail closed");}
        catch(InvalidDataException){check(true,"completed record does not suppress online manifest integrity failures");}
        provider.InvalidManifest=false;
        var pendingPath=Path.Combine(root,".dflegacy","v4","pending.json");
        File.WriteAllText(pendingPath,"{}");
        try{await BootstrapLayout.ValidateInstalledAsync(root,arch);check(false,"pending installation cannot use a completed validation record");}
        catch(InvalidDataException){check(true,"UI handoff refuses incomplete transactions before any receipt fast path");}
        try{await BootstrapUpdateClient.RunAsync(new(root,[]),provider,launch:(_,_)=>{check(false,"malformed pending transaction cannot execute UI");return Task.CompletedTask;});check(false,"malformed transaction must fail closed");}
        catch(InvalidDataException){check(File.Exists(pendingPath),"ordinary bootstrap preserves invalid pending recovery evidence and refuses launch");}
        File.Delete(pendingPath);
        var installed=new Core.InstallationStore(root).ReadInstalled()!;
        var changedEntry=installed.Release.Manifest with {Files=installed.Release.Manifest.Files.Select(f=>f.Path==BootstrapLayout.EntryName?f with{Sha256=new string('f',64)}:f).ToArray()};
        try{BootstrapLayout.RequireStableEntry(root,changedEntry);check(false,"fixed running entry cannot be replaced during repair");}catch(InvalidDataException){check(true,"changed native entry is rejected before repair");}
        var holdStart=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,CreateNoWindow=true};
        holdStart.ArgumentList.Add("--repair-lock-fixture");holdStart.ArgumentList.Add(Path.Combine(root,BootstrapLayout.EntryName));
        using(var holder=System.Diagnostics.Process.Start(holdStart)!)
        {
            try
            {
                check(await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="ready","native supervisor fixture owns unchanged entry");
                File.Delete(Path.Combine(root,BootstrapLayout.AppName));
                await BootstrapUpdateClient.RunAsync(new(root,[]),provider,launch:(_,_)=>Task.CompletedTask);
                check(!holder.HasExited,"repair never asks to terminate unchanged native supervisor");
            }
            finally{await holder.StandardInput.WriteLineAsync("release");await holder.StandardInput.FlushAsync();await holder.WaitForExitAsync();}
        }
        var launches=0;
        async Task Launch(System.Diagnostics.ProcessStartInfo start,CancellationToken token)
        {
            await BootstrapLayout.ValidateInstalledAsync(root,arch,token);
            check(start.FileName==Path.Combine(root,BootstrapLayout.AppName)&&start.WorkingDirectory==root&&start.ArgumentList.Take(2).SequenceEqual(new[]{"--bootstrap-verified","--"}),"UI handoff fixed executable/root/flags after full verification");launches++;
        }
        File.Delete(Path.Combine(root,BootstrapLayout.AppName));
        await BootstrapUpdateClient.RunAsync(new(root,[]),provider,launch:Launch);
        check(launches==1&&File.ReadAllBytes(Path.Combine(root,BootstrapLayout.AppName)).SequenceEqual(files[BootstrapLayout.AppName]),"missing UI repaired before UI handoff");
        provider.Offline=true;var offline=await BootstrapUpdateClient.RunAsync(new(root,[]),provider,launch:Launch);
        check(offline.Offline&&offline.Message.Contains("未确认线上最新版本")&&launches==2,"fully verified offline startup explicitly reports freshness unknown");
        File.WriteAllText(Path.Combine(root,BootstrapLayout.AppName),"same length?");
        try{await BootstrapUpdateClient.RunAsync(new(root,[]),provider,launch:Launch);check(false,"offline corrupt UI must block");}catch(InvalidDataException){check(launches==2,"offline corrupt files never execute UI");}
        provider.Offline=false;await RepairClient.RunAsync(new(root),provider,validateManifest:BootstrapLayout.RequireManifest);
        // A valid manifest hash is insufficient if its descriptor describes a
        // different UI. The repaired installation must not execute that UI.
        var mismatch=new Dictionary<string,byte[]>(files){[BootstrapLayout.DescriptorName]=Encoding.ASCII.GetBytes(text)};
        var mismatchRoot=Path.Combine(scratch,"mismatched-layout");Directory.CreateDirectory(mismatchRoot);File.WriteAllText(Path.Combine(mismatchRoot,"launcher.json"),config);
        File.WriteAllBytes(Path.Combine(mismatchRoot,BootstrapLayout.EntryName),files[BootstrapLayout.EntryName]);
        try{await BootstrapUpdateClient.RunAsync(new(mismatchRoot,[]),new FixtureProvider(arch,mismatch),launch:Launch);check(false,"descriptor must match full receipt");}
        catch(InvalidDataException){check(launches==2,"descriptor identity disagreement blocks UI even after file hashes pass");}
        using(var held=BootstrapLayout.AcquireAppLock(root))
        {try{using var other=BootstrapLayout.AcquireAppLock(root);check(false,"duplicate UI lock blocked");}catch(IOException){check(true,"app lock exclusive");}}
        using(var released=BootstrapLayout.AcquireAppLock(root)){check(true,"app lock released by disposal");}
        File.WriteAllText(Path.Combine(root,"launcher.json"),"{bad config");var calls=provider.FindCalls;
        try{await BootstrapUpdateClient.RunAsync(new(root,[]),provider,launch:Launch);check(false,"malformed config must not reset");}catch(System.Text.Json.JsonException){check(launches==2&&File.ReadAllText(Path.Combine(root,"launcher.json"))=="{bad config"&&provider.FindCalls==calls,"config corruption retained and no launch/discovery");}
    }
    private sealed class FixtureProvider:Core.IReleaseProvider
    {
        private readonly Core.VerifiedRelease release;private readonly Dictionary<string,byte[]> blobs=[];
        internal bool Offline,InvalidManifest;internal int OpenCalls,FindCalls;
        public Core.ReleaseSource Source=>release.Source;public string Identity=>Source.Identity;
        internal FixtureProvider(string arch,Dictionary<string,byte[]> files)
        {
            string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
            var source=new Core.ReleaseSource("gitea","firekula/DFLegacy","gitea-test",arch,"normal");
            var managed=files.Select(f=>{var hash=Hash(f.Value);blobs[hash]=f.Value;return new Core.ManagedFile(f.Key,f.Value.Length,hash,[new(hash,f.Value.Length,"v0.1.8-giteatest.3",hash+".bin")]);}).ToArray();
            var manifest=new Core.ClientManifest(4,"v0.1.8-giteatest.3",new string('b',40),arch,"normal","gitea-test",3,0,managed,[],[]);
            var bytes=Core.ManifestCodec.Serialize(manifest);release=new(source,manifest.Version,Hash(bytes),bytes);
        }
        public Task<Core.VerifiedRelease?> FindAsync(CancellationToken token=default){FindCalls++;if(Offline)throw new System.Net.Http.HttpRequestException("offline fixture");return Task.FromResult<Core.VerifiedRelease?>(InvalidManifest?release with {ManifestSha256=new string('f',64)}:release);}
        public Task<System.Net.Http.HttpResponseMessage> OpenAsync(Core.ResourcePart part,long offset,string? etag,CancellationToken token){OpenCalls++;return Task.FromResult(new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK){Content=new System.Net.Http.ByteArrayContent(blobs[part.Sha256])});}
    }
}
