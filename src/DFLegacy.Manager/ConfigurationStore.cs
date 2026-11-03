using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.Manager;

public sealed class ConfigurationStore
{
    private readonly ManagerPaths _paths;
    private readonly IDictionary<string, string?> _environment;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, string> Fields = new()
    {
        [nameof(DeploymentConfig.ScriptPvfPath)] = "ScriptPvfPath",
        [nameof(DeploymentConfig.BindHost)] = "Entrance:Host",
        [nameof(DeploymentConfig.AdminHost)] = "Admin:Host",
        [nameof(DeploymentConfig.GameplayAdvertisedHost)] = "GameplayDatagram:AdvertisedHost",
        [nameof(DeploymentConfig.EntrancePort)] = "Entrance:Port",
        [nameof(DeploymentConfig.ChannelPort)] = "Channel:Port",
        [nameof(DeploymentConfig.AdminPort)] = "Admin:Port",
        [nameof(DeploymentConfig.GmPanelHost)] = "WebPanels:Gm:Host",
        [nameof(DeploymentConfig.GmPanelPort)] = "WebPanels:Gm:Port",
        [nameof(DeploymentConfig.GmPanelEnabled)] = "WebPanels:Gm:Enabled",
        [nameof(DeploymentConfig.PlayerPanelHost)] = "WebPanels:Player:Host",
        [nameof(DeploymentConfig.PlayerPanelPort)] = "WebPanels:Player:Port",
        [nameof(DeploymentConfig.PlayerPanelEnabled)] = "WebPanels:Player:Enabled",
        [nameof(DeploymentConfig.CharacterPort)] = "CharacterDatagram:Port",
        [nameof(DeploymentConfig.GameplayPrimaryPort)] = "GameplayDatagram:PrimaryPort",
        [nameof(DeploymentConfig.GameplaySecondaryPort)] = "GameplayDatagram:SecondaryPort",
        [nameof(DeploymentConfig.FallbackRelayEnabled)] = "FallbackRelay:Enabled",
        [nameof(DeploymentConfig.FallbackRelayAdvertisedHost)] = "FallbackRelay:AdvertisedHost",
        [nameof(DeploymentConfig.FallbackRelayTcpPort)] = "FallbackRelay:TcpPort",
        [nameof(DeploymentConfig.FallbackRelayUdpPort)] = "FallbackRelay:UdpPort",
        [nameof(DeploymentConfig.PvpEnabled)] = "Pvp:Enabled",
        [nameof(DeploymentConfig.RegionalChannelsEnabled)] = "Channel:RegionalChannelsEnabled",
        [nameof(DeploymentConfig.PvpPort)] = "Pvp:Port",
        [nameof(DeploymentConfig.ChannelName)] = "Channel:Name",
        [nameof(DeploymentConfig.MaximumUsers)] = "Channel:MaximumUsers",
        [nameof(DeploymentConfig.MonsterExperienceMultiplier)] = "Experience:MonsterMultiplier",
        [nameof(DeploymentConfig.ClearExperienceMultiplier)] = "Experience:ClearMultiplier",
        [nameof(DeploymentConfig.DropRatePercent)] = "Drop:RatePercent",
        [nameof(DeploymentConfig.DropEconomicRate)] = "Drop:EconomicRate"
    };

    public ConfigurationStore(ManagerPaths paths, IDictionary<string, string?>? environment = null)
    {
        _paths = paths;
        _environment = environment ?? Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(x => (string)x.Key, x => x.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(paths.ServerConfig))
        {
            var template = Path.Combine(paths.Root, "templates", "server.json");
            var root = File.Exists(template) ? ParseFile(template) : new JsonObject();
            var section = root["DFLegacy"] as JsonObject ?? new JsonObject();
            if (root["DFLegacy"] is null) root["DFLegacy"] = section;
            Set(section, "SeedTestAccount", JsonValue.Create(false));
            Set(section, "DataPath", JsonValue.Create(paths.GameData));
            Set(section, "ScriptPvfPath", JsonValue.Create(Path.Combine(paths.Root, "resources", "Script.pvf")));
            AtomicFile.Write(paths.ServerConfig, root.ToJsonString(Json));
        }
        if (!File.Exists(paths.SettingsFile)) AtomicFile.Write(paths.SettingsFile, JsonSerializer.Serialize(new ManagerSettings(), Json));
    }

