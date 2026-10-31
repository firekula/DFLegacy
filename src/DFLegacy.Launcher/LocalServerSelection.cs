namespace DFLegacy.Launcher;

internal sealed record LocalServerChoice(string Name, string Host, int Port)
{
    internal string Endpoint => (Host.Contains(':') && !Host.StartsWith('[') ? $"[{Host}]" : Host) + ":" + Port;
    internal static LocalServerChoice From(LauncherOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EntranceHost) || options.EntrancePort is < 1 or > 65535)
            throw new InvalidDataException("连接配置中的服务器地址或端口无效，请先保存连接设置。");
        if (options.Accounts is null || options.Accounts.Any(a => a is null) || options.Relay is null)
            throw new InvalidDataException("连接配置结构不完整，请重新读取配置。");
        return new("本地服务器", options.EntranceHost.Trim(), options.EntrancePort);
    }
}

internal sealed class LocalServerSelection
{
    internal IReadOnlyList<LocalServerChoice> Choices { get; }
    internal string AccountServiceUrl { get; }
    private LocalServerSelection(LocalServerChoice choice, string accountServiceUrl)
    { Choices = Array.AsReadOnly(new[] { choice }); AccountServiceUrl = accountServiceUrl; }
    internal static LocalServerSelection Open(LauncherAccountStore store)
    {
        var options = store.Load();
        return new(LocalServerChoice.From(options), options.ResolveAccountServiceUrl());
    }
    internal LauncherOptions Confirm(LauncherAccountStore store)
    {
        var options = store.Load(); var current = LocalServerChoice.From(options); var selected = Choices[0];
        if (!string.Equals(current.Host, selected.Host, StringComparison.OrdinalIgnoreCase) || current.Port != selected.Port)
            throw new InvalidOperationException("已保存的服务器连接发生变化，请重新选择并确认。");
        // Selection is read-only. Normalize the in-memory endpoint without persisting anything.
        options.EntranceHost = current.Host;
        return options;
    }
}
