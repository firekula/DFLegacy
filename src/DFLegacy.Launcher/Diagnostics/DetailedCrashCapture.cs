using System.Diagnostics;
namespace DFLegacy.Launcher.Diagnostics;

internal static class DetailedCrashCapture
{
    internal static string PrepareDirectory(string root,int processId)
    {
        ClientLogPaths.EnsureDirectory(root);
        var lease=Path.Combine(root,".capture.lock");
        if(ClientLogPaths.HasLink(lease))throw new IOException("详细诊断锁不能是链接。");
        using var held=new FileStream(lease,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        // Retain evidence until the player has shared it. Refuse a third set
        // rather than silently consuming another GiB or removing active dumps.
        if(Directory.EnumerateDirectories(root).Take(2).Count()>=2)
            throw new IOException("详细崩溃采集已保留两次记录；请先备份并清理 Incidents/Detailed 中的旧目录后再开启采集。");
        var directory=Path.Combine(root,$"{DateTime.Now:yyyyMMdd-HHmmss}-{processId}-{Guid.NewGuid():N}");
        ClientLogPaths.EnsureDirectory(directory);return directory;
    }
    internal static ProcessStartInfo? CreateStartInfo(string executable,int processId,string directory,bool optedIn)
    {
        if(!optedIn) return null;
        var start=new ProcessStartInfo(executable) { UseShellExecute=false, CreateNoWindow=true };
        start.ArgumentList.Add("monitor"); start.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture)); start.ArgumentList.Add(directory);
        return start;
    }
    internal static Process? TryStart(int processId)
    {
        if(!Program.DetailedCaptureEnabled) return null;
        try
        {
            var executable=Path.Combine(AppContext.BaseDirectory,"Diagnostics","DFLegacy.CrashCapture.exe");
            if(!File.Exists(executable)) throw new FileNotFoundException("详细崩溃采集需要 Diagnostics/DFLegacy.CrashCapture.exe。",executable);
            var directory=PrepareDirectory(Path.Combine(ClientLogPaths.GetDirectory(Program.LogClientPath),"Incidents","Detailed"),processId);
            Program.WriteLauncherLog($"Detailed crash capture opted in; reports={directory}; create STOP in this directory to detach.");
            return Process.Start(CreateStartInfo(executable,processId,directory,true)!);
        }
        catch(Exception ex) { Program.ReportError("detailed-capture",ex); return null; }
    }
}
