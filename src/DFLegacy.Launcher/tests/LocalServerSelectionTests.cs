using DFLegacy.Launcher;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

internal static class LocalServerSelectionTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "server-choice.json");
        var store = new LauncherAccountStore(path);
        var options = new LauncherOptions { EntranceHost = "custom.example", EntrancePort = 2345,
            ClientPath = @"G:\custom\DNF.exe", Account = "keep", Password = "secret", AccountServiceUrl = "http://legacy.example:8091",
            Accounts = [new() { Account = "profile", Password = "profile-secret" }] };
        store.Save(options);
        var before = File.ReadAllBytes(path); var stamp = File.GetLastWriteTimeUtc(path);
        var selection = LocalServerSelection.Open(store);
        check(selection.Choices.Count == 1 && selection.Choices[0].Name == "本地服务器" && selection.Choices[0].Region == "telecom", "legacy endpoint provides one real telecom choice");
        check(selection.SelectedChoice is null, "opening requires explicit selection regardless of saved selected identity");
        try { selection.Confirm(store); check(false, "unselected confirmation is rejected"); } catch (InvalidOperationException) { }
        selection.Select(selection.Choices[0].Id);
        var confirmed = selection.Confirm(store);
        check(confirmed.Account == "keep" && confirmed.Accounts[0].Password == "profile-secret" && confirmed.ClientPath == options.ClientPath
            && confirmed.ResolveAccountServiceUrl() == options.AccountServiceUrl, "legacy confirm preserves account/display/API configuration");
        check(File.ReadAllBytes(path).SequenceEqual(before) && File.GetLastWriteTimeUtc(path) == stamp, "open/select/confirm never changes bytes or mtime");
        options.Servers = [new() { Id = "a", Name = "电信一区", Region = "telecom", Host = "2001:db8::1", Port = 2312, AccountServicePort = 8092 },
            new() { Id = "b", Name = "网通二区", Region = "netcom", Host = "other.example", Port = 3311, AccountServiceUrl = "http://api.other.example:8071/base" }];
        options.SelectedServerId = "a";
        store.Save(options);
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!;
        // Unknown keys at every persisted model boundary must survive ordinary settings edits.
        options.ExtensionData = new() { ["FutureSetting"] = JsonSerializer.SerializeToElement(new { nested = "preserve-me" }) };
        options.Accounts[0].ExtensionData = new() { ["FutureAccount"] = JsonSerializer.SerializeToElement(17) };
        options.Relay.ExtensionData = new() { ["FutureRelay"] = JsonSerializer.SerializeToElement(true) };
        options.Servers[0].ExtensionData = new() { ["FutureServer"] = JsonSerializer.SerializeToElement("keep-server") };
        store.Save(options);
        json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!;
        json["FutureSetting"] = JsonSerializer.SerializeToElement(new { nested = "preserve-me" });
        File.WriteAllText(path, JsonSerializer.Serialize(json)); before = File.ReadAllBytes(path);
        selection = LocalServerSelection.Open(store);
        check(selection.SelectedChoice is null && selection.Choices.Select(c => c.Region).SequenceEqual(new[] { "telecom", "netcom" }), "both configured regions load without implicit highlight");
        selection.Select("a"); confirmed = selection.Confirm(store);
        check(confirmed.EntranceHost == "2001:db8::1" && confirmed.EntrancePort == 2312 && confirmed.ResolveAccountServiceUrl() == "http://[2001:db8::1]:8092", "first server owns its IPv6 endpoint and independent account port");
        check(selection.SelectedChoice!.Endpoint == "[2001:db8::1]:2312", "IPv6 display adds brackets without rewriting the saved host");
        selection.Select("b"); confirmed = selection.Confirm(store);
        check(confirmed.EntranceHost == "other.example" && confirmed.ResolveAccountServiceUrl() == "http://api.other.example:8071/base" && confirmed.SelectedServerId == "b", "second server owns explicit account API and selected identity");
        check(File.ReadAllBytes(path).SequenceEqual(before), "multi-server confirmation preserves saved bytes");
        store.Update(o => o.Theme = "dark");
        check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("FutureSetting").GetProperty("nested").GetString() == "preserve-me", "normal settings update preserves unknown configuration fields");
        var preserved = store.Load();
        check(preserved.Accounts[0].ExtensionData!["FutureAccount"].GetInt32() == 17
            && preserved.Relay.ExtensionData!["FutureRelay"].GetBoolean()
            && preserved.Servers[0].ExtensionData!["FutureServer"].GetString() == "keep-server",
            "account, relay and per-server unknown fields survive normal store update");
        options.Servers[1].AccountServiceUrl = "http://changed.example"; store.Save(options); before = File.ReadAllBytes(path);
        try { selection.Confirm(store); check(false, "changed selected account target requires re-selection"); } catch (InvalidOperationException) { }
        check(File.ReadAllBytes(path).SequenceEqual(before), "changed target is never restored from cached snapshot");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { try { selection.Confirm(store); check(false, "locked config fails closed"); } catch (IOException) { } }
        var intactConfiguration=File.ReadAllBytes(path);
        File.WriteAllText(path, "{not-json"); before = File.ReadAllBytes(path);
        try { selection.Confirm(store); check(false, "corrupt saved config fails closed"); } catch (JsonException) { }
        check(File.ReadAllBytes(path).SequenceEqual(before), "corrupt config bytes remain untouched");
        File.WriteAllBytes(path,intactConfiguration);
        options.Servers[1].Id = "a"; store.Save(options);
        try { LocalServerSelection.Open(store); check(false, "duplicate identities are rejected"); } catch (InvalidDataException) { }
        options.Servers = []; options.EntrancePort = 65536; store.Save(options);
        try { LocalServerSelection.Open(store); check(false, "invalid legacy port is rejected"); } catch (InvalidDataException) { }
        options.EntrancePort = 2311; options.EntranceHost = ""; store.Save(options);
        try { LocalServerSelection.Open(store); check(false, "empty legacy endpoint is rejected"); } catch (InvalidDataException) { }
        ProbeChecks(check).GetAwaiter().GetResult();
        Console.WriteLine("Local server selection, preservation and real TCP latency checks executed");
    }

    private static async Task ProbeChecks(Action<bool, string> check)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var choice = new LocalServerChoice("fixture", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
        var accept = listener.AcceptTcpClientAsync();
        var result = await ServerConnectionProbe.ProbeAsync(choice, TimeSpan.FromSeconds(2), CancellationToken.None);
        using var accepted = await accept;
        check(result.Reachable && result.Elapsed is { TotalMilliseconds: >= 0 }, "real TCP listener reports measured connection duration");
        using var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        occupied.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var closed = choice with { Port = ((IPEndPoint)occupied.LocalEndPoint!).Port };
        var failed = await ServerConnectionProbe.ProbeAsync(closed, TimeSpan.FromSeconds(2), CancellationToken.None);
        check(!failed.Reachable && failed.Elapsed is null, "closed TCP endpoint has no invented latency");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await ServerConnectionProbe.ProbeAsync(choice, TimeSpan.FromSeconds(2), cancellation.Token); check(false, "cancelled TCP probe propagates cancellation"); }
        catch (OperationCanceledException) { }
    }
}
