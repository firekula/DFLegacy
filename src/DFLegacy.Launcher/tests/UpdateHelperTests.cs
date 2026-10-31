using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Text.Json;
using DFLegacy.Launcher.Updates;
internal static class UpdateHelperTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if(args.Length>0 && args[0]=="--dflegacy-apply-update")
        {
            var outcome=await UpdateInstaller.RunHelperWithOutcomeAsync(args);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"helper-outcome.json"),JsonSerializer.Serialize(outcome,UpdateSafety.Json));
            return outcome.ExitCode;
        }
        if(args.Length==3 && args[0]=="--update-parent")
        {
            var target=AppContext.BaseDirectory;
            var bytes=File.ReadAllBytes(Path.Combine(target,"fixture.zip"));
            using var http=UpdateTransportTests.Client(bytes);
            var prepared=await new GitHubUpdateClient(http,"firekula/DFLegacy").DownloadAsync(UpdateTransportTests.Release(bytes),args[1]);
            var installer=new UpdateInstaller(target);
            try
            {
                using var session=new UpdateSession(target,new DFLegacy.Launcher.LauncherOptions());
                var handoff=await session.PrepareAsync(prepared,CancellationToken.None);
                File.WriteAllText(Path.Combine(target,"helper.path"),handoff.HelperPath);
                CopySidecars(Path.GetDirectoryName(handoff.HelperPath)!);
                if(args[2]=="stage-tamper") File.AppendAllText(Path.Combine(prepared.Directory,"DFLegacy.Client.dll"),"tamper");
                if(args[2]=="helper-tamper") File.AppendAllText(handoff.HelperPath,"tamper");
                if(args[2]=="parent-tamper") handoff=new UpdateHandoff(handoff.Plan with { ParentStartTicks=handoff.Plan.ParentStartTicks+1 });
                if(args[2]=="target-tamper") handoff=new UpdateHandoff(handoff.Plan with { Target=args[1] });
                if(args[2]=="raw-parent") await SendRaw(handoff.Plan with { ParentStartTicks=handoff.Plan.ParentStartTicks+1 });
                if(args[2]=="raw-target") await SendRaw(handoff.Plan with { Target=args[1] });
                var helper=await installer.StartHelperAsync(handoff);
                if(args[2]=="late-stage") File.AppendAllText(Path.Combine(prepared.Directory,"DFLegacy.Client.dll"),"tampered after readiness");
                File.WriteAllText(Path.Combine(target,"ready.marker"),helper.ProcessId.ToString());
                return 0;
            }
            catch(Exception ex) { File.WriteAllText(Path.Combine(target,"rejected.marker"),ex.GetType().Name); return 3; }
        }
        if(args.Length==0 && File.Exists(Path.Combine(AppContext.BaseDirectory,"fixture-owned.marker")))
        { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"restarted.marker"),"yes"); return 0; }
        return null;
    }
    private static async Task SendRaw(UpdatePlan plan)
    {
        var name="dflegacy-update-"+Guid.NewGuid().ToString("N");
        using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var info=new ProcessStartInfo(plan.HelperPath){UseShellExecute=false,CreateNoWindow=true};
        info.ArgumentList.Add("--dflegacy-apply-update"); info.ArgumentList.Add(name);
        using var child=Process.Start(info)!; using var timeout=new CancellationTokenSource(10000);
        await pipe.WaitForConnectionAsync(timeout.Token);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(plan,UpdateSafety.Json);
        await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length),timeout.Token); await pipe.WriteAsync(bytes,timeout.Token); await pipe.FlushAsync(timeout.Token);
        await child.WaitForExitAsync(timeout.Token);
        if(child.ExitCode==0) throw new Exception("Malicious raw handoff was accepted");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"raw-rejected.marker"),"rejected");
        throw new InvalidDataException("Raw handoff rejected by helper");
    }
    internal static void CopySidecars(string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(var source in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            var name=Path.GetFileName(source);
            if(name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase) && name!="DFLegacy.Client.dll" || name.EndsWith(".deps.json") || name.EndsWith(".runtimeconfig.json"))
                File.Copy(source,Path.Combine(destination,name),true);
        }
    }
    private static byte[] Package(byte[] exe)
    {
        byte[] dll=[11,22,33,44];
        var manifest=new { schemaVersion=2, flavor="normal", version="dev-test", commit=new string('a',40), files=new[]{new{path="DFLegacy.Launcher.exe",sha256=UpdateTransportTests.Hash(exe),size=exe.Length},new{path="DFLegacy.Client.dll",sha256=UpdateTransportTests.Hash(dll),size=dll.Length}}};
        using var memory=new MemoryStream();
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            void Add(string name,byte[] bytes) { using var output=zip.CreateEntry(name).Open(); output.Write(bytes); }
            Add("DFLegacy.Launcher.exe",exe); Add("DFLegacy.Client.dll",dll); Add("update-manifest.json",JsonSerializer.SerializeToUtf8Bytes(manifest));
        }
        return memory.ToArray();
    }
    internal static async Task Run(string root,Action<bool,string> check)
    {
        check(await UpdateInstaller.RunHelperAsync(["--dflegacy-apply-update","arbitrary-plan.json"])!=0,"helper rejects arbitrary plan filenames");
        foreach(var mode in new[]{"valid","stage-tamper","helper-tamper","parent-tamper","target-tamper","raw-parent","raw-target","late-stage","restart-failure","rollback"})
        {
            var target=Path.Combine(root,"process-"+mode); Directory.CreateDirectory(target); CopySidecars(target);
            var executable=Path.Combine(target,"DFLegacy.Launcher.exe"); var own=Environment.ProcessPath!;
            File.Copy(own,executable); File.WriteAllBytes(Path.Combine(target,"DFLegacy.Client.dll"),[1,2,3]);
            UpdateV2Tests.InstallMetadata(target);
            File.WriteAllText(Path.Combine(target,"fixture-owned.marker"),"owned");
            File.WriteAllBytes(Path.Combine(target,"fixture.zip"),Package(mode=="restart-failure"?new byte[]{1,2,3}:File.ReadAllBytes(own)));
            var needsFeedback=mode is "late-stage" or "restart-failure" or "rollback";
            var receipt=Path.Combine(target,".dflegacy","last-result.json");
            if(needsFeedback) { Directory.CreateDirectory(Path.GetDirectoryName(receipt)!); File.WriteAllText(receipt,"unchanged receipt"); }
            using var receiptLock=needsFeedback?new FileStream(receipt,FileMode.Open,FileAccess.Read,FileShare.Read):null;
            using var secondLock=mode=="rollback"?new FileStream(Path.Combine(target,"DFLegacy.Client.dll"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite):null;
            var info=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=target };
            info.ArgumentList.Add("--update-parent"); info.ArgumentList.Add(root); info.ArgumentList.Add(mode);
            using var parent=Process.Start(info)!; using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await parent.WaitForExitAsync(timeout.Token);
            if(mode=="valid")
            {
                check(parent.ExitCode==0 && File.Exists(Path.Combine(target,"ready.marker")),"helper handshake completes before parent exits");
                var end=DateTime.UtcNow.AddSeconds(15);
                while(!File.Exists(Path.Combine(target,"restarted.marker")) && DateTime.UtcNow<end) await Task.Delay(50);
                check(File.Exists(Path.Combine(target,"restarted.marker")),"same-exe helper waits for parent then restarts new launcher");
                check(File.ReadAllBytes(Path.Combine(target,"DFLegacy.Client.dll")).SequenceEqual(new byte[]{11,22,33,44}),"helper installs package after parent exit");
                if(File.Exists(Path.Combine(target,"ready.marker")))
                { try { using var helper=Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(target,"ready.marker")))); await helper.WaitForExitAsync(timeout.Token); } catch(ArgumentException) {} }
            }
            else if(needsFeedback)
            {
                if(File.Exists(Path.Combine(target,"ready.marker")))
                { try { using var helper=Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(target,"ready.marker")))); await helper.WaitForExitAsync(timeout.Token); } catch(ArgumentException) {} }
                var expected=mode=="restart-failure"?new byte[]{11,22,33,44}:new byte[]{1,2,3};
                using(var dll=new FileStream(Path.Combine(target,"DFLegacy.Client.dll"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                using(var content=new MemoryStream())
                { dll.CopyTo(content); check(content.ToArray().SequenceEqual(expected) && !File.Exists(Path.Combine(target,"restarted.marker")),"helper preserves truthful file outcome after parent exit: "+mode); }
                var helperDirectory=Path.GetDirectoryName(File.ReadAllText(Path.Combine(target,"helper.path")))!;
                using var outcome=JsonDocument.Parse(File.ReadAllText(Path.Combine(helperDirectory,"helper-outcome.json")));
                var result=outcome.RootElement.GetProperty("result");
                check(result.ValueKind==JsonValueKind.Object,"helper retains immediate structured outcome despite unwritable receipt: "+mode);
                if(result.ValueKind==JsonValueKind.Object)
                {
                    check(result.GetProperty("success").GetBoolean()==(mode=="restart-failure") && !result.GetProperty("restarted").GetBoolean(),"helper immediate outcome matches committed files and failed restart: "+mode);
                    check(outcome.RootElement.GetProperty("target").GetString()==Path.TrimEndingDirectorySeparator(target),"helper feedback identifies authenticated installation");
                    if(mode=="rollback") check(File.Exists(Path.Combine(result.GetProperty("backupDirectory").GetString()!,"receipt.json")),"rollback feedback retains actual recovery location");
                }
                check(outcome.RootElement.GetProperty("exitCode").GetInt32()!=0,"helper exit signals missing restart/failure: "+mode);
                check(File.ReadAllText(receipt)=="unchanged receipt","immediate feedback does not require receipt writes: "+mode);
            }
            else check(parent.ExitCode!=0 && File.ReadAllBytes(Path.Combine(target,"DFLegacy.Client.dll")).SequenceEqual(new byte[]{1,2,3}),"helper rejects "+mode+" and preserves target");
            if(mode.StartsWith("raw-")) check(File.Exists(Path.Combine(target,"raw-rejected.marker")),"raw malicious handoff rejected by helper itself");
            var helperRecord=Path.Combine(target,"helper.path");
            if(File.Exists(helperRecord))
            {
                var directory=Path.GetDirectoryName(File.ReadAllText(helperRecord))!;
                if(Path.GetDirectoryName(directory)==Path.TrimEndingDirectorySeparator(Path.GetTempPath()) && Path.GetFileName(directory).StartsWith("dflegacy-helper-"))
                    Directory.Delete(directory,true);
            }
        }
        Console.WriteLine("Update helper process tests completed");
    }
}
