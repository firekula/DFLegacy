using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.UpdateCore;

public sealed record ClientChapterIdentity(int Schema, string Chapter, string Entry, string[] RequiredFiles)
{
    public const string FileName = "DFLegacy.Chapter.json";
    public const string ChapterOne = "chapter1";
    public const string ChapterTwo = "chapter2";
    private const int MaximumDescriptorBytes = 64 * 1024;
    internal static IReadOnlyList<string> FirstChapterModules { get; } =
        Array.AsReadOnly(new[] { "DFLegacy.Client.dll", "DFLegacy.Debug.dll", "debug.dll" });

    public static ClientChapterIdentity Read(string root, VerifiedRelease release)
    {
        var identity = ReadFile(release, PathPolicy.Resolve(root, FileName));
        if (identity.Chapter == ChapterTwo) RequireNoFirstChapterModules(root);
        return identity;
    }

    internal static void RequireNoFirstChapterModules(string root)
    {
        foreach (var module in FirstChapterModules)
        {
            var path = PathPolicy.Resolve(root, module);
            if (File.Exists(path) || Directory.Exists(path))
                throw new InvalidDataException("第二章安装中残留第一章加载模块：" + module);
        }
    }

    internal static ClientChapterIdentity ReadFile(VerifiedRelease release, string path) =>
        Parse(release, InstallationStore.ReadBounded(path, MaximumDescriptorBytes));

    internal static ManagedFile? Descriptor(VerifiedRelease release) =>
        release.Manifest.Files.SingleOrDefault(file => file.Path.Equals(FileName, StringComparison.OrdinalIgnoreCase));

    internal static ClientChapterIdentity? ReadStaged(string root, StagedUpdate staged, ContentCache cache)
    {
        var file = Descriptor(staged.Release);
        if (file == null) return null;
        var installed = PathPolicy.Resolve(root, file.Path);
        var path = FileVerifier.MatchesAsync(installed, file.Size, file.Sha256).GetAwaiter().GetResult()
            ? installed : cache.ObjectPath(file.Sha256, file.Size);
        if (!File.Exists(path)) throw new InvalidDataException("缺少已校验的章别描述。");
        return ReadFile(staged.Release, path);
    }

    public static ClientChapterIdentity Parse(VerifiedRelease release, byte[] descriptor)
    {
        release.Validate(release.Source);
        var manifest = release.Manifest;
        var managed = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        if (descriptor is not { Length: > 0 and <= MaximumDescriptorBytes }
            || !managed.TryGetValue(FileName, out var file) || file.Path != FileName
            || file.Size != descriptor.LongLength || file.Sha256 != Convert.ToHexStringLower(SHA256.HashData(descriptor)))
            throw new InvalidDataException("章别描述未绑定到已验证的资源清单。");

        try
        {
            using var document = JsonDocument.Parse(descriptor, new JsonDocumentOptions { MaxDepth = 8 });
            ManifestCodec.RejectDuplicateKeys(document.RootElement);
            var identity = document.RootElement.Deserialize<ClientChapterIdentity>(ManifestCodec.Json);
            if (identity is not { Schema: 1, Chapter: ChapterOne or ChapterTwo, RequiredFiles.Length: > 0 and <= 128 }
                || string.IsNullOrEmpty(identity.Entry) || identity.Entry.Contains('/')
                || !identity.Entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("章别描述格式无效。");

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in identity.RequiredFiles)
            {
                PathPolicy.Validate(path);
                if (!required.Add(path) || !managed.TryGetValue(path, out var requiredFile) || requiredFile.Path != path)
                    throw new InvalidDataException("章别启动文件缺失或重复：" + path);
            }
            if (!required.Contains(identity.Entry) || !required.Contains("Script.pvf")
                || identity.Chapter == ChapterOne && !required.Contains("DFLegacy.Client.dll")
                || identity.Chapter == ChapterTwo && !required.Contains("DNF.exe"))
                throw new InvalidDataException("章别描述遗漏必要的启动文件。");
            if (identity.Chapter == ChapterTwo && FirstChapterModules.Any(managed.ContainsKey))
                throw new InvalidDataException("第二章清单包含第一章加载模块。");
            return identity;
        }
        catch (JsonException ex) { throw new InvalidDataException("章别描述格式无效。", ex); }
    }
}
