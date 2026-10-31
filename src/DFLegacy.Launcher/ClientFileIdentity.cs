using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DFLegacy.Launcher;

internal static class ClientFileIdentity
{
    [StructLayout(LayoutKind.Sequential, Pack=4)]
    private struct Information { internal uint Attributes; internal long Created,Accessed,Written; internal uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow; }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Information info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint size,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool GetProcessTimes(SafeProcessHandle process,out long created,out long exited,out long kernel,out long user);
    internal static (uint,uint,uint) Of(SafeFileHandle handle)
    {
        if(!GetFileInformationByHandle(handle,out var info)) throw new Win32Exception();
        return (info.Volume,info.IndexHigh,info.IndexLow);
    }
    internal static string Canonical(SafeFileHandle handle)
    {
        var path = new StringBuilder(32768); var length=GetFinalPathNameByHandle(handle,path,(uint)path.Capacity,0);
        if(length==0 || length>=path.Capacity) throw new Win32Exception();
        var value=path.ToString();
        return value.StartsWith(@"\\?\UNC\",StringComparison.Ordinal) ? @"\\"+value[8..] : value.StartsWith(@"\\?\",StringComparison.Ordinal) ? value[4..] : value;
    }
    internal static void RequireSameDirectory(string first,string second)
    {
        using var a=CreateFile(Path.GetFullPath(first),0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        using var b=CreateFile(Path.GetFullPath(second),0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        if(a.IsInvalid || b.IsInvalid || Of(a)!=Of(b)) throw new InvalidDataException("登录器与所选 DNF.exe 不在同一客户端目录。请将完整工具包放入游戏根目录后，从该目录启动登录器。");
    }
}
