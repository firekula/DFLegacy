using System.Text.Json;

namespace DFLegacy.UpdateCore;

public sealed record InstalledReceipt(VerifiedRelease Release, string[] ManagedHistory);

public sealed class InstallationStore
{
    internal const int MaximumReceiptBytes = 48 * 1024 * 1024;
    public string Root { get; }
    public string StateDirectory => Path.Combine(Root, ".dflegacy", "v4");
    internal string ReceiptPath => Path.Combine(StateDirectory, "installed.json");
    internal string JournalPath => Path.Combine(StateDirectory, "pending.json");
    public InstallationStore(string root) { Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); PathPolicy.NoLinks(Root); }
    public InstalledReceipt? ReadInstalled()
    {
        PathPolicy.NoLinks(ReceiptPath);
        if (!File.Exists(ReceiptPath))
        { if (Directory.Exists(ReceiptPath)) throw new InvalidDataException("安装记录不是文件。"); return null; }
        var receipt = ReadJson<InstalledReceipt>(ReceiptPath, MaximumReceiptBytes);
        Validate(receipt); return receipt;
    }
    internal static void Validate(InstalledReceipt receipt)
    {
        if (receipt.Release?.Source == null || receipt.ManagedHistory is not { Length: <= 100000 }) throw new InvalidDataException("安装记录缺少来源或管理历史。");
        receipt.Release.Validate(receipt.Release.Source);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in receipt.ManagedHistory) { PathPolicy.Validate(path); if (!paths.Add(path)) throw new InvalidDataException("管理历史包含重复路径。"); }
        if (receipt.Release.Manifest.Files.Any(f => !paths.Contains(f.Path))) throw new InvalidDataException("管理历史遗漏已安装文件。");
        if (receipt.Release.Manifest.Retired.Any(r => r.Action == "unmanage" && paths.Contains(r.Path))) throw new InvalidDataException("管理历史包含已解除管理文件。");
    }
    internal static byte[] ReadBounded(string path, int maximum)
    {
        PathPolicy.NoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximum) throw new InvalidDataException("本地记录大小无效：" + Path.GetFileName(path));
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    internal static T ReadJson<T>(string path, int maximum)
    {
        try
        {
            using var doc = JsonDocument.Parse(ReadBounded(path, maximum), new JsonDocumentOptions { MaxDepth = 32 });
            ManifestCodec.RejectDuplicateKeys(doc.RootElement);
            return doc.RootElement.Deserialize<T>(ManifestCodec.Json) ?? throw new InvalidDataException("本地记录为空。");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("本地记录格式无效；恢复资料已保留。", ex); }
    }
}
