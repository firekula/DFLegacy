using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DFLegacy.Launcher;

internal sealed class CursorVisibilityLease
{
    private int raises;
    internal void Update(bool hovered,bool visible,Func<bool,int> show)
    {
        if(hovered)
        {
            if(!visible)while(raises<32){raises++;if(show(true)>=0)break;}
        }
        else while(raises>0){show(false);raises--;}
    }
}

// The game's software cursor is below owned tool windows. Borrow OS visibility
// over this window only, and balance the exact increments on leave/hide/close.
internal sealed class OwnedPanelCursor : IDisposable
{
    private readonly Window window;
    private readonly HwndSource source;
    private readonly DispatcherTimer timer;
    private readonly CursorVisibilityLease lease=new();
    internal OwnedPanelCursor(Window window)
    {
        this.window=window;
        source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source.AddHook(Hook);
        window.IsVisibleChanged+=VisibilityChanged;
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
        timer.Tick+=(_,_)=>Update();timer.Start();
    }
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)=>Update();
    private void Update()
    {
        var handle=source.Handle;
        Point point=default;
        bool inside=window.IsVisible&&!IsIconic(handle)&&GetCursorPos(out point)&&
            GetWindowRect(handle,out var rect)&&point.X>=rect.Left&&point.X<rect.Right&&point.Y>=rect.Top&&point.Y<rect.Bottom;
        var target=inside?WindowFromPoint(point):0;
        bool hovered=window.IsVisible&&(GetCapture()==handle||(inside&&(target==handle||GetAncestor(target,2)==handle)));
        var info=new CursorInfo{Size=Marshal.SizeOf<CursorInfo>()};
        bool visible=!GetCursorInfo(ref info)||(info.Flags&1)!=0;
        lease.Update(hovered,visible,ShowCursor);
    }
    private nint Hook(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message==0x20) // WM_SETCURSOR
        {
            Update();
            int hit=(int)((long)lParam&0xffff);
            int shape=hit is 10 or 11?32644:hit is 12 or 15?32645:
                hit is 13 or 17?32642:hit is 14 or 16?32643:32512;
            SetCursor(LoadCursor(0,shape));
            if(hit==1)Mouse.UpdateCursor();
            handled=true;return 1;
        }
        return 0;
    }
    public void Dispose()
    {
        timer.Stop();window.IsVisibleChanged-=VisibilityChanged;source.RemoveHook(Hook);
        lease.Update(false,true,ShowCursor);
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct CursorInfo{public int Size;public uint Flags;public nint Cursor;public Point Point;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out Rect rect);
    [DllImport("user32.dll")]private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint window,uint flag);
    [DllImport("user32.dll")]private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")]private static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)]bool show);
    [DllImport("user32.dll")]private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]private static extern nint GetCapture();
    [DllImport("user32.dll",EntryPoint="LoadCursorW")]private static extern nint LoadCursor(nint module,nint name);
    [DllImport("user32.dll")]private static extern nint SetCursor(nint cursor);
}
