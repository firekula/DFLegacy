using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Launcher;

internal static class StandaloneTests
{
    internal static string WriteManifest(string root, string flavor = "normal")
    {
        var names = flavor == "normal" ? new[] { "DFLegacy.Launcher.exe", "DFLegacy.Client.dll" } : new[] { "DFLegacy.Launcher.exe", "DFLegacy.Client.dll", "debug.dll" };
        var json = JsonSerializer.Serialize(new { schemaVersion = 2, version = "1.2.3", commit = new string('a',40), flavor,
            files = names.Select(name => new { path = name, size = new FileInfo(Path.Combine(root,name)).Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,name)))) }) });
        File.WriteAllText(Path.Combine(root,"client-package.json"),json);
        return json;
    }
    internal static async Task Run(string parent, Action<bool,string> check)
    {
        var root = Path.Combine(parent,"standalone 中文 𠮷"); Directory.CreateDirectory(root);
        foreach(var name in new[] { "DFLegacy.Launcher.exe", "DFLegacy.Client.dll", "debug.dll", "DNF.exe" }) File.WriteAllText(Path.Combine(root,name),name);
        var manifestPath = Path.Combine(root,"client-package.json"); var json = WriteManifest(root);
        var manifest = ClientPackageManifest.LoadAndValidate(root);
        check(manifest.Flavor == "normal" && manifest.Files.Count == 2,"strict normal package allows inert leftover debug file");
        foreach(var invalid in new[] { json.Replace("\"schemaVersion\":2","\"schemaVersion\":1"), json.Replace("\"schemaVersion\":2","\"schemaVersion\":2,\"schemaVersion\":2"), json.Replace("\"normal\"","\"test\""), json.Replace("DFLegacy.Client.dll","../DFLegacy.Client.dll"), json.Replace("\"size\":19","\"size\":0"), json.Replace("\"commit\":\"a","\"commit\":\"A"), json.Replace("\"flavor\"","\"extra\":0,\"flavor\""), json + "{}" })
        {
            File.WriteAllText(manifestPath,invalid);
            try { ClientPackageManifest.LoadAndValidate(root); check(false,"reject malformed/mixed manifest"); } catch(InvalidDataException) { }
        }
        File.WriteAllText(manifestPath,json); File.AppendAllText(Path.Combine(root,"DFLegacy.Client.dll"),"changed");
        try { ClientPackageManifest.LoadAndValidate(root); check(false,"mixed hash rejected"); } catch(InvalidDataException) { }
        var client = Path.Combine(root,"DNF.exe"); var ijl = Path.Combine(root,"ijl15.dll");
        File.WriteAllText(ijl,"unknown"); File.WriteAllText(Path.Combine(root,"ijl15.original.dll"),"misleading patch");
        var before = Directory.GetFiles(root).ToDictionary(p=>p,File.ReadAllBytes);
        var inspection = ClientIjlMigration.Inspect(client);
        check(inspection.State == IjlMigrationState.Unknown,"unknown IJL and misleading backup never trusted");
        try { ClientIjlMigration.RestoreConfirmed(inspection,false); } catch(InvalidDataException) { }
        check(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && before.Count == Directory.GetFiles(root).Length,"cancel/unknown migration makes zero writes");
        check(ClientIjlMigration.OriginalSha256 == "334aa12f7dee453d1c6cb1b661a3bb3494d3e4cc9c2ff3f9002064c78404e43a","actual original trust identity");
        foreach(var name in new[] { "launcher.log","netdump-123.log","Config.ini","unrelated.txt","state.json","netdump-no.log" }) File.WriteAllText(Path.Combine(root,name),name);
        var logs = ClientLogPaths.GetDirectory(client); Directory.CreateDirectory(Path.Combine(logs,"legacy"));
        File.WriteAllText(Path.Combine(logs,"legacy","launcher.log"),"keep");
        using(var locked = File.Open(Path.Combine(root,"netdump-123.log"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)) ClientLogPaths.ArchiveLegacy(client);
        check(File.Exists(Path.Combine(root,"netdump-123.log")),"locked logs survive");
        check(File.ReadAllText(Path.Combine(logs,"legacy","launcher.log"))=="keep" && !File.Exists(Path.Combine(root,"launcher.log")),"archive collisions never overwrite");
        check(new[]{"Config.ini","unrelated.txt","state.json","netdump-no.log"}.All(n=>File.Exists(Path.Combine(root,n))),"unrelated configs/control files survive archive");
        ClientLogPaths.ArchiveLegacy(client); check(!File.Exists(Path.Combine(root,"netdump-123.log")),"locked log archival retries");
        var blocked = Path.Combine(parent,"blocked-log"); Directory.CreateDirectory(blocked); File.WriteAllText(Path.Combine(blocked,"DNF.exe"),"x"); File.WriteAllText(Path.Combine(blocked,"Logs"),"blocked");
        check(!ClientLogPaths.TryWrite(Path.Combine(blocked,"DNF.exe"),"test"),"unwritable log path reports unavailable without root fallback");
        var migrationRoot=Path.Combine(parent,"migration transaction"); Directory.CreateDirectory(migrationRoot);
        var migrationClient=Path.Combine(migrationRoot,"DNF.exe"); var target=Path.Combine(migrationRoot,"ijl15.dll");
        var candidate=Path.Combine(migrationRoot,"ijl15.dll.bak");
        File.WriteAllText(target,"controlled synthetic project patch"); File.WriteAllText(candidate,"controlled synthetic original");
        File.WriteAllText(Path.Combine(migrationRoot,"ijl15.original.dll"),"controlled synthetic project patch");
        var expectedOriginal=ClientIjlMigration.Hash(candidate); var expectedPatch=ClientIjlMigration.Hash(target); var trusted=new HashSet<string>{expectedPatch};
        var synthetic=ClientIjlMigration.InspectCore(migrationClient,expectedOriginal,trusted);
        check(synthetic.CandidatePath==candidate,"identity selects verified candidate over misleading original filename");
        ClientIjlMigration.RestoreCore(synthetic,false,expectedOriginal,trusted);
        check(ClientIjlMigration.Hash(target)==expectedPatch && Directory.GetFiles(migrationRoot).Length==3,"explicit cancellation makes no transaction files");
        using(var locked=File.Open(target,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try { ClientIjlMigration.RestoreCore(synthetic,true,expectedOriginal,trusted); check(false,"locked target must fail restoration"); } catch(IOException) { }
        }
        check(ClientIjlMigration.Hash(target)==expectedPatch && Directory.GetFiles(migrationRoot).Length==3,"failed replacement rolls back and cleans owned temporary");
        ClientIjlMigration.RestoreCore(synthetic,true,expectedOriginal,trusted);
        check(ClientIjlMigration.Hash(target)==expectedOriginal && Directory.GetFiles(migrationRoot,"*.legacy-patch-*.bak").Single() is { } backup && ClientIjlMigration.Hash(backup)==expectedPatch,"confirmed synthetic restore retains exact previous patch backup");
        var zip=Environment.GetEnvironmentVariable("DFL_LEGACY_PATCH_ARCHIVE") ?? throw new InvalidOperationException("Set DFL_LEGACY_PATCH_ARCHIVE to the immutable previous release ZIP (SHA-256 is checked by this test).");
        using(var archive=System.IO.Compression.ZipFile.OpenRead(zip))
        using(var input=archive.Entries.Single(e=>e.FullName=="ijl15.dll").Open())
        using(var output=File.Create(target)) input.CopyTo(output);
        File.Copy(target,Path.Combine(migrationRoot,"ijl15.original.dll"),true);
        File.Delete(candidate);
        check(ClientIjlMigration.Hash(target)=="f73618ba6bdca6483ef12e5eb4bee7e621cdbfd910b5bcac7fe1743078c28cc7","actual immutable prior release patch fixture identity");
        var actual=ClientIjlMigration.Inspect(migrationClient);
        check(actual.State==IjlMigrationState.RestoreRequired && actual.CandidatePath is null,"actual previous patch is recognized but misleading backup never restored");
        try { ClientIjlMigration.RestoreConfirmed(actual,true); check(false,"no valid original blocks migration"); } catch(InvalidDataException) { }
        check(Directory.GetFiles(migrationRoot,"*.tmp").Length==0,"no original candidate creates no temporary files");
        var diagnostic=Path.Combine(root,"render-diagnostics"); Directory.CreateDirectory(diagnostic);
        File.WriteAllText(Path.Combine(diagnostic,"21.json"),JsonSerializer.Serialize(new{version=1,pid=21,processStartFileTime=DateTime.UtcNow.AddMinutes(-1).ToFileTimeUtc(),writtenFileTime=DateTime.UtcNow.ToFileTimeUtc(),requested="sharp",effective="sharp",reason="ok",hresult="0x00000000",width=800,height=600}));
        File.WriteAllText(Path.Combine(diagnostic,"22.json"),"{\"unrelated\":true}");
        ClientLogPaths.ArchiveLegacy(client);
        check(!File.Exists(Path.Combine(diagnostic,"21.json")) && File.Exists(Path.Combine(diagnostic,"22.json")),"archive only validated owned PID diagnostics");
        check(ClientRenderDiagnostic.DescribeLatest(client).Contains("800 × 600"),"archived diagnostic stays readable");
        var newDiagnostics=Path.Combine(logs,"render-diagnostics"); Directory.CreateDirectory(newDiagnostics);
        File.WriteAllText(Path.Combine(newDiagnostics,"23.json"),File.ReadAllText(Path.Combine(logs,"legacy","render-diagnostics","21.json")).Replace("\"width\":800","\"width\":1280"));
        check(ClientRenderDiagnostic.DescribeLatest(client).Contains("1280 × 600"),"new diagnostic takes priority over legacy");
        using(var first=File.OpenRead(client)) using(var second=File.OpenRead(ijl)) check(ClientFileIdentity.Of(first.SafeFileHandle)!=ClientFileIdentity.Of(second.SafeFileHandle),"distinct file IDs cannot compare equal");
        ClientFileIdentity.RequireSameDirectory(root,Path.Combine(root,"..",Path.GetFileName(root)));
        try { ClientFileIdentity.RequireSameDirectory(root,parent); check(false,"mismatched installation root blocked"); } catch(InvalidDataException ex) { check(ex.Message.Contains("完整工具包"),"root mismatch is actionable"); }
        var helper=StandaloneClientInstaller.EnsureHelper(root);
        check(helper.Contains(Path.Combine(".dflegacy","runtime")) && !File.Exists(Path.Combine(root,"DFLegacy.Bootstrap32.exe")),"embedded helper extracts only into controlled runtime identity folder");
        File.AppendAllText(helper,"tamper");
        try { StandaloneClientInstaller.EnsureHelper(root); check(false,"tampered extracted helper rejected"); } catch(InvalidDataException) { }
        var linkRoot=Path.Combine(parent,"linked-logs"); Directory.CreateDirectory(linkRoot);
        var external=Path.Combine(parent,"external-owned-fixture"); Directory.CreateDirectory(external); File.WriteAllText(Path.Combine(external,"launcher.log"),"keep linked");
        var junction=new System.Diagnostics.ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"/c","mklink","/J",Path.Combine(linkRoot,"Logs"),external}) junction.ArgumentList.Add(arg);
        using(var created=System.Diagnostics.Process.Start(junction)!) { created.WaitForExit(); check(created.ExitCode==0,"owned directory junction fixture created"); }
        check(!ClientLogPaths.TryWrite(Path.Combine(linkRoot,"DNF.exe"),"must not follow"),"reparse log directory is rejected");
        ClientLogPaths.ArchiveLegacy(Path.Combine(linkRoot,"Logs","DNF.exe"));
        check(File.ReadAllText(Path.Combine(external,"launcher.log"))=="keep linked","archive skips reparse root without touching target");
        Directory.Delete(Path.Combine(linkRoot,"Logs"));
        await BootstrapManagedTests.Run(parent,check);
    }
}
