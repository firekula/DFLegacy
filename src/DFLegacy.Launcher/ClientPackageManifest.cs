using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DFLegacy.Launcher;

internal sealed record ClientPackageFile(string Path, long Size, string Sha256);

internal sealed class ClientPackageManifest
{
    internal string DirectoryPath { get; }
    internal string Version { get; }
    internal string Commit { get; }
    internal string Flavor { get; }
    internal IReadOnlyList<ClientPackageFile> Files { get; }
    private ClientPackageManifest(string directory, string version, string commit, string flavor, List<ClientPackageFile> files)
    { DirectoryPath = directory; Version = version; Commit = commit; Flavor = flavor; Files = new ReadOnlyCollection<ClientPackageFile>(files); }

    internal static ClientPackageManifest LoadAndValidate(string directory)
    {
        try
        {
            directory = Path.GetFullPath(directory);
            using var source = new FileStream(Path.Combine(directory,"client-package.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length is <= 0 or > 16384) throw new InvalidDataException();
            var bytes = new byte[(int)source.Length]; source.ReadExactly(bytes);
            var manifest = Parse(bytes,directory);
            manifest.ValidateFiles();
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
            foreach(var entry in root.GetProperty("files").EnumerateArray())
            {
                ExactKeys(entry,"path","size","sha256");
                var name = entry.GetProperty("path").GetString()!;
                var size = entry.GetProperty("size").GetInt64(); var hash = entry.GetProperty("sha256").GetString()!;
                if(name is null || !expected.Remove(name) || size <= 0 || !Hex(hash,64)) throw new InvalidDataException();
                files.Add(new(name,size,hash));
            }
            if(expected.Count!=0) throw new InvalidDataException();
            return new(directory,version,commit,flavor,files);
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or DecoderFallbackException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("客户端安装清单或文件校验失败。请将同一版本的完整普通包或测试包解压到客户端目录，不要混用文件。",ex); }
    }
    internal void ValidateFiles()
    {
        foreach(var entry in Files)
        {
            using var file = new FileStream(Path.Combine(DirectoryPath,entry.Path),FileMode.Open,FileAccess.Read,FileShare.Read);
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
