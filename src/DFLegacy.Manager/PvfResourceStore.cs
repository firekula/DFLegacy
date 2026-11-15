using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DFLegacy.Manager;

public sealed record PvfResourceStatus(bool Available, long SizeBytes, string Path, bool ExternallyControlled, long MaximumBytes);
public sealed record StoredPvf(string Path, long SizeBytes, bool Created);

/// <summary>Bounded streaming storage; user filenames never select a filesystem location.</summary>
public sealed class PvfResourceStore(ManagerPaths paths, long maximumBytes = PvfResourceStore.MaximumBytes)
{
    public const long MaximumBytes = 1024L * 1024 * 1024;
    private static ReadOnlySpan<byte> Magic => "DNF_SCRIPT_PACK"u8;
    private string Resources => System.IO.Path.Combine(paths.Data, "resources");

    public PvfResourceStatus GetStatus(ConfigurationSnapshot config)
    {
        var path = System.IO.Path.GetFullPath(config.Effective.ScriptPvfPath, paths.Root);
        var available = File.Exists(path);
        return new(available, available ? new FileInfo(path).Length : 0, path,
            config.EnvironmentOverrides.Any(name => name.Equals("DFLegacy__ScriptPvfPath", StringComparison.OrdinalIgnoreCase)), MaximumBytes);
    }

    public async Task<StoredPvf> StoreAsync(Stream input, CancellationToken cancellationToken = default)
    {
        if (maximumBytes is <= 0 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Resources);
        if ((File.GetAttributes(Resources) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("资源目录不能是符号链接。");
        var temporary = System.IO.Path.Combine(Resources, Guid.NewGuid().ToString("N") + ".tmp");
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        var header = new byte[Magic.Length + 12]; var headerRead = 0; long length = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var output = new FileStream(temporary, options))
            {
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (count == 0) break;
                    length = checked(length + count);
                    if (length > maximumBytes) throw new InvalidDataException("Script.pvf 超过允许的上传大小（最大 1 GiB）。");
                    var take = Math.Min(count, header.Length - headerRead);
                    buffer.AsSpan(0, take).CopyTo(header.AsSpan(headerRead)); headerRead += take;
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }
            if (headerRead != header.Length || !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                throw new InvalidDataException("请选择有效的 Script.pvf（DNF_SCRIPT_PACK）资源文件。");
            var treeLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(Magic.Length, 4));
            var fileCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(Magic.Length + 8, 4));
            if (treeLength <= 0 || treeLength % 4 != 0 || treeLength > length - header.Length || fileCount > int.MaxValue || fileCount * 20L > treeLength)
                throw new InvalidDataException("Script.pvf 索引长度或文件数量无效，文件可能不完整。");
            await PvfIndexValidator.ValidateAsync(temporary, header, length, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var digest = hash.GetHashAndReset();
            var destination = System.IO.Path.Combine(Resources, Convert.ToHexStringLower(digest) + ".pvf");
            if (File.Exists(destination))
            {
                if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("资源文件不能是符号链接。");
                await using (var existing = File.OpenRead(destination))
                    if (existing.Length == length && CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(existing, cancellationToken), digest))
                        return new(destination, length, false);
                File.Move(temporary, destination, overwrite: true);
                return new(destination, length, false);
            }
            File.Move(temporary, destination);
            return new(destination, length, true);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
