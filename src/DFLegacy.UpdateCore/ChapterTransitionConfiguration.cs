using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.UpdateCore;

public sealed record ChapterDisplayPreference(int Width, int Height, string Filter);

public static partial class ChapterTransition
{
    private static readonly string[] AccountFields = ["Accounts", "AccountIndex", "Account", "Password"];

    private static string ConfigurationPath(InstallationStore store, string name)
    {
        if (name is not (FirstConfig or SecondConfig)) throw new ArgumentException("Unexpected configuration name.", nameof(name));
        var path = Path.Combine(store.Root, name);
        PathPolicy.NoLinks(path);
        return path;
    }

    private static byte[]? ReadConfig(InstallationStore store, string name)
    {
        var path = ConfigurationPath(store, name);
        return Exists(path) ? InstallationStore.ReadBounded(path, ConfigurationMigration.MaxBytes) : null;
    }

    private static JsonObject ParseConfiguration(byte[]? bytes)
    {
        if (bytes == null) return new();
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 };
            using var document = JsonDocument.Parse(bytes, options);
            RejectAmbiguousFields(document.RootElement);
            return JsonNode.Parse(bytes, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, options) as JsonObject
                ?? throw new InvalidDataException("启动器配置必须是 JSON 对象。");
        }
        catch (JsonException) { throw new InvalidDataException("启动器配置无法读取，原文件已保留。"); }
    }

    private static void RejectAmbiguousFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectAmbiguousFields(item);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in element.EnumerateObject())
            {
                if (!fields.Add(field.Name)) throw new InvalidDataException("启动器配置包含重复字段。");
                RejectAmbiguousFields(field.Value);
            }
        }
    }

    private static byte[] MergeConfiguration(byte[]? accountSource, byte[]? settings, bool includeDisplay,
        ChapterDisplayPreference? firstDisplay = null)
    {
        var source = ParseConfiguration(accountSource);
        var result = ParseConfiguration(settings);
        if (accountSource != null)
        {
            if (source.ContainsKey("Accounts") && source["Accounts"] is not JsonArray
                || source["Accounts"] is JsonArray accounts && accounts.Any(account => account is not JsonObject)
                || source.ContainsKey("AccountIndex") && (source["AccountIndex"] is not JsonValue index || !index.TryGetValue<int>(out _))
                || !IsString(source, "Account") || !IsString(source, "Password"))
                throw new InvalidDataException("启动器账号配置字段类型无效。");

            foreach (var field in AccountFields)
            {
                result.Remove(field);
                if (source.TryGetPropertyValue(field, out var value)) result.Add(field, value?.DeepClone());
            }
        }
        if (includeDisplay && source.TryGetPropertyValue("Theme", out var theme) && IsString(source, "Theme"))
            result["Theme"] = theme?.DeepClone();
        if (firstDisplay != null && !result.ContainsKey("ChapterTwoDisplay"))
        {
            if (firstDisplay.Width is < 640 or > 8192 || firstDisplay.Height is < 480 or > 8192
                || firstDisplay.Filter is not ("sharp" or "point" or "linear"))
                throw new InvalidDataException("跨章显示设置无效。");
            result.Add("ChapterTwoDisplay", JsonSerializer.SerializeToNode(firstDisplay));
        }
        var output = JsonSerializer.SerializeToUtf8Bytes(result, new JsonSerializerOptions { WriteIndented = true });
        if (output.Length > ConfigurationMigration.MaxBytes) throw new InvalidDataException("迁移后的启动器配置超过大小限制。");
        return output;
    }

    private static bool IsString(JsonObject value, string name) =>
        !value.ContainsKey(name) || value[name] is JsonValue field && field.TryGetValue<string>(out _);
}
