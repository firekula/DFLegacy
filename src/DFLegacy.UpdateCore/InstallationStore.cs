using System.Text.Json;
using System.Security.Cryptography;

namespace DFLegacy.UpdateCore;

public sealed record InstalledReceipt(VerifiedRelease Release, string[] ManagedHistory);

public sealed class InstallationStore
{
    internal const int MaximumReceiptBytes = 48 * 1024 * 1024;
    public string Root { get; }
    public string StateDirectory => Path.Combine(Root, ".dflegacy", "v4");
    internal string ReceiptPath => Path.Combine(StateDirectory, "installed.json");
    internal string JournalPath => Path.Combine(StateDirectory, "pending.json");
    internal string ValidationPath => Path.Combine(StateDirectory, "validated.json");
    public InstallationStore(string root) { Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); PathPolicy.NoLinks(Root); }
    public InstalledReceipt? ReadInstalled()
    {
        PathPolicy.NoLinks(ReceiptPath);
        if (!File.Exists(ReceiptPath))
        { if (Directory.Exists(ReceiptPath)) throw new InvalidDataException("安装记录不是文件。"); return null; }
        var receipt = ReadJson<InstalledReceipt>(ReceiptPath, MaximumReceiptBytes);
        Validate(receipt); return receipt;
    }
    private sealed record CriticalFile(string Path, long Size, long Written, long Created);
    private sealed record ValidationReceipt(int Schema, string Root, string ReceiptSha256, CriticalFile[] Files);
    private string ReceiptHash() => Convert.ToHexStringLower(SHA256.HashData(ReadBounded(ReceiptPath, MaximumReceiptBytes)));
    private CriticalFile[] CriticalFiles(InstalledReceipt receipt) => receipt.Release.Manifest.Files
        .Where(f => f.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.Path == "DFLegacy.Bootstrap.ini")
        .Select(f =>
        {
            var path = PathPolicy.Resolve(Root, f.Path); var info = new FileInfo(path);
            if (!info.Exists || info.Length != f.Size) throw new InvalidDataException("关键启动文件缺失或大小改变：" + f.Path);
            return new CriticalFile(f.Path, info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
        }).ToArray();
    /// <summary>Trust completed installation evidence; resource contents are verified on install or explicit repair.</summary>
    public bool HasValidatedReceipt(InstalledReceipt receipt)
    {
        if (InstallTransaction.HasPending(Root)) return false;
        PathPolicy.NoLinks(ValidationPath);
        if (!File.Exists(ValidationPath)) return false;
        try
        {
            var validated = ReadJson<ValidationReceipt>(ValidationPath, MaximumReceiptBytes);
            return validated.Schema == 1 && string.Equals(validated.Root, Root, StringComparison.OrdinalIgnoreCase)
                && validated.ReceiptSha256 == ReceiptHash() && validated.Files != null && validated.Files.SequenceEqual(CriticalFiles(receipt));
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
    internal void RecordValidation(InstalledReceipt receipt)
    {
        // Caller holds the installation lease and has completed full verification.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ValidationReceipt(1, Root, ReceiptHash(), CriticalFiles(receipt)), ManifestCodec.Json);
        PathPolicy.NoLinks(ValidationPath); Directory.CreateDirectory(StateDirectory);
        var temporary = ValidationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            PathPolicy.NoLinks(ValidationPath); File.Move(temporary, ValidationPath, true);
        }
        finally { PathPolicy.NoLinks(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal void InvalidateValidation() { PathPolicy.NoLinks(ValidationPath); if (File.Exists(ValidationPath)) File.Delete(ValidationPath); }
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
