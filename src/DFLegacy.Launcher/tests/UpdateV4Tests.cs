using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

internal static class UpdateV4Tests
{
    internal static async Task Run(string scratch, Action<bool,string> check)
    {
        var root = Path.Combine(scratch,"v4-launcher"); Directory.CreateDirectory(root);
        var source = new Core.ReleaseSource("gitee","firekula/DFLegacy","gitee-test",System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.X86?"x86":"x64","normal");
        string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
        var blobs = new Dictionary<string,byte[]>();
        Core.ManagedFile Entry(string name,string content)
        {
            var bytes=System.Text.Encoding.UTF8.GetBytes(content); var hash=Hash(bytes); blobs[hash]=bytes;
            return new(name,bytes.Length,hash,[new(hash,bytes.Length,"v4-test",hash+".bin")]);
        }
        var m = new Core.ClientManifest(4,"v4-test",new string('a',40),source.Architecture,"normal","gitee-test",1,0,
            [Entry("DFLegacy.Launcher.exe","launcher"),Entry("DFLegacy.Client.dll","dll"),Entry("DFLegacy.Diagnostics.exe","helper"),Entry("ImagePacks2/test.npk","npk")],[],[]);
        var raw=Core.ManifestCodec.Serialize(m); var release=new Core.VerifiedRelease(source,m.Version,Hash(raw),raw);
        var provider=new Provider(source,release,blobs);
        var options=new LauncherOptions { UpdateProvider="gitee",UpdateChannel="gitee-test",UpdateServiceUrl="http://127.0.0.1:1" };
        File.WriteAllText(Path.Combine(root,"client-package.json"),"broken old manifest");
        using(var session=new V4UpdateSession(root,options,provider))
        {
            var found=await session.CheckAsync(0,default);
            check(found?.Version=="v4-test","explicit Gitee source repairs even when legacy manifest is damaged and old hosted URL remains");
            var attempt=Path.Combine(scratch,"v4-attempt"); Directory.CreateDirectory(attempt);
            var staged=await session.DownloadAsync(found!,attempt,new Progress<UpdateProgress>(),default);
            check(!session.Adopt(staged),"missing managed files require installation");
            Core.InstallTransaction.Apply(root,new(release,Path.Combine(root,".dflegacy","v4","cache")));
            check(await session.CheckAsync(0,default)==null && session.StatusMessage.Contains("完整校验"),"V4 adapter reports actual complete verification");
        }
        var manifest=ClientPackageManifest.LoadAndValidate(root);
        check(manifest.Files.Count==4 && manifest.Files.Any(f=>f.Path.EndsWith(".npk")),"launch validation includes arbitrary managed resources");
        foreach(var privatePath in new[]{"Diagnostics/session.json","nested/sessions/session.json","captures/x.dmp","nested/preference.json","queue/body.json","nested/closed.json","collections/11111111111111111111111111111111.json","nested/copied/collections/22222222222222222222222222222222.json"})
        {
            try {Core.PathPolicy.Validate(privatePath);check(false,"managed reader must reject diagnostic private path "+privatePath);}catch(InvalidDataException){check(true,"private diagnostic path rejected: "+privatePath);}
        }
        var diagnosticHelper=Path.Combine(root,"DFLegacy.Diagnostics.exe");File.Delete(diagnosticHelper);
        provider.Offline=true;
        using(var offline=new V4UpdateSession(root,options,provider))
        {
            try {await offline.CheckAsync(0,default);check(false,"offline missing managed helper cannot weaken integrity");}catch(InvalidDataException){check(true,"offline missing helper retains trusted-manifest launch block");}
        }
        provider.Offline=false;
        using(var repair=new V4UpdateSession(root,options,provider))check(await repair.CheckAsync(0,default)!=null,"online missing helper selects same-version V4 repair");
        var helperEngine=new Core.UpdateEngine(root,provider);Core.InstallTransaction.Apply(root,await helperEngine.DownloadAsync(await helperEngine.CheckAsync()));
        check(File.ReadAllText(diagnosticHelper)=="helper","V4 repair restores hash-verified helper bytes");
        File.WriteAllText(Path.Combine(root,"ImagePacks2/test.npk"),"bad");
        try { ClientPackageManifest.LoadAndValidate(root); check(false,"launch blocks changed managed NPK"); } catch(InvalidDataException) { }
        using(var session=new V4UpdateSession(root,options,provider)) check(await session.CheckAsync(0,default)!=null,"same version damage stays repairable through UI session");
        File.WriteAllText(Path.Combine(root,"ImagePacks2/test.npk"),"npk"); provider.Offline=true;
        using(var session=new V4UpdateSession(root,options,provider))
        {
            check(await session.CheckAsync(0,default)==null && session.ShowStatusNotice && session.StatusMessage.Contains("未确认线上最新版本"),"offline installed baseline is visibly different from latest confirmed");
        }
        var config=Path.Combine(root,"launcher.json");
        File.WriteAllText(config,"{\"Account\":\"alice\",\"Password\":\"private\",\"EntranceHost\":\"custom.example\",\"Relay\":{\"TcpPorts\":\"2311,9000\",\"Vendor\":1},\"Unknown\":7}");
        var store=new LauncherAccountStore(config); var before=store.Load();
        var json=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(File.ReadAllText(config))!;
        File.WriteAllText(config,File.ReadAllText(config).Replace("2311,9000","2311,9000,7110,7111,7112"));
        before.Theme="dark"; store.Save(before);
        var after=store.Load();
        check(after.Relay.TcpPorts=="2311,9000,7110,7111,7112" && after.Account=="alice" && after.Password=="private" && after.EntranceHost=="custom.example" && after.Theme=="dark","stale settings save preserves concurrent port migration and credentials");
        before.Theme="light"; store.Save(before);
        check(store.Load().Relay.TcpPorts!.Contains("7112"),"repeated stale object saves do not erase migrated fields");
        using(var lease=Core.InstallationLease.Acquire(root))
        {
            try { store.Update(o=>o.Theme="auto"); check(false,"configuration write must share install lock"); } catch(IOException) { }
        }
        var heldFile=Path.Combine(root,"DFLegacy.Client.dll");
        using(var launchLease=Core.InstallationLease.Acquire(root))
        {
            using var live=await LaunchFlow.StartWithInstallationLeaseAsync(async()=>
            {
                try { using var unexpected=Core.InstallationLease.Acquire(root); check(false,"launch holds mutation lock until bootstrap handoff"); } catch(IOException) {check(true,"launch verification and bootstrap share mutation lease");}
                var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
                start.ArgumentList.Add("--v4-lock-fixture"); start.ArgumentList.Add(heldFile);
                var process=System.Diagnostics.Process.Start(start)!;
                using var ready=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while(!File.Exists(heldFile+".ready")) await Task.Delay(20,ready.Token);
                return process;
            },launchLease);
            try
            {
                store.Update(o=>o.Theme="after-launch");
                check(!live.HasExited && store.Load().Theme=="after-launch","running client does not keep account/config mutex locked");
                provider.Offline=false;
                using var session=new V4UpdateSession(root,options,provider);
                await session.CheckAsync(0,default);
                var owners=await session.InspectLocksAsync(default);
                check(owners.Any(o=>o.ProcessId==live.Id),"second updater reaches actual resource owner inspection while first client runs");
            }
            finally
            {
                if(!live.HasExited) live.Kill();
                using var exit=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await FileLockOwners.WaitForExitConfirmedAsync(live,exit.Token);
            }
        }
        var withOld=m with {Version="v4-retire-a",Sequence=2,Files=[..m.Files,Entry("obsolete.dll","obsolete")]};
        raw=Core.ManifestCodec.Serialize(withOld); provider.Target=new(source,withOld.Version,Hash(raw),raw);
        var engine=new Core.UpdateEngine(root,provider);
        Core.InstallTransaction.Apply(root,await engine.DownloadAsync(await engine.CheckAsync()));
        var withoutOld=withOld with {Version="v4-retire-b",Sequence=3,Files=m.Files,Retired=[new("obsolete.dll","delete",3)]};
        raw=Core.ManifestCodec.Serialize(withoutOld); provider.Target=new(source,withoutOld.Version,Hash(raw),raw);
        using(var session=new V4UpdateSession(root,options,provider))
        {
            var update=await session.CheckAsync(0,default);
            var attempt=Path.Combine(scratch,"retirement-attempt"); Directory.CreateDirectory(attempt);
            var prepared=await session.DownloadAsync(update!,attempt,new Progress<UpdateProgress>(),default);
            var obsolete=Path.Combine(root,"obsolete.dll");
            var lockStart=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
            lockStart.ArgumentList.Add("--v4-lock-fixture"); lockStart.ArgumentList.Add(obsolete);
            using var holder=System.Diagnostics.Process.Start(lockStart)!;
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while(!File.Exists(obsolete+".ready")) await Task.Delay(20,timeout.Token);
                check((await session.InspectLocksAsync(default)).Any(o=>o.ProcessId==holder.Id),"occupancy inspection includes retired managed resources");
            }
            finally
            {
                if(!holder.HasExited) holder.Kill();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await FileLockOwners.WaitForExitConfirmedAsync(holder,timeout.Token);
            }
            check(!session.Adopt(prepared)&&File.Exists(Path.Combine(root,"obsolete.dll")),"retiring managed file cannot bypass helper and occupancy consent through metadata adoption");
        }
        var testRoot=Path.Combine(scratch,"v4-missing-test-debug");Directory.CreateDirectory(testRoot);
        var testSource=source with {Flavor="test"};var testManifest=m with {Flavor="test"};raw=Core.ManifestCodec.Serialize(testManifest);
        var testProvider=new Provider(testSource,new(testSource,testManifest.Version,Hash(raw),raw),blobs);var testEngine=new Core.UpdateEngine(testRoot,testProvider);
        Core.InstallTransaction.Apply(testRoot,await testEngine.DownloadAsync(await testEngine.CheckAsync()));
        foreach(var leftover in new[]{false,true})
        {
            if(leftover)File.WriteAllText(Path.Combine(testRoot,"debug.dll"),"unmanaged leftover must never load");
            try {ClientPackageManifest.LoadAndValidate(testRoot);check(false,"test flavor requires verified managed debug DLL; leftover="+leftover);}
            catch(InvalidDataException){check(true,"test flavor refuses untracked debug module; leftover="+leftover);}
        }
    }
    private sealed class Provider(Core.ReleaseSource source,Core.VerifiedRelease target,Dictionary<string,byte[]> blobs):Core.IReleaseProvider
    {
        public Core.ReleaseSource Source=>source;
        public string Identity=>source.Identity;
        public bool Offline;
        public Core.VerifiedRelease Target=target;
        public Task<Core.VerifiedRelease?> FindAsync(CancellationToken token=default)=>Offline?throw new HttpRequestException("fixture offline"):Task.FromResult<Core.VerifiedRelease?>(Target);
        public Task<HttpResponseMessage> OpenAsync(Core.ResourcePart part,long offset,string? etag,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(blobs[part.Sha256])});
    }
}
