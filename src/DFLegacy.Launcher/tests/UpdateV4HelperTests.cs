using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Launcher.Updates;
using Core=DFLegacy.UpdateCore;

internal static class UpdateV4HelperTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if(args.Length!=2 || args[0]!="--v4-update-parent") return null;
        var root=AppContext.BaseDirectory;
        var release=JsonSerializer.Deserialize<Core.VerifiedRelease>(File.ReadAllBytes(Path.Combine(root,"v4-release.json")),Core.ManifestCodec.Json)!;
        var staged=new Core.StagedUpdate(release,Path.Combine(root,".dflegacy","v4","cache"));
        var installer=new UpdateInstaller(root);
        var handoff=await installer.PrepareV4Async(staged,null,default);
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
            while(!File.Exists(outcome)) await Task.Delay(30,timeout.Token);
            var outcomeText=File.ReadAllText(outcome);
            using(var result=JsonDocument.Parse(outcomeText))
                if(result.RootElement.GetProperty("exitCode").GetInt32()!=(mode=="valid"?0:1)) throw new Exception("V4 helper unexpected outcome: "+outcomeText);
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
    }
}
