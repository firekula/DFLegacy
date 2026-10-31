using System.Runtime.InteropServices;
using System.Text;

namespace DFLegacy.Launcher;

internal sealed record ClientDisplaySnapshot(string ClientPath, string ConfigPath, byte[]? OriginalBytes,
    int Width, int Height, string PresentationFilter);

internal sealed record DisplayResolution(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}" + (Width == 640 && Height == 480 ? "（原尺寸）" : Width == 1280 && Height == 960 ? "（整数 2 倍）" : "");
}

internal static class ClientDisplayConfiguration
{
    internal static IReadOnlyList<DisplayResolution> Presets { get; } =
        [new(640, 480), new(800, 600), new(960, 720), new(1024, 768), new(1280, 960), new(1600, 1200)];

    internal static ClientDisplaySnapshot Load(string clientPath)
    {
        var client = Program.ResolveClientPath(clientPath);
        if (!File.Exists(client)) throw new FileNotFoundException("客户端路径不存在，请先在连接页保存正确的 DNF.exe 路径。", client);
        var path = Path.Combine(Path.GetDirectoryName(client)!, "Config.ini");
        if (!File.Exists(path)) return new(client, path, null, 800, 600, "sharp");
        var document = IniConfiguration.Load(path);
        if (!document.NativeEncodingSupported || !document.HasSection)
            throw new InvalidDataException("现有 Config.ini 格式无效或编码不受游戏支持，未替换原文件。");
        var width = Math.Clamp(unchecked((int)GetPrivateProfileInt("Resolution", "Width", 800, path)), 640, 8192);
        var height = Math.Clamp(unchecked((int)GetPrivateProfileInt("Resolution", "Height", 600, path)), 480, 8192);
        var value = new StringBuilder(64);
        GetPrivateProfileString("Rendering", "PresentationFilter", "legacy", value, value.Capacity, path);
        var mode = value.ToString().Trim(' ', '\t').ToLowerInvariant();
        if (!IsMode(mode)) mode = "legacy";
        return new(client, path, document.OriginalBytes, width, height, mode);
    }

    internal static ClientDisplaySnapshot Save(ClientDisplaySnapshot snapshot, int width, int height, string mode)
    {
        if (!IsMode(mode)) throw new InvalidDataException("请选择有效的显示画质。");
        if (width < 640 || height < 480 || width > 8192 || height > 8192
            || (mode != "legacy" && !Presets.Any(p => p.Width == width && p.Height == height)))
            throw new InvalidDataException("增强画质需要选择支持的 4:3 分辨率。");
        if (!File.Exists(snapshot.ClientPath)) throw new FileNotFoundException("客户端已移动，请重新读取显示设置。", snapshot.ClientPath);
        var bytes = snapshot.OriginalBytes;
        if (bytes is null)
        {
            using var defaults = typeof(ClientDisplayConfiguration).Assembly.GetManifestResourceStream("DFLegacy.Launcher.DefaultConfig.ini")
                ?? throw new InvalidDataException("登录器缺少默认显示配置。");
            using var stream = new MemoryStream(); defaults.CopyTo(stream); bytes = stream.ToArray();
        }
        var document = IniConfiguration.Parse(bytes);
        if (!document.NativeEncodingSupported) throw new InvalidDataException("游戏不支持该 INI 编码。");
        var output = document.With(new("Resolution", "Width", width.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Resolution", "Height", height.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Rendering", "PresentationFilter", mode));
        IniConfiguration.SaveExpected(snapshot.ConfigPath, snapshot.OriginalBytes, output);
        return snapshot with { OriginalBytes = output, Width = width, Height = height, PresentationFilter = mode };
    }

    internal static void RequireCurrentTarget(string persistedPath, string cachedPath, string editedPath, ClientDisplaySnapshot snapshot)
    {
        if (!SameClientPath(persistedPath, cachedPath) || !SameClientPath(persistedPath, editedPath)
            || !SameClientPath(persistedPath, snapshot.ClientPath))
            throw new InvalidDataException("已保存的客户端路径或显示目标已经变化，请重新打开设置并确认连接路径。");
    }

    internal static ClientDisplaySnapshot SaveWithAuthority(LauncherAccountStore store, ClientDisplaySnapshot snapshot,
        string cachedPath, string editedPath, int width, int height, string mode)
    {
        using var lease = store.AcquireReadLease();
        RequireCurrentTarget(lease.Options.ClientPath, cachedPath, editedPath, snapshot);
        return Save(snapshot, width, height, mode);
    }

    internal static bool SameClientPath(string savedPath, string editedPath)
    {
        try { return Program.ResolveClientPath(savedPath).Equals(Program.ResolveClientPath(editedPath), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    private static bool IsMode(string mode) => mode is "sharp" or "point" or "linear" or "legacy";

    [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileIntW", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileInt(string section, string key, int fallback, string path);
    [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, int capacity, string path);
}
