using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DFLegacy.UpdateCore;

public sealed record ResourcePart(string Sha256, long Size, string Release, string Asset);
public sealed record ManagedFile(string Path, long Size, string Sha256, ResourcePart[] Parts);
public sealed record RetiredFile(string Path, string Action, long Sequence);
public sealed record ConfigMigration(string Id, string File, string Operation, string Key, JsonElement Value, JsonElement? When = null);
public sealed record ClientManifest(int SchemaVersion, string Version, string Commit, string Architecture,
    string Flavor, string Channel, long Sequence, long MinimumSequence, ManagedFile[] Files,
    RetiredFile[] Retired, ConfigMigration[] Migrations);
public sealed record UpdateProgress(string Phase, string Path, int CompletedFiles, int TotalFiles,
    long BytesReceived = 0, long TotalBytes = 0, double BytesPerSecond = 0, string? Detail = null);

public static class ManifestCodec
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public const long PartLimit = 48L * 1024 * 1024;
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32
    };
    public static byte[] Serialize(ClientManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
    public static ClientManifest Parse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (bytes.Length is <= 0 or > MaxBytes) throw new InvalidDataException("更新清单大小无效。");
            using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes), new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateKeys(document.RootElement);
            var manifest = document.RootElement.Deserialize<ClientManifest>(Json) ?? throw new InvalidDataException("更新清单为空。");
            Validate(manifest);
            return manifest;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("更新清单格式无效。", ex); }
    }
    internal static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in element.EnumerateObject())
            {
                if (!names.Add(item.Name)) throw new InvalidDataException("JSON 包含重复字段。");
                RejectDuplicateKeys(item.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateKeys(item);
    }
    public static bool IsHash(string? value, int length = 64) => value != null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool IsName(string? value) => value is { Length: > 0 and <= 128 } && Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9._-]*\\z", RegexOptions.CultureInvariant);
    public static void Validate(ClientManifest m)
    {
        if (m.SchemaVersion != 4 || !IsName(m.Version) || !IsHash(m.Commit, 40)
            || m.Architecture is not ("x86" or "x64") || m.Flavor is not ("normal" or "test")
            || !IsName(m.Channel) || m.Sequence < 1 || m.MinimumSequence < 0 || m.MinimumSequence > m.Sequence
            || m.Files is not { Length: > 0 and <= 100000 } || m.Retired == null || m.Migrations == null)
            throw new InvalidDataException("更新清单身份或文件集合无效。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var resources = new Dictionary<string, (string Hash, long Size)>(StringComparer.Ordinal);
        foreach (var file in m.Files)
        {
            if (file == null) throw new InvalidDataException("文件记录为空。");
            PathPolicy.Validate(file.Path);
            if (!paths.Add(file.Path) || file.Size < 0 || !IsHash(file.Sha256) || file.Parts is not { Length: > 0 and <= 100000 })
                throw new InvalidDataException("重复路径或无效文件长度/哈希。");
            long sum = 0;
            foreach (var part in file.Parts)
            {
                if (part == null || !IsHash(part.Sha256) || part.Size < 0 || part.Size > PartLimit
                    || part.Size == 0 && (file.Size != 0 || file.Parts.Length != 1)
                    || !IsName(part.Release) || !IsName(part.Asset)) throw new InvalidDataException("传输分卷无效。");
                sum = checked(sum + part.Size);
                var key = part.Release + "/" + part.Asset;
                if (resources.TryGetValue(key, out var prior) && prior != (part.Sha256, part.Size))
                    throw new InvalidDataException("同一附件引用了不同内容。");
                resources[key] = (part.Sha256, part.Size);
            }
            if (sum != file.Size) throw new InvalidDataException("分卷总长度与文件不符。");
            total = checked(total + file.Size);
        }
        foreach (var path in paths)
        {
            var slash = path.IndexOf('/');
            while (slash >= 0)
            {
                if (paths.Contains(path[..slash])) throw new InvalidDataException("文件和目录路径冲突。");
                slash = path.IndexOf('/', slash + 1);
            }
        }
        var retired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in m.Retired)
        {
            if (item == null) throw new InvalidDataException("退役记录为空。");
            PathPolicy.Validate(item.Path);
            if (!retired.Add(item.Path) || paths.Contains(item.Path) || item.Action is not ("delete" or "unmanage")
                || item.Sequence < 1 || item.Sequence > m.Sequence) throw new InvalidDataException("文件退役记录无效。");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var migration in m.Migrations)
        {
            if (migration == null || !IsName(migration.Id) || !ids.Add(migration.Id)
                || migration.File != "launcher.json" || migration.Operation is not ("unionPorts" or "setDefault" or "replaceIf")
                || migration.Key is not ("Relay.TcpPorts" or "Relay.UdpPorts" or "Relay.Enabled" or "Relay.Preference"))
                throw new InvalidDataException("配置迁移无效或不支持该字段。");
        }
    }
}
