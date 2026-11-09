using System.Reflection;
using DFLegacy.Launcher;

internal static class PvpRelayPortTests
{
    internal static void Run(Action<bool, string> check)
    {
        var method = typeof(LauncherOptions).Assembly.GetType("DFLegacy.Launcher.Program")!
            .GetMethod("BuildRelayPorts", BindingFlags.NonPublic | BindingFlags.Static)!;
        int[] Ports(string? configured, int? required = 2311) =>
            (int[])method.Invoke(null, [configured, new[] { 2311, 7001, 8080 }, required])!;
        var legacy = "2311,7001,7004,7005,7100,7101,7102,7103,7104,7105,8080";
        check(Ports(legacy).SequenceEqual(new[] { 2311,7001,7004,7005,7100,7101,7102,7103,7104,7105,7110,7111,7112,8080 }), "preserved legacy PvP TCP list reaches all four channels");
        check(Ports(legacy + ",45678").Contains(45678), "custom relay ports survive PvP supplementation");
        check(Ports(null).SequenceEqual(new[] { 2311,7001,8080 }), "non-PvP default list does not acquire PvP listeners");
        check(Ports("2311,7004", null).SequenceEqual(new[] { 2311,7004 }), "UDP list does not acquire TCP PvP ports");
        var disabled = new LauncherOptions { EntranceHost = "invalid.test" };
        disabled.Relay.Enabled = false;
        check(DFLegacy.Launcher.Program.StartRelayIfConfiguredAsync(disabled).GetAwaiter().GetResult() is null,
            "disabled relay returns without resolving a host or binding new ports");
    }
}
