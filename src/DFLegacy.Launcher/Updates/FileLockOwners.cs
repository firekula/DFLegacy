using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DFLegacy.Launcher.Updates;

internal sealed record FileLockOwner(int ProcessId,long StartFileTime,string Name,bool CanTerminate);
internal static class FileLockOwners
{
    internal static IReadOnlyList<FileLockOwner> Find(IEnumerable<string> paths)
    {
        var files=paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(files.Length==0) return [];
        foreach(var path in files) UpdateSafety.NoLinks(path);
        var key=new StringBuilder(Guid.NewGuid().ToString("N"),33);
        var error=RmStartSession(out var session,0,key); if(error!=0) throw new Win32Exception(error);
        try
        {
            error=RmRegisterResources(session,(uint)files.Length,files,0,IntPtr.Zero,0,IntPtr.Zero);
            if(error!=0) throw new Win32Exception(error);
            uint count=0; error=RmGetList(session,out var needed,ref count,null,out _);
            if(error==0) return [];
            for(var attempt=0;error==234 && attempt<8;attempt++)
            {
                if(needed>4096) throw new IOException("占用进程数量异常，无法安全处理。");
                var info=new ProcessInfo[needed]; count=needed;
                error=RmGetList(session,out needed,ref count,info,out _);
                if(error!=0) continue;
                using var current=Process.GetCurrentProcess();
                return info.Take((int)count).Where(p=>p.Process.ProcessId>4 && p.Process.ProcessId!=Environment.ProcessId)
                    .Select(p=>new FileLockOwner(p.Process.ProcessId,((long)p.Process.StartHigh<<32)|(uint)p.Process.StartLow,p.ApplicationName,
                        p.Type is not (3 or 4 or 1000) && p.Session==(uint)current.SessionId)).ToArray();
            }
            throw new Win32Exception(error);
        }
        finally { RmEndSession(session); }
    }
    internal static async Task CloseAsync(IReadOnlyList<FileLockOwner> owners,bool playerConfirmed,CancellationToken token)
    {
        if(!playerConfirmed) return;
        foreach(var owner in owners)
        {
            token.ThrowIfCancellationRequested();
            if(!owner.CanTerminate || owner.ProcessId==Environment.ProcessId) continue;
            Process process;
            try { process=Process.GetProcessById(owner.ProcessId); } catch(ArgumentException) {continue;}
            using(process)
            {
                try { _=process.SafeHandle; }
                catch(InvalidOperationException) when(process.HasExited) { continue; }
                if(process.HasExited) continue;
                // Recheck creation time, not only PID: never close a reused process identifier.
                if(process.StartTime.ToUniversalTime().ToFileTimeUtc()!=owner.StartFileTime)
                    throw new IOException("占用进程已改变，请重新检查。");
                if(process.CloseMainWindow())
                {
                    using var grace=CancellationTokenSource.CreateLinkedTokenSource(token); grace.CancelAfter(TimeSpan.FromSeconds(3));
                    try { await WaitForExitConfirmedAsync(process,grace.Token); }
                    catch(OperationCanceledException) when(!token.IsCancellationRequested) { }
                }
                token.ThrowIfCancellationRequested();
                if(!process.HasExited) process.Kill(false);
                using var exit=CancellationTokenSource.CreateLinkedTokenSource(token); exit.CancelAfter(TimeSpan.FromSeconds(10));
                await WaitForExitConfirmedAsync(process,exit.Token);
            }
        }
    }
    // Wait on the kernel process object: managed exit callbacks can precede handle/file release.
    internal static async Task WaitForExitConfirmedAsync(Process process,CancellationToken token)
    {
        var handle=process.SafeHandle; var added=false;
        try
        {
            handle.DangerousAddRef(ref added);
            var handles=new[] {handle.DangerousGetHandle(),token.WaitHandle.SafeWaitHandle.DangerousGetHandle()};
            var result=await Task.Run(()=>{var code=WaitForMultipleObjects(2,handles,false,uint.MaxValue); return (code,error:Marshal.GetLastWin32Error());}).ConfigureAwait(false);
            if(result.code==1) throw new OperationCanceledException(token);
            if(result.code!=0) throw new Win32Exception(result.error,"无法等待占用进程退出。");
        }
        finally {if(added) handle.DangerousRelease();}
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint WaitForMultipleObjects(uint count,IntPtr[] handles,[MarshalAs(UnmanagedType.Bool)] bool waitAll,uint milliseconds);
    [StructLayout(LayoutKind.Sequential)] private struct UniqueProcess { public int ProcessId; public int StartLow; public int StartHigh; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string ApplicationName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string ServiceName;
        public int Type; public uint Status; public uint Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmStartSession(out uint handle,uint flags,StringBuilder key);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmRegisterResources(uint handle,uint files,[MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr)] string[] names,uint processes,IntPtr processList,uint services,IntPtr serviceList);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmGetList(uint handle,out uint needed,ref uint count,[In,Out] ProcessInfo[]? info,out uint reasons);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint handle);
}
