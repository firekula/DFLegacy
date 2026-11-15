using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Launcher.Updates;
using Core=DFLegacy.UpdateCore;

internal static class UpdateV4HelperTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"chapter-ready-fixture")) &&
            (args is ["--bootstrap-verified","--"] || args is ["--chapter-ack-child"]))
        {
            if(File.Exists(Path.Combine(AppContext.BaseDirectory,"fail-chapter-ready")))return 5;
            if(args is ["--chapter-ack-child"])
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ack-child.pid"),Environment.ProcessId.ToString());
            BootstrapUpdateClient.SignalReady();
            await Task.Delay(args is ["--chapter-ack-child"]?Timeout.Infinite:250);
            return 0;
        }
        if(args.Length!=2 || args[0]!="--v4-update-parent") return null;
        var root=AppContext.BaseDirectory;
        var release=JsonSerializer.Deserialize<Core.VerifiedRelease>(File.ReadAllBytes(Path.Combine(root,"v4-release.json")),Core.ManifestCodec.Json)!;
        var staged=new Core.StagedUpdate(release,Path.Combine(root,".dflegacy","v4","cache"));
        var installer=new UpdateInstaller(root);
        var handoff=args[1] switch
        {
            "chapter-upgrade"=>await installer.PrepareChapterUpgradeAsync(staged,release.Source,default),
            "chapter-rollback"=>await installer.PrepareChapterRollbackAsync(release.Source,default),
            _=>await installer.PrepareV4Async(staged,null,default)
        };
        UpdateHelperTests.CopySidecars(Path.GetDirectoryName(handoff.HelperPath)!);
        var helper=await installer.StartHelperAsync(handoff);
        if(args[1]=="tamper")
        {
            var file=release.Manifest.Files.Single(f=>f.Path=="DFLegacy.Client.dll");
            File.AppendAllText(new Core.ContentCache(staged.CacheDirectory).ObjectPath(file.Sha256,file.Size),"changed after ready");
        }
        File.WriteAllText(Path.Combine(root,"ready.marker"),helper.ProcessId.ToString());
        File.WriteAllText(Path.Combine(root,"helper.path"),handoff.HelperPath);
        return 0;
    }
    internal static async Task Run(string scratch,Action<bool,string> check)
    {
        var local=new Core.ReleaseSource("local","http://127.0.0.2:37135/c2","c2-test","x64","normal");
        using(var http=Core.ReleaseProvider.CreateHttpClient())
        using(var provider=V4UpdateSession.CreateProvider(http,local) as Core.LoopbackReleaseProvider)
            check(provider?.Source==local,"explicit local source uses loopback-only provider");
        foreach(var mode in new[]{"valid","tamper"})
        {
            var root=Path.Combine(scratch,"v4-helper-"+mode); Directory.CreateDirectory(root);
            UpdateHelperTests.CopySidecars(root);
            var exe=Path.Combine(root,"DFLegacy.Launcher.exe"); File.Copy(Environment.ProcessPath!,exe);
            File.WriteAllText(Path.Combine(root,"fixture-owned.marker"),"owned");
            File.WriteAllText(Path.Combine(root,"DFLegacy.Client.dll"),"old DLL");
            File.WriteAllText(Path.Combine(root,"launcher.json"),"{\"Account\":\"alice\",\"Relay\":{\"TcpPorts\":\"2311,9000\"}}");
            var cache=new Core.ContentCache(Path.Combine(root,".dflegacy","v4","cache"));
            Core.ManagedFile Entry(string path,byte[] body)
            {
                var hash=Convert.ToHexStringLower(SHA256.HashData(body)); File.WriteAllBytes(cache.ObjectPath(hash,body.Length),body);
                return new(path,body.Length,hash,[new(hash,body.Length,"v4-helper",hash+".bin")]);
            }
            var m=new Core.ClientManifest(4,"v4-helper",new string('a',40),"x64","normal","gitee-test",1,0,
                [Entry("DFLegacy.Launcher.exe",File.ReadAllBytes(exe)),Entry("DFLegacy.Client.dll","new DLL"u8.ToArray()),Entry("ImagePacks2/test.npk","npk"u8.ToArray())],[],
                [new("pvp-ports","launcher.json","unionPorts","Relay.TcpPorts",JsonSerializer.SerializeToElement(new[]{7110,7111,7112}))]);
            var raw=Core.ManifestCodec.Serialize(m);
            var release=new Core.VerifiedRelease(new("gitee","firekula/DFLegacy","gitee-test","x64","normal"),m.Version,Convert.ToHexStringLower(SHA256.HashData(raw)),raw);
            File.WriteAllBytes(Path.Combine(root,"v4-release.json"),JsonSerializer.SerializeToUtf8Bytes(release,Core.ManifestCodec.Json));
            var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};
            start.ArgumentList.Add("--v4-update-parent"); start.ArgumentList.Add(mode);
            using var parent=Process.Start(start)!;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await FileLockOwners.WaitForExitConfirmedAsync(parent,timeout.Token);
            check(parent.ExitCode==0 && File.Exists(Path.Combine(root,"ready.marker")),"V4 authenticated helper ready before parent exits ("+mode+")");
            var helperPath=File.ReadAllText(Path.Combine(root,"helper.path"));
            var outcome=Path.Combine(Path.GetDirectoryName(helperPath)!,"helper-outcome.json");
            using(var result=await ReadHelperOutcomeAsync(outcome,timeout.Token))
                if(result.RootElement.GetProperty("exitCode").GetInt32()!=(mode=="valid"?0:1))
                    throw new Exception("V4 helper unexpected outcome: "+result.RootElement.GetRawText());
            check(true,"V4 helper returns actual install outcome ("+mode+")");
            if(mode=="valid")
            {
                while(!File.Exists(Path.Combine(root,"restarted.marker"))) await Task.Delay(30,timeout.Token);
                check(File.ReadAllText(Path.Combine(root,"DFLegacy.Client.dll"))=="new DLL" && File.Exists(Path.Combine(root,"ImagePacks2/test.npk")),"real helper installs DLL and nested resources");
                check(new Core.InstallationStore(root).ReadInstalled()?.Release.Manifest.Version==m.Version,"real helper commits verified V4 receipt");
                var config=File.ReadAllText(Path.Combine(root,"launcher.json"));
                check(config.Contains("alice")&&config.Contains("9000,7110,7111,7112"),"real helper migrates ports preserving account/custom port");
            }
            else check(File.ReadAllText(Path.Combine(root,"DFLegacy.Client.dll"))=="old DLL" && !File.Exists(Path.Combine(root,"restarted.marker")),"tampered post-readiness cache cannot replace or restart client");
        }
        await RunChapter(scratch,local,check);
        await VerifyFailedAcknowledgementStopsOwnedChild(scratch,check);
    }

    private static async Task VerifyFailedAcknowledgementStopsOwnedChild(string scratch,Action<bool,string> check)
    {
        var root=Path.Combine(scratch,"v4-chapter-ack");Directory.CreateDirectory(root);
        UpdateHelperTests.CopySidecars(root);
        File.WriteAllText(Path.Combine(root,"chapter-ready-fixture"),"owned");
        var exe=Path.Combine(root,BootstrapLayout.AppName);
        File.Copy(Environment.ProcessPath!,exe);
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};
        start.ArgumentList.Add("--chapter-ack-child");
        var acknowledgementCalled=false;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await BootstrapUpdateClient.LaunchAndConfirmAsync(start,deadline.Token,()=>
            {
                acknowledgementCalled=true;
                throw new IOException("fixture acknowledgement failed");
            });
            throw new Exception("Failed acknowledgement was accepted.");
        }
        catch(IOException ex) when(ex.Message=="fixture acknowledgement failed") { }
        var pid=int.Parse(File.ReadAllText(Path.Combine(root,"ack-child.pid")));
        try
        {
            using var child=Process.GetProcessById(pid);
            check(acknowledgementCalled&&child.HasExited,"failed acknowledgement exits the exact child before recovery");
        }
        catch(ArgumentException) { check(acknowledgementCalled,"failed acknowledgement exits the exact child before recovery"); }
    }

    private static async Task RunChapter(string scratch,Core.ReleaseSource local,Action<bool,string> check)
    {
        foreach(var (name,fixedLayout,failReady,official) in new[]{
            ("standalone",false,false,false),("fixed",true,false,false),("ready-failure",false,true,false),("official-fixed",true,false,true)})
        {
            var root=Path.Combine(scratch,"v4-chapter-helper-"+name);Directory.CreateDirectory(root);
            UpdateHelperTests.CopySidecars(root);
            File.WriteAllText(Path.Combine(root,"fixture-owned.marker"),"owned");
            File.WriteAllText(Path.Combine(root,"chapter-ready-fixture"),"owned");
            if(failReady)File.WriteAllText(Path.Combine(root,"fail-chapter-ready"),"fail");
            var exe=Path.Combine(root,BootstrapLayout.EntryName);
            var hostBytes=File.ReadAllBytes(Environment.ProcessPath!);
            var hostHash=Hash(hostBytes);
            var bootstrap=System.Text.Encoding.ASCII.GetBytes("[Bootstrap]\nSchemaVersion=1\nArchitecture=x64\nLauncherSha256="+hostHash+"\nUpdaterSha256="+hostHash+"\n");
            var cache=new Core.ContentCache(Path.Combine(root,".dflegacy","v4","cache"));
            Core.ManagedFile Entry(string path,byte[] body)
            {
                var hash=Hash(body);File.WriteAllBytes(cache.ObjectPath(hash,body.Length),body);
                return new(path,body.Length,hash,[new(hash,body.Length,"chapter-helper",hash+".bin")]);
            }
            var shared=new List<Core.ManagedFile>{Entry(BootstrapLayout.EntryName,hostBytes),Entry("DNF.exe","game"u8.ToArray())};
            if(fixedLayout)shared.AddRange([Entry(BootstrapLayout.AppName,hostBytes),Entry(BootstrapLayout.UpdaterName,hostBytes),Entry(BootstrapLayout.DescriptorName,bootstrap)]);
            Core.ManagedFile Descriptor(string chapter,string[] required)=>Entry(Core.ClientChapterIdentity.FileName,
                JsonSerializer.SerializeToUtf8Bytes(new Core.ClientChapterIdentity(1,chapter,BootstrapLayout.EntryName,required),Core.ManifestCodec.Json));
            var firstSource=official?new Core.ReleaseSource("github","firekula/DFLegacy","stable","x64","normal")
                :new Core.ReleaseSource("gitee","firekula/DFLegacy","c1-test","x64","normal");
            var secondSource=official?firstSource with {Channel="chapter-two"}:local;
            Core.VerifiedRelease Release(Core.ReleaseSource source,long sequence,Core.ManagedFile[] files,Core.RetiredFile[] retired)
            {
                var manifest=new Core.ClientManifest(4,"chapter-helper",new string('a',40),"x64","normal",source.Channel,sequence,0,files,retired,[]);
                var bytes=Core.ManifestCodec.Serialize(manifest);
                return new(source,manifest.Version,Hash(bytes),bytes);
            }
            var first=Release(firstSource,1,[..shared,Entry("DFLegacy.Client.dll","first DLL"u8.ToArray()),Entry("Script.pvf","first PVF"u8.ToArray()),
                Descriptor(Core.ClientChapterIdentity.ChapterOne,[BootstrapLayout.EntryName,"Script.pvf","DFLegacy.Client.dll"])],[]);
            var second=Release(secondSource,2,[..shared,Entry("Script.pvf","second PVF"u8.ToArray()),
                Descriptor(Core.ClientChapterIdentity.ChapterTwo,[BootstrapLayout.EntryName,"Script.pvf","DNF.exe"])],[new("DFLegacy.Client.dll","delete",2)]);
            Core.InstallTransaction.Apply(root,new(first,cache.DirectoryPath));
            File.WriteAllText(Path.Combine(root,"launcher.json"),"{\"Account\":\"fixture-user\",\"Password\":\"fixture-password\"}");
            var ini="; preserve custom settings\r\n[Resolution]\r\nWidth=1024\r\nHeight=768\r\n[Rendering]\r\nPresentationFilter=point\r\n[Custom]\r\nValue=unchanged\r\n"u8.ToArray();
            File.WriteAllBytes(Path.Combine(root,"Config.ini"),ini);
            if(fixedLayout)File.WriteAllText(Path.Combine(root,"launcher.chapter2.json"),"""{"ChapterTwoDisplay":{"Width":800,"Height":600,"Filter":"sharp"},"UnknownSetting":17}""");
            File.WriteAllBytes(Path.Combine(root,"v4-release.json"),JsonSerializer.SerializeToUtf8Bytes(second,Core.ManifestCodec.Json));
            var parentPath=fixedLayout?Path.Combine(root,BootstrapLayout.AppName):exe;
            var result=await RunChapterHandoff(root,parentPath,"chapter-upgrade",check);
            check(result==(failReady?1:0),"chapter helper readiness result ("+name+")");
            check(new Core.InstallationStore(root).ReadInstalled()?.Release.Source==(failReady?firstSource:secondSource),
                "verified receipt selects restored or upgraded source ("+name+")");
            if(!failReady)
            {
                if(result!=0)throw new InvalidDataException(File.ReadAllText(Path.Combine(root,".dflegacy","last-result.json")));
                var configuration=new DFLegacy.Launcher.LauncherAccountStore(Path.Combine(root,"launcher.chapter2.json")).LoadCurrent();
                check(configuration.ChapterTwoDisplay==(fixedLayout?new DFLegacy.Launcher.ChapterTwoDisplayOptions(800,600,"sharp"):new(1024,768,"point"))
                    && File.ReadAllBytes(Path.Combine(root,"Config.ini")).SequenceEqual(ini),
                    "known C1 preset migrates only if C2 has no display setting ("+name+")");
                if(fixedLayout)check(configuration.ExtensionData?["UnknownSetting"].GetInt32()==17,"unknown C2 settings survive migration");
                check(RepairClient.ReadSettings(new(root)).Source==secondSource &&
                    V4UpdateSession.SourceFor(root,new())==secondSource,"repair and update select the bound C2 source ("+name+")");
                if(official)check(configuration.ChapterTwoServerHost=="games.firekula.site" && configuration.UpdateChannel=="chapter-two"
                    && !configuration.ChapterTwoDebugEnabled && !configuration.ChapterTwoTestMuteEnabled
                    && configuration.Account=="fixture-user","official helper writes public defaults and keeps the saved account");
                var rollback=await RunChapterHandoff(root,parentPath,"chapter-rollback",check);
                check(rollback==0&&new Core.InstallationStore(root).ReadInstalled()?.Release.Source==firstSource&&
                    Core.ChapterTransition.RequiresExplicitUpgrade(root),"chapter helper rolls back and leaves re-upgrade on hold ("+name+")");
            }
        }
    }

    private static async Task<int> RunChapterHandoff(string root,string parentPath,string mode,Action<bool,string> check)
    {
        File.Delete(Path.Combine(root,"helper.path"));
        var start=new ProcessStartInfo(parentPath){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};
        start.ArgumentList.Add("--v4-update-parent");start.ArgumentList.Add(mode);
        using var parent=Process.Start(start)!;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await FileLockOwners.WaitForExitConfirmedAsync(parent,timeout.Token);
        check(parent.ExitCode==0 && File.Exists(Path.Combine(root,"helper.path")),"chapter helper authenticates actual parent ("+mode+")");
        var helper=File.ReadAllText(Path.Combine(root,"helper.path"));
        var outcome=Path.Combine(Path.GetDirectoryName(helper)!,"helper-outcome.json");
        using var result=await ReadHelperOutcomeAsync(outcome,timeout.Token);
        return result.RootElement.GetProperty("exitCode").GetInt32();
    }

    private static async Task<JsonDocument> ReadHelperOutcomeAsync(string path,CancellationToken token)
    {
        // File creation precedes close/flush. Wait for a complete readable result,
        // not only its directory entry (observed sharing violation under load).
        while(true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return JsonDocument.Parse(File.ReadAllBytes(path));
            }
            catch(IOException){ }
            catch(JsonException){ }
            await Task.Delay(30,token);
        }
    }

    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
