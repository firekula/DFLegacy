using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace DFLegacy.Launcher.Updates;

internal static class UpdateSafety
{
    internal const long MaxZip = 256L * 1024 * 1024;
    internal const long MaxExpanded = 512L * 1024 * 1024;
    internal const string InstalledManifestName = "client-package.json";
    internal static IEnumerable<string> TransactionNames(UpdateManifest manifest) => manifest.Files.Select(f=>f.Path).Append(InstalledManifestName);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    internal static string Hash(string file) { using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    internal static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("更新路径不能包含链接或重解析点。");
    }
    // Hold every existing directory against rename/reparse substitution throughout a transaction.
    internal static IDisposable PinDirectory(string path)
    {
        NoLinks(path);
        var handles = new List<SafeFileHandle>();
        try
        {
            for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                var handle = CreateFile(current, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("无法锁定更新目录。", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error())); }
                handles.Add(handle);
            }
            NoLinks(path);
            return new DirectoryPin(handles);
        }
        catch { foreach (var h in handles) h.Dispose(); throw; }
    }
    private sealed class DirectoryPin(List<SafeFileHandle> handles) : IDisposable { public void Dispose() { foreach(var handle in handles) handle.Dispose(); } }
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    internal static void ValidateManifest(UpdateManifest manifest)
    {
        _ = ReadManifest(JsonSerializer.SerializeToUtf8Bytes(manifest,Json));
    }
    internal static UpdateManifest ReadManifest(byte[] bytes)
    {
        var parsed=ClientPackageManifest.Parse(bytes,string.Empty);
        if(parsed.Files.Any(f=>f.Size>MaxExpanded) || parsed.Files.Sum(f=>f.Size)>MaxExpanded) throw new InvalidDataException("更新解压大小超出限制。");
        return new(2,parsed.Version,parsed.Commit,parsed.Files.Select(f=>new UpdateFile(f.Path,f.Sha256,f.Size)).ToArray(),parsed.Flavor);
    }
    internal static ClientPackageManifest ValidateInstalled(string target, UpdateManifest update)
    {
        NoLinks(Path.Combine(target,InstalledManifestName));
        foreach(var name in update.Files.Select(f=>f.Path)) NoLinks(Path.Combine(target,name));
        var installed=ClientPackageManifest.LoadAndValidate(target);
        if(installed.Flavor!=update.Flavor) throw new InvalidDataException("更新包类型与已安装客户端不同，请使用同类型的 V2 更新包。");
        return installed;
    }
    internal static T ReadJson<T>(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        void Duplicates(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object) { var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach(var p in e.EnumerateObject()) { if(!keys.Add(p.Name)) throw new InvalidDataException("重复的 JSON 字段。"); Duplicates(p.Value); } }
            else if(e.ValueKind==JsonValueKind.Array) foreach(var item in e.EnumerateArray()) Duplicates(item);
        }
        Duplicates(doc.RootElement);
        try { return doc.RootElement.Deserialize<T>(Json) ?? throw new InvalidDataException("缺少更新数据。"); }
        catch (JsonException ex) { throw new InvalidDataException("更新数据格式无效。",ex); }
    }
}
