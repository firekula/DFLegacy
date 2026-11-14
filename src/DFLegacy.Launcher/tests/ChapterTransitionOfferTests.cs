using System.Text.Json;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

internal static class ChapterTransitionOfferTests
{
    internal static void Run(string parent, Action<bool,string> check)
    {
        var root = Path.Combine(parent,"chapter-offer");
        Directory.CreateDirectory(Path.Combine(root,".dflegacy"));
        check(ChapterTransitionOffer.ReadSelectedSource(root)==null,"no implicit cross-chapter source");
        var path=Path.Combine(root,".dflegacy","chapter-upgrade-source.json");
        var local=new Core.ReleaseSource("local","http://127.0.0.2:48127/c2","c2-test","x64","test");
        File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(local,Core.ManifestCodec.Json));
        check(ChapterTransitionOffer.ReadSelectedSource(root)==local,"explicit isolated source accepted");
        foreach(var source in new[]{local with {Provider="github",Repository="firekula/DFLegacy"},local with{Repository="http://127.0.0.1:48127"},local with{Repository="http://127.0.0.2:48127/../c2"}})
        {
            File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(source,Core.ManifestCodec.Json));
            try { ChapterTransitionOffer.ReadSelectedSource(root);check(false,"unapproved cross-chapter source rejected"); }
            catch(InvalidDataException){check(true,"unapproved cross-chapter source rejected");}
        }
        File.WriteAllText(path,new string('x',65537));
        try { ChapterTransitionOffer.ReadSelectedSource(root);check(false,"oversize offer rejected"); }
        catch(InvalidDataException){check(true,"oversize offer rejected");}
        File.WriteAllText(path,"null");
        try { ChapterTransitionOffer.ReadSelectedSource(root);check(false,"null offer rejected"); }
        catch(InvalidDataException){check(true,"null offer rejected");}
        var bridge=Path.Combine(parent,"chapter-bridge-offer");
        Directory.CreateDirectory(Path.Combine(bridge,".dflegacy"));
        File.WriteAllBytes(Path.Combine(bridge,".dflegacy","chapter-bridge-source.json"),
            JsonSerializer.SerializeToUtf8Bytes(local with{Channel="c1-bridge"},Core.ManifestCodec.Json));
        var payload=new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll"}.Select(name=>
        {
            var bytes=System.Text.Encoding.UTF8.GetBytes(name);
            File.WriteAllBytes(Path.Combine(bridge,name),bytes);
            return new {path=name,size=bytes.Length,sha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))};
        }).ToArray();
        File.WriteAllBytes(Path.Combine(bridge,"client-package.json"),JsonSerializer.SerializeToUtf8Bytes(
            new{schemaVersion=2,version="c1-bridge-test",commit=new string('a',40),flavor="normal",files=payload}));
        check(ChapterTransitionOffer.Read(bridge)?.Enrollment==true,"explicit bridge enrolls validated standalone C1");
        File.WriteAllText(Path.Combine(bridge,"DFLegacy.Client.dll"),"changed");
        try { ChapterTransitionOffer.Read(bridge);check(false,"bridge offer rejects damaged old package"); }
        catch(InvalidDataException){check(true,"bridge offer rejects damaged old package");}

        var officialRoot=Path.Combine(parent,"official-chapter-offer");
        Directory.CreateDirectory(officialRoot);
        var firstSource=new Core.ReleaseSource("github","firekula/DFLegacy","stable","x86","normal");
        var secondSource=new Core.ReleaseSource("github","firekula/DFLegacy","chapter-two","x86","normal");
        var first=InstallFixture(officialRoot,firstSource,Core.ClientChapterIdentity.ChapterOne,2);
        check(ChapterTransitionOffer.Read(officialRoot) is {Rollback:false,Enrollment:false} offer && offer.Source==secondSource,
            "managed stable Chapter One offers official Chapter Two without a local override");
        var sourceFile=Path.Combine(officialRoot,".dflegacy","chapter-upgrade-source.json");
        File.WriteAllBytes(sourceFile,JsonSerializer.SerializeToUtf8Bytes(local with {Architecture="x86"},Core.ManifestCodec.Json));
        check(ChapterTransitionOffer.Read(officialRoot)?.Source==local with {Architecture="x86"},
            "an explicit loopback override selects the acceptance fixture");
        File.Delete(sourceFile);
        foreach(var untrusted in new[]{firstSource with {Channel="preview"},firstSource with {Provider="gitee"}})
        {
            InstallFixture(officialRoot,untrusted,Core.ClientChapterIdentity.ChapterOne,2);
            check(ChapterTransitionOffer.Read(officialRoot)==null,"untrusted baseline does not offer official Chapter Two");
        }
        InstallFixture(officialRoot,firstSource,Core.ClientChapterIdentity.ChapterOne,2);
        File.WriteAllText(Path.Combine(officialRoot,Core.ClientChapterIdentity.FileName),"tampered");
        try { ChapterTransitionOffer.Read(officialRoot);check(false,"tampered official Chapter One descriptor is rejected"); }
        catch(InvalidDataException){check(true,"tampered official Chapter One descriptor is rejected");}
        InstallFixture(officialRoot,firstSource,Core.ClientChapterIdentity.ChapterOne,2);
        var second=InstallFixture(officialRoot,secondSource,Core.ClientChapterIdentity.ChapterTwo,3,stageOnly:true);
        Core.InstallTransaction.ApplyChapterUpgrade(officialRoot,second,secondSource,explicitSelection:true,deferReadiness:true);
        check(ChapterTransitionOffer.Read(officialRoot) is {Rollback:true},
            "the rollback menu can be built during the readiness probe before the first public launch");
        Core.InstallTransaction.ValidateChapterReadyProbe(officialRoot,second.Release.ManifestSha256);
        Core.InstallTransaction.ConfirmChapterReady(officialRoot,second.Release.ManifestSha256);
        check(ChapterTransitionOffer.Read(officialRoot) is {Rollback:true,Enrollment:false} rollback && rollback.Source==secondSource,
            "official Chapter Two offers rollback from a durable checkpoint bound to its source");
        var checkpoint=Path.Combine(officialRoot,".dflegacy","chapter-transition","checkpoint.json");
        File.WriteAllText(checkpoint,"{}");
        check(ChapterTransitionOffer.Read(officialRoot)==null,"malformed checkpoint never offers rollback");
        File.Delete(checkpoint);
        check(ChapterTransitionOffer.Read(officialRoot)==null,"fresh official Chapter Two has no rollback offer");
        var config=Path.Combine(officialRoot,"launcher.chapter2.json");
        File.Delete(config);
        var accountStore=new DFLegacy.Launcher.LauncherAccountStore(config);
        var defaults=accountStore.LoadCurrent();
        check(defaults.ChapterTwoServerHost=="games.firekula.site" && defaults.UpdateChannel=="chapter-two"
            && defaults.UpdateFlavor=="normal" && !defaults.StartServer && !defaults.ChapterTwoTestMuteEnabled
            && defaults.Accounts.Count==0 && defaults.Account.Length==0 && defaults.Password.Length==0 && !File.Exists(config),
            "official managed launch derives clean public defaults if its configuration is missing");
        accountStore.Update(options=>options.Account="fixture-user");
        var saved=accountStore.LoadCurrent();
        check(saved.ChapterTwoServerHost=="games.firekula.site" && saved.UpdateChannel=="chapter-two"
            && !saved.ChapterTwoTestMuteEnabled && saved.Account=="fixture-user",
            "saving a first account retains derived official defaults");
    }

    private static Core.StagedUpdate InstallFixture(string root,Core.ReleaseSource source,string chapter,int sequence,bool stageOnly=false)
    {
        var module=chapter==Core.ClientChapterIdentity.ChapterOne?"DFLegacy.Client.dll":"DNF.exe";
        const string entry="DFLegacy.Launcher.exe";
        var files=new Dictionary<string,byte[]>
        {
            [entry]="launcher"u8.ToArray(), [module]="module"u8.ToArray(), ["Script.pvf"]=System.Text.Encoding.UTF8.GetBytes(chapter),
            [Core.ClientChapterIdentity.FileName]=JsonSerializer.SerializeToUtf8Bytes(
                new Core.ClientChapterIdentity(1,chapter,entry,[entry,module,"Script.pvf"]),Core.ManifestCodec.Json)
        };
        var cache=new Core.ContentCache(Path.Combine(root,".dflegacy","v4","cache"));
        var managed=files.Select(file=>
        {
            var hash=Hash(file.Value);
            File.WriteAllBytes(stageOnly?cache.ObjectPath(hash,file.Value.Length):Path.Combine(root,file.Key),file.Value);
            return new Core.ManagedFile(file.Key,file.Value.Length,hash,[new(hash,file.Value.Length,"fixture",hash+".bin")]);
        }).ToArray();
        var manifest=new Core.ClientManifest(4,"fixture",new string('a',40),source.Architecture,source.Flavor,source.Channel,sequence,0,
            managed,stageOnly?[new("DFLegacy.Client.dll","delete",sequence)]:[],[]);
        var bytes=Core.ManifestCodec.Serialize(manifest);
        var release=new Core.VerifiedRelease(source,manifest.Version,Hash(bytes),bytes);
        if(!stageOnly)
            File.WriteAllBytes(Path.Combine(root,".dflegacy","v4","installed.json"),JsonSerializer.SerializeToUtf8Bytes(
                new Core.InstalledReceipt(release,managed.Select(file=>file.Path).ToArray()),Core.ManifestCodec.Json));
        return new(release,cache.DirectoryPath);
    }

    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}
