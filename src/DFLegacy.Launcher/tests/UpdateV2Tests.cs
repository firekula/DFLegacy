using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;

internal static class UpdateV2Tests
{
    private static readonly string[] PreservedFiles = ["Config.ini", "launcher.json", "ijl15.dll", "DNF.exe", "Script.pvf", "player.NPK",
        "ImagePacks2/player.NPK", "Logs/DFLegacy/launcher.log", "Logs/DFLegacy/legacy/rs_log.txt", "unrelated.json", "notes.txt"];
    private static void CheckPreserved(string directory, Action<bool,string> check, string boundary)
    {
        foreach(var name in PreservedFiles)
            check(File.Exists(Path.Combine(directory,name)) && File.ReadAllBytes(Path.Combine(directory,name)).SequenceEqual(Encoding.UTF8.GetBytes("preserve "+name)),
                "V2 preserves player configuration, originals, resources and logs: "+boundary+" "+name);
    }
    internal static byte[] Manifest(string directory, string flavor = "normal", string version = "old", bool legacy = false)
        => JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = legacy ? 1 : 2, version, commit = new string('a',40), flavor,
            files = (flavor == "test" ? new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","debug.dll"} : new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll"})
                .Select(name => new { path = name, size = new FileInfo(Path.Combine(directory,name)).Length, sha256 = UpdateSafety.Hash(Path.Combine(directory,name)) })
        });
    internal static void InstallMetadata(string directory, string flavor = "normal", string version = "old")
        => File.WriteAllBytes(Path.Combine(directory,"client-package.json"),Manifest(directory,flavor,version));
    internal static string Installation(string root, string flavor = "normal")
    {
        var target = Path.Combine(root,"v2-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target,"DFLegacy.Launcher.exe"),"old exe");
        File.WriteAllText(Path.Combine(target,"DFLegacy.Client.dll"),"old dll");
        File.WriteAllText(Path.Combine(target,"debug.dll"),"old debug");
        foreach(var name in PreservedFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(target,name))!);
            File.WriteAllText(Path.Combine(target,name),"preserve "+name);
        }
        InstallMetadata(target,flavor); return target;
    }
    internal static byte[] Package(string root, string flavor = "normal", string variant = "valid", byte[]? launcher = null)
    {
        var stage = Installation(root,flavor);
        File.WriteAllBytes(Path.Combine(stage,"DFLegacy.Launcher.exe"),launcher ?? Encoding.UTF8.GetBytes("new executable"));
        File.WriteAllText(Path.Combine(stage,"DFLegacy.Client.dll"),"new dll");
        File.WriteAllText(Path.Combine(stage,"debug.dll"),"new debug");
        var manifest = Manifest(stage,flavor,"dev-test",variant=="legacy");
        if(variant=="stale-debug") File.WriteAllText(Path.Combine(stage,"debug.dll"),"old debug");
        using var memory = new MemoryStream();
        using(var zip = new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            void Add(string name, byte[] bytes) { using var output=zip.CreateEntry(name).Open(); output.Write(bytes); }
            if(variant!="missing-metadata") Add("update-manifest.json",manifest);
            foreach(var name in flavor=="test" ? new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","debug.dll"} : new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll"})
                if(variant!="missing-debug" || name!="debug.dll") Add(name,File.ReadAllBytes(Path.Combine(stage,name)));
        }
        return memory.ToArray();
    }
    internal static UpdateRelease Release(byte[] bytes, string flavor="normal") => new(7,"dev-test",DateTimeOffset.UtcNow,true,
        new Uri("https://github.com/firekula/DFLegacy/releases/download/dev-test/"+(flavor=="test"?"DFLegacy-client-update-v2-test-win-x64.zip":"DFLegacy-client-update-v2-win-x64.zip")),bytes.Length,UpdateTransportTests.Hash(bytes));
    internal static PreparedUpdate Extract(string root,byte[] bytes,string flavor="normal")
    {
        var stage=Path.Combine(root,"dflegacy-update-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        var archive=Path.Combine(stage,"package.zip"); File.WriteAllBytes(archive,bytes);
        return UpdatePackage.Extract(Release(bytes,flavor),stage,archive);
    }
    internal static async Task Run(string root,Action<bool,string> check)
    {
        await HostedUpdateTests.Run(root, check);
        // Omitting V2 selection must fail at transport, before any target is changed.
        var bytes=Package(root);
        using(var http=UpdateTransportTests.Client(bytes))
        {
            try { await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(Release(bytes),root); check(true,"normal V2 asset downloads"); }
            catch(InvalidDataException) { check(false,"normal V2 asset downloads"); }
        }
        foreach(var flavor in new[]{"normal","test"})
        {
            var feed=JsonSerializer.SerializeToUtf8Bytes(new[]{new {id=7,tag_name="dev-test",published_at="2026-01-01T00:00:00Z",draft=false,prerelease=false,
                assets=new[]{"DFLegacy-client-update-win-x64.zip","DFLegacy-client-update-v2-win-x64.zip","DFLegacy-client-update-v2-test-win-x64.zip"}
                    .Select(name=>new {name,size=12,digest="sha256:"+new string('a',64),browser_download_url="https://github.com/firekula/DFLegacy/releases/download/dev-test/"+name})}});
            using var http=UpdateTransportTests.Client(feed);
            var release=await new GitHubUpdateClient(http,"firekula/DFLegacy",flavor:flavor).CheckAsync();
            check(release?.DownloadUrl.AbsolutePath.EndsWith(flavor=="normal"?"/DFLegacy-client-update-v2-win-x64.zip":"/DFLegacy-client-update-v2-test-win-x64.zip")==true,"flavor selects exact V2 asset with legacy and other flavor present: "+flavor);
        }
        var disguised=Package(root,"test");
        using(var http=UpdateTransportTests.Client(disguised))
            await UpdateTransportTests.Reject(async()=>await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(Release(disguised),root),check,"normal_update_rejects_test_or_legacy: test manifest cannot hide under normal asset name");
        PreparedUpdate prepared;
        try { prepared=Extract(root,bytes); }
        catch(InvalidDataException) { check(false,"schema2 exact standalone package accepted"); return; }
        var target=Installation(root); var oldMetadata=File.ReadAllBytes(Path.Combine(target,"client-package.json"));
        var result=UpdateTransaction.Apply(target,prepared,_=>false);
        check(result.Success && !result.Restarted,"schema2 installation commits despite restart failure");
        check(ClientPackageManifest.LoadAndValidate(target).Version=="dev-test","schema2 installed metadata matches committed binaries");
        check(result.BackupDirectory!=null && File.ReadAllBytes(Path.Combine(result.BackupDirectory,"client-package.json")).SequenceEqual(oldMetadata),"schema2 metadata backed up byte-for-byte");
        CheckPreserved(target,check,"normal commit");
        check(File.ReadAllText(Path.Combine(target,"debug.dll"))=="old debug" && ClientPackageManifest.LoadAndValidate(target).Flavor=="normal", "retained_debug_is_not_loaded_by_normal_mode: update preserves explicit normal flavor");
        foreach(var variant in new[]{"legacy","missing-metadata"})
        {
            try { Extract(root,Package(root,variant:variant)); check(false,"normal_update_rejects_test_or_legacy: "+variant); }
            catch(InvalidDataException) { }
        }
        foreach(var variant in new[]{"missing-debug","stale-debug"})
        {
            try { Extract(root,Package(root,"test",variant),"test"); check(false,"test_update_requires_same_version_debug: "+variant); }
            catch(InvalidDataException) { }
        }
        var test=Extract(root,Package(root,"test"),"test"); var normalTarget=Installation(root);
        check(!UpdateTransaction.Apply(normalTarget,test,_=>true).Success && ClientPackageManifest.LoadAndValidate(normalTarget).Version=="old","normal_update_rejects_test_or_legacy: test cannot replace normal");
        var testTarget=Installation(root,"test");
        check(UpdateTransaction.Apply(testTarget,test,_=>true).Success && ClientPackageManifest.LoadAndValidate(testTarget).Flavor=="test" && File.ReadAllText(Path.Combine(testTarget,"debug.dll"))=="new debug","test_update_requires_same_version_debug: exact test set commits");
        CheckPreserved(testTarget,check,"test commit");
        foreach(var invalid in new[]{"missing","hash"})
        {
            var bad=Installation(root);
            if(invalid=="missing") File.Delete(Path.Combine(bad,"client-package.json")); else File.AppendAllText(Path.Combine(bad,"DFLegacy.Client.dll"),"tamper");
            check(!UpdateTransaction.Apply(bad,prepared,_=>true).Success && File.ReadAllText(Path.Combine(bad,"DFLegacy.Launcher.exe"))=="old exe","invalid installed metadata cannot enter transaction: "+invalid);
            using var session=new UpdateSession(bad,new LauncherOptions { UpdateServiceUrl = "https://games.example.test:18083" });
            await UpdateTransportTests.Reject(async()=>await session.CheckAsync(0,CancellationToken.None),check,"invalid installed metadata rejects before hosted network check: "+invalid);
        }
        // Real sharing violations exercise each replacement boundary, including metadata and state.
        foreach(var flavor in new[]{"normal","test"})
        foreach(var leaf in (flavor=="test"?new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","debug.dll","client-package.json",".dflegacy/state.json"}:new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","client-package.json",".dflegacy/state.json"}))
        {
            var rollback=Installation(root,flavor); var update=flavor=="test"?test:prepared;
            Directory.CreateDirectory(Path.Combine(rollback,".dflegacy"));
            UpdateTransaction.WriteJson(Path.Combine(rollback,".dflegacy","state.json"),new UpdateState(1,"old",[]));
            var names=(flavor=="test"?new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","debug.dll","client-package.json"}:new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","client-package.json"});
            var before=names.ToDictionary(n=>n,n=>File.ReadAllBytes(Path.Combine(rollback,n)));
            using(var locked=new FileStream(Path.Combine(rollback,leaf),FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                result=UpdateTransaction.Apply(rollback,update,_=>true);
                check(result.BackupDirectory!=null && File.Exists(Path.Combine(result.BackupDirectory,"receipt.json")),"rollback reached durable journal: "+flavor+" "+leaf);
                check(!result.Success && before.All(p=>ReadShared(Path.Combine(rollback,p.Key)).SequenceEqual(p.Value)),"schema2_metadata_rollback_at_each_boundary: "+flavor+" "+leaf);
                check(!Directory.EnumerateFiles(rollback,"*.tmp",SearchOption.AllDirectories).Any(),"failed metadata replacement cleans owned temporary writes: "+flavor+" "+leaf);
                CheckPreserved(rollback,check,"sharing violation "+flavor+" "+leaf);
            }
        }
        foreach(var flavor in new[]{"normal","test"})
        {
            var update=flavor=="test"?test:prepared;
            var names=update.Manifest.Files.Select(f=>f.Path).Append("client-package.json").ToArray();
            for(var boundary=0;boundary<=names.Length+1;boundary++)
            {
                var interrupted=Installation(root,flavor); var id=Guid.NewGuid().ToString("N");
                var backup=Path.Combine(interrupted,".dflegacy-backups",id); Directory.CreateDirectory(backup);
                var original=names.ToDictionary(n=>n,n=>File.ReadAllBytes(Path.Combine(interrupted,n)));
                foreach(var pair in original) File.WriteAllBytes(Path.Combine(backup,pair.Key),pair.Value);
                Directory.CreateDirectory(Path.Combine(interrupted,".dflegacy"));
                File.WriteAllBytes(Path.Combine(interrupted,".dflegacy","update.lock"),[]);
                var receipt=new UpdateReceipt(id,null,new(7,"dev-test",update.Manifest.Files),original.Select(p=>new UpdateFile(p.Key,UpdateTransportTests.Hash(p.Value),p.Value.Length)).ToArray(),update.Manifest);
                UpdateTransaction.WriteJson(Path.Combine(interrupted,".dflegacy","pending.json"),receipt);
                foreach(var name in names.Take(boundary))
                    File.WriteAllBytes(Path.Combine(interrupted,name),File.ReadAllBytes(Path.Combine(update.Directory,name=="client-package.json"?"update-manifest.json":name)));
                if(boundary>names.Length) UpdateTransaction.WriteJson(Path.Combine(interrupted,".dflegacy","state.json"),receipt.NewState);
                using var session=new UpdateSession(interrupted,new LauncherOptions());
                var recovery=session.ReadRecovery()!;
                var recovered=UpdateTransaction.RecoverAndRestart(interrupted,recovery,UpdateSafety.Hash(Path.Combine(interrupted,"DFLegacy.Launcher.exe")),_=>false);
                var state=session.ReadState();
                check(recovered.Success && state==null && original.All(p=>File.ReadAllBytes(Path.Combine(interrupted,p.Key)).SequenceEqual(p.Value)) && !File.Exists(Path.Combine(interrupted,".dflegacy","pending.json")),
                    "schema2_metadata_rollback_at_each_boundary: startup power-loss recovery "+flavor+" "+boundary);
                CheckPreserved(interrupted,check,"power-loss recovery "+flavor+" "+boundary);
            }
        }
        foreach(var invalid in new[]{"journal-path","backup-hash","missing-metadata"})
        {
            var interrupted=Installation(root); var id=Guid.NewGuid().ToString("N");
            var backup=Path.Combine(interrupted,".dflegacy-backups",id); Directory.CreateDirectory(backup);
            var names=new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","client-package.json"};
            var originals=names.Select(name=> { File.Copy(Path.Combine(interrupted,name),Path.Combine(backup,name)); return new UpdateFile(name,UpdateSafety.Hash(Path.Combine(backup,name)),new FileInfo(Path.Combine(backup,name)).Length); }).ToArray();
            if(invalid=="journal-path") originals[0]=originals[0] with {Path="Config.ini"};
            if(invalid=="backup-hash") File.AppendAllText(Path.Combine(backup,"DFLegacy.Client.dll"),"tamper");
            if(invalid=="missing-metadata") File.Delete(Path.Combine(backup,"client-package.json"));
            Directory.CreateDirectory(Path.Combine(interrupted,".dflegacy"));
            File.WriteAllBytes(Path.Combine(interrupted,".dflegacy","update.lock"),[]);
            UpdateTransaction.WriteJson(Path.Combine(interrupted,".dflegacy","pending.json"),new UpdateReceipt(id,null,new(7,"dev-test",prepared.Manifest.Files),originals,prepared.Manifest));
            File.WriteAllText(Path.Combine(interrupted,"DFLegacy.Launcher.exe"),"interrupted new exe");
            using var session=new UpdateSession(interrupted,new LauncherOptions());
            try { session.ReadRecovery(); check(false,"unsafe recovery must fail closed: "+invalid); } catch(InvalidDataException) { }
            check(File.ReadAllText(Path.Combine(interrupted,"DFLegacy.Launcher.exe"))=="interrupted new exe" && File.ReadAllText(Path.Combine(interrupted,"Config.ini"))=="preserve Config.ini" && File.Exists(Path.Combine(interrupted,".dflegacy","pending.json")),"invalid journal/backup never restores partial or unowned files: "+invalid);
        }
        Console.WriteLine("Update V2 tests completed");
    }
    private static byte[] ReadShared(string path) { using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite); using var bytes=new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray(); }
}
