using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.Manager;

public sealed record ClientExportProgress(string Stage, long CompletedBytes, long TotalBytes, string Message);

public sealed class ClientPackageExporter
{
    private readonly ManagerPaths _paths;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly bool _allowUnpackagedAssets;
    public ClientPackageExporter(ManagerPaths paths, bool allowUnpackagedAssets = false)
    {
        _paths = paths;
        _allowUnpackagedAssets = allowUnpackagedAssets;
    }

    public async Task<string> ExportPairAsync(DeploymentConfig config, bool testBuild, ClientUpdateStore? updates,
        CancellationToken cancellationToken, Action<ClientExportProgress>? progress)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("正在生成其他客户端包，请稍后重试。");
        string? x64 = null, x86 = null, combined = null;
        try
        {
            x64 = await ExportCoreAsync("client", config, testBuild, cancellationToken, progress, "x64", updates?.PublicUrl, isPair: true);
            if (updates is null && !File.Exists(Path.Combine(_paths.AssetsDirectory, "launcher-x86", "DFLegacy.Launcher.exe")))
            {
                var legacy = x64;
                x64 = null;
                return legacy;
            }
            x86 = await ExportCoreAsync("client", config, testBuild, cancellationToken, progress, "x86", updates?.PublicUrl, isPair: true);
            combined = Path.Combine(_paths.Exports, $"dflegacy-client-win-x86-x64-{Guid.NewGuid():N}.zip");
            progress?.Invoke(new("finalizing", 0, 0, "正在合并两个安装包并发布更新…"));
            using (var archive = ZipFile.Open(combined, ZipArchiveMode.Create))
            {
                foreach (var (arch, path) in new[] { ("x86", x86), ("x64", x64) })
                {
                    await using var source = File.OpenRead(path);
                    await using var output = archive.CreateEntry($"DFLegacy-client-win-{arch}.zip", CompressionLevel.NoCompression).Open();
                    await source.CopyToAsync(output, cancellationToken);
                }
            }
            if (updates is not null) await updates.PublishAsync(x86, x64, testBuild, cancellationToken);
            var size = new FileInfo(combined).Length;
            progress?.Invoke(new("ready", size, size, "两个客户端包生成完成。"));
            return combined;
        }
        catch { if (combined is not null) File.Delete(combined); throw; }
        finally
        {
            try
            {
                if (x86 is not null) File.Delete(x86);
                if (x64 is not null) File.Delete(x64);
            }
            finally { _gate.Release(); }
        }
    }

    public async Task<string> ExportAsync(string kind, DeploymentConfig config, bool testBuild = false,
        CancellationToken cancellationToken = default, Action<ClientExportProgress>? progress = null)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("正在生成其他客户端包，请稍后重试。");
        try { return await ExportCoreAsync(kind, config, testBuild, cancellationToken, progress, "x64", null); }
        finally { _gate.Release(); }
    }

    private async Task<string> ExportCoreAsync(string kind, DeploymentConfig config, bool testBuild, CancellationToken cancellationToken, Action<ClientExportProgress>? progress, string arch, string? updateUrl, bool isPair = false)
    {
        if (kind is not ("client" or "patch" or "launcher")) throw new ArgumentException("未知客户端包类型。");
        testBuild = kind == "client" && testBuild;
        kind = "client";
        ConfigurationStore.Validate(config);
        var launcherAsset = arch == "x86" ? "launcher-x86/DFLegacy.Launcher.exe" : "launcher/DFLegacy.Launcher.exe";
        var diagnosticAsset = arch == "x86" ? "launcher-x86/DFLegacy.Diagnostics.exe" : "launcher/DFLegacy.Diagnostics.exe";
        string? destination = null;
        try
        {
            progress?.Invoke(new("preparing", 0, 0, "正在读取发行清单…"));
            var manifestPath = Path.Combine(_paths.AssetsDirectory, "manifest.json");
            if (!File.Exists(manifestPath)) throw new InvalidDataException("未安装配套 Windows 客户端资源，请使用完整发行包。");
            if (new FileInfo(manifestPath).Length > 1024 * 1024) throw new InvalidDataException("客户端资源清单过大。");
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
            ValidateJson(manifestBytes);
            var manifest = JsonNode.Parse(manifestBytes) as JsonObject ?? throw new InvalidDataException("客户端资源清单损坏。");
            if (!manifest.Select(p => p.Key).Order().SequenceEqual(new[] { "commit", "files", "version" })) throw new InvalidDataException("客户端资源清单格式无效。");
            var releaseFile = Path.Combine(_paths.Root, "release-manifest.json");
            var version = manifest["version"]?.GetValue<string>() ?? "";
            var commit = manifest["commit"]?.GetValue<string>() ?? "";
            if (version.Length is < 1 or > 100 || !version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')
                || commit.Length != 40 || !commit.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new InvalidDataException("客户端资源版本或提交格式无效。");
            if (File.Exists(releaseFile))
            {
                var release = JsonNode.Parse(await File.ReadAllTextAsync(releaseFile, cancellationToken))!;
                var anchoredAssetManifest = release["files"]?.AsArray().FirstOrDefault(entry => entry?["path"]?.GetValue<string>() == "client-assets/manifest.json");
                var manifestHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
                if (!manifestHash.Equals(anchoredAssetManifest?["sha256"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("客户端资源清单与发行摘要不一致，请重新安装完整发行包。");
                if (release["version"]?.GetValue<string>() != version || release["commit"]?.GetValue<string>() != commit)
                    throw new InvalidDataException("客户端资源与当前服务版本不一致，拒绝混用。");
            }
            else if (!_allowUnpackagedAssets) throw new InvalidDataException("缺少发行版本清单。");
            var entries = manifest["files"]?.AsArray() ?? throw new InvalidDataException("缺少客户端文件清单。");
            if (entries.Count is < 1 or > 4096) throw new InvalidDataException("客户端文件数量不合理。");
            var files = new Dictionary<string, (string FullPath, string Hash)>(StringComparer.OrdinalIgnoreCase);
            long bytes = 0;
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileIndex = 0;
            foreach (var entry in entries)
            {
                var name = entry?["path"]?.GetValue<string>() ?? "";
                var hash = entry?["sha256"]?.GetValue<string>() ?? "";
                if (!SafeAssetName(name) || hash.Length != 64 || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) throw new InvalidDataException("客户端文件清单包含不安全路径或摘要。");
                if (entry is not JsonObject row || !row.Select(p => p.Key).Order().SequenceEqual(new[] { "path", "sha256" }) || !declared.Add(name))
                    throw new InvalidDataException("客户端文件清单格式无效或路径重复。");
                if (name == "patch/debug.dll" && !testBuild) continue;
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
                if ((name is "launcher/DFLegacy.Diagnostics.exe" or "launcher-x86/DFLegacy.Diagnostics.exe") && name != diagnosticAsset) continue;
                if ((name is "launcher/DFLegacy.Launcher.exe" or "launcher-x86/DFLegacy.Launcher.exe") && name != launcherAsset) continue;
                bytes = checked(bytes + new FileInfo(path).Length);
                if (bytes > 1024L * 1024 * 1024) throw new InvalidDataException("客户端资源超过 1 GiB 上限。");
                if (!files.TryAdd(name, (path, hash))) throw new InvalidDataException("客户端文件路径重复。");
                await using var stream = File.OpenRead(path);
                fileIndex++;
                using var hashState = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var hashBuffer = new byte[81920];
                long verifiedBytes = 0;
                progress?.Invoke(new("validating", 0, stream.Length, $"校验资源 {fileIndex}/{entries.Count}：{name}"));
                int count;
                while ((count = await stream.ReadAsync(hashBuffer, cancellationToken)) != 0)
                {
                    hashState.AppendData(hashBuffer, 0, count);
                    verifiedBytes += count;
                    progress?.Invoke(new("validating", verifiedBytes, stream.Length, $"校验资源 {fileIndex}/{entries.Count}：{name}"));
                }
                var actual = Convert.ToHexStringLower(hashState.GetHashAndReset());
                if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"客户端资源校验失败：{name}");
            }
            foreach (var required in new[] { "patch/DFLegacy.Client.dll", "patch/Config.ini", launcherAsset, diagnosticAsset, "patch/DFLegacy.CrashCapture.exe" })
                if (!files.ContainsKey(required)) throw new InvalidDataException($"客户端资源不完整：{required}");

            if (testBuild && !files.ContainsKey("patch/debug.dll")) throw new InvalidDataException("缺少配套测试资源 debug.dll，请安装同版本完整发行包。");
            if (files.TryGetValue("launcher/DFLegacy.Client.dll", out var bundledPatch) && bundledPatch.Hash != files["patch/DFLegacy.Client.dll"].Hash)
                throw new InvalidDataException("客户端资源混用了不同版本补丁。");

            foreach (var old in new DirectoryInfo(_paths.Exports).GetFiles("dflegacy-*.zip").Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1))) old.Delete();
            destination = Path.Combine(_paths.Exports, $"dflegacy-{kind}-{version}-{Guid.NewGuid():N}.zip");
            var sums = new StringBuilder();
            var compressionTotal = files.Where(file => file.Key is "patch/DFLegacy.Client.dll" or "patch/debug.dll" or "patch/DFLegacy.CrashCapture.exe" or "launcher/Assets/Skin/fwtk.html" || file.Key == launcherAsset || file.Key == diagnosticAsset)
                .Sum(file => new FileInfo(file.Value.FullPath).Length);
            long compressedBytes = 0;
            progress?.Invoke(new("compressing", 0, compressionTotal, "正在压缩客户端资源…"));
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
                var installedFiles = new List<object>();
                foreach (var (name, file) in files)
                {
                    var zipName = name switch
                    {
                        "patch/DFLegacy.Client.dll" => "DFLegacy.Client.dll",
                        "patch/debug.dll" => "debug.dll",
                        "patch/DFLegacy.CrashCapture.exe" => "DFLegacy.CrashCapture.exe",
                        _ when name == diagnosticAsset => "DFLegacy.Diagnostics.exe",
                        _ when name == launcherAsset => "DFLegacy.Launcher.exe",
                        "launcher/Assets/Skin/fwtk.html" => "Assets/Skin/fwtk.html",
                        _ => null
                    };
                    if (zipName is null) continue;
                    await using var source = File.OpenRead(file.FullPath);
                    await using var target = archive.CreateEntry(zipName, CompressionLevel.Fastest).Open();
                    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920]; int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                    {
                        digest.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        compressedBytes += read;
                        progress?.Invoke(new("compressing", compressedBytes, compressionTotal, $"正在压缩：{zipName}"));
                    }
                    var actual = Convert.ToHexStringLower(digest.GetHashAndReset());
                    if (!actual.Equals(file.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"生成时客户端资源已发生变更：{name}");
                    sums.Append(actual).Append("  ").Append(zipName).Append('\n');
                    if (zipName is "DFLegacy.Launcher.exe" or "DFLegacy.Client.dll" or "debug.dll" or "DFLegacy.Diagnostics.exe" or "DFLegacy.CrashCapture.exe")
                    {
                        if (source.Length <= 0) throw new InvalidDataException("客户端程序为空。");
                        installedFiles.Add(new { path = zipName, size = source.Length, sha256 = actual });
                    }
                }
                progress?.Invoke(new("finalizing", 0, 0, "正在写入连接配置和校验清单…"));
                await Add("client-package.json", JsonSerializer.Serialize(new { schemaVersion = 2, version, commit, flavor = testBuild ? "test" : "normal", files = installedFiles }));
                var launcher = new
                {
                    ClientPath = "DNF.exe", EntranceHost = config.AdvertisedHost, config.EntrancePort, AccountServicePort = config.AdminPort,
                    config.AccountServiceUrl, UpdateServiceUrl = updateUrl ?? "", DisableGitHubUpdates = updateUrl is not null, Theme = "dark", Account = "", Password = "", Accounts = Array.Empty<object>(), AccountIndex = 0, StartServer = false,
                    D3D9Adapter = 0, D3D9TimeoutSeconds = 30, D3D9RetryIntervalSeconds = 1,
                    Relay = new { Enabled = config.RelayEnabled, TcpPorts = string.Join(',', config.ForwardedTcpPorts()),
                        UdpPorts = string.Join(',', config.ForwardedUdpPorts()), Preference = config.RelayPreference }
                };
                await Add("launcher.json", JsonSerializer.Serialize(launcher, new JsonSerializerOptions { WriteIndented = true }));
                await Add("README.txt", $"DFLegacy {version}\n提交：{commit}\n\n新版使用独立 DFLegacy.Client.dll。旧版用户需先迁移一次：退出游戏并备份客户端，在新登录器中确认经哈希验证的原版 IJL 恢复；不要按备份文件名猜测原版。\n"
                    + "将 ZIP 内容直接解压到 DNF.exe 所在目录，运行 DFLegacy.Launcher.exe；首次自行注册或输入账号。\n默认配置内嵌，已有 Config.ini 仅更新必要连接字段并保留窗口设置。\n"
                    + "保持原始 EXE、DLL、PVF、NPK 不变；仅支持已验证的同版本兼容展开客户端，不修复原始旧壳。\n不包含客户端或 Script.pvf，请自行提供与服务端匹配的资源。\n普通版和测试版默认自动发送基本故障信息，可在设置关闭；小型转储仅保存在当前用户本地。普通版包含完整游戏优化且不加载调试覆盖层；测试包默认在首个游戏内画面加载本地诊断，自动输入和测试控制仍需显式启用。仅服务端覆盖数据需要共享本机目录，不自动同步远程数据。\n"
                    + "Detours 完整许可和版本信息已内嵌于补丁、引导器和登录器。查看：DFLegacy.Launcher.exe --third-party-notices | more\n");
                var checksumData = Encoding.UTF8.GetBytes(sums.ToString());
                await using var checksum = archive.CreateEntry("SHA256SUMS").Open();
                await checksum.WriteAsync(checksumData, cancellationToken);
            }
            if (!isPair || arch == "x64" && updateUrl is null && !File.Exists(Path.Combine(_paths.AssetsDirectory, "launcher-x86", "DFLegacy.Launcher.exe")))
                progress?.Invoke(new("ready", new FileInfo(destination).Length, new FileInfo(destination).Length, "客户端包生成完成，准备下载。"));
            return destination;
        }
        catch { if (destination is not null && File.Exists(destination)) File.Delete(destination); throw; }
    }

    private static bool SafeAssetName(string name)
    {
        if (name.Length is < 3 or > 512 || name.Contains('\\') || name.Contains(':') || name.Any(char.IsControl) || name.StartsWith('/')) return false;
        var parts = name.Split('/');
        if (parts.Any(part => part is "" or "." or ".." || part.StartsWith('.'))) return false;
        return name is "patch/DFLegacy.Client.dll" or "patch/debug.dll" or "patch/Config.ini"
            or "launcher/DFLegacy.Launcher.exe" or "launcher-x86/DFLegacy.Launcher.exe" or "launcher/DFLegacy.Client.dll" or "launcher/launcher.json"
            or "launcher/DFLegacy.Diagnostics.exe" or "launcher-x86/DFLegacy.Diagnostics.exe" or "patch/DFLegacy.CrashCapture.exe"
            or "launcher/Config.ini" or "launcher/Assets/Skin/fwtk.html";
    }

    private static void ValidateJson(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            void Visit(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!names.Add(property.Name)) throw new InvalidDataException("客户端清单存在重复字段。");
                        Visit(property.Value);
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Visit(item);
            }
            Visit(document.RootElement);
        }
        catch (JsonException error) { throw new InvalidDataException("客户端清单 JSON 无效。", error); }
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
