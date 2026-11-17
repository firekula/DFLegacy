using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

/// <summary>Stages a full client beside the current installation and commits the switch only after startup.</summary>
internal static partial class FullStarterMigration
{
    private const string WorkspaceName = "c2-upgrade";
    private const string AccountSettings = "launcher.chapter2.json";
    private const long ExpandedLimit = 2L * 1024 * 1024 * 1024;

    private static string Workspace(string root)
    {
        var path = Path.Combine(Path.GetFullPath(root), ".dflegacy", WorkspaceName);
        Core.PathPolicy.NoLinks(path);
        return path;
    }

    private static string ActivationPath(string root)
    {
        var path = Path.Combine(Workspace(root), "active.json");
        Core.PathPolicy.NoLinks(path);
        return path;
    }

    internal static string CreateAttempt(string root)
    {
        var path = Path.Combine(Workspace(root), Guid.NewGuid().ToString("N"));
        Core.PathPolicy.NoLinks(path);
        Directory.CreateDirectory(path);
        return path;
    }

    internal static async Task<string> ImportAsync(Stream input, string attempt, long length, string sha256,
        IProgress<Core.UpdateProgress>? progress = null, CancellationToken token = default)
    {
        if (length <= 0 || length >= ExpandedLimit || !Core.ManifestCodec.IsHash(sha256))
            throw new InvalidDataException("完整启动包的校验信息无效。");
        Core.PathPolicy.NoLinks(attempt);
        Directory.CreateDirectory(attempt);
        var archive = Path.Combine(attempt, "starter.zip");
        await using var file = new FileStream(archive, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var timer = Stopwatch.StartNew();
        var buffer = new byte[128 * 1024];
        long received = 0;
        long lastReport = -100;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0) break;
            received += read;
            if (received > length) throw new InvalidDataException("完整启动包超出发布的文件长度。");
            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            if (timer.ElapsedMilliseconds - lastReport >= 100 || received == length)
            {
                progress?.Report(new("download", "完整启动包", 0, 1, received, length,
                    received / Math.Max(timer.Elapsed.TotalSeconds, 0.1)));
                lastReport = timer.ElapsedMilliseconds;
            }
        }
        if (received != length || Convert.ToHexStringLower(hash.GetHashAndReset()) != sha256)
            throw new InvalidDataException("完整启动包下载不完整或 SHA-256 校验失败。");

        token.ThrowIfCancellationRequested();
        await file.FlushAsync(token).ConfigureAwait(false);
        file.Position = 0;
        var client = Path.Combine(attempt, "client");
        if (Directory.Exists(client) || File.Exists(client))
            throw new IOException("完整启动包的暂存路径已被占用。");
        Core.PathPolicy.NoLinks(client);
        Directory.CreateDirectory(client);
        await Task.Run(() => Extract(file, client, progress, token), token).ConfigureAwait(false);
        return client;
    }

    private static void Extract(Stream file, string client, IProgress<Core.UpdateProgress>? progress, CancellationToken token)
    {
        using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count is 0 or > 4096)
            throw new InvalidDataException("完整启动包的文件数量无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        var count = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.TrimEnd('/');
            if (!names.Add(name) || (expanded += entry.Length) > ExpandedLimit)
                throw new InvalidDataException("完整启动包含有重复文件或解压大小超出上限。");
            var privateBootstrap = name.EndsWith(".exe", StringComparison.Ordinal)
                && Core.ManifestCodec.IsHash(Path.GetFileNameWithoutExtension(name))
                && name == ".dflegacy/bootstrap/" + Path.GetFileName(name);
            if (name is not (AccountSettings or ".dflegacy/v4/installed.json" or ".dflegacy/bootstrap/recovery.ini")
                && !privateBootstrap)
                Core.PathPolicy.Validate(name);
            var path = Path.Combine(client, name.Replace('/', Path.DirectorySeparatorChar));
            Core.PathPolicy.NoLinks(path);
            if (entry.Name.Length == 0)
            {
                if (entry.Length != 0) throw new InvalidDataException("完整启动包的目录记录无效。");
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path);
            }
            if (++count % 20 == 0 || count == zip.Entries.Count)
                progress?.Report(new("stage", "完整启动包", count, zip.Entries.Count));
        }
    }

    internal static void CopySettings(string root, string client)
    {
        // This is the only writer in the old installation during the handoff.
        // Keep its ordinary configuration lease while taking the snapshot.
        using var lease = Core.InstallationLease.Acquire(root);
        foreach (var name in new[] { AccountSettings, "Config.ini" })
        {
            var source = Path.Combine(root, name);
            var destination = Path.Combine(client, name);
            Core.PathPolicy.NoLinks(source);
            Core.PathPolicy.NoLinks(destination);
            if (!File.Exists(source)) continue;
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 4 * 1024 * 1024) throw new InvalidDataException("待迁移的客户端配置过大。");
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
        ChapterTwoGameSettings.CopySaved(root, client);
    }

    internal static string? ReadActive(string root)
    {
        var marker = ActivationPath(root);
        if (!File.Exists(marker)) return null;
        using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is 0 or > 128) throw new InvalidDataException("完整启动包的激活记录大小无效。");
        var id = JsonSerializer.Deserialize<string>(stream);
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidDataException("完整启动包的激活记录无效。");
        var path = Path.Combine(Workspace(root), id!, "client");
        Core.PathPolicy.NoLinks(path);
        if (!Directory.Exists(path)) throw new InvalidDataException("已激活的客户端目录不存在。");
        return path;
    }

    internal static async Task ActivateAsync(string root, string client, Func<Action, CancellationToken, Task> launch,
        CancellationToken token = default)
    {
        Core.PathPolicy.NoLinks(client);
        var folder = Path.GetDirectoryName(Path.GetFullPath(client));
        var id = Path.GetFileName(folder);
        if (!Guid.TryParseExact(id, "N", out _) || !Directory.Exists(client) || Path.GetFileName(client) != "client"
            || !string.Equals(Path.GetDirectoryName(folder), Workspace(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("完整启动包的激活目录不属于当前安装。");
        var marker = ActivationPath(root);
        var committed = false;
        await launch(() =>
        {
            var temporary = marker + "." + Guid.NewGuid().ToString("N");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(JsonSerializer.SerializeToUtf8Bytes(id));
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, marker);
                committed = true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, token).ConfigureAwait(false);
        if (!committed) throw new InvalidDataException("新版登录器尚未确认启动。");
    }

    internal static void Deactivate(string root) => File.Delete(ActivationPath(root));
}
