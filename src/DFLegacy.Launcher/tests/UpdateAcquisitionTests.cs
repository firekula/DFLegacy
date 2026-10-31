using DFLegacy.Launcher.Updates;
internal static class UpdateAcquisitionTests
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        // Keep the Win32-pinned updater paths below MAX_PATH even when the suite's
        // caller supplies a long TEMP. This root is created and owned only here.
        var tempBase=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Temp"));
        var ownedName="ua-"+Guid.NewGuid().ToString("N");
        var ownedTemp=Path.Combine(tempBase,ownedName);
        Directory.CreateDirectory(ownedTemp);
        var originalTemp=Environment.GetEnvironmentVariable("TEMP");
        var originalTmp=Environment.GetEnvironmentVariable("TMP");
        var originalPath=Path.GetTempPath();
        // This exact sentinel path is owned by this test, but represents a concurrent
        // helper outside the acquisition scope. Never discover ownership by enumeration.
        var concurrent=Path.Combine(originalPath,"dflegacy-helper-"+Guid.NewGuid().ToString("N"));
        var concurrentExe=Path.Combine(concurrent,"DFLegacy.Launcher.exe");
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creation=Task.Run(async ()=>
        {
            await start.Task; Directory.CreateDirectory(concurrent);
            File.WriteAllText(concurrentExe,"concurrent helper sentinel");
        });
        try
        {
            await WithOwnedTemp(ownedTemp,async ()=>
            {
                start.SetResult(); await creation;
                var isolated=Path.TrimEndingDirectorySeparator(Path.GetTempPath()).Equals(ownedTemp,StringComparison.OrdinalIgnoreCase);
                check(isolated,"acquisition helper allocation is isolated inside the unique test-owned TEMP root");
                // Do not invoke allocation-failure tests unless ownership is established.
                if(isolated) await RunOwned(root,ownedTemp,check);
                check(File.Exists(concurrentExe) && File.ReadAllText(concurrentExe)=="concurrent helper sentinel",
                    "concurrent helper outside owned acquisition root is unchanged");
            });
            check(Environment.GetEnvironmentVariable("TEMP")==originalTemp && Environment.GetEnvironmentVariable("TMP")==originalTmp && Path.GetTempPath()==originalPath,
                "acquisition fixture restores process TEMP and TMP");
            try { await WithOwnedTemp(ownedTemp,()=>Task.FromException(new IOException("owned scope failure"))); }
            catch(IOException) { }
            check(Environment.GetEnvironmentVariable("TEMP")==originalTemp && Environment.GetEnvironmentVariable("TMP")==originalTmp && Path.GetTempPath()==originalPath,
                "acquisition fixture restores TEMP and TMP even if its body throws");
        }
        finally
        {
            start.TrySetResult(); await creation;
            // Exact paths created above, never a set difference from global TEMP.
            if(File.Exists(concurrentExe)) File.Delete(concurrentExe);
            if(Directory.Exists(concurrent)) Directory.Delete(concurrent,false);
            var resolved=Path.GetFullPath(ownedTemp);
            if(!Path.GetDirectoryName(resolved)!.Equals(tempBase,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(resolved)!=ownedName)
                throw new InvalidOperationException("Acquisition cleanup escaped its exact owned root.");
            if(Directory.Exists(resolved)) Directory.Delete(resolved,true);
        }
    }
    private static async Task WithOwnedTemp(string directory,Func<Task> action)
    {
        var previousTemp=Environment.GetEnvironmentVariable("TEMP");
        var previousTmp=Environment.GetEnvironmentVariable("TMP");
        try
        {
            Environment.SetEnvironmentVariable("TEMP",directory);
            Environment.SetEnvironmentVariable("TMP",directory);
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMP",previousTemp);
            Environment.SetEnvironmentVariable("TMP",previousTmp);
        }
    }
    private static async Task RunOwned(string root,string ownedTemp,Action<bool,string> check)
    {
        var install=Path.Combine(root,"acquisition"); Directory.CreateDirectory(install);
        var source=Path.Combine(install,"source.exe"); File.WriteAllText(source,"owned executable fixture");
        var untouched=Path.Combine(install,"unrelated.txt"); File.WriteAllText(untouched,"retain unrelated bytes");
        var backup=Path.Combine(install,".dflegacy-backups"); Directory.CreateDirectory(backup); File.WriteAllText(Path.Combine(backup,"keep"),"keep backup");
        foreach(var failure in new[]{"copy","hash"})
        {
            try
            {
                if(failure=="copy")
                {
                    using var denied=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None);
                    UpdateInstaller.CreateHelperCopy(source,UpdateTransportTests.Hash(File.ReadAllBytes(untouched)));
                }
                else UpdateInstaller.CreateHelperCopy(source,new string('0',64));
                check(false,"helper preparation must reject real "+failure+" failure");
            }
            catch(IOException) { }
            check(!Directory.EnumerateDirectories(ownedTemp,"dflegacy-helper-*").Any(),
                "failed helper "+failure+" phase reclaims its exact generated directory within owned scope");
        }
        var bytes=UpdateTransportTests.Package(); using var http=UpdateTransportTests.Client(bytes); var client=new GitHubUpdateClient(http,"firekula/DFLegacy");
        foreach(var failure in new[]{"receipt-lock","marker-conflict","receipt-write"})
        {
            using var lease=UpdateArtifacts.Create(install); var leasePath=lease.Directory;
            var prepared=await client.DownloadAsync(UpdateTransportTests.Release(bytes),lease.Directory); lease.Track(prepared);
            var helper=UpdateInstaller.CreateHelperCopy(source,UpdateSafety.Hash(source)); var helperDir=Path.GetDirectoryName(helper)!;
            var handoff=new UpdateHandoff(new UpdatePlan(install,prepared,0,0,"",helper,""));
            var record=Path.Combine(lease.Directory,"cleanup.json");
            FileStream? denied=null;
            try
            {
                if(failure=="receipt-lock") denied=new FileStream(record,FileMode.Open,FileAccess.Read,FileShare.Read);
                else if(failure=="marker-conflict") Directory.CreateDirectory(Path.Combine(helperDir,"dflegacy-owner"));
                else File.SetAttributes(record,FileAttributes.ReadOnly);
                try { lease.TrackHelper(handoff); check(false,"helper registration rejects "+failure); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
                // Error cleanup while the receipt remains unwritable must still release staging.
                lease.Dispose();
                check(!Directory.Exists(prepared.Directory),"registration "+failure+" failure reclaims stage using live ownership");
                if(failure=="marker-conflict")
                {
                    check(!File.Exists(helper) && Directory.Exists(Path.Combine(helperDir,"dflegacy-owner")),"marker failure removes the exact known executable while preserving unknown marker content");
                    Directory.Delete(Path.Combine(helperDir,"dflegacy-owner"),false);
                    if(File.Exists(helper)) File.Delete(helper); Directory.Delete(helperDir,false);
                }
                else check(!Directory.Exists(helperDir),"registration "+failure+" failure reclaims exact not-started helper");
            }
            finally
            {
                denied?.Dispose(); if(File.Exists(record)) File.SetAttributes(record,FileAttributes.Normal);
                UpdateArtifacts.Sweep(install);
            }
            if(failure!="marker-conflict") check(!Directory.Exists(leasePath),"after receipt becomes writable startup reclaims remaining attempt metadata");
        }
        check(File.ReadAllText(untouched)=="retain unrelated bytes" && File.ReadAllText(Path.Combine(backup,"keep"))=="keep backup","acquisition failure cleanup preserves unrelated paths and backups");
        Console.WriteLine("Update helper acquisition failure checks executed");
    }
}
