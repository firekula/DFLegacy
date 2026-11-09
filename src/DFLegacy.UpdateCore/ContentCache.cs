using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace DFLegacy.UpdateCore;

public sealed record CachePreparation(IReadOnlySet<string> ProtectedObjects, long RequiredFreeBytes, long AvailableFreeBytes, long RemovedBytes);

/// <summary>Only hash/length-addressed files under this directory are owned by the cache.</summary>
public sealed class ContentCache
{
    public const long DefaultBudgetBytes = 2L * 1024 * 1024 * 1024;
    public string DirectoryPath { get; }
    public ContentCache(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory); PathPolicy.NoLinks(DirectoryPath); Directory.CreateDirectory(DirectoryPath);
    }
    public string ObjectPath(string hash, long size)
    {
        if (!ManifestCodec.IsHash(hash) || size < 0) throw new InvalidDataException("缓存身份无效。");
        var path = Path.Combine(DirectoryPath, hash + "-" + size + ".blob"); PathPolicy.NoLinks(path); return path;
    }
    public string PartialPath(ResourcePart part) => ObjectPath(part.Sha256, part.Size) + ".partial";
    /// <summary>Call under the installation lease, before downloading and only with no pending transaction.</summary>
    public async Task<CachePreparation> PrepareAsync(ClientManifest target, IReadOnlyCollection<ManagedFile> changedFiles,
        long budgetBytes = DefaultBudgetBytes, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ManifestCodec.Validate(target);
        if (budgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        CheckNoPending();
        using var pins = OperatingSystem.IsWindows() ? new DirectoryPin() : null; pins?.Ensure(DirectoryPath);
        var selected = target.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in changedFiles)
            if (file == null || !selected.TryGetValue(file.Path, out var declared) || file.Size != declared.Size || file.Sha256 != declared.Sha256 || !changedPaths.Add(file.Path))
                throw new InvalidDataException("下载空间预检包含不属于所选目标的文件。");
        var protectedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in target.Files)
        {
            protectedObjects.Add(ObjectPath(file.Sha256, file.Size));
            foreach (var part in file.Parts) protectedObjects.Add(ObjectPath(part.Sha256, part.Size));
        }
        var completed = new List<(string Path, string Hash, long Size, DateTime Written)>();
        long total = 0, protectedBytes = 0, removed = 0;
        foreach (var path in Directory.EnumerateFiles(DirectoryPath))
        {
            token.ThrowIfCancellationRequested();
            var match = Regex.Match(Path.GetFileName(path), @"\A([a-f0-9]{64})-(0|[1-9][0-9]*)\.blob\z", RegexOptions.CultureInvariant);
            if (!match.Success || !long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            try
            {
                if (!await FileVerifier.MatchesAsync(path, size, match.Groups[1].Value, token).ConfigureAwait(false)) continue;
                total = checked(total + size);
                if (protectedObjects.Contains(path)) protectedBytes = checked(protectedBytes + size);
                completed.Add((path, match.Groups[1].Value, size, File.GetLastWriteTimeUtc(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { /* Busy or foreign objects remain untouched. */ }
        }
        var allowance = Math.Max(budgetBytes, protectedBytes);
        foreach (var item in completed.Where(f => !protectedObjects.Contains(f.Path)).OrderBy(f => f.Written).ThenBy(f => f.Path, StringComparer.Ordinal))
        {
            if (total <= allowance) break;
            token.ThrowIfCancellationRequested(); CheckNoPending();
            if (await TryEvictAsync(item.Path, item.Size, item.Hash, token).ConfigureAwait(false))
            { total -= item.Size; removed = checked(removed + item.Size); }
        }
        // Conservatively reserve full missing parts, retaining resume data; invalid/stale partials may restart.
        // Split transport additionally needs its complete logical assembly at the same time as its parts.
        var requiredObjects = new Dictionary<string, (long Size, string Hash)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in changedPaths)
        {
            var file = selected[path]; var logical = ObjectPath(file.Sha256, file.Size);
            if (await FileVerifier.MatchesAsync(logical, file.Size, file.Sha256, token).ConfigureAwait(false)) continue;
            foreach (var part in file.Parts) requiredObjects[ObjectPath(part.Sha256, part.Size)] = (part.Size, part.Sha256);
            requiredObjects[logical] = (file.Size, file.Sha256);
        }
        long required = 1024 * 1024;
        foreach (var (path, identity) in requiredObjects)
        {
            token.ThrowIfCancellationRequested();
            if (!await FileVerifier.MatchesAsync(path, identity.Size, identity.Hash, token).ConfigureAwait(false))
                required = checked(required + identity.Size + 4096);
        }
        CheckNoPending(); var available = EnsureAvailableBytes(required);
        return new(protectedObjects, required, available, removed);
    }
    internal long EnsureAvailableBytes(long required)
    {
        if (required < 0) throw new ArgumentOutOfRangeException(nameof(required));
        var available = new DriveInfo(Path.GetPathRoot(DirectoryPath)!).AvailableFreeSpace;
        if (available < required) throw new IOException($"更新缓存磁盘空间不足：至少需要 {required} 字节，可用 {available} 字节。");
        return available;
    }
    private void CheckNoPending()
    {
        var cache = new DirectoryInfo(DirectoryPath);
        var state = cache.Parent;
        if (state == null || !cache.Name.Equals("cache", StringComparison.OrdinalIgnoreCase) || !state.Name.Equals("v4", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.Parent?.Name, ".dflegacy", StringComparison.OrdinalIgnoreCase)) return;
        var pending = Path.Combine(state.FullName, "pending.json"); PathPolicy.NoLinks(pending);
        if (File.Exists(pending) || Directory.Exists(pending)) throw new InvalidDataException("存在未决安装事务，不能清理更新缓存。");
    }
    private async Task<bool> TryEvictAsync(string path, long size, string hash, CancellationToken token)
    {
        try
        {
            PathPolicy.NoLinks(path); PathPolicy.NoLinks(path + ".lock");
            using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (OperatingSystem.IsWindows())
            {
                // Delete by the verified file handle: no path-swap window and no following reparse points.
                using var handle = CreateFileW(path, 0x80010000, 0, IntPtr.Zero, 3, 0x08200000, IntPtr.Zero);
                if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 9, out var attributes, 8) || (attributes.Attributes & 0x410) != 0) return false;
                using var stream = new FileStream(handle, FileAccess.Read);
                if (stream.Length != size || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != hash) return false;
                CheckNoPending(); var disposition = new Disposition { Delete = true };
                return SetFileInformationByHandle(handle, 4, ref disposition, 4);
            }
            else
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                if (stream.Length != size || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != hash) return false;
                CheckNoPending(); PathPolicy.NoLinks(path); File.Delete(path); return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { internal uint Attributes, ReparseTag; }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.Bool)] internal bool Delete; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, uint kind, out AttributeTag value, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, uint kind, ref Disposition value, uint size);
    internal async Task<FileStream> LockAsync(string path, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            token.ThrowIfCancellationRequested(); PathPolicy.NoLinks(path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < until) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    public async Task<string> MaterializeAsync(ManagedFile file, ResourceDownloader downloader,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        var target = ObjectPath(file.Sha256, file.Size);
        if (await FileVerifier.MatchesAsync(target, file.Size, file.Sha256, token).ConfigureAwait(false)) return target;
        var parts = new List<string>();
        foreach (var part in file.Parts) parts.Add(await downloader.FetchAsync(part, this, progress, token).ConfigureAwait(false));
        if (parts.Count == 1 && parts[0] == target) return target;
        using var lease = await LockAsync(target + ".lock", token).ConfigureAwait(false);
        if (await FileVerifier.MatchesAsync(target, file.Size, file.Sha256, token).ConfigureAwait(false)) return target;
        var temporary = target + ".assembling"; PathPolicy.NoLinks(temporary);
        try
        {
            EnsureAvailableBytes(checked(file.Size + 65536));
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                foreach (var path in parts)
                {
                    PathPolicy.NoLinks(path);
                    using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
                    await source.CopyToAsync(output, token).ConfigureAwait(false);
                }
                output.Flush(true);
            }
            if (!await FileVerifier.MatchesAsync(temporary, file.Size, file.Sha256, token).ConfigureAwait(false))
                throw new InvalidDataException("分卷合并后校验失败：" + file.Path);
            PathPolicy.NoLinks(target); File.Move(temporary, target, true);
            return target;
        }
        finally { PathPolicy.NoLinks(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
