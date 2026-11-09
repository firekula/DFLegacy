using System.Diagnostics;
using DFLegacy.Launcher.Updates;

internal static class FileLockOwnerTests
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var file=Path.Combine(root,"lock-fixture.dll"); File.WriteAllText(file,"fixture");
        var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add("--v4-lock-fixture"); start.ArgumentList.Add(file);
        using var child=Process.Start(start)!;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(!File.Exists(file+".ready")) { if(child.HasExited) throw new Exception("lock fixture exited early"); await Task.Delay(20,deadline.Token); }
            var owners=FileLockOwners.Find([file]);
            check(owners.Any(o=>o.ProcessId==child.Id && o.CanTerminate),"Restart Manager identifies actual locking process and PID");
            await FileLockOwners.CloseAsync(owners,false,default);
            check(!child.HasExited,"declining process termination keeps holder alive");
            await FileLockOwners.CloseAsync(owners.Where(o=>o.ProcessId==child.Id).ToArray(),true,default);
            await child.WaitForExitAsync(deadline.Token);
            using var probe=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            check(child.HasExited,"confirmed fixture termination releases file");
        }
        catch(Exception ex) {Console.Error.WriteLine("Lock fixture diagnostic: "+ex); throw;}
        finally
        {
            if(!child.HasExited) child.Kill(true);
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await FileLockOwners.WaitForExitConfirmedAsync(child,cleanup.Token);
        }
        var dir=Path.Combine(root,"pinned-v3"); Directory.CreateDirectory(dir);
        using(UpdateSafety.PinDirectory(dir))
        {
            try { Directory.Move(dir,dir+"-moved"); check(false,"V3 pinned directory must not be renamed"); }
            catch(IOException) { check(true,"V3 pin rejects rename while held"); }
        }
    }
}
