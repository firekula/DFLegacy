namespace DFLegacy.Launcher;

internal sealed record LocalServerChoice(string Name, string Host, int Port)
{
    internal string Id { get; init; } = "local";
    internal string Region { get; init; } = "telecom";
    internal int AccountServicePort { get; init; } = 8081;
    internal string AccountServiceUrl { get; init; } = string.Empty;
    internal string Endpoint => (Host.Contains(':') && !Host.StartsWith('[') ? $"[{Host}]" : Host) + ":" + Port;
    internal string ResolveAccountServiceUrl() => string.IsNullOrWhiteSpace(AccountServiceUrl)
        ? $"http://{(Host.Contains(':') && !Host.StartsWith('[') ? $"[{Host}]" : Host)}:{AccountServicePort}"
        : AccountServiceUrl.Trim();
}

internal sealed class LocalServerSelection
{
    internal IReadOnlyList<LocalServerChoice> Choices { get; }
    internal LocalServerChoice? SelectedChoice { get; private set; }
    internal string AccountServiceUrl => SelectedChoice?.ResolveAccountServiceUrl() ?? string.Empty;
    private LocalServerSelection(IReadOnlyList<LocalServerChoice> choices) => Choices = choices;
    internal static LocalServerSelection Open(LauncherAccountStore store) => new(ReadChoices(store.Load()));

    private static IReadOnlyList<LocalServerChoice> ReadChoices(LauncherOptions options)
    {
        if (options.Accounts is null || options.Accounts.Any(a => a is null) || options.Relay is null || options.Servers is null)
            throw new InvalidDataException("连接配置结构不完整，请重新读取配置。");
        var choices = options.Servers.Count == 0
            ? new[] { new LocalServerChoice("本地服务器", options.EntranceHost, options.EntrancePort)
                { AccountServicePort = options.AccountServicePort, AccountServiceUrl = options.AccountServiceUrl } }
            : options.Servers.Select(p => p is null ? throw new InvalidDataException("服务器配置不能为空。") :
                new LocalServerChoice(p.Name, p.Host, p.Port) { Id = p.Id, Region = p.Region,
                    AccountServicePort = p.AccountServicePort, AccountServiceUrl = p.AccountServiceUrl }).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            if (string.IsNullOrWhiteSpace(choice.Id) || !ids.Add(choice.Id) || string.IsNullOrWhiteSpace(choice.Name)
                || string.IsNullOrWhiteSpace(choice.Host) || choice.Port is < 1 or > 65535
                || choice.AccountServicePort is < 1 or > 65535 || choice.Region is not ("telecom" or "netcom"))
                throw new InvalidDataException("服务器的标识、分区、地址或端口无效。");
            choices[i] = choice with { Host = choice.Host.Trim(), AccountServiceUrl = choice.AccountServiceUrl?.Trim() ?? "" };
            if (!Uri.TryCreate(choices[i].ResolveAccountServiceUrl(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException("服务器账号 API 地址必须是有效的 HTTP 或 HTTPS 地址。");
        }
        return Array.AsReadOnly(choices);
    }

    internal void Select(string id) => SelectedChoice = Choices.FirstOrDefault(c => c.Id == id)
        ?? throw new ArgumentException("找不到该服务器。", nameof(id));

    internal LauncherOptions Confirm(LauncherAccountStore store)
    {
        var selected = SelectedChoice ?? throw new InvalidOperationException("请先选择服务器。");
        var options = store.Load();
        var current = ReadChoices(options).FirstOrDefault(c => c.Id == selected.Id);
        if (current != selected)
            throw new InvalidOperationException("已保存的服务器连接发生变化，请重新选择并确认。");
        // Load creates a detached snapshot. Never save selection or overwrite externally edited bytes.
        options.EntranceHost = selected.Host; options.EntrancePort = selected.Port;
        options.AccountServicePort = selected.AccountServicePort; options.AccountServiceUrl = selected.AccountServiceUrl;
        options.SelectedServerId = selected.Id;
        return options;
    }
}
