param(
    [string]$Path = 'G:\Game\DNF_ACT1\DFLegacy\src\DFLegacy.Launcher\Assets\Skin\backgd_di1.png'
)

Add-Type -AssemblyName System.Drawing.Common
# .NET 10 上 System.Drawing 的实现分散在多个程序集（含内部 Windows 程序集），
# Add-Type 只引 System.Drawing 会报 CS1069 / CS0012，这里把它们逐个加载后引用其路径。
# 注意：-ReferencedAssemblies 会替换默认引用集，所以 C# 里不能再用 System.Collections。
$refs = @()
foreach ($name in @(
        'System.Drawing.Common',
        'System.Drawing.Primitives',
        'System.Private.Windows.GdiPlus',
        'System.Private.Windows.Core',
        'System.Runtime.InteropServices')) {
    try {
        $assembly = [System.Reflection.Assembly]::Load($name)
        if ($assembly.Location -and ($refs -notcontains $assembly.Location)) { $refs += $assembly.Location }
    } catch {
        Write-Warning ("could not load reference {0}: {1}" -f $name, $_.Exception.Message)
    }
}

Add-Type -ReferencedAssemblies $refs -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class MagentaKeyer
{
    // 原版色键：0xFF00FF。背景图里该颜色是不透明的，需要抠成 alpha=0。
    // 用"从图像边框连通填充"而不是全局颜色替换：画面本身有紫罗兰色区块，
    // 全局替换会误伤；连通填充只吃从边框连成一片的品红及其混色边缘。
    public static int[] Key(string path)
    {
        int w, h, stride;
        byte[] buf;

        // 先把像素读进内存并释放文件句柄，否则后面 Save 会因源文件被锁而报 GDI+ 通用错误。
        using (var src = new Bitmap(path))
        {
            w = src.Width; h = src.Height;
            using var copy = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy)) { g.DrawImage(src, new Rectangle(0, 0, w, h)); }
            var rect = new Rectangle(0, 0, w, h);
            var data = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            stride = data.Stride;
            buf = new byte[stride * h];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            copy.UnlockBits(data);
        }

        int R(int i) => buf[i + 2];
        int G(int i) => buf[i + 1];
        int B(int i) => buf[i];
        int Idx(int x, int y) => y * stride + x * 4;
        bool Flood(int x, int y) { int i = Idx(x, y); return R(i) >= 170 && B(i) >= 170 && G(i) <= 100; }
        bool Tight(int x, int y) { int i = Idx(x, y); return R(i) >= 200 && B(i) >= 200 && G(i) <= 60; }
        bool Leaning(int x, int y) { int i = Idx(x, y); return R(i) > G(i) + 40 && B(i) > G(i) + 40; }

        var keyed = new bool[w * h];
        // 手写数组队列（默认引用集里没有 System.Collections，不能用 Queue<T>）。
        var queue = new int[w * h];
        int head = 0, tail = 0;
        void Seed(int x, int y)
        {
            int p = y * w + x;
            if (!keyed[p] && Flood(x, y)) { keyed[p] = true; queue[tail++] = p; }
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (head < tail)
        {
            int p = queue[head++]; int x = p % w, y = p / w;
            void Try(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                int np = ny * w + nx;
                if (keyed[np] || !Flood(nx, ny)) return;
                keyed[np] = true; queue[tail++] = np;
            }
            Try(x - 1, y); Try(x + 1, y); Try(x, y - 1); Try(x, y + 1);
        }
        // 画面内部孤立、不与边框连通的品红，按严格色键再扫一遍
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int p = y * w + x; if (!keyed[p] && Tight(x, y)) keyed[p] = true; }
        // 边缘修剪：紧贴已抠区域、且偏品红的混色像素也抠掉（两轮，约 2px），避免紫边
        var additions = new int[w * h];
        for (int round = 0; round < 2; round++)
        {
            int addCount = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int p = y * w + x; if (keyed[p]) continue;
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy; if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (keyed[ny * w + nx]) { near = true; break; }
                }
                if (near && Leaning(x, y)) additions[addCount++] = p;
            }
            for (int i = 0; i < addCount; i++) keyed[additions[i]] = true;
            if (addCount == 0) break;
        }

        int removed = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int p = y * w + x;
            if (keyed[p]) { buf[Idx(x, y) + 3] = 0; removed++; }
        }

        int leftMagenta = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int p = y * w + x;
            if (!keyed[p] && Tight(x, y)) leftMagenta++;
        }

        var temp = path + ".keyed.tmp";
        using (var outBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            var rect = new Rectangle(0, 0, w, h);
            var od = outBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(buf, 0, od.Scan0, buf.Length);
            outBmp.UnlockBits(od);
            outBmp.Save(temp, ImageFormat.Png);
        }
        if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }
        System.IO.File.Move(temp, path);

        return new[] { removed, leftMagenta, w, h };
    }
}
"@

$result = [MagentaKeyer]::Key($Path)
Write-Host ("size={0}x{1} keyed={2} remainingOpaqueMagenta={3}" -f $result[2], $result[3], $result[0], $result[1])
Write-Host ("wrote {0}" -f $Path)
