using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DFLegacy.Launcher.Controls;

internal sealed record PixelRaster(BitmapSource Image, int Advance, int GlyphWidth, int GlyphHeight);

internal static class PixelRasterizer
{
    private const int MaxWidth = 4096;
    private const int MaxBytes = 4 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<(string, int, Color, int), PixelRaster> Cache = [];
    private static int _bytes;
    internal static int CachedLineCount { get { lock (Gate) return Cache.Count; } }
    internal static int CachedBytes { get { lock (Gate) return _bytes; } }

    private static string Bounded(string text)
    {
        if (text.Length <= 4096) return text;
        var end = char.IsHighSurrogate(text[4095]) ? 4095 : 4096;
        return text[..end] + "…";
    }

    internal static string[] Lines(string text, int size, int width, bool wrap)
    {
        text = Bounded(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        if (text.Length == 0) return [];
        if (!wrap) return text.Split('\n');
        using var font = new NativeFont(size);
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(""); continue; }
            var line = new StringBuilder();
            foreach (var rune in paragraph.EnumerateRunes())
            {
                var next = rune.ToString();
                if (line.Length > 0 && font.Measure(line.ToString() + next).Width > Math.Max(1, width))
                { result.Add(line.ToString()); line.Clear(); }
                line.Append(next);
            }
            if (line.Length > 0) result.Add(line.ToString());
        }
        return result.ToArray();
    }

    internal static PixelRaster Render(string text, int size, Color color, int outline)
    {
        text = Bounded(text); size = Math.Clamp(size, 6, 96); outline = Math.Clamp(outline, 0, 2);
        var key = (text, size, color, outline);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var raster = RenderCore(text, size, color, outline);
            var bytes = raster.Image.PixelWidth * raster.Image.PixelHeight * 4;
            if (Cache.Count >= 256 || _bytes + bytes > MaxBytes) { Cache.Clear(); _bytes = 0; }
            if (bytes <= MaxBytes) { Cache[key] = raster; _bytes += bytes; }
            return raster;
        }
    }

    private static PixelRaster RenderCore(string text, int size, Color color, int outline)
    {
        using var font = new NativeFont(size);
        var extent = font.Measure(text);
        var width = Math.Min(MaxWidth, extent.Width) + 4;
        var height = Math.Max(size * 2 + 8, extent.Height + 4);
        var info = new BitmapInfo { Header = new BitmapHeader { Size = 40, Width = width, Height = -height,
            Planes = 1, BitCount = 32 } };
        var bitmap = CreateDIBSection(font.Dc, ref info, 0, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr previous = IntPtr.Zero;
        try
        {
            previous = SelectObject(font.Dc, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var raw = new byte[width * height * 4]; Marshal.Copy(raw, 0, bits, raw.Length);
            SetTextColor(font.Dc, 0x00ffffff); SetBkColor(font.Dc, 0); SetBkMode(font.Dc, 1);
            if (text.Length > 0 && !TextOutW(font.Dc, 2, 2, text, text.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
            GdiFlush(); Marshal.Copy(bits, raw, 0, raw.Length);
            var left = width; var top = height; var right = -1; var bottom = -1;
            bool Ink(int x, int y) => raw[(y * width + x) * 4] != 0 || raw[(y * width + x) * 4 + 1] != 0 || raw[(y * width + x) * 4 + 2] != 0;
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (Ink(x, y))
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            var glyphWidth = Math.Max(0, right - left + 1); var glyphHeight = Math.Max(0, bottom - top + 1);
            var outWidth = Math.Max(1, glyphWidth + 2 * outline); var outHeight = Math.Max(1, glyphHeight + 2 * outline);
            var pixels = new byte[outWidth * outHeight * 4];
            for (var y = 0; y < glyphHeight; y++) for (var x = 0; x < glyphWidth; x++) if (Ink(left + x, top + y))
                for (var dy = -outline; dy <= outline; dy++) for (var dx = -outline; dx <= outline; dx++)
                    pixels[((y + outline + dy) * outWidth + x + outline + dx) * 4 + 3] = color.A;
            for (var y = 0; y < glyphHeight; y++) for (var x = 0; x < glyphWidth; x++) if (Ink(left + x, top + y))
            {
                var offset = ((y + outline) * outWidth + x + outline) * 4;
                pixels[offset] = (byte)(color.B * color.A / 255); pixels[offset + 1] = (byte)(color.G * color.A / 255);
                pixels[offset + 2] = (byte)(color.R * color.A / 255); pixels[offset + 3] = color.A;
            }
            var image = BitmapSource.Create(outWidth, outHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, outWidth * 4);
            image.Freeze(); return new(image, Math.Min(MaxWidth, extent.Width), glyphWidth, glyphHeight);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(font.Dc, previous);
            DeleteObject(bitmap);
        }
    }

    private sealed class NativeFont : IDisposable
    {
        internal IntPtr Dc { get; private set; }
        private IntPtr _font, _previous;
        internal NativeFont(int size)
        {
            Dc = CreateCompatibleDC(IntPtr.Zero);
            if (Dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                _font = CreateFontW(-Math.Clamp(size, 6, 96), 0, 0, 0, 400, 0, 0, 0, 134, 0, 0, 3, 0, "宋体");
                if (_font == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                _previous = SelectObject(Dc, _font);
                if (_previous == IntPtr.Zero || _previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch { Dispose(); throw; }
        }
        internal (int Width, int Height) Measure(string text)
        {
            if (text.Length == 0) return (0, 0);
            if (!GetTextExtentPoint32W(Dc, text, text.Length, out var size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return (size.X, size.Y);
        }
        public void Dispose()
        {
            if (Dc == IntPtr.Zero) return;
            if (_previous != IntPtr.Zero && _previous != new IntPtr(-1)) SelectObject(Dc, _previous);
            if (_font != IntPtr.Zero) DeleteObject(_font);
            DeleteDC(Dc); Dc = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Colors; }
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int count, out NativeSize size);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool TextOutW(IntPtr dc, int x, int y, string text, int count);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
