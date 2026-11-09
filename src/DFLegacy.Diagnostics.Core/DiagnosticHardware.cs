using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DFLegacy.Diagnostics;

internal static class DiagnosticHardware
{
    // A single registry value and fixed-size native calls; no WMI, child commands, network, or device inventory.
    internal static void ReadInto(Dictionary<string,string> values)
    {
        values["cpuModel"]=values["gpuModel"]=values["totalMemoryBytes"]=values["availableMemoryBytes"]="unknown";
        if(!OperatingSystem.IsWindows())return;
        try { values["cpuModel"]=Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0","ProcessorNameString",null) as string??"unknown"; }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }
        var memory=new MemoryStatus {Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(GlobalMemoryStatusEx(ref memory)){values["totalMemoryBytes"]=memory.TotalPhysical.ToString();values["availableMemoryBytes"]=memory.AvailablePhysical.ToString();}
        var display=new DisplayDevice {Size=Marshal.SizeOf<DisplayDevice>()};
        if(EnumDisplayDevices(null,0,ref display,0))values["gpuModel"]=display.Description??"unknown";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus {public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct DisplayDevice {
        public int Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string? Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string? Description;
        public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string? Id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string? Key;
    }
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("user32.dll",EntryPoint="EnumDisplayDevicesW",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device,uint number,ref DisplayDevice display,uint flags);
}
