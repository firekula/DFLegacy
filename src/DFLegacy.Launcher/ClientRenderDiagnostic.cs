using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DFLegacy.Launcher;

internal static class ClientRenderDiagnostic
{
    internal static string DescribeLatest(string clientPath)
    {
        try
        {
            var resolved=Program.ResolveClientPath(clientPath);
            var folders = new[]{Path.Combine(ClientLogPaths.GetDirectory(resolved),"render-diagnostics"),Path.Combine(Path.GetDirectoryName(resolved)!,"render-diagnostics"),Path.Combine(ClientLogPaths.GetDirectory(resolved),"legacy","render-diagnostics")};
            var paths = folders.Where(p=>Directory.Exists(p) && !ClientLogPaths.HasLink(p)).SelectMany(p=>new DirectoryInfo(p).EnumerateFiles("*.json")
                .Where(f=>!ClientLogPaths.HasLink(f.FullName)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(32)).ToArray();
            foreach (var file in paths)
            {
                try
                {
                    if (file.Length is <= 0 or > 65536) continue;
                    using var stream = file.OpenRead();
                    using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
                    var r = json.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || r.GetProperty("version").GetInt32() != 1) continue;
                    var pid = r.GetProperty("pid").GetInt32();
                    var written = DateTime.FromFileTimeUtc(r.GetProperty("writtenFileTime").GetInt64());
                    var started = DateTime.FromFileTimeUtc(r.GetProperty("processStartFileTime").GetInt64());
                    if (pid <= 0 || written < DateTime.UnixEpoch || written > DateTime.UtcNow.AddMinutes(5) || started > written || started < DateTime.UnixEpoch) continue;
                    var requested = r.GetProperty("requested").GetString();
                    var effective = r.GetProperty("effective").GetString();
                    if (!IsMode(requested) || !(IsMode(effective) || effective is "original-fallback" or "device-lost" or "render-error")) continue;
                    var reason = r.GetProperty("reason").GetString() ?? "";
                    var hr = r.GetProperty("hresult").GetString() ?? "";
                    var width = r.GetProperty("width").GetInt32();
                    var height = r.GetProperty("height").GetInt32();
                    if (width is < 0 or > 8192 || height is < 0 or > 8192 || !Regex.IsMatch(reason, "\\A[a-z0-9-]{1,64}\\z") || !Regex.IsMatch(hr, "\\A0x[0-9A-Fa-f]{8}\\z")) continue;
                    var size = width > 0 && height > 0 ? $"，{width} × {height}" : "";
                    var detail = effective is "original-fallback" or "device-lost" or "render-error" ? $"；原因 {reason}（{hr}）" : "";
                    return $"最近一次运行：{written.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}，PID {pid}\n请求：{Label(requested)}；实际：{Label(effective)}{size}{detail}\n这是历史运行记录，不代表当前进程的实时状态。";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentOutOfRangeException) { }
            }
            return paths.Length == 0 ? "尚无运行记录。保存的设置在下次启动游戏时应用。" : "最近一次运行记录无法读取；保存的设置仍以下次启动为准。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return "最近一次运行记录无法读取：" + ex.Message; }
    }

    private static bool IsMode(string? value) => value is "sharp" or "point" or "linear" or "legacy";
    private static string Label(string? value) => value switch
    {
        "sharp" => "清晰平滑", "point" => "清晰", "linear" => "平滑", "legacy" => "旧版兼容",
        "original-fallback" => "原呈现回退", "device-lost" => "设备丢失", "render-error" => "渲染错误", _ => "未知"
    };
}
