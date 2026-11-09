using DFLegacy.Launcher.Updates;
using System.Diagnostics;
using System.Text.Json.Nodes;
internal static class UpdateArtifactTests
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var installation=Path.Combine(root,"artifact-install"); Directory.CreateDirectory(installation);
        var backup=Path.Combine(installation,".dflegacy-backups"); Directory.CreateDirectory(backup); File.WriteAllText(Path.Combine(backup,"keep"),"original");
        var bytes=UpdateTransportTests.Package(); using var http=UpdateTransportTests.Client(bytes); var client=new GitHubUpdateClient(http,"firekula/DFLegacy");
        var lease=UpdateArtifacts.Create(installation); var leasePath=lease.Directory;
        var package=await client.DownloadAsync(UpdateTransportTests.Release(bytes),lease.Directory); lease.Track(package);
        UpdateArtifacts.Sweep(installation);
        check(Directory.Exists(package.Directory),"cleanup does not touch caller's active lease");
        lease.Dispose();
        check(!Directory.Exists(leasePath),"disposing declined/cancelled/current attempt removes its exact package and lease");
        check(File.ReadAllText(Path.Combine(backup,"keep"))=="original","artifact cleanup never removes recovery backups");
        lease=UpdateArtifacts.Create(installation); leasePath=lease.Directory;
        package=await client.DownloadAsync(UpdateTransportTests.Release(bytes),lease.Directory); lease.Track(package); lease.Release();
        UpdateArtifacts.Sweep(installation);
        check(!Directory.Exists(leasePath),"next startup reclaims orphaned download attempt");
        lease=UpdateArtifacts.Create(installation); leasePath=lease.Directory;
        package=await client.DownloadAsync(UpdateTransportTests.Release(bytes),lease.Directory); lease.Track(package);
        File.WriteAllText(Path.Combine(package.Directory,"unowned.txt"),"keep"); lease.Dispose();
        check(File.Exists(Path.Combine(package.Directory,"unowned.txt")),"cleanup refuses unowned filenames instead of recursive deletion");
        check(UpdateArtifacts.PresentOnce(installation,"same warning") && !UpdateArtifacts.PresentOnce(installation,"same warning"),"same update result is presented once across startup instances");

        // Exercise a real owned native process with the same temporary helper path shape.
        lease=UpdateArtifacts.Create(installation); leasePath=lease.Directory;
        package=await client.DownloadAsync(UpdateTransportTests.Release(bytes),lease.Directory); lease.Track(package);
        var helperDirectory=Path.Combine(Path.GetTempPath(),"dflegacy-helper-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(helperDirectory);
        var helperPath=Path.Combine(helperDirectory,"DFLegacy.Launcher.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"ping.exe"),helperPath);
        var handoff=new UpdateHandoff(new UpdatePlan(installation,package,0,0,"",helperPath,"")); lease.TrackHelper(handoff);
        var info=new ProcessStartInfo(helperPath) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        info.ArgumentList.Add("-t"); info.ArgumentList.Add("127.0.0.1");
        using(var child=Process.Start(info)!)
        {
            try
            {
                await Task.Delay(100); check(!child.HasExited,"owned native helper fixture is running");
                lease.MarkStarted(new(child.Id)); lease.Release();
                UpdateArtifacts.DeleteUnstartedHelper(helperPath,true);
                check(File.Exists(helperPath),"failed-acquisition cleanup write probe also preserves a running helper executable");
                UpdateArtifacts.Sweep(installation);
                check(File.Exists(helperPath) && File.Exists(Path.Combine(package.Directory,"DFLegacy.Client.dll")),"active helper identity defers deletion of helper and staging inputs");
            }
            finally { if(!child.HasExited) child.Kill(); await child.WaitForExitAsync(); }
        }
        UpdateArtifacts.Sweep(installation);
        check(!Directory.Exists(helperDirectory) && !Directory.Exists(leasePath),"helper exit permits exact helper and staged bytes cleanup");

        lease=UpdateArtifacts.Create(installation); leasePath=lease.Directory;
        var outside=Path.Combine(root,"outside-preserve"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside,"DFLegacy.Launcher.exe"),"do not delete");
        lease.Release();
        var record=Path.Combine(leasePath,"cleanup.json"); var node=JsonNode.Parse(File.ReadAllText(record))!;
        node["helper"]=outside; File.WriteAllText(record,node.ToJsonString()); UpdateArtifacts.Sweep(installation);
        check(File.ReadAllText(Path.Combine(outside,"DFLegacy.Launcher.exe"))=="do not delete","mutable cleanup receipt cannot name arbitrary helper paths");

        lease=UpdateArtifacts.Create(installation); leasePath=lease.Directory;
        var foreign=Path.Combine(Path.GetTempPath(),"dflegacy-helper-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign,"DFLegacy.Launcher.exe"),"foreign helper"); File.WriteAllText(Path.Combine(foreign,"dflegacy-owner"),"somebody else");
        lease.Release(); record=Path.Combine(leasePath,"cleanup.json"); node=JsonNode.Parse(File.ReadAllText(record))!;
        node["helper"]=Path.GetFileName(foreign); File.WriteAllText(record,node.ToJsonString()); UpdateArtifacts.Sweep(installation);
        check(File.ReadAllText(Path.Combine(foreign,"DFLegacy.Launcher.exe"))=="foreign helper","valid-shaped helper path still requires matching caller ownership marker");
        // Exact test-owned files only; never follow paths from that forged receipt.
        File.Delete(Path.Combine(foreign,"DFLegacy.Launcher.exe")); File.Delete(Path.Combine(foreign,"dflegacy-owner")); Directory.Delete(foreign,false);

        lease=UpdateArtifacts.Create(installation); leasePath=lease.Directory;
        var linked=Path.Combine(leasePath,"dflegacy-update-"+Guid.NewGuid().ToString("N"));
        var junction=new ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        junction.ArgumentList.Add("/c"); junction.ArgumentList.Add("mklink"); junction.ArgumentList.Add("/J"); junction.ArgumentList.Add(linked); junction.ArgumentList.Add(outside);
        using(var process=Process.Start(junction)!) { await process.WaitForExitAsync(); check(process.ExitCode==0,"owned reparse cleanup fixture created"); }
        lease.Dispose();
        check(File.ReadAllText(Path.Combine(outside,"DFLegacy.Launcher.exe"))=="do not delete" && Directory.Exists(linked),"cleanup refuses reparse traversal and preserves target bytes");
        Directory.Delete(linked,false);
        Console.WriteLine("Update artifact ownership checks executed");
    }
}
