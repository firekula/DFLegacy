using DFLegacy.Launcher.Updates;
internal static class UpdateAcquisitionTests
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var install=Path.Combine(root,"acquisition"); Directory.CreateDirectory(install);
        var source=Path.Combine(install,"source.exe"); File.WriteAllText(source,"owned executable fixture");
        var untouched=Path.Combine(install,"unrelated.txt"); File.WriteAllText(untouched,"retain unrelated bytes");
        var backup=Path.Combine(install,".dflegacy-backups"); Directory.CreateDirectory(backup); File.WriteAllText(Path.Combine(backup,"keep"),"keep backup");
        string[] Helpers()=>Directory.GetDirectories(Path.GetTempPath(),"dflegacy-helper-*");
        foreach(var failure in new[]{"copy","hash"})
        {
            var before=Helpers().ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            var leaked=Helpers().Where(p=>!before.Contains(p)).ToArray();
            check(leaked.Length==0,"failed helper "+failure+" phase reclaims its exact generated directory");
            // RED fixtures only: remove exact newly observed owned directories with known files.
            foreach(var path in leaked)
            {
                var exe=Path.Combine(path,"DFLegacy.Launcher.exe"); if(File.Exists(exe)) File.Delete(exe);
                Directory.Delete(path,false);
            }
        }
        var bytes=UpdateTransportTests.Package(); using var http=UpdateTransportTests.Client(bytes); var client=new GitHubUpdateClient(http,"firekula/DFLegacy");
        foreach(var failure in new[]{"receipt-lock","marker-conflict","receipt-write"})
        {
            var lease=UpdateArtifacts.Create(install); var leasePath=lease.Directory;
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