    public ConfigurationSnapshot Read()
    {
        lock (_gate)
        {
            var root = ParseFile(_paths.ServerConfig);
            var section = root["DFLegacy"] as JsonObject ?? throw new InvalidDataException("配置缺少 DFLegacy 节。");
            var settings = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(_paths.SettingsFile), Json) ?? new();
            var saved = ReadConfig(section, settings);
            var effectiveNode = ApplyEnvironment(section, out var overrides);
            return new(saved, ReadConfig(effectiveNode, settings), overrides.ToArray(), settings.Configured);
        }
    }

    public ConfigurationSnapshot Save(DeploymentConfig config)
    {
        lock (_gate)
        {
            var before = Read();
            foreach (var (field, path) in Fields)
            {
                if (field == nameof(DeploymentConfig.GameplayAdvertisedHost)) continue;
                var env = "DFLegacy__" + path.Replace(":", "__", StringComparison.Ordinal);
                if (_environment.Keys.Any(key => key.Equals(env, StringComparison.OrdinalIgnoreCase))
                    && !Equals(typeof(DeploymentConfig).GetProperty(field)!.GetValue(config), typeof(DeploymentConfig).GetProperty(field)!.GetValue(before.Saved)))
                    throw new InvalidOperationException($"{field} 被环境变量 {env} 控制，请修改部署环境。");
            }
            var gameHost = config.RelayEnabled ? "127.0.0.1" : config.AdvertisedHost;
            var controlledGameHost = _environment.FirstOrDefault(entry => entry.Key.Equals("DFLegacy__GameplayDatagram__AdvertisedHost", StringComparison.OrdinalIgnoreCase)).Value;
            if (controlledGameHost is not null && !controlledGameHost.Equals(gameHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("游戏协议地址由环境变量 DFLegacy__GameplayDatagram__AdvertisedHost 控制，与当前连接/Relay 设置冲突；中继模式须设为 127.0.0.1。");
            var root = ParseFile(_paths.ServerConfig);
            var section = root["DFLegacy"]!.AsObject();
            foreach (var (field, path) in Fields) Set(section, path, JsonSerializer.SerializeToNode(typeof(DeploymentConfig).GetProperty(field)!.GetValue(config)));
            Set(section, "GameplayDatagram:AdvertisedHost", JsonValue.Create(gameHost));
            if (config.BindHost != before.Saved.BindHost)
                foreach (var path in new[] { "CharacterDatagram:Host", "GameplayDatagram:Host", "Channel:Host" }) Set(section, path, JsonValue.Create(config.BindHost));
            var extraPorts = Get(section, "Entrance:AdditionalPorts")?.DeepClone() as JsonArray ?? new JsonArray(before.Saved.ChannelPort, before.Saved.AdditionalEntrancePort);
            foreach (var (oldPort, newPort) in new[] { (before.Saved.ChannelPort, config.ChannelPort), (before.Saved.AdditionalEntrancePort, config.AdditionalEntrancePort) })
            {
                if (oldPort == newPort) continue;
                var found = false;
                for (var index = 0; index < extraPorts.Count; index++) if (extraPorts[index]?.GetValue<int>() == oldPort)
                {
                    var env = $"DFLegacy__Entrance__AdditionalPorts__{index}";
                    if (_environment.Keys.Any(key => key.Equals(env, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException($"附加端口由 {env} 控制，请修改部署环境。");
                    extraPorts[index] = newPort; found = true;
                }
                if (!found) extraPorts.Add(newPort);
            }
            Set(section, "Entrance:AdditionalPorts", extraPorts);
            var settings = new ManagerSettings
            {
                AdvertisedHost = config.AdvertisedHost, AccountServiceUrl = config.AccountServiceUrl,
                RelayEnabled = config.RelayEnabled, RelayPreference = config.RelayPreference, Autostart = config.Autostart, Configured = true
            };
            var effectiveDocument = ApplyEnvironment(section, out _);
            var effectiveConfig = ReadConfig(effectiveDocument, settings);
            if (Get(effectiveDocument, "Entrance:AdditionalPorts") is JsonArray actualPorts)
            {
                // Validate the actual array before the UI projection can replace
                // a missing non-channel entry with its default visible port.
                var ports = actualPorts.Select(port => port!.GetValue<int>()).ToArray();
                var exclusive = new[] { effectiveConfig.EntrancePort, effectiveConfig.AdminPort }
                    .Concat(effectiveConfig.FallbackRelayEnabled ? new[] { effectiveConfig.FallbackRelayTcpPort } : [])
                    .Concat(effectiveConfig.PvpEnabled ? new[] { effectiveConfig.PvpPort } : []);
                if (ports.Any(port => port is < 1 or > 65535) || ports.Distinct().Count() != ports.Length || ports.Intersect(exclusive).Any())
                    throw new ArgumentException("附加 TCP 端口无效、重复或与其他监听端口冲突。");
            }
            Validate(effectiveConfig);
            Set(section, "ScriptPvfPath", JsonValue.Create(Path.GetFullPath(config.ScriptPvfPath, _paths.Root)));
            settings = settings with { AccountServiceUrl = settings.AccountServiceUrl.TrimEnd('/') };
            var backup = Path.Combine(_paths.Backups, "server-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
            File.Copy(_paths.ServerConfig, backup);
            AtomicFile.Write(_paths.ServerConfig, root.ToJsonString(Json));
            AtomicFile.Write(_paths.SettingsFile, JsonSerializer.Serialize(settings, Json));
            foreach (var old in new DirectoryInfo(_paths.Backups).GetFiles("server-*.json").OrderByDescending(x => x.Name).Skip(20)) old.Delete();
            return Read();
        }
    }

    public JsonObject ReadServerDocument() { lock (_gate) return ParseFile(_paths.ServerConfig); }

    public void SetPvfPath(string path)
    {
        lock (_gate)
        {
            if (_environment.Keys.Any(key => key.Equals("DFLegacy__ScriptPvfPath", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("PVF 由环境变量或容器挂载控制，请修改部署环境。");
            var document = ParseFile(_paths.ServerConfig);
            Set(document["DFLegacy"]!.AsObject(), "ScriptPvfPath", JsonValue.Create(Path.GetFullPath(path)));
            var backup = Path.Combine(_paths.Backups, "server-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
            File.Copy(_paths.ServerConfig, backup);
            AtomicFile.Write(_paths.ServerConfig, document.ToJsonString(Json));
        }
    }

    private JsonObject ApplyEnvironment(JsonObject section, out List<string> overrides)
    {
        var effectiveNode = section.DeepClone().AsObject();
        overrides = new List<string>();
        foreach (var (key, value) in _environment)
        {
            if (!key.StartsWith("DFLegacy__", StringComparison.OrdinalIgnoreCase) || value is null) continue;
            var path = key[10..].Replace("__", ":", StringComparison.Ordinal);
            JsonNode? node;
            var stringField = Fields.Any(field => field.Value.Equals(path, StringComparison.OrdinalIgnoreCase) && typeof(DeploymentConfig).GetProperty(field.Key)!.PropertyType == typeof(string));
            if (stringField || Get(effectiveNode, path) is JsonValue existing && existing.TryGetValue<string>(out _)) node = JsonValue.Create(value);
            else try { node = JsonNode.Parse(value); } catch (JsonException) { node = JsonValue.Create(value); }
            if (node is not JsonValue) node = JsonValue.Create(value);
            Set(effectiveNode, path, node);
            overrides.Add(key);
        }
        return effectiveNode;
    }

    public static void Validate(DeploymentConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ScriptPvfPath) || config.ScriptPvfPath.Length > 4096) throw new ArgumentException("请指定 Script.pvf 路径。");
        if (!IPAddress.TryParse(config.BindHost, out _) || !IPAddress.TryParse(config.AdminHost, out _)) throw new ArgumentException("监听地址必须为 IP 地址，不能使用域名。");
        ValidateDestination(config.AdvertisedHost);
        if (config.PanelListeners().Any(listener => !IPAddress.TryParse(listener.Host, out _))) throw new ArgumentException("面板监听地址必须为 IP 地址。");
        var tcp = config.ForwardedTcpPorts().Append(config.AdminPort).Concat(config.PanelListeners().Select(listener => listener.Port)).ToArray();
        var udp = config.ForwardedUdpPorts();
        if (tcp.Concat(udp).Any(port => port is < 1 or > 65535)) throw new ArgumentException("端口必须为 1–65535。");
        if (tcp.Distinct().Count() != tcp.Length || udp.Distinct().Count() != udp.Length) throw new ArgumentException("同类 TCP 或 UDP 监听端口不能重复。");
        if (config.FallbackRelayEnabled)
        {
            var fallbackHost = string.IsNullOrWhiteSpace(config.FallbackRelayAdvertisedHost)
                ? config.RelayEnabled ? "127.0.0.1" : config.AdvertisedHost : config.FallbackRelayAdvertisedHost;
            if (!IPAddress.TryParse(fallbackHost, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast) || ip.GetAddressBytes()[0] >= 224)
                throw new ArgumentException("ACT1 回退中继须通告具体 IPv4；域名直连请填写回退中继 IPv4 或关闭该中继。");
            var bind = IPAddress.Parse(config.BindHost);
            if ((bind.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !bind.Equals(IPAddress.IPv6Any))
                || IPAddress.IsLoopback(bind) && !bind.Equals(ip)
                || IPAddress.IsLoopback(ip) && !bind.Equals(IPAddress.Any) && !bind.Equals(IPAddress.IPv6Any) && !bind.Equals(ip))
                throw new ArgumentException("回退中继监听地址无法服务通告的 IPv4，请调整监听地址。");
            if (config.RelayEnabled && !ip.Equals(IPAddress.Loopback)) throw new ArgumentException("客户端 Relay 模式的回退中继 IPv4 请留空或填写 127.0.0.1。");
        }
        if (config.MaximumUsers is < 1 or > 65535) throw new ArgumentException("最大人数必须为 1–65535。");
        if (string.IsNullOrWhiteSpace(config.ChannelName) || config.ChannelName.Length > 64 || config.ChannelName.Any(char.IsControl)) throw new ArgumentException("频道名长度为 1–64 个字符。");
        if (new[] { config.MonsterExperienceMultiplier, config.ClearExperienceMultiplier, config.DropEconomicRate }.Any(value => !double.IsFinite(value) || value < 0 || value > 1000)
            || !double.IsFinite(config.DropRatePercent) || config.DropRatePercent < 0 || config.DropRatePercent > 10000) throw new ArgumentException("倍率或掉落百分比不在有效范围。");
        if (!Uri.TryCreate(config.AccountServiceUrl, UriKind.Absolute, out var api) || api.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(api.UserInfo)
            || !string.IsNullOrEmpty(api.Query) || !string.IsNullOrEmpty(api.Fragment)) throw new ArgumentException("账号 API 基址须为不含密码、查询参数的 HTTP(S) 地址。");
        if (config.AccountServiceUrl.Length > 1000 || config.AccountServiceUrl.Any(character => character < 33 || character > 126 || character is '%' or '\\' or '"' or '?' or '#' or '@'))
            throw new ArgumentException("客户端网页基址须为最多 1000 字节的 ASCII HTTP(S) 地址，不能包含百分号、空白、凭据或查询参数。");
        ValidateDestination(api.Host.Trim('[', ']'));
        if (config.RelayEnabled && api.IsLoopback)
            throw new ArgumentException("启用玩家本机中继时，玩家账号服务 URL 不能指向 127.0.0.1、localhost 或 ::1。注册请求直接连接账号服务，请填写玩家可访问的服务器域名或 IP（例如 http://服务器地址:8081）。");
        if (config.RelayPreference is not ("ipv4" or "ipv6")) throw new ArgumentException("Relay 地址族必须为 ipv4 或 ipv6。");
        if (config.RelayEnabled && (config.AdvertisedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(config.AdvertisedHost, out var address) && IPAddress.IsLoopback(address))) throw new ArgumentException("Relay 的服务器地址不能是本机回环地址。");
    }

    public static string PanelUrl(string accountServiceUrl, int port) => new UriBuilder(accountServiceUrl)
        { Scheme = "http", Port = port, Path = "/", Query = "", Fragment = "" }.Uri.AbsoluteUri;

    public static void ValidateDestination(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host != host.Trim() || host.Contains('/') || host.Contains('\\') || host.Any(char.IsWhiteSpace) || host.Any(char.IsControl))
            throw new ArgumentException("请填写玩家可访问的 IP 或域名。");
        if (IPAddress.TryParse(host, out var address))
        {
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) throw new ArgumentException("玩家连接地址不能为 0.0.0.0 或 ::。");
        }
        else if (Uri.CheckHostName(host) != UriHostNameType.Dns || host.Contains(':')) throw new ArgumentException("玩家连接地址格式错误。");
    }

    public static DeploymentConfig ReadSavedClientConfiguration(string directory)
    {
        var section = ParseFile(Path.Combine(directory, "server.json"))["DFLegacy"] as JsonObject
            ?? throw new InvalidDataException("配置缺少 DFLegacy 节。");
        var settings = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(Path.Combine(directory, "manager.json")), Json)
            ?? throw new InvalidDataException("管理器配置为空。");
        return ReadConfig(section, settings, "Script.pvf");
    }

    private DeploymentConfig ReadConfig(JsonObject node, ManagerSettings settings) =>
        ReadConfig(node, settings, Path.Combine(_paths.Root, "resources", "Script.pvf"));

    private static DeploymentConfig ReadConfig(JsonObject node, ManagerSettings settings, string defaultPvf)
    {
        var values = JsonSerializer.SerializeToNode(new DeploymentConfig
        {
            ScriptPvfPath = defaultPvf, AdvertisedHost = settings.AdvertisedHost,
            AccountServiceUrl = settings.AccountServiceUrl, RelayEnabled = settings.RelayEnabled, RelayPreference = settings.RelayPreference, Autostart = settings.Autostart
        })!.AsObject();
        foreach (var (field, path) in Fields) { var value = Get(node, path); if (value is not null) values[field] = value.DeepClone(); }
        if (Get(node, "Entrance:AdditionalPorts") is JsonArray ports)
        {
            var channelPort = values[nameof(DeploymentConfig.ChannelPort)]!.GetValue<int>();
            var additional = ports.FirstOrDefault(port => port is not null && port.GetValue<int>() != channelPort);
            if (additional is not null) values[nameof(DeploymentConfig.AdditionalEntrancePort)] = additional.DeepClone();
        }
        try { return values.Deserialize<DeploymentConfig>(Json)!; }
        catch (JsonException exception) { throw new InvalidDataException("服务配置或环境变量类型错误，请检查端口、倍率和开关。", exception); }
    }

    private static JsonObject ParseFile(string file) => JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
        ?? throw new InvalidDataException("配置必须为 JSON 对象。");
    internal static JsonNode? Get(JsonNode node, string path)
    {
        foreach (var part in path.Split(':'))
        {
            if (node is JsonObject obj) { var key = obj.Select(x => x.Key).FirstOrDefault(x => x.Equals(part, StringComparison.OrdinalIgnoreCase)); if (key is null || obj[key] is null) return null; node = obj[key]!; }
            else if (node is JsonArray arr && int.TryParse(part, out var index) && index >= 0 && index < arr.Count && arr[index] is not null) node = arr[index]!;
            else return null;
        }
        return node;
    }
    private static void Set(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split(':'); JsonNode current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (current is JsonArray array)
            {
                var index = int.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
                while (array.Count <= index) array.Add(null);
                array[index] ??= int.TryParse(parts[i + 1], out _) ? new JsonArray() : new JsonObject(); current = array[index]!;
            }
            else
            {
                var obj = current.AsObject(); var key = obj.Select(x => x.Key).FirstOrDefault(x => x.Equals(part, StringComparison.OrdinalIgnoreCase)) ?? part;
                obj[key] ??= int.TryParse(parts[i + 1], out _) ? new JsonArray() : new JsonObject(); current = obj[key]!;
            }
        }
        if (current is JsonArray end)
        {
            var index = int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture);
            if (index < 0 || index > 1024) throw new InvalidDataException("环境变量数组索引不合理。");
            while (end.Count <= index) end.Add(null); end[index] = value;
        }
        else
        {
            var obj = current.AsObject(); var key = obj.Select(x => x.Key).FirstOrDefault(x => x.Equals(parts[^1], StringComparison.OrdinalIgnoreCase)) ?? parts[^1]; obj[key] = value;
        }
    }
}
