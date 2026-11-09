using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Diagnostics;
using DFLegacy.Launcher.Diagnostics;
using Core=DFLegacy.UpdateCore;
namespace DFLegacy.Launcher;
internal static class DiagnosticHelperIntegrityTests
{
    internal static void Run(string scratch,Action<bool,string> check)
    {
        var root=Path.Combine(scratch,"helper-integrity");Directory.CreateDirectory(root);
        var helper=Path.Combine(root,"DFLegacy.Diagnostics.exe");
        // A real valid executable image. Creation is intercepted so a mismatched
        // replacement is never executed even during the regression's RED phase.
        File.Copy(Environment.ProcessPath!,helper);
        var binaryRoot=Path.GetDirectoryName(Environment.ProcessPath!)!;
        foreach(var file in Directory.EnumerateFiles(binaryRoot))
            if(file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||file.EndsWith(".deps.json",StringComparison.OrdinalIgnoreCase)||file.EndsWith(".runtimeconfig.json",StringComparison.OrdinalIgnoreCase))
                File.Copy(file,Path.Combine(root,Path.GetFileName(file)));
        var size=new FileInfo(helper).Length;
        var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(helper)));
        var bad=new string('0',64);var next=0;var legacy=Path.Combine(root,"client-package.json");
        void Legacy(string digest,long? length=null)
        {
            var rows=new[]{new {path="DFLegacy.Launcher.exe",size=1L,sha256=bad},new {path="DFLegacy.Client.dll",size=1L,sha256=bad},new {path="DFLegacy.Diagnostics.exe",size=length??size,sha256=digest},new {path="DFLegacy.CrashCapture.exe",size=1L,sha256=bad}};
            File.WriteAllText(legacy,JsonSerializer.Serialize(new {schemaVersion=2,version="integrity-test",commit=new string('a',40),flavor="normal",files=rows}));
        }
        var receipt=Path.Combine(root,".dflegacy/v4/installed.json");
        void V4(string digest,bool includesHelper=true,string version="integrity-test",string? commit=null)
        {
            var arch=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.X86?"x86":"x64";
            var file=new Core.ManagedFile(includesHelper?"DFLegacy.Diagnostics.exe":"public.bin",size,digest,[new(digest,size,"integrity-test",digest+".bin")]);
            var manifest=new Core.ClientManifest(4,version,commit??new string('a',40),arch,"normal","gitea-test",1,0,[file],[],[]);
            var raw=Core.ManifestCodec.Serialize(manifest);var release=new Core.VerifiedRelease(new("gitea","firekula/DFLegacy","gitea-test",arch,"normal"),manifest.Version,Convert.ToHexStringLower(SHA256.HashData(raw)),raw);
            Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);File.WriteAllBytes(receipt,JsonSerializer.SerializeToUtf8Bytes(new Core.InstalledReceipt(release,[file.Path]),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        void Case(string label,bool expected)
        {
            var calls=0;var imageLocked=false;Process? child=null;var marker=Path.Combine(root,"verified-control-"+next+".txt");
            Process? Capture(ProcessStartInfo info)
            {
                calls++;
                try {using var writable=new FileStream(helper,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);}
                catch(IOException){imageLocked=true;}
                if(!expected)return null;
                info.Environment["DFL_TEST_HELPER_ENTRY_MARKER"]=marker;
                return child=Process.Start(info);
            }
            var settings=new DiagnosticSettings(Path.Combine(scratch,"helper-integrity-user-"+next++));
            AutomaticDiagnostics.Configure(new LauncherOptions{ClientPath=Path.Combine(root,"DNF.exe"),AccountServiceUrl="http://127.0.0.1:1"},settings,helperDirectory:root,startProcess:Capture);
            check(calls==(expected?1:0),label);
            if(expected)
            {
                check(imageLocked,"verified helper stays held against write/replacement until process creation");
                check(child is not null&&child.WaitForExit(5000)&&File.Exists(marker),"verified executable actually starts while its checksum handle is held");
            }
        }
        Case("present valid executable without trusted installation metadata is not executed",false);
        Legacy(bad);Case("legacy wrong-hash valid executable replacement is not executed before verification",false);
        Legacy(hash,size+1);Case("legacy wrong-size executable replacement is not executed",false);
        Legacy(hash);Case("legacy trusted helper reaches the normal process-creation path",true);
        V4(bad);Case("V4 wrong-hash replacement is not executed or trusted through legacy fallback",false);
        V4(hash);Case("V4 trusted helper reaches the normal process-creation path",true);
        V4(hash,false);Case("V4 receipt without helper cannot authorize an unmanaged executable",false);
        foreach(var suffix in new[]{"alpha.1","alpha.2"})
        {
            var version="1.2.3-"+suffix;var commit=new string(suffix.EndsWith("1")?'a':'b',40);V4(hash,version:version,commit:commit);
            Case("installed prerelease helper remains valid "+suffix,true);
            var session=AutomaticDiagnostics.Session!;
            session.WriteIncident(new DiagnosticIncident{Stage="identity"},"confirmed");session.CollectConfirmed();
            var body=new DiagnosticQueue(session).ReadPending().Single().Body;
            using var wire=JsonDocument.Parse(body);
            check(wire.RootElement.GetProperty("clientVersion").GetString()==version+"+"+commit,"actual wire carries authoritative V4 prerelease and source identity "+suffix);
        }
        File.WriteAllText(receipt,"{");Case("malformed V4 receipt cannot fall back to legacy authorization",false);
        File.Delete(receipt);
        foreach(var name in new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll"})File.WriteAllText(Path.Combine(root,name),"verified legacy identity");
        var legacyRows=new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll"}.Select(name=>new {path=name,size=new FileInfo(Path.Combine(root,name)).Length,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,name))))});
        File.WriteAllText(legacy,JsonSerializer.Serialize(new {schemaVersion=2,version="2.3.4-legacy.7",commit=new string('c',40),flavor="normal",files=legacyRows}));
        check(AutomaticDiagnostics.ResolveInstalledIdentity(root)=="2.3.4-legacy.7+"+new string('c',40),"verified legacy manifest preserves release and source identity");
        File.AppendAllText(Path.Combine(root,"DFLegacy.Client.dll"),"changed");check(AutomaticDiagnostics.ResolveInstalledIdentity(root)=="unknown","unverified legacy bytes yield honest unknown identity");
        File.Delete(legacy);check(AutomaticDiagnostics.ResolveInstalledIdentity(root)!="unknown","metadata-free development build falls back to informational identity");
        File.Delete(receipt);File.WriteAllText(legacy,"{");Case("malformed legacy metadata cannot authorize helper execution",false);
    }
}
