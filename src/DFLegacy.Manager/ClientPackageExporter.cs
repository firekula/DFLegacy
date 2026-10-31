using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.Manager;

public sealed class ClientPackageExporter
{
    private readonly ManagerPaths _paths;
    private readonly SemaphoreSlim _gate = new(1);
    public ClientPackageExporter(ManagerPaths paths) => _paths = paths;

    public async Task<string> ExportAsync(string kind, DeploymentConfig config, CancellationToken cancellationToken = default)
    {
        if (kind is not ("client" or "patch" or "launcher")) throw new ArgumentException("未知客户端包类型。");
        kind = "client";
        ConfigurationStore.Validate(config);
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("正在生成其他客户端包，请稍后重试。");
        string? destination = null;
        try
        {
            var manifestPath = Path.Combine(_paths.AssetsDirectory, "manifest.json");
            if (!File.Exists(manifestPath)) throw new InvalidDataException("未安装配套 Windows 客户端资源，请使用完整发行包。");
            if (new FileInfo(manifestPath).Length > 1024 * 1024) throw new InvalidDataException("客户端资源清单过大。");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken)) ?? throw new InvalidDataException("客户端资源清单损坏。");
            var releaseFile = Path.Combine(_paths.Root, "release-manifest.json");
            if (!File.Exists(releaseFile)) throw new InvalidDataException("缺少发行版本清单。");
            var release = JsonNode.Parse(await File.ReadAllTextAsync(releaseFile, cancellationToken))!;
            var anchoredAssetManifest = release["files"]?.AsArray().FirstOrDefault(entry => entry?["path"]?.GetValue<string>() == "client-assets/manifest.json");
            await using (var manifestStream = File.OpenRead(manifestPath))
            {
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(manifestStream, cancellationToken));
                if (!actual.Equals(anchoredAssetManifest?["sha256"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("客户端资源清单与发行摘要不一致，请重新安装完整发行包。");
            }
            var version = manifest["version"]?.GetValue<string>() ?? "";
            var commit = manifest["commit"]?.GetValue<string>() ?? "";
            if (version.Length is < 1 or > 100 || !version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')
                || commit.Length != 40 || !commit.All(Uri.IsHexDigit) || release["version"]?.GetValue<string>() != version || release["commit"]?.GetValue<string>() != commit)
                throw new InvalidDataException("客户端资源与当前服务版本不一致，拒绝混用。");
            var entries = manifest["files"]?.AsArray() ?? throw new InvalidDataException("缺少客户端文件清单。");
            if (entries.Count is < 1 or > 4096) throw new InvalidDataException("客户端文件数量不合理。");
            var files = new Dictionary<string, (string FullPath, string Hash)>(StringComparer.OrdinalIgnoreCase);
            long bytes = 0;
            foreach (var entry in entries)
            {
                var name = entry?["path"]?.GetValue<string>() ?? "";
                var hash = entry?["sha256"]?.GetValue<string>() ?? "";
                if (!SafeAssetName(name) || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("客户端文件清单包含不安全路径或摘要。");
                var path = Path.GetFullPath(name.Replace('/', Path.DirectorySeparatorChar), _paths.AssetsDirectory);
                if (!path.StartsWith(_paths.AssetsDirectory + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("客户端文件超出资源目录。");
                var component = _paths.AssetsDirectory;
                foreach (var part in name.Split('/'))
                {
                    component = Path.Combine(component, part);
                    if (!File.Exists(component) && !Directory.Exists(component)) throw new InvalidDataException($"缺少客户端文件：{name}");
                    if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("客户端资源不允许符号链接。");
                }
                bytes = checked(bytes + new FileInfo(path).Length);
                if (bytes > 1024L * 1024 * 1024) throw new InvalidDataException("客户端资源超过 1 GiB 上限。");
                if (!files.TryAdd(name, (path, hash))) throw new InvalidDataException("客户端文件路径重复。");
                await using var stream = File.OpenRead(path);
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"客户端资源校验失败：{name}");
            }
            foreach (var required in new[] { "patch/ijl15.dll", "patch/Config.ini", "launcher/DFLegacy.Launcher.exe" })
                if (!files.ContainsKey(required)) throw new InvalidDataException($"客户端资源不完整：{required}");

            foreach (var old in new DirectoryInfo(_paths.Exports).GetFiles("dflegacy-*.zip").Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1))) old.Delete();
            destination = Path.Combine(_paths.Exports, $"dflegacy-{kind}-{version}-{Guid.NewGuid():N}.zip");
            var sums = new StringBuilder();
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
            {
                async Task Add(string name, string contents)
                {
                    var data = Encoding.UTF8.GetBytes(contents);
                    await using var target = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                    await target.WriteAsync(data, cancellationToken);
                    sums.Append(Convert.ToHexStringLower(SHA256.HashData(data))).Append("  ").Append(name).Append('\n');
                }
                foreach (var (name, file) in files)
                {
                    var zipName = name switch
                    {
                        "patch/ijl15.dll" => "ijl15.dll",
                        "launcher/DFLegacy.Launcher.exe" => "DFLegacy.Launcher.exe",
                        "launcher/Assets/Skin/fwtk.html" => "Assets/Skin/fwtk.html",
                        _ => null
                    };
                    if (zipName is null) continue;
                    await using var source = File.OpenRead(file.FullPath);
                    await using var target = archive.CreateEntry(zipName, CompressionLevel.Fastest).Open();
                    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920]; int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0) { digest.AppendData(buffer, 0, read); await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken); }
                    var actual = Convert.ToHexStringLower(digest.GetHashAndReset());
                    if (!actual.Equals(file.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"生成时客户端资源已发生变更：{name}");
                    sums.Append(actual).Append("  ").Append(zipName).Append('\n');
                }
                var launcher = new
                {
                    ClientPath = "DNF.exe", EntranceHost = config.AdvertisedHost, config.EntrancePort, AccountServicePort = config.AdminPort,
                    config.AccountServiceUrl, Theme = "dark", Account = "", Password = "", Accounts = Array.Empty<object>(), AccountIndex = 0, StartServer = false,
                    D3D9Adapter = 0, D3D9TimeoutSeconds = 30, D3D9RetryIntervalSeconds = 1,
                    Relay = new { Enabled = config.RelayEnabled, TcpPorts = string.Join(',', config.ForwardedTcpPorts()),
                        UdpPorts = string.Join(',', config.ForwardedUdpPorts()), Preference = config.RelayPreference }
                };
                await Add("launcher.json", JsonSerializer.Serialize(launcher, new JsonSerializerOptions { WriteIndented = true }));
                await Add("README.txt", $"DFLegacy {version}\n提交：{commit}\n\n请先退出游戏并备份客户端 ijl15.dll 和 Config.ini。\n"
                    + "将 ZIP 内容直接解压到 DNF.exe 所在目录，运行 DFLegacy.Launcher.exe；首次自行注册或输入账号。\n默认配置内嵌，已有 Config.ini 仅更新必要连接字段并保留窗口设置。\n"
                    + "不包含客户端或 Script.pvf，请自行提供与服务端匹配的资源。\n远程调试浮窗默认关闭；当前调试浮窗需要与服务端共享本机目录。\n");
                var checksumData = Encoding.UTF8.GetBytes(sums.ToString());
                await using var checksum = archive.CreateEntry("SHA256SUMS").Open();
                await checksum.WriteAsync(checksumData, cancellationToken);
            }
            return destination;
        }
        catch { if (destination is not null && File.Exists(destination)) File.Delete(destination); throw; }
        finally { _gate.Release(); }
    }

    private static bool SafeAssetName(string name)
    {
        if (name.Length is < 3 or > 512 || name.Contains('\\') || name.Contains(':') || name.Any(char.IsControl) || name.StartsWith('/')) return false;
        var parts = name.Split('/');
        if (parts.Any(part => part is "" or "." or ".." || part.StartsWith('.'))) return false;
        if (name.StartsWith("patch/", StringComparison.Ordinal)) return parts.Length == 2 && parts[1] is "ijl15.dll" or "debug.dll" or "Config.ini";
        if (!name.StartsWith("launcher/", StringComparison.Ordinal)) return false;
        if (parts.Length == 2)
            return parts[1] is "launcher.json" or "Config.ini" or "DFLegacy.Launcher.deps.json" or "DFLegacy.Launcher.runtimeconfig.json"
                || parts[1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || parts[1].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || parts[1].EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);
        if (name == "launcher/Assets/Skin/fwtk.html") return true;
        return parts.Length == 3 && parts[1].Length is >= 2 and <= 12 && parts[1].All(character => char.IsAsciiLetter(character) || character == '-')
            && parts[2].EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
    }

    internal static string SetIni(string text, string section, string key, string value)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var start = lines.FindIndex(line => line.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) { lines.Add("[" + section + "]"); lines.Add(key + "=" + value); return string.Join('\n', lines); }
        var end = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        var found = false;
        for (var index = end - 1; index > start; index--)
        {
            if (lines[index].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) { if (!found) { lines[index] = key + "=" + value; found = true; } else lines.RemoveAt(index); }
        }
        if (!found) lines.Insert(start + 1, key + "=" + value);
        return string.Join('\n', lines);
    }
}
