using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DFLegacy.UpdateCore;

/// <summary>Windows directory handles deny rename/delete while destination paths are in use.</summary>
internal sealed class DirectoryPin : IDisposable
{
    private readonly Dictionary<string, SafeFileHandle> handles = new(StringComparer.OrdinalIgnoreCase);
    internal void Ensure(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (handles.ContainsKey(full)) return;
        var parent = Path.GetDirectoryName(full);
        if (parent != null && parent != full) Ensure(parent);
        PathPolicy.NoLinks(full);
        Directory.CreateDirectory(full);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("客户端事务要求 Windows 目录固定句柄。");
        // FILE_READ_ATTRIBUTES alone does not participate in Windows share-access checks.
        // GENERIC_READ ensures denying FILE_SHARE_DELETE really prevents directory rename.
        var handle = CreateFileW(full, 0x80000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("无法固定安装目录：" + full, new Win32Exception(error)); }
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0x10)
        { handle.Dispose(); throw new InvalidDataException("安装目录身份无效或为链接：" + full); }
        handles.Add(full, handle);
        PathPolicy.NoLinks(full);
    }
    public void Dispose() { foreach (var handle in handles.Values.Reverse()) handle.Dispose(); handles.Clear(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
}
