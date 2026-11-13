using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DFLegacy.Launcher;

// Windows SDK audiopolicy.h / mmdeviceapi.h. Changes session volume, never
// endpoint/master volume or the game's saved sound/key settings.
internal sealed class ChapterTwoTestAudio : IDisposable
{
    private readonly Process process;
    private readonly long started;
    private sealed record SavedSession(float Volume,object Reference,ISimpleAudioVolume Audio);
    private readonly Dictionary<string,SavedSession> previous=new(StringComparer.Ordinal);
    internal ChapterTwoTestAudio(int processId)
    {
        process=Process.GetProcessById(processId);
        try {started=process.StartTime.ToUniversalTime().Ticks;}
        catch {process.Dispose();throw;}
    }
    internal static ChapterTwoTestAudio? TryCreate(int processId)
    {
        try {return new(processId);}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {return null;}
    }
    internal int SetMuted(bool muted)
    {
        int count=0;
        // A game normally ends before its debug owner. Retain the exact COM
        // session so restoration neither depends on a live PID nor follows reuse.
        if(!muted)
        {
            foreach(var (id,saved) in previous.ToArray())
            {
                var context=Guid.Empty;saved.Audio.SetMasterVolume(saved.Volume,ref context);
                previous.Remove(id);Marshal.ReleaseComObject(saved.Reference);count++;
            }
            return count;
        }
        if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=started)return 0;
        Visit(process.Id,(id,reference,volume)=>
        {
            var context=Guid.Empty;
            if(!previous.ContainsKey(id))
            {
                volume.GetMasterVolume(out var original);volume.SetMasterVolume(0,ref context);
                previous.Add(id,new(original,reference,volume));count++;return true;
            }
            volume.SetMasterVolume(0,ref context);count++;return false;
        });
        return count;
    }
    internal static float[] ReadVolumes(int processId)
    {
        var values=new SortedDictionary<string,float>(StringComparer.Ordinal);
        Visit(processId,(id,reference,volume)=>{volume.GetMasterVolume(out var value);values[id]=value;return false;});
        return values.Values.ToArray();
    }
    public void Dispose()
    {
        try {SetMuted(false);}catch(COMException){ }
        finally {foreach(var saved in previous.Values)Marshal.ReleaseComObject(saved.Reference);previous.Clear();process.Dispose();}
    }
    private static void Visit(int processId,Func<string,object,ISimpleAudioVolume,bool> action)
    {
        if(processId<=0)throw new ArgumentOutOfRangeException(nameof(processId));
        var enumerator=(IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        try
        {
            enumerator.EnumAudioEndpoints(0,1,out var devices);
            try
            {
                devices.GetCount(out var count);
                for(uint i=0;i<count;i++)
                {
                    devices.Item(i,out var device);
                    try
                    {
                        var iid=typeof(IAudioSessionManager2).GUID;device.Activate(ref iid,23,0,out var managerObject);
                        try
                        {
                            ((IAudioSessionManager2)managerObject).GetSessionEnumerator(out var sessions);
                            try
                            {
                                sessions.GetCount(out var size);
                                for(int n=0;n<size;n++)
                                {
                                    sessions.GetSession(n,out var session);
                                    bool retained=false;
                                    try
                                    {
                                        var control=(IAudioSessionControl2)session;
                                        // S_FALSE means the session spans multiple processes. Never
                                        // mute that session merely because its first PID matches.
                                        if(control.GetProcessId(out var owner)!=0||owner!=(uint)processId)continue;
                                        control.GetSessionInstanceIdentifier(out var id);
                                        retained=action(id,session,(ISimpleAudioVolume)session);
                                    }
                                    finally {if(!retained)Marshal.ReleaseComObject(session);}
                                }
                            }
                            finally {Marshal.ReleaseComObject(sessions);}
                        }
                        finally {Marshal.ReleaseComObject(managerObject);}
                    }
                    finally {Marshal.ReleaseComObject(device);}
                }
            }
            finally {Marshal.ReleaseComObject(devices);}
        }
        finally {Marshal.ReleaseComObject(enumerator);}
    }

    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator {void EnumAudioEndpoints(int flow,uint state,out IMMDeviceCollection devices);}
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection {void GetCount(out uint count);void Item(uint index,out IMMDevice device);}
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice {void Activate(ref Guid iid,uint context,nint parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);}
    [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        void GetAudioSessionControl(nint guid,uint flags,out nint control);
        void GetSimpleAudioVolume(nint guid,uint flags,out nint volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator {void GetCount(out int count);void GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)]out object session);}
    [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,nint context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string path,nint context);
        void GetGroupingParam(out Guid value);
        void SetGroupingParam(ref Guid value,nint context);
        void RegisterAudioSessionNotification(nint notification);
        void UnregisterAudioSessionNotification(nint notification);
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetProcessId(out uint processId);
    }
    [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        void SetMasterVolume(float value,ref Guid context);
        void GetMasterVolume(out float value);
        void SetMute([MarshalAs(UnmanagedType.Bool)]bool value,ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)]out bool value);
    }
}
