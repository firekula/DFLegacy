using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;
using Core=DFLegacy.UpdateCore;

internal static class GiteaSourceTests
{
    private const string Origin="http://firekula.site:3000";
    private static LauncherOptions Options(string hosted="")=>new() {UpdateProvider="gitea",UpdateChannel="gitea-test",UpdateServiceUrl=hosted};
    private static string Root(string scratch,string name)
    {
        var root=Path.Combine(scratch,"gitea-"+name);Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"client-package.json"),"damaged legacy metadata");
        return root;
    }
    private static async Task Case(string name,Action<bool,string> check,Func<Task> test)
    {
        try {await test();}
        catch(Exception error) {check(false,name+": "+error.GetType().Name+": "+error.Message);}
    }
    internal static async Task RunV4(string scratch,Action<bool,string> check)
    {
        await Case("background source inherits installed starter identity",check,async()=>
        {
            var root=Root(scratch,"background-inherited-source");var provider=FixtureProvider.Create();await Install(root,provider);
            using(var session=new V4UpdateSession(root,new LauncherOptions(),provider))
                check(await session.CheckAsync(0,default)==null&&provider.FindCalls>0,"starter without update settings keeps discovering its installed Gitea channel");
            using(var session=new V4UpdateSession(root,new LauncherOptions{UpdateServiceUrl="invalid inherited hosted URL"},provider))
                check(await session.CheckAsync(0,default)==null,"inherited Gitea source keeps precedence over leftover hosted URL");
            File.WriteAllText(Path.Combine(root,"launcher.json"),"{\"UpdateProvider\":\"github\",\"UpdateChannel\":\"stable\"}");
            check(V4UpdateSession.SourceFor(root,new LauncherOptions()).Provider=="github","explicit default-valued source overrides installed starter fallback");
        });
        await Case("explicit Gitea overrides inherited hosted source",check,async()=>
        {
            var root=Root(scratch,"explicit");var provider=FixtureProvider.Create();
            using var session=new V4UpdateSession(root,Options("invalid inherited hosted URL"),provider);
            var found=await session.CheckAsync(0,default);
            check(found?.Version==provider.Target.Tag&&provider.FindCalls==1,"explicit Gitea discovers V4 despite damaged legacy metadata and inherited hosted URL");
            check(found?.DownloadUrl.AbsoluteUri==$"{Origin}/firekula/DFLegacy/releases/download/{provider.Target.Tag}/{provider.Source.ManifestAssetName}","V4 release identity exposes the pinned Gitea origin and Gitea manifest asset");
            var attempt=Path.Combine(scratch,"gitea-explicit-attempt");Directory.CreateDirectory(attempt);
            var staged=await session.DownloadAsync(found!,attempt,new Progress<UpdateProgress>(),default);
            check(staged.Manifest.SchemaVersion==4&&provider.OpenCalls==2,"Gitea session downloads verified V4 resources through the selected provider");
        });
        await Case("missing Gitea manifest cannot bridge to GitHub",check,async()=>
        {
            // Invalid legacy metadata makes an accidental bridge fail before any real HTTP request.
            var root=Root(scratch,"missing");var provider=FixtureProvider.Create();provider.Missing=true;
            using var session=new V4UpdateSession(root,Options(),provider);
            try {await session.CheckAsync(0,default);check(false,"missing Gitea V4 manifest must be reported as unavailable");}
            catch(Exception error)
            {
                check(error is InvalidDataException&&error.Message.Contains("Gitea")&&error.Message.Contains("V4")
                    &&provider.FindCalls==1&&!session.StatusMessage.Contains("GitHub"),"missing Gitea manifest reports its own source instead of selecting the GitHub V3 bridge");
            }
        });
        await Case("Gitea disabled networking validates local receipt",check,async()=>
        {
            var root=Root(scratch,"disabled");var provider=FixtureProvider.Create();await Install(root,provider);
            var options=Options("invalid inherited hosted URL");options.DisableGitHubUpdates=true;provider.FindCalls=0;provider.OpenCalls=0;
            using var session=new V4UpdateSession(root,options,provider);
            check(await session.CheckAsync(0,default)==null&&session.ShowStatusNotice&&session.StatusMessage.Contains("已停用联网更新")
                &&provider.FindCalls==0&&provider.OpenCalls==0,"disabled Gitea startup uses completed receipt without discovery or downloads");
            File.WriteAllText(Path.Combine(root,"Script.pvf"),"damaged");
            check(await session.CheckAsync(0,default)==null,"disabled Gitea startup trusts completed asset record");
            File.Delete(Path.Combine(root,".dflegacy/v4/validated.json"));
            try {await session.CheckAsync(0,default);check(false,"disabled Gitea startup must reject a damaged managed file");}
            catch(InvalidDataException) {check(provider.FindCalls==0&&provider.OpenCalls==0,"disabled Gitea rejects corrupt local files without contacting any provider");}
        });
        await Case("Gitea cannot reuse a Gitee receipt",check,async()=>
        {
            var root=Root(scratch,"source-identity");await Install(root,FixtureProvider.Create("gitee"));
            var provider=FixtureProvider.Create();using var session=new V4UpdateSession(root,Options(),provider);
            try {await session.CheckAsync(0,default);check(false,"Gitea must reject an installed Gitee source identity");}
            catch(InvalidDataException) {check(new Core.InstallationStore(root).ReadInstalled()!.Release.Source.Provider=="gitee"&&provider.OpenCalls==0,"Gitea source mismatch preserves the Gitee receipt without downloading or committing");}
        });
        await Case("GitHub retains hosted V3 precedence",check,async()=>
        {
            var root=Root(scratch,"github-hosted");var provider=FixtureProvider.Create("github");
            using var session=new V4UpdateSession(root,new LauncherOptions {UpdateServiceUrl="invalid inherited hosted URL"},provider);
            try {await session.CheckAsync(0,default);check(false,"GitHub configuration must retain hosted source precedence");}
            catch(ArgumentException) {check(provider.FindCalls==0,"existing GitHub configuration still selects its explicit hosted V3 source");}
        });
    }
    internal static async Task RunRepair(string scratch,Action<bool,string> check)
    {
        await Case("Gitea repair selection and source identity",check,async()=>
        {
            var root=Root(scratch,"repair");var path=Path.Combine(root,"launcher.json");
            const string config="{\"UpdateProvider\":\"gitea\",\"UpdateChannel\":\"gitea-test\",\"UpdateServiceUrl\":\"invalid inherited hosted URL\",\"UnknownSetting\":77}";
            File.WriteAllText(path,config);
            var request=RepairClient.ParseArguments(["--repair-client",root,"--update-provider","gitea","--update-channel","gitea-test","--no-dialog"]);
            check(request.Provider=="gitea"&&request.Channel=="gitea-test"&&request.NoDialog,"external repair arguments preserve explicit Gitea provider and channel");
            var provider=FixtureProvider.Create();var settings=RepairClient.ReadSettings(new(root));
            check(settings.Source==provider.Source&&!settings.NetworkDisabled,"configured Gitea repair overrides an inherited hosted V3 URL");
            var result=await RepairClient.RunAsync(new(root),provider);
            check(result.Success&&!result.Offline&&File.ReadAllText(Path.Combine(root,"DFLegacy.Launcher.exe"))=="Gitea launcher fixture","external Gitea repair installs trusted bytes without executing the managed launcher");
            check(File.ReadAllText(path)==config&&new Core.InstallationStore(root).ReadInstalled()!.Release.Source==provider.Source,"Gitea repair preserves configuration and commits its distinct source identity");
            File.WriteAllText(path,"{\"UpdateServiceUrl\":\"invalid inherited hosted URL\",\"DisableGitHubUpdates\":true}");
            settings=RepairClient.ReadSettings(new(root));provider.FindCalls=0;provider.OpenCalls=0;
            check(settings.Source==provider.Source&&settings.NetworkDisabled,"repair inherits the Gitea receipt when source settings are absent");
            result=await RepairClient.RunAsync(new(root),provider);
            check(result.Success&&result.Offline&&result.Message.Contains("已停用联网更新")&&provider.FindCalls==0&&provider.OpenCalls==0,"disabled Gitea repair verifies the cached installation without networking");
            File.WriteAllText(Path.Combine(root,"Script.pvf"),"damaged");
            try {await RepairClient.RunAsync(new(root),provider);check(false,"disabled Gitea repair cannot claim to fix damaged files");}
            catch(InvalidDataException) {check(provider.FindCalls==0&&provider.OpenCalls==0,"disabled Gitea repair rejects corruption without requests");}
            var other=FixtureProvider.Create("gitee");
            try {await RepairClient.RunAsync(new(root),other);check(false,"Gitea repair must reject a Gitee provider");}
            catch(InvalidDataException) {check(other.FindCalls==0&&other.OpenCalls==0,"repair rejects a different provider before discovery or download");}
            result=await RepairClient.RunAsync(request,provider);
            check(result.Success&&!result.Offline&&provider.FindCalls>0,"explicit repair source retains the existing opt-in override for disabled network updates");
        });
    }
    private static async Task Install(string root,FixtureProvider provider)
    {
        var engine=new Core.UpdateEngine(root,provider);Core.InstallTransaction.Apply(root,await engine.DownloadAsync(await engine.CheckAsync()));
    }
    private sealed class FixtureProvider(Core.VerifiedRelease target,Dictionary<string,byte[]> blobs):Core.IReleaseProvider
    {
        internal Core.VerifiedRelease Target=>target;
        public Core.ReleaseSource Source=>target.Source;
        public string Identity=>Source.Identity;
        internal bool Missing;
        internal int FindCalls,OpenCalls;
        public Task<Core.VerifiedRelease?> FindAsync(CancellationToken token=default)
        {
            token.ThrowIfCancellationRequested();FindCalls++;return Task.FromResult(Missing?null:(Core.VerifiedRelease?)target);
        }
        public Task<HttpResponseMessage> OpenAsync(Core.ResourcePart part,long offset,string? etag,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();OpenCalls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(blobs[part.Sha256])});
        }
        internal static FixtureProvider Create(string provider="gitea")
        {
            var source=new Core.ReleaseSource(provider,"firekula/DFLegacy",provider=="github"?"stable":provider+"-test",RuntimeInformation.ProcessArchitecture==Architecture.X86?"x86":"x64","normal");
            var blobs=new Dictionary<string,byte[]>();
            string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
            Core.ManagedFile File(string path,string text)
            {
                var bytes=System.Text.Encoding.UTF8.GetBytes(text);var hash=Hash(bytes);blobs[hash]=bytes;
                return new(path,bytes.Length,hash,[new(hash,bytes.Length,"v4-gitea-fixture",hash+".bin")]);
            }
            var manifest=new Core.ClientManifest(4,"v4-gitea-fixture",new string('a',40),source.Architecture,source.Flavor,source.Channel,1,0,
                [File("DFLegacy.Launcher.exe","Gitea launcher fixture"),File("Script.pvf","Gitea resource fixture")],[],[]);
            var raw=Core.ManifestCodec.Serialize(manifest);return new(new(source,manifest.Version,Hash(raw),raw),blobs);
        }
    }
}
