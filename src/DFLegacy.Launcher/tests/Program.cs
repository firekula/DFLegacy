using DFLegacy.Launcher;
using System.Runtime.InteropServices;
using System.Text;

var failures = 0;
void Check(bool condition, string message)
{
    if (condition) return;
    Console.Error.WriteLine("FAIL: " + message);
    failures++;
}

var root = Path.Combine(Path.GetTempPath(), "dflegacy-launcher-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new LauncherAccountStore(Path.Combine(root, "launcher.json"));
    var fresh = store.Load();
    Check(fresh.Account == "" && fresh.Password == "" && fresh.Accounts.Count == 0,
        "a missing release profile must not create test/test credentials");
    File.WriteAllText(Path.Combine(root, "launcher.json"), """
        {"Account":"","Password":"","Accounts":[],"StartServer":false}
        """);
    var empty = store.Load();
    try
    {
        DFLegacy.Launcher.Program.ValidateOptions(empty);
    }
    catch (Exception ex)
    {
        Check(false, "empty saved credentials must allow UI-entered login: " + ex.Message);
    }
    Check(empty.GetSelectedAccount().Account == "" && empty.GetSelectedAccount().Password == "",
        "empty saved profile must remain empty");
    store.Save(empty);
    Check(!File.ReadAllText(Path.Combine(root, "launcher.json")).Contains("test"),
        "saving an empty profile must not generate a test account");
    try
    {
        _ = DFLegacy.Launcher.Program.BuildLauncherData(empty);
        Check(false, "headless launch must reject absent login credentials");
    }
    catch (InvalidDataException) { }
    var remembered = new LauncherOptions { Account = "player", Password = "secret" };
    Check(DFLegacy.Launcher.Program.BuildLauncherData(remembered).StartsWith("99?127.0.0.1?2311?player?secret?"),
        "existing remembered credentials still create launcher data");

    // Installing a release patch needs no saved login, and must propagate the
    // same resolved URL that the normal launch flow uses before starting DNF.
    var launcherConfig = Path.Combine(AppContext.BaseDirectory, "launcher.json");
    var originalLauncherConfig = File.Exists(launcherConfig) ? File.ReadAllBytes(launcherConfig) : null;
    try
    {
        var clientDirectory = Path.Combine(root, "headless-install");
        Directory.CreateDirectory(clientDirectory);
        var clientPath = Path.Combine(clientDirectory, "DNF.exe");
        File.WriteAllBytes(clientPath, []);
        File.WriteAllBytes(Path.Combine(clientDirectory, "Script.pvf"), []);
        File.WriteAllText(launcherConfig, System.Text.Json.JsonSerializer.Serialize(new LauncherOptions
        {
            ClientPath = clientPath, Account = "", Password = "", Accounts = [],
            AccountServiceUrl = "https://remote.example/service", StartServer = false
        }));
        var exitCode = await DFLegacy.Launcher.Program.RunHeadlessAsync(["--install-ijl15", "--no-dialog"]);
        Check(exitCode == 0, "patch installation accepts an empty release account list");
        var installedConfig = Path.Combine(clientDirectory, "Config.ini");
        Check(File.Exists(installedConfig) && File.ReadAllText(installedConfig).Contains("ClientServiceUrl=https://remote.example/service"),
            "headless install propagates the resolved account service URL");
    }
    finally
    {
        if (originalLauncherConfig is null) File.Delete(launcherConfig);
        else File.WriteAllBytes(launcherConfig, originalLauncherConfig);
    }

    {
        // Exercise the real installer, using its PE validation and packaged native DLL.
        var client = Path.Combine(root, "DNF.exe");
        File.WriteAllBytes(client, []);
        var config = Path.Combine(root, "Config.ini");
        const string customized = "; 用户自定义\r\n[Resolution]\r\nWidth=1234\r\n[Network]\r\nOther=keep\r\nClientServiceUrl=http://old.example\r\n[Custom]\r\nValue=保留\r\n";
        File.WriteAllText(config, customized);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api///");
        var text = File.ReadAllText(config);
        Check(text.Contains("ClientServiceUrl=https://game.example:8443/api"), "installer writes the remote URL");
        Check(text.Contains("Width=1234") && text.Contains("Other=keep") && text.Contains("Value=保留") && text.Contains("; 用户自定义"),
            "installer preserves unrelated customized INI content");
        File.WriteAllText(config, "[Network]\r\nOther=keep\r\n[Custom]\r\nValue=保留\r\n[Network]\r\nClientServiceUrl=http://old.example\r\n");
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        var effective = new StringBuilder(1100);
        _ = NativeProfile.GetPrivateProfileString("Network", "ClientServiceUrl", "", effective, effective.Capacity, config);
        Check(effective.ToString() == "https://game.example:8443/api", "duplicate INI sections cannot shadow the installed URL");
        var ansi = Encoding.Latin1.GetBytes("; legacy ANSI \u0081\u0040\r\n[Custom]\r\nValue=\u00fe\u00ff\r\n");
        File.WriteAllBytes(config, ansi);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        Check(File.ReadAllBytes(config).AsSpan().StartsWith(ansi), "legacy ANSI custom bytes are preserved");
        File.WriteAllText(config, customized, Encoding.Unicode);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        Check(File.ReadAllBytes(config).AsSpan().StartsWith(Encoding.Unicode.GetPreamble())
            && File.ReadAllText(config).Contains("Value=保留"), "UTF-16 BOM and custom text are preserved");
        effective.Clear();
        _ = NativeProfile.GetPrivateProfileString("Network", "ClientServiceUrl", "", effective, effective.Capacity, config);
        Check(effective.ToString() == "https://game.example:8443/api", "native profile API reads updated Unicode INI");
        File.WriteAllText(config, customized);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        text = File.ReadAllText(config);
        var before = text;
        foreach (var invalid in new[] { "ftp://game.example", "https://user:password@game.example", "https://game.example/%s", "https://game.example?x=1", "https://game.example\n[Features]", "https://game.example:70000" })
        {
            try
            {
                Ijl15ReplacementInstaller.Install(client, invalid);
                Check(false, "installer rejects an invalid URL: " + invalid);
            }
            catch (InvalidDataException) { }
            Check(File.ReadAllText(config) == before, "invalid URL must not alter the existing config");
        }
        var boundary = "https://example.org/" + new string('a', 980);
        Ijl15ReplacementInstaller.Install(client, boundary);
        Check(File.ReadAllText(config).Contains("ClientServiceUrl=" + boundary), "1000 byte URL base is accepted");
        try
        {
            Ijl15ReplacementInstaller.Install(client, boundary + "a");
            Check(false, "1001 byte URL base must be rejected");
        }
        catch (InvalidDataException) { }
        File.Delete(config);
        Ijl15ReplacementInstaller.Install(client, "http://game.example:8081");
        Check(File.ReadAllText(config).Contains("ClientServiceUrl=http://game.example:8081"),
            "first installation writes the URL before game launch");
        var resolved = new LauncherOptions { EntranceHost = "2001:db8::1", AccountServicePort = 8081 };
        Check(resolved.ResolveAccountServiceUrl() == "http://[2001:db8::1]:8081", "IPv6 account service base uses URL brackets");
    }
}
finally { Directory.Delete(root, recursive: true); }
Console.WriteLine(failures == 0 ? "Launcher configuration tests passed" : $"Launcher configuration tests failed: {failures}");
return failures == 0 ? 0 : 1;

internal static class NativeProfile
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPrivateProfileStringW")]
    internal static extern uint GetPrivateProfileString(string section, string key, string fallback,
        StringBuilder output, int capacity, string path);
}
