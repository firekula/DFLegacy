# Focus the DNF client window via a REAL mouse click at its center.
#
# Why a click and not SetForegroundWindow: DNF 2008 only resumes its
# keyboard polling (the synthetic-input pump anchor) after it receives a
# real input event -- a programmatic SetForegroundWindow changes the
# foreground state but generates no input, so the game's own activation
# check never re-acquires. A real OS-level click both focuses the window
# and wakes the input path (the game ignores the click itself -- Spike 1).
#
# The window is z-ordered to the top first (without activating), so the
# click lands on it even when two DNF windows overlap on the desktop.
param(
    [Parameter(Mandatory = $true)][int]$Pid2
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class B3Click {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@

$HWND_TOPMOST = [IntPtr](-1)
$HWND_NOTOPMOST = [IntPtr](-2)
$SWP_NOMOVE = 0x0002
$SWP_NOSIZE = 0x0001
$SWP_NOACTIVATE = 0x0010
$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004

$proc = Get-Process -Id $Pid2 -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { throw "process $Pid2 has no main window" }

# Z-order to top WITHOUT activating (the classic two-step).
[B3Click]::SetWindowPos($hwnd, $HWND_TOPMOST, 0, 0, 0, 0, $SWP_NOMOVE -bor $SWP_NOSIZE -bor $SWP_NOACTIVATE) | Out-Null
[B3Click]::SetWindowPos($hwnd, $HWND_NOTOPMOST, 0, 0, 0, 0, $SWP_NOMOVE -bor $SWP_NOSIZE -bor $SWP_NOACTIVATE) | Out-Null
Start-Sleep -Milliseconds 200

# Real click at the window center: focuses the window and delivers a real
# input event, which is what wakes DNF's polling.
$rect = New-Object B3Click+RECT
[B3Click]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$cx = [int](($rect.Left + $rect.Right) / 2)
$cy = [int](($rect.Top + $rect.Bottom) / 2)
[B3Click]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 100
[B3Click]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[B3Click]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400

$now = [B3Click]::GetForegroundWindow()
if ($now -eq $hwnd) {
    Write-Host "click-focused pid=$Pid2 ok"
} else {
    Write-Host "click-focused pid=$Pid2 WARN foreground=$now"
}
