using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.UpdateCore;

public static class ConfigurationMigration
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public static byte[] Apply(byte[] json, ConfigMigration[] migrations)
    {
        try
        {
            if (json.Length is 0 or > MaxBytes || migrations == null) throw new InvalidDataException("配置大小或迁移集合无效。");
            using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(json), new JsonDocumentOptions { MaxDepth = 64 });
            ValidateObjectNames(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("配置必须是 JSON 对象；原始文件已保留。");
            var root = JsonNode.Parse(json)!.AsObject(); var changed = false;
            foreach (var migration in migrations)
            {
                ValidateMigration(migration);
                var relayName = Find(root, "Relay");
                if (relayName == null && migration.Operation == "replaceIf") continue;
                if (relayName == null) { relayName = "Relay"; root[relayName] = new JsonObject(); changed = true; }
                if (root[relayName] is not JsonObject relay) throw new InvalidDataException("Relay 配置不是对象；原始文件已保留。");
                var field = migration.Key[6..]; var existing = Find(relay, field);
                var desired = JsonNode.Parse(migration.Value.GetRawText());
                var isPorts = field is "TcpPorts" or "UdpPorts";
                if (isPorts && migration.Operation != "unionPorts") desired = JsonValue.Create(string.Join(',', Ports(desired)));
                switch (migration.Operation)
                {
                    case "unionPorts":
                        var values = existing == null || relay[existing] == null ? (field == "TcpPorts" ? new[] { 2311, 7001, 8080 } : new[] { 2311, 7002, 7003 }) : ConfigPorts(relay[existing]);
                        var merged = values.Concat(Ports(desired)).Distinct().ToArray();
                        var target = JsonValue.Create(string.Join(',', merged));
                        if (existing == null || !JsonNode.DeepEquals(relay[existing], target)) { relay[existing ?? field] = target; changed = true; }
                        break;
                    case "setDefault":
                        if (existing == null) { relay[field] = desired; changed = true; }
                        break;
                    case "replaceIf":
                        var when = JsonNode.Parse(migration.When!.Value.GetRawText());
                        var matches = existing != null && (isPorts && relay[existing] != null
                            ? ConfigPorts(relay[existing]).Distinct().SequenceEqual((when is JsonArray ? Ports(when) : ConfigPorts(when)).Distinct())
                            : JsonNode.DeepEquals(relay[existing], when));
                        if (matches && !JsonNode.DeepEquals(relay[existing!], desired))
                        { relay[existing!] = desired; changed = true; }
                        break;
                }
            }
            return changed ? JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true }) : json;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        { throw new InvalidDataException("配置迁移无效；原始文件已保留。", ex); }
    }
    public static bool IsSatisfied(byte[] json, ConfigMigration[] migrations)
    {
        try { return Apply(json, migrations).AsSpan().SequenceEqual(json); }
        catch (InvalidDataException) { return false; }
    }
    private static string? Find(JsonObject obj, string name) => obj.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static int[] Ports(JsonNode? node)
    {
        if (node is not JsonArray array) throw new InvalidDataException("端口集合不是数组。");
        return array.Select(n => n is JsonValue v && v.TryGetValue<int>(out var p) && p is >= 1 and <= 65535 ? p : throw new InvalidDataException("端口超出有效范围。")).ToArray();
    }
    private static int[] ConfigPorts(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text)) throw new InvalidDataException("端口配置必须是字符串。");
        return text.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 65535 ? number : throw new InvalidDataException("端口超出有效范围。")).ToArray();
    }
    private static void ValidateMigration(ConfigMigration m)
    {
        if (m == null || !ManifestCodec.IsName(m.Id) || m.File != "launcher.json" || m.Operation is not ("unionPorts" or "setDefault" or "replaceIf")
            || m.Key is not ("Relay.TcpPorts" or "Relay.UdpPorts" or "Relay.Enabled" or "Relay.Preference")
            || m.Operation == "replaceIf" && m.When == null || m.Operation != "replaceIf" && m.When != null)
            throw new InvalidDataException("配置迁移操作或字段无效。");
        var node = JsonNode.Parse(m.Value.GetRawText());
        if (m.Key.EndsWith("Ports", StringComparison.Ordinal)) _ = Ports(node);
        else if (m.Operation == "unionPorts" || m.Key == "Relay.Enabled" && m.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || m.Key == "Relay.Preference" && !Preference(m.Value)) throw new InvalidDataException("配置迁移值类型无效。");
        if (m.When is { } when)
        {
            if (m.Key.EndsWith("Ports", StringComparison.Ordinal))
            { var condition = JsonNode.Parse(when.GetRawText()); _ = condition is JsonArray ? Ports(condition) : ConfigPorts(condition); }
            else if (m.Key == "Relay.Enabled" && when.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || m.Key == "Relay.Preference" && !Preference(when)) throw new InvalidDataException("配置迁移条件类型无效。");
        }
    }
    private static bool Preference(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString()?.ToLowerInvariant() is "ipv4" or "ipv6";
    private static void ValidateObjectNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in element.EnumerateObject())
            { if (!names.Add(item.Name)) throw new InvalidDataException("配置包含歧义字段；原始文件已保留。"); ValidateObjectNames(item.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) ValidateObjectNames(item);
    }
}
