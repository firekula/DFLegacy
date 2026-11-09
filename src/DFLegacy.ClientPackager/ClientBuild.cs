using System.Diagnostics;
using System.Text.Json;
using DFLegacy.Manager;

namespace DFLegacy.ClientPackager;

internal static class ClientBuild
{
    internal static async Task<string> RunAsync(string root, string output, DeploymentConfig config,
        bool testBuild, bool publish, IProgress<ClientExportProgress> progress)
    {
        root = Path.GetFullPath(root);
        output = Path.GetFullPath(output);
        var assets = Path.Combine(root, "client-assets");
        if (!File.Exists(Path.Combine(assets, "launcher-x86", "DFLegacy.Launcher.exe")))
            throw new InvalidDataException("请选择包含双架构 client-assets 的发行目录。");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "manifest.json")));
        var version = manifest.RootElement.GetProperty("version").GetString() ?? "";
        var commit = manifest.RootElement.GetProperty("commit").GetString() ?? "";
        if (version.Length is 0 or > 100 || version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '+')))
            throw new InvalidDataException("客户端资源版本号格式无效。");

        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DFLegacy", "ClientPackager");
        var paths = new ManagerPaths(root, cache);
        Directory.CreateDirectory(output);
        var destination = Path.Combine(output, $"DFLegacy-{version}-{(testBuild ? "test" : "normal")}-{Guid.NewGuid():N}.zip");
        var lastReport = Stopwatch.StartNew();
        var zip = await new ClientPackageExporter(paths, allowUnpackagedAssets: true).ExportPairAsync(config, testBuild, null, CancellationToken.None, value =>
        {
            if (value.Stage == "ready" || lastReport.ElapsedMilliseconds >= 100)
            {
                progress.Report(value);
                lastReport.Restart();
            }
        });
        try
        {
            File.Copy(zip, destination + ".tmp");
            File.Move(destination + ".tmp", destination);
        }
        finally
        {
            File.Delete(destination + ".tmp");
            File.Delete(zip);
        }
        progress.Report(new("ready", 0, 0, $"安装包已生成：{destination}"));
        if (!publish) return destination;

        try
        {
            var updates = Path.Combine(output, Path.GetFileNameWithoutExtension(destination) + "-updates");
            progress.Report(new("release", 0, 0, "正在校验 GitHub 发布权限并生成四个更新包…"));
            await RunPythonAsync("github_publish.py", ["--preflight"]);
            await RunPythonAsync("release.py", ["build-client-packages", "--updates-only", "--client-assets", assets,
                "--output", updates, "--version", version, "--commit", commit]);
            progress.Report(new("release", 0, 0, "正在上传 GitHub 草稿并校验附件，完成后自动公开…"));
            var args = new List<string> { "--updates-only", "--version", version, "--commit", commit };
            foreach (var arch in new[] { "x64", "x86" })
            foreach (var flavor in new[] { "", "-test" })
            {
                args.Add("--archive");
                args.Add(Path.Combine(updates, $"DFLegacy-client-update-v2{flavor}-win-{arch}.zip"));
            }
            await RunPythonAsync("github_publish.py", args);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"安装包已生成：{destination}\nGitHub 发布失败：{error.Message}", error);
        }
        return destination;
    }

    internal static Task<string> RunV4Async(string command, string location, string? output = null, string? targetCommit = null)
    {
        if (command is not ("preview" or "build" or "verify" or "publish"))
            throw new ArgumentException("未知 V4 操作。");
        var args = new List<string> { command, command is "preview" or "build" ? "--request" : "--bundle", location };
        if (command == "build") { args.Add("--output"); args.Add(output ?? throw new ArgumentException("缺少输出目录。")); }
        if (command == "publish") { args.Add("--target-commitish"); args.Add(targetCommit ?? throw new ArgumentException("缺少公开仓库提交。")); }
        return RunPythonAsync("update_v4.py", args);
    }

    private static async Task<string> RunPythonAsync(string script, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo("py") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-3.12");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "tools", "release", script));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        Process? running;
        try { running = Process.Start(start); }
        catch (System.ComponentModel.Win32Exception launchError)
        {
            throw new IOException("无法启动 Python 3.12，请确认 Windows 的 py -3.12 命令可用。", launchError);
        }
        using var process = running ?? throw new IOException("无法启动 Python 3.12。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new IOException(string.IsNullOrWhiteSpace(error) ? $"{script} 退出码 {process.ExitCode}" : error.Trim());
        return result;
    }
}
