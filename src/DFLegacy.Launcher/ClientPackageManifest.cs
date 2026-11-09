using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DFLegacy.Launcher.Updates;

namespace DFLegacy.Launcher;

internal sealed record ClientPackageFile(string Path, long Size, string Sha256);

internal sealed class ClientPackageManifest
{
    internal const string ScriptPvfName = "Script.pvf";
    internal string DirectoryPath { get; }
    internal string Version { get; }
    internal string Commit { get; }
    internal string Flavor { get; }
    internal IReadOnlyList<ClientPackageFile> Files { get; }
    internal string MetadataPath {get;}
    internal bool HasScriptPvf => Files.Any(file => file.Path == ScriptPvfName);
    private ClientPackageManifest(string directory, string version, string commit, string flavor, List<ClientPackageFile> files,bool v4=false)
    { DirectoryPath = directory; Version = version; Commit = commit; Flavor = flavor; Files = new ReadOnlyCollection<ClientPackageFile>(files); MetadataPath=Path.Combine(directory,v4?Path.Combine(".dflegacy","v4","installed.json"):"client-package.json"); }

    internal static ClientPackageManifest LoadAndValidate(string directory, string? unchangedScriptPvfPath = null)
    {
        try
        {
            directory = Path.GetFullPath(directory);
            if(DFLegacy.UpdateCore.InstallTransaction.HasPending(directory)) throw new InvalidDataException("存在未完成的 V4 更新，请先恢复。");
            var receipt = new DFLegacy.UpdateCore.InstallationStore(directory).ReadInstalled();
            if(receipt!=null)
            {
                var target=receipt.Release.Manifest;
                var verification=DFLegacy.UpdateCore.FileVerifier.VerifyAsync(directory,target).GetAwaiter().GetResult();
                if(!verification.IsValid) throw new InvalidDataException("V4 文件校验失败："+string.Join("、",verification.Problems.Take(5).Select(p=>p.Path)));
                if(!target.Files.Any(f=>f.Path=="DFLegacy.Launcher.exe") || !target.Files.Any(f=>f.Path=="DFLegacy.Client.dll"))
                    throw new InvalidDataException("V4 清单缺少登录器或客户端补丁。");
                if(target.Flavor=="test" && !target.Files.Any(f=>f.Path=="debug.dll"))
                    throw new InvalidDataException("V4 测试版本缺少受管理的 debug.dll，不能加载遗留的未知模块。");
                if(target.Migrations.Length>0)
                {
                    var config=DFLegacy.UpdateCore.PathPolicy.Resolve(directory,"launcher.json",true);
                    if(!File.Exists(config) || new FileInfo(config).Length>16*1024*1024 || !DFLegacy.UpdateCore.ConfigurationMigration.IsSatisfied(File.ReadAllBytes(config),target.Migrations))
                        throw new InvalidDataException("客户端配置需要完成更新迁移。");
                }
                return new(directory,target.Version,target.Commit,target.Flavor,target.Files.Select(f=>new ClientPackageFile(f.Path,f.Size,f.Sha256)).ToList(),v4:true);
            }
            using var source = new FileStream(Path.Combine(directory,"client-package.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length is <= 0 or > 16384) throw new InvalidDataException();
            var bytes = new byte[(int)source.Length]; source.ReadExactly(bytes);
            var manifest = Parse(bytes,directory);
            manifest.ValidateFiles(unchangedScriptPvfPath);
            return manifest;
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new InvalidDataException("客户端安装清单或文件校验失败。请将同一版本的完整普通包或测试包解压到客户端目录，不要混用文件。",ex); }
    }

    // Installation and update archives use exactly the same schema and file policy.
    internal static ClientPackageManifest Parse(byte[] bytes, string directory)
    {
        try
        {
            if(bytes.Length is <= 0 or > 16384) throw new InvalidDataException();
            using var json = JsonDocument.Parse(new UTF8Encoding(false,true).GetString(bytes), new JsonDocumentOptions { MaxDepth = 5 });
            var root = json.RootElement; ExactKeys(root,"schemaVersion","version","commit","flavor","files");
            if(root.GetProperty("schemaVersion").GetInt32()!=2) throw new InvalidDataException();
            var version = root.GetProperty("version").GetString()!;
            var commit = root.GetProperty("commit").GetString()!;
            var flavor = root.GetProperty("flavor").GetString()!;
            if(version is null || version.Length is < 1 or > 128 || version.Any(char.IsControl) || string.IsNullOrWhiteSpace(version)
                || !Hex(commit,40) || flavor is not ("normal" or "test")) throw new InvalidDataException();
            var files = new List<ClientPackageFile>();
            var expected = new HashSet<string>(["DFLegacy.Launcher.exe","DFLegacy.Client.dll"],StringComparer.Ordinal);
            if(flavor=="test") expected.Add("debug.dll");
            expected.Add(ScriptPvfName);
            expected.Add("DFLegacy.Diagnostics.exe");expected.Add("DFLegacy.CrashCapture.exe");
            foreach(var entry in root.GetProperty("files").EnumerateArray())
            {
                ExactKeys(entry,"path","size","sha256");
                var name = entry.GetProperty("path").GetString()!;
                var size = entry.GetProperty("size").GetInt64(); var hash = entry.GetProperty("sha256").GetString()!;
                if(name is null || !expected.Remove(name) || size <= 0 || !Hex(hash,64)) throw new InvalidDataException();
                files.Add(new(name,size,hash));
            }
            expected.Remove(ScriptPvfName);
            // Keep old schema-2 releases readable; new packages carry both helpers.
            if(expected.Contains("DFLegacy.Diagnostics.exe")&&expected.Contains("DFLegacy.CrashCapture.exe"))
            {expected.Remove("DFLegacy.Diagnostics.exe");expected.Remove("DFLegacy.CrashCapture.exe");}
            if(expected.Count!=0) throw new InvalidDataException();
            return new(directory,version,commit,flavor,files);
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or DecoderFallbackException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("客户端安装清单或文件校验失败。请将同一版本的完整普通包或测试包解压到客户端目录，不要混用文件。",ex); }
    }
    internal void ValidateFiles(string? unchangedScriptPvfPath = null)
    {
        foreach(var entry in Files)
        {
            var path = Path.Combine(DirectoryPath, entry.Path);
            if(entry.Path==ScriptPvfName)
            {
                path = unchangedScriptPvfPath ?? path;
                UpdateSafety.NoLinks(path);
            }
            using var file = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(file.Length!=entry.Size || Convert.ToHexStringLower(SHA256.HashData(file))!=entry.Sha256) throw new InvalidDataException("客户端安装清单或文件校验失败。");
        }
    }
    internal static bool Hex(string? value, int length) => value is not null && Regex.IsMatch(value,$"\\A[0-9a-f]{{{length}}}\\z");
    internal static void ExactKeys(JsonElement element, params string[] keys)
    {
        if(element.ValueKind!=JsonValueKind.Object) throw new InvalidDataException();
        var remaining = new HashSet<string>(keys,StringComparer.Ordinal);
        foreach(var p in element.EnumerateObject()) if(!remaining.Remove(p.Name)) throw new InvalidDataException();
        if(remaining.Count!=0) throw new InvalidDataException();
    }
}
