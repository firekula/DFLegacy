using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.UpdateCore.Tests;

internal static class MigrationTests
{
    internal static ConfigMigration Migration(string operation, string key, string value, string? when = null) =>
        new("test-" + operation + "-" + key.Replace('.', '-'), "launcher.json", operation, key,
            JsonDocument.Parse(value).RootElement.Clone(), when == null ? null : JsonDocument.Parse(when).RootElement.Clone());

    internal static void Run()
    {
        // Reflection makes the first RED an actual missing-behavior assertion, not a compiler error.
        var type = typeof(ClientManifest).Assembly.GetType("DFLegacy.UpdateCore.ConfigurationMigration");
        TestRun.Check(type != null, "configuration migration implementation is available");
        byte[] Apply(byte[] json, ConfigMigration[] migrations)
        {
            try { return (byte[])type!.GetMethod("Apply")!.Invoke(null, [json, migrations])!; }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { throw (InvalidDataException)ex.InnerException; }
        }
        bool Satisfied(byte[] json, ConfigMigration[] migrations) => (bool)type!.GetMethod("IsSatisfied")!.Invoke(null, [json, migrations])!;
        var ports = Migration("unionPorts", "Relay.TcpPorts", "[7110,7111,7112]");
        var original = TestRun.Bytes("""{"accounts":[{"password":"secret","name":"player"}],"relay":{"tcpports":"9999,2311;9999 8080","Address":"custom"},"unknown":{"nested":42}}""");
        var changed = Apply(original, [ports]); var obj = JsonNode.Parse(changed)!;
        TestRun.Check(obj["accounts"]![0]!["password"]!.GetValue<string>() == "secret" && obj["unknown"]!["nested"]!.GetValue<int>() == 42, "credentials and unknown fields preserved");
        TestRun.Check(obj["relay"]!["tcpports"]!.GetValue<string>() == "9999,2311,8080,7110,7111,7112", "port merge preserves actual launcher string format, casing and valid custom ports, de-duplicates");
        TestRun.Check(changed.SequenceEqual(Apply(changed, [ports])) && Satisfied(changed, [ports]) && !Satisfied(original, [ports]), "migration is idempotent and postconditions are checked");
        var missing = JsonNode.Parse(Apply(TestRun.Bytes("{}"), [ports, Migration("unionPorts", "Relay.UdpPorts", "[7112]")]))!;
        TestRun.Check(missing["Relay"]!["TcpPorts"]!.GetValue<string>() == "2311,7001,8080,7110,7111,7112", "missing TCP ports expand old defaults");
        TestRun.Check(missing["Relay"]!["UdpPorts"]!.GetValue<string>() == "2311,7002,7003,7112", "missing UDP ports expand old defaults");
        TestRun.Check(JsonNode.Parse(Apply(TestRun.Bytes("""{"Relay":{"TcpPorts":null}}"""), [ports]))!["Relay"]!["TcpPorts"]!.GetValue<string>() == "2311,7001,8080,7110,7111,7112", "null TCP ports expand defaults");
        var defaults = Migration("setDefault", "Relay.Enabled", "true");
        var conditional = Migration("replaceIf", "Relay.Preference", "\"IPv6\"", "\"IPv4\"");
        TestRun.Check(Apply(TestRun.Bytes("{}"), [conditional]).SequenceEqual(TestRun.Bytes("{}")), "conditional migration with absent old value does not invent a relay section");
        var custom = Apply(TestRun.Bytes("""{"Relay":{"Enabled":false,"Preference":"Custom"}}"""), [defaults, conditional]);
        TestRun.Check(JsonNode.Parse(custom)!["Relay"]!["Enabled"]!.GetValue<bool>() == false && JsonNode.Parse(custom)!["Relay"]!["Preference"]!.GetValue<string>() == "Custom", "defaults and replaceIf preserve customized values");
        var converted = JsonNode.Parse(Apply(TestRun.Bytes("""{"Relay":{"Preference":"IPv4"}}"""), [defaults, conditional]))!;
        TestRun.Check(converted["Relay"]!["Enabled"]!.GetValue<bool>() && converted["Relay"]!["Preference"]!.GetValue<string>() == "IPv6", "missing defaults and matching conditional migration applied");
        foreach (var when in new[] { "[2311,7001]", "\"2311;7001\"" })
        {
            var portCondition = Migration("replaceIf", "Relay.TcpPorts", "[2311,7110]", when);
            var portChange = Apply(TestRun.Bytes("""{"Relay":{"TcpPorts":"2311,7001"}}"""), [portCondition]);
            TestRun.Check(JsonNode.Parse(portChange)!["Relay"]!["TcpPorts"]!.GetValue<string>() == "2311,7110" && Satisfied(portChange, [portCondition]), "conditional port conversion matches array or string precondition and produces valid launcher string");
        }
        TestRun.Reject(() => Apply(original, [Migration("setDefault", "Relay.Preference", "\"custom\"")]), "unsupported preference migration");
        foreach (var invalid in new[] { "{", "[]", "{\"Relay\":null}", "{\"Relay\":{},\"relay\":{}}", "{\"Relay\":{\"TcpPorts\":\"0\"}}", "{\"Relay\":{\"TcpPorts\":\"65536\"}}" })
            TestRun.Reject(() => Apply(TestRun.Bytes(invalid), [ports]), "malformed/ambiguous configuration retained");
        TestRun.Reject(() => Apply(original, [Migration("unionPorts", "Relay.TcpPorts", "[0]")]), "invalid new port");
        TestRun.Reject(() => Apply(original, [Migration("setDefault", "Accounts.Password", "\"bad\"")]), "credential field migration");
        var binding = Migration("bindLocalBridge", "UpdateSource",
            """{"provider":"local","repository":"http://127.0.0.2:40123/c1","channel":"c1-bridge","architecture":"x64","flavor":"normal"}""",
            "\"http://127.0.0.2:40123\"");
        var bindingManifest = ManifestTests.Sample() with { Migrations = [binding] };
        var parsedBinding = ManifestCodec.Parse(ManifestCodec.Serialize(bindingManifest)).Migrations.Single();
        TestRun.Check(parsedBinding.Operation == "bindLocalBridge" && parsedBinding.Key == "UpdateSource",
            "local enrollment binding survives actual release manifest parsing");
        TestRun.Reject(() => ManifestCodec.Parse(ManifestCodec.Serialize(bindingManifest with
            { Migrations = [binding with { Key = "Accounts.Password" }] })), "manifest rejects private field binding");
        TestRun.Reject(() => ManifestCodec.Parse(ManifestCodec.Serialize(bindingManifest with
            { Migrations = [binding with { When = JsonDocument.Parse("\"http://127.0.0.1:40123\"").RootElement.Clone() }] })),
            "manifest rejects binding to mismatched legacy origin");
        var selected = TestRun.Bytes("""{"UpdateServiceUrl":"http://127.0.0.2:40123","Account":"test","Password":"secret","Unknown":7}""");
        var enrolled = Apply(selected, [binding]);
        var settings = JsonNode.Parse(enrolled)!;
        TestRun.Check(settings["UpdateProvider"]!.GetValue<string>() == "local"
            && settings["UpdateRepository"]!.GetValue<string>() == "http://127.0.0.2:40123/c1"
            && settings["UpdateChannel"]!.GetValue<string>() == "c1-bridge"
            && settings["UpdateFlavor"]!.GetValue<string>() == "normal"
            && settings["UpdateServiceUrl"]!.GetValue<string>() == ""
            && settings["Password"]!.GetValue<string>() == "secret" && settings["Unknown"]!.GetValue<int>() == 7
            && Satisfied(enrolled, [binding]), "local enrollment atomically updates only updater keys and is idempotent");
        TestRun.Reject(() => Apply(original, [binding]), "local enrollment without selected hosted origin");
        TestRun.Reject(() => Apply(TestRun.Bytes("""{"UpdateServiceUrl":"http://127.0.0.2:40123","UpdateChannel":"different"}"""), [binding]),
            "local enrollment overriding custom channel");
    }
}
