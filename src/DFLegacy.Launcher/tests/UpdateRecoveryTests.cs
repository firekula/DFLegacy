using System.Diagnostics;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;

internal static class UpdateRecoveryTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if(args.Length!=2 || args[0]!="--recovery-parent") return null;
        var target=AppContext.BaseDirectory;
        try
        {
            using var session=new UpdateSession(target,new LauncherOptions());
            var recovery=session.ReadRecovery()!;
            var handoff=await session.PrepareRecoveryAsync(recovery,CancellationToken.None);
            File.WriteAllText(Path.Combine(target,"helper.path"),handoff.HelperPath);
            UpdateHelperTests.CopySidecars(Path.GetDirectoryName(handoff.HelperPath)!);
            if(args[1]=="journal-tamper") File.AppendAllText(Path.Combine(target,".dflegacy","pending.json")," ");
            using var cancelled=new CancellationTokenSource(); if(args[1]=="cancel") cancelled.Cancel();
            var helper=await session.StartAsync(handoff,cancelled.Token);
            if(args[1]=="late-journal") File.AppendAllText(Path.Combine(target,".dflegacy","pending.json")," ");
            File.WriteAllText(Path.Combine(target,"ready.marker"),helper.ProcessId.ToString());
            return 0;
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(target,"rejected.marker"),ex.GetType().Name); return 3; }
    }
    internal static string Create(string root)
    {
        var target=UpdateV2Tests.Installation(root);
        var originalExe=File.ReadAllBytes(Environment.ProcessPath!).Concat(new byte[]{0x6f,0x6c,0x64}).ToArray();
        File.WriteAllBytes(Path.Combine(target,"DFLegacy.Launcher.exe"),originalExe);
        UpdateV2Tests.InstallMetadata(target);
        var id=Guid.NewGuid().ToString("N"); var backup=Path.Combine(target,".dflegacy-backups",id); Directory.CreateDirectory(backup);
        var originals=new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll","client-package.json"}.Select(name=>
        { File.Copy(Path.Combine(target,name),Path.Combine(backup,name)); return new UpdateFile(name,UpdateSafety.Hash(Path.Combine(backup,name)),new FileInfo(Path.Combine(backup,name)).Length); }).ToArray();
        File.WriteAllBytes(Path.Combine(target,"DFLegacy.Launcher.exe"),File.ReadAllBytes(Environment.ProcessPath!));
        File.WriteAllText(Path.Combine(target,"DFLegacy.Client.dll"),"new dll");
        var manifest=UpdateSafety.ReadManifest(UpdateV2Tests.Manifest(target,version:"dev-test"));
        Directory.CreateDirectory(Path.Combine(target,".dflegacy"));
        File.WriteAllBytes(Path.Combine(target,".dflegacy","update.lock"),[]);
        UpdateTransaction.WriteJson(Path.Combine(target,".dflegacy","pending.json"),new UpdateReceipt(id,null,new(7,"dev-test",manifest.Files),originals,manifest));
        File.Delete(Path.Combine(target,"client-package.json")); // Interrupted before metadata replacement.
        return target;
    }
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var target=Create(root); var before=Directory.GetFiles(target,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        var closed=0;
        using(var session=new UpdateSession(target,new LauncherOptions()))
        using(var controller=new UpdateController(session,target,()=>closed++))
        {
            await controller.StartAsync();
            check(controller.State.Phase==UpdatePhase.Ready,"pending recovery becomes ready without installed metadata or network");
            await controller.InstallAsync(false);
            check(closed==0 && before.Count==Directory.GetFiles(target,"*",SearchOption.AllDirectories).Length && before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"recovery decline makes no writes and keeps launcher alive");
        }
        foreach(var mode in new[]{"valid","journal-tamper","late-journal","cancel","unknown-image"})
        {
            target=Create(root); UpdateHelperTests.CopySidecars(target);
            File.WriteAllText(Path.Combine(target,"fixture-owned.marker"),"owned");
            var backup=Directory.GetDirectories(Path.Combine(target,".dflegacy-backups")).Single();
            var originalHash=UpdateSafety.Hash(Path.Combine(backup,"DFLegacy.Launcher.exe"));
            if(mode=="unknown-image") File.AppendAllText(Path.Combine(target,"DFLegacy.Launcher.exe"),"unknown image");
            var currentHash=UpdateSafety.Hash(Path.Combine(target,"DFLegacy.Launcher.exe"));
            var info=new ProcessStartInfo(Path.Combine(target,"DFLegacy.Launcher.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=target};
            info.ArgumentList.Add("--recovery-parent"); info.ArgumentList.Add(mode);
            using var parent=Process.Start(info)!; using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await parent.WaitForExitAsync(timeout.Token);
            if(File.Exists(Path.Combine(target,"ready.marker")))
            { try { using var helper=Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(target,"ready.marker")))); await helper.WaitForExitAsync(timeout.Token); } catch(ArgumentException) {} }
            if(mode=="valid")
            {
                for(var i=0;i<100 && !File.Exists(Path.Combine(target,"restarted.marker"));i++) await Task.Delay(50);
                check(parent.ExitCode==0 && originalHash!=currentHash && UpdateSafety.Hash(Path.Combine(target,"DFLegacy.Launcher.exe"))==originalHash && File.Exists(Path.Combine(target,"restarted.marker")),"actual mapped launcher recovers original EXE only after authenticated parent exit");
                check(ClientPackageManifest.LoadAndValidate(target).Version=="old" && !File.Exists(Path.Combine(target,".dflegacy","pending.json")),"recovery helper restores whole manifest/version and clears journal after verification");
            }
            else
            {
                check(mode=="late-journal"?parent.ExitCode==0 && File.Exists(Path.Combine(target,"ready.marker")):parent.ExitCode==3 && File.Exists(Path.Combine(target,"rejected.marker")),"recovery rejection occurs at intended authenticated boundary: "+mode);
                check(UpdateSafety.Hash(Path.Combine(target,"DFLegacy.Launcher.exe"))==currentHash && !File.Exists(Path.Combine(target,"client-package.json")) && File.Exists(Path.Combine(target,".dflegacy","pending.json")) && !File.Exists(Path.Combine(target,"restarted.marker")),"recovery rejects alteration/cancel without target writes: "+mode);
            }
            Console.WriteLine($"Recovery fixture: {mode}; parentExit={parent.ExitCode}; restored={UpdateSafety.Hash(Path.Combine(target,"DFLegacy.Launcher.exe"))==originalHash}; journalRetained={File.Exists(Path.Combine(target,".dflegacy","pending.json"))}");
            var helperRecord=Path.Combine(target,"helper.path");
            if(File.Exists(helperRecord))
            {
                var helperDirectory=Path.GetDirectoryName(File.ReadAllText(helperRecord))!;
                if(Path.GetDirectoryName(helperDirectory)==Path.TrimEndingDirectorySeparator(Path.GetTempPath()) && Path.GetFileName(helperDirectory).StartsWith("dflegacy-helper-")) Directory.Delete(helperDirectory,true);
            }
        }
        Console.WriteLine("Update recovery tests completed");
    }
}
