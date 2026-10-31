using DFLegacy.Launcher;

internal static class LocalServerSelectionTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "server-choice.json");
        var store = new LauncherAccountStore(path);
        var options = new LauncherOptions { EntranceHost = "custom.example", EntrancePort = 2345, ClientPath = @"G:\custom\DNF.exe",
            Account = "keep-account", Password = "keep-password", AccountServiceUrl = "http://account.example:8081", StartServer = false,
            Accounts = [new LauncherAccountProfile { Account = "profile", Password = "profile-password" }] };
        store.Save(options); var before = File.ReadAllBytes(path); var stamp = File.GetLastWriteTimeUtc(path);
        var selection = LocalServerSelection.Open(store);
        check(selection.Choices.Count == 1 && selection.Choices[0].Name == "本地服务器", "server page exposes exactly one configured local entry");
        check(selection.Choices[0].Host == "custom.example" && selection.Choices[0].Port == 2345, "local entry preserves custom saved endpoint");
        var confirmed = selection.Confirm(store);
        check(confirmed.Account == options.Account && confirmed.Password == options.Password && confirmed.Accounts[0].Password == options.Accounts[0].Password
            && confirmed.ClientPath == options.ClientPath && confirmed.AccountServiceUrl == options.AccountServiceUrl,
            "server selection preserves client/account/API configuration");
        check(File.ReadAllBytes(path).SequenceEqual(before) && File.GetLastWriteTimeUtc(path) == stamp, "open/confirm performs no config write or mtime update");
        options.EntranceHost = "2001:db8::1"; store.Save(options);
        var external = File.ReadAllBytes(path);
        try { selection.Confirm(store); check(false, "changed saved endpoint requires re-selection"); }
        catch (InvalidOperationException) { }
        check(File.ReadAllBytes(path).SequenceEqual(external), "stale selection never restores old endpoint bytes");
        var ipv6 = LocalServerSelection.Open(store);
        check(ipv6.Choices[0].Endpoint == "[2001:db8::1]:2345" && ipv6.Confirm(store).EntranceHost == "2001:db8::1", "IPv6 endpoint display is bracketed without rewriting host");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { ipv6.Confirm(store); check(false, "locked store cannot silently confirm stale connection"); }
            catch (IOException) { }
        }
        File.WriteAllText(path, "{not-json"); var broken = File.ReadAllBytes(path);
        try { ipv6.Confirm(store); check(false, "malformed store cannot silently confirm cached connection"); }
        catch (System.Text.Json.JsonException) { }
        check(File.ReadAllBytes(path).SequenceEqual(broken), "malformed store remains untouched");
        options.EntranceHost = ""; store.Save(options);
        try { LocalServerSelection.Open(store); check(false, "empty saved endpoint is rejected"); } catch (InvalidDataException) { }
        options.EntranceHost = "127.0.0.1"; options.EntrancePort = 65536; store.Save(options);
        try { LocalServerSelection.Open(store); check(false, "invalid saved port is rejected"); } catch (InvalidDataException) { }
        Console.WriteLine("Local server real-store selection checks executed");
    }
}
