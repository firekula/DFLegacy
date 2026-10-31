using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DFLegacy.Launcher;

internal static class WindowWorkArea
{
    // Use the actual owner/current monitor, including negative virtual-desktop coordinates.
    // https://learn.microsoft.com/windows/win32/gdi/positioning-objects-on-multiple-display-monitors
    internal static Rect? Read(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return null;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info)
            ? new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top) : null;
    }

    internal static void Clamp(Window window, Rect physicalWorkArea)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return;
        var x = Math.Clamp(rect.Left, (int)physicalWorkArea.Left, Math.Max((int)physicalWorkArea.Left, (int)physicalWorkArea.Right - (rect.Right - rect.Left)));
        var y = Math.Clamp(rect.Top, (int)physicalWorkArea.Top, Math.Max((int)physicalWorkArea.Top, (int)physicalWorkArea.Bottom - (rect.Bottom - rect.Top)));
        if (x != rect.Left || y != rect.Top) SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x15); // no size/z-order/activation change
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
