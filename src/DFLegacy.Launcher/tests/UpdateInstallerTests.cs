using DFLegacy.Launcher.Updates;
using System.Text.Json;
internal static class UpdateInstallerTests
{
    internal static async Task<PreparedUpdate> Prepare(string root)
    {
        var bytes=UpdateTransportTests.Package(); using var http=UpdateTransportTests.Client(bytes);
        return await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes),root);
    }
    internal static string Installation(string root)
    {
        var target=Path.Combine(root,"install-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target,"DFLegacy.Launcher.exe"),"old exe"); File.WriteAllText(Path.Combine(target,"ijl15.dll"),"old dll");
        foreach(var name in new[]{"Config.ini","launcher.json","DNF.exe","Script.pvf","save.bin"}) File.WriteAllText(Path.Combine(target,name),"preserve "+name);
        return target;
    }
    private static string ReadShared(string path) { using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite); using var reader=new StreamReader(file); return reader.ReadToEnd(); }
    private static bool Old(string target)=>ReadShared(Path.Combine(target,"DFLegacy.Launcher.exe"))=="old exe" && ReadShared(Path.Combine(target,"ijl15.dll"))=="old dll";
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var prepared=await Prepare(root); var target=Installation(root);
        var result=UpdateTransaction.Apply(target,prepared,UpdateInstaller.Restart);
        check(result.Success && !result.Restarted,"successful install survives restart failure");
        check(File.ReadAllText(Path.Combine(target,"DFLegacy.Launcher.exe"))=="new executable" && File.ReadAllText(Path.Combine(target,"ijl15.dll"))=="new dll","both binaries replaced");
        check(result.BackupDirectory!=null && File.ReadAllText(Path.Combine(result.BackupDirectory,"DFLegacy.Launcher.exe"))=="old exe" && File.ReadAllText(Path.Combine(result.BackupDirectory,"ijl15.dll"))=="old dll","verified backup contains both originals");
        foreach(var name in new[]{"Config.ini","launcher.json","DNF.exe","Script.pvf","save.bin"}) check(File.ReadAllText(Path.Combine(target,name))=="preserve "+name,"preserved "+name);
        check(new UpdateInstaller(target).ReadState()?.ReleaseId==7,"successful install records release ID");
        check(new UpdateInstaller(target).IsCurrent(prepared),"matching hashes prevent repeat installation");
        var fresh=Installation(root); File.WriteAllText(Path.Combine(fresh,"DFLegacy.Launcher.exe"),"new executable"); File.WriteAllText(Path.Combine(fresh,"ijl15.dll"),"new dll");
        check(new UpdateInstaller(fresh).IsCurrent(prepared),"legacy no-state matching install is current");
        check(new UpdateInstaller(fresh).RecordCurrentIfMatching(prepared) && new UpdateInstaller(fresh).ReadState()?.ReleaseId==7,"matching no-state install adopts release ID without reinstalling");
        var before=File.ReadAllBytes(Path.Combine(target,".dflegacy","state.json"));
        result=UpdateTransaction.Apply(target,prepared with { Release=prepared.Release with { Id=6 } },_=>true);
        check(!result.Success && File.ReadAllBytes(Path.Combine(target,".dflegacy","state.json")).SequenceEqual(before),"older release cannot lower state");
        var locked=Installation(root);
        using(var game=new FileStream(Path.Combine(locked,"ijl15.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))
        { result=UpdateTransaction.Apply(locked,prepared,_=>true); check(!result.Success && Old(locked),"game DLL read lock leaves both originals"); }
        var rollback=Installation(root);
        using(var second=new FileStream(Path.Combine(rollback,"ijl15.dll"),FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))
        { result=UpdateTransaction.Apply(rollback,prepared,_=>true); check(!result.Success && Old(rollback),"failed second replacement rolls back first binary"); check(result.BackupDirectory!=null && File.Exists(Path.Combine(result.BackupDirectory,"receipt.json")),"second replace reached backed-up transaction"); }
        var duplicate=Installation(root); Directory.CreateDirectory(Path.Combine(duplicate,".dflegacy"));
        using(var exclusive=new FileStream(Path.Combine(duplicate,".dflegacy","update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        { result=UpdateTransaction.Apply(duplicate,prepared,_=>true); check(!result.Success && Old(duplicate),"exclusive updater lock rejects simultaneous installation"); }
        foreach(var restartSucceeded in new[]{true,false})
        {
            var receiptLocked=Installation(root); var metadata=Path.Combine(receiptLocked,".dflegacy"); Directory.CreateDirectory(metadata);
            var resultPath=Path.Combine(metadata,"last-result.json"); File.WriteAllText(resultPath,"previous receipt");
            var restartCalls=0;
            using(var resultLock=new FileStream(resultPath,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                result=UpdateTransaction.Apply(receiptLocked,prepared,_=>{ restartCalls++; return restartSucceeded; });
                check(result.Success && result.Restarted==restartSucceeded && restartCalls==1,
                    "locked result receipt preserves committed install and actual restart outcome: "+restartSucceeded);
                check(File.ReadAllText(Path.Combine(receiptLocked,"DFLegacy.Launcher.exe"))=="new executable" && File.ReadAllText(Path.Combine(receiptLocked,"ijl15.dll"))=="new dll" &&
                    new UpdateInstaller(receiptLocked).ReadState()?.ReleaseId==7 && !File.Exists(Path.Combine(metadata,"pending.json")),
                    "locked result receipt does not roll back committed binaries or state");
                check(result.BackupDirectory!=null && File.ReadAllText(Path.Combine(result.BackupDirectory,"ijl15.dll"))=="old dll",
                    "locked result receipt retains recovery backup");
                check(File.ReadAllText(resultPath)=="previous receipt" && result.Message.Contains("记录"),
                    "locked result receipt preserves old receipt and reports metadata warning");
                Console.WriteLine($"Result receipt lock: restartRequested={restartSucceeded}; Success={result.Success}; Restarted={result.Restarted}; restartCalls={restartCalls}");
            }
        }
        File.AppendAllText(Path.Combine(prepared.Directory,"ijl15.dll"),"tampered"); var tampered=Installation(root);
        result=UpdateTransaction.Apply(tampered,prepared,_=>true); check(!result.Success && Old(tampered),"modified stage rejected before writing target");
        Console.WriteLine("Update installer tests completed");
    }
}
