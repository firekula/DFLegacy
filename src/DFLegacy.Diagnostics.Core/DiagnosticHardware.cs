using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DFLegacy.Diagnostics;

internal static class DiagnosticHardware
{
    // Fixed-count registry values and fixed-size native calls; no WMI, commands, network or inventory.
    internal static void ReadInto(Dictionary<string,string> values)
    {
        values["cpuModel"]=values["gpuModel"]=values["gpuDriverVersion"]=values["gpuMemoryBytes"]=values["totalMemoryBytes"]=values["availableMemoryBytes"]="unknown";
        if(!OperatingSystem.IsWindows())return;
        try { values["cpuModel"]=Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0","ProcessorNameString",null) as string??"unknown"; }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){ }
        var memory=new MemoryStatus {Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(GlobalMemoryStatusEx(ref memory)){values["totalMemoryBytes"]=memory.TotalPhysical.ToString();values["availableMemoryBytes"]=memory.AvailablePhysical.ToString();}
        var display=new DisplayDevice {Size=Marshal.SizeOf<DisplayDevice>()};
        if(EnumDisplayDevices(null,0,ref display,0))
        {
            values["gpuModel"]=display.Description??"unknown";
            // Read only the first enumerated display's OS-provided adapter key.
            // This is adapter capacity, never evidence of the game's active renderer.
            const string prefix=@"\Registry\Machine\";
            if(display.Key is {Length:<=256} key&&key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)
                &&key[prefix.Length..].StartsWith(@"SYSTEM\CurrentControlSet\Control\Video\",StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var path="HKEY_LOCAL_MACHINE\\"+key[prefix.Length..];
                    values["gpuDriverVersion"]=Registry.GetValue(path,"DriverVersion",null) as string??"unknown";
                    var bytes=Registry.GetValue(path,"HardwareInformation.qwMemorySize",null)
                        ??Registry.GetValue(path,"HardwareInformation.MemorySize",null);
                    var capacity=bytes switch {long n when n>0=>(ulong)n,int n=>(ulong)(uint)n,byte[] {Length:8} b=>BitConverter.ToUInt64(b),byte[] {Length:4} b=>BitConverter.ToUInt32(b),_=>0UL};
                    if(capacity>0&&capacity<=1024UL*1024*1024*1024)values["gpuMemoryBytes"]=capacity.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException){ }
            }
        }
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
