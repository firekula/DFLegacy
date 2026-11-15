using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Diagnostics;
using DFLegacy.Launcher.Diagnostics;
using DFLegacy.Launcher.Updates;
using Core=DFLegacy.UpdateCore;
namespace DFLegacy.Launcher;
// Explicit opt-in real-release acceptance. Uses only a newly allocated isolated fixture root.
internal static class DiagnosticReleaseAcceptanceTests
{
    internal static async Task Run(string specPath,Action<bool,string> check)
    {
        using var spec=JsonDocument.Parse(File.ReadAllBytes(specPath));
        var expectedVersion=spec.RootElement.GetProperty("expectedVersion").GetString();
        if(string.IsNullOrWhiteSpace(expectedVersion)||expectedVersion.Length>128)throw new ArgumentException("An explicit expected test version is required.");
        var fixtureRoot=Path.GetFullPath(spec.RootElement.GetProperty("fixturesRoot").GetString()!);
        var previous=Path.GetFullPath(spec.RootElement.GetProperty("previousRoot").GetString()!);
        var arch=RuntimeInformation.ProcessArchitecture==Architecture.X86?"x86":"x64";
        if(!fixtureRoot.StartsWith(Path.GetFullPath("G:/dnf_local/DFLegacy_Diagnostics_Release_Test_20261109")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            ||Directory.Exists(Path.Combine(fixtureRoot,arch)))throw new InvalidOperationException("A new test-owned fixture is required.");
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
        var results=new List<object>();
        foreach(var flavor in new[]{"normal","test"})
        {
            var variant=arch+"-"+flavor;var client=Path.Combine(fixtureRoot,arch,flavor,"client");Directory.CreateDirectory(client);
            var old=Path.Combine(previous,variant);var manifestName=$"DFLegacy-client-update-v4-gitea-test-{flavor}-win-{arch}.json";
            var raw=File.ReadAllBytes(Path.Combine(old,"bundle",manifestName));var source=new Core.ReleaseSource("gitea","firekula/DFLegacy","gitea-test",arch,flavor);
            var baseline=new Core.VerifiedRelease(source,"v0.1.8-giteatest.4",Convert.ToHexStringLower(SHA256.HashData(raw)),raw);
            foreach(var file in baseline.Manifest.Files){var target=Core.PathPolicy.Resolve(client,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(old,"source",file.Path),target);}
            var store=new Core.InstallationStore(client);Directory.CreateDirectory(store.StateDirectory);
            File.WriteAllBytes(Path.Combine(store.StateDirectory,"installed.json"),JsonSerializer.SerializeToUtf8Bytes(new Core.InstalledReceipt(baseline,baseline.Manifest.Files.Select(f=>f.Path).ToArray()),json));
            var configPath=Path.Combine(client,"launcher.json");
            var config=new JsonObject { ["ClientPath"]="DNF.exe",["AccountServiceUrl"]="http://127.0.0.1:1",["UpdateProvider"]="gitea",["UpdateChannel"]="gitea-test",["UpdateFlavor"]=flavor,["Diagnostics"]=new JsonObject{["Enabled"]=true,["ServiceUrl"]="http://127.0.0.1:1"},["Relay"]=new JsonObject{["TcpPorts"]="9000"},["Servers"]=new JsonArray() };
            File.WriteAllText(configPath,config.ToJsonString(json));
            var settings=new DiagnosticSettings(Path.Combine(fixtureRoot,arch,flavor,"user"));settings.SetEnabled(false);Program.UserDiagnosticSettings=settings;
            long downloadBytes=0,repairBytes=0;var progress=new InlineProgress(p=>{if(p.Phase=="download")downloadBytes=Math.Max(downloadBytes,p.BytesReceived);});
            var request=new RepairRequest(client,NoDialog:true);
            async Task<RepairOutcome> Repair(IProgress<Core.UpdateProgress>? currentProgress=null)
            {
                // Antivirus can briefly retain freshly copied public binaries. Never
                // terminate or ignore a lock owner: retry the entire real preflight.
                for(var attempt=0;;attempt++)
                {
                    try {return await RepairClient.RunAsync(request,progress:currentProgress);}
                    catch(OperationCanceledException) when(attempt<29)
                    {Console.WriteLine("Waiting for transient fixture file lock; attempt "+(attempt+1));await Task.Delay(500);}
                }
            }
            var outcome=await Repair(progress);
            var installed=store.ReadInstalled()!;
            check(outcome.Success&&!outcome.Offline&&installed.Release.Tag==expectedVersion,variant+" actual repair control path upgrades from real Gitea metadata/resources");
            check(!settings.Read().Enabled,variant+" real update preserves current-user opt-out despite copied enabled default");
            var helper=installed.Release.Manifest.Files.Single(f=>f.Path=="DFLegacy.Diagnostics.exe");
            File.Delete(Core.PathPolicy.Resolve(client,helper.Path));
            var cache=new Core.ContentCache(Path.Combine(store.StateDirectory,"cache"));
            foreach(var identity in helper.Parts.Select(p=>(p.Sha256,p.Size)).Append((helper.Sha256,helper.Size)).Distinct())File.Delete(cache.ObjectPath(identity.Sha256,identity.Size));
            downloadBytes=0;outcome=await Repair(progress);repairBytes=downloadBytes;
            check(outcome.Success&&repairBytes>=helper.Size,variant+" missing helper repairs from real remote bytes after its cache objects are removed");
            config=JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();config["DisableGitHubUpdates"]=true;File.WriteAllText(configPath,config.ToJsonString(json));
            File.Delete(Core.PathPolicy.Resolve(client,helper.Path));var blocked=false;
            try {await Repair();}catch(InvalidDataException){blocked=true;}
            check(blocked,variant+" offline missing managed helper retains integrity block");
            config["DisableGitHubUpdates"]=false;File.WriteAllText(configPath,config.ToJsonString(json));
            outcome=await Repair();var valid=await Core.FileVerifier.VerifyAsync(client,installed.Release.Manifest);
            check(outcome.Success&&valid.IsValid&&!Core.InstallTransaction.HasPending(client),variant+" final real published installation is fully verified");
            results.Add(new {variant,buildCommit=installed.Release.Manifest.Commit,version=installed.Release.Tag,manifestSha256=installed.Release.ManifestSha256,helperSha256=helper.Sha256,helperBytes=helper.Size,helperRepairBytes=repairBytes,offlineMissingHelperBlocked=blocked,optOutPreserved=!settings.Read().Enabled,managedFiles=installed.Release.Manifest.Files.Length});
            File.WriteAllText(Path.Combine(fixtureRoot,arch+"-results.json"),JsonSerializer.Serialize(results,json));
            AutomaticDiagnostics.Session?.Complete();
            Console.WriteLine("PASS real Gitea diagnostic release "+variant);
        }
        File.WriteAllText(Path.Combine(fixtureRoot,arch+"-results.json"),JsonSerializer.Serialize(results,json));
    }
    private sealed class InlineProgress(Action<Core.UpdateProgress> report):IProgress<Core.UpdateProgress>{public void Report(Core.UpdateProgress value)=>report(value);}
}
