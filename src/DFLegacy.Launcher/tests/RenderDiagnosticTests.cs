using DFLegacy.Launcher;
using System.Text.Json;

internal static class RenderDiagnosticTests
{
    internal static void Run(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "render-result");
        Directory.CreateDirectory(root);
        var client = Path.Combine(root, "DNF.exe");
        File.WriteAllBytes(client, []);
        check(ClientRenderDiagnostic.DescribeLatest(client).Contains("尚无"), "no runtime record does not claim requested mode is active");
        var folder = Path.Combine(root, "render-diagnostics");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "12345.json");
        var now = DateTime.UtcNow;
        string Record(string effective = "sharp", string reason = "ok") => JsonSerializer.Serialize(new
        {
            version = 1, pid = 12345, processStartFileTime = now.AddMinutes(-5).ToFileTimeUtc(),
            writtenFileTime = now.ToFileTimeUtc(), requested = "sharp", effective, reason,
            hresult = "0x00000000", logicalWidth = 640, logicalHeight = 480, width = 800, height = 600
        });
        File.WriteAllText(path, Record());
        var result = ClientRenderDiagnostic.DescribeLatest(client);
        check(result.Contains("清晰平滑") && result.Contains("800 × 600") && result.Contains("12345") && result.Contains("最近一次") && result.Contains("实时"), "runtime result identifies recent time/PID without asserting a live process");
        File.WriteAllText(path, Record("original-fallback", "output-chain"));
        result = ClientRenderDiagnostic.DescribeLatest(client);
        check(result.Contains("回退") && result.Contains("output-chain") && result.Contains("0x00000000"), "fallback result and reason are distinct from requested sharp setting");
        File.WriteAllText(path, Record("device-lost", "reset"));
        check(ClientRenderDiagnostic.DescribeLatest(client).Contains("设备丢失"), "device lost result remains visible");
        foreach (var malformed in new[] { "{", "[]", Record("invented"), Record().Replace("\"pid\":12345", "\"pid\":-1"), Record().Replace("\"version\":1", "\"version\":9"), Record().Replace("\"width\":800", "\"width\":-8"), new string('x', 65537) })
        {
            File.WriteAllText(path, malformed);
            check(ClientRenderDiagnostic.DescribeLatest(client).Contains("无法读取"), "malformed or unsupported diagnostic does not become a success result");
        }
        File.WriteAllText(path, Record());
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(ClientRenderDiagnostic.DescribeLatest(client).Contains("无法读取"), "locked runtime log cannot break configuration page");
        check(!File.Exists(Path.Combine(root, "Config.ini")), "reading diagnostics never creates or modifies configuration");
    }
}
