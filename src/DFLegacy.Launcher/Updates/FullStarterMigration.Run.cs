using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal static partial class FullStarterMigration
{
    internal const string BridgeVersion = "v1.0.13-bridge";
    private const string TargetVersion = "v1.0.13";
    private const string TargetBuild = "a4f6744b29506fd3e1487bf6fa5844fb163e1a33";
    private const string BridgeEntrySha256 = "ebe2fbc158df3a70db7ffdc67de49f204fadfcfa6a1100b9d19f14ee149d7804";
    private const string TargetEntrySha256 = "cf16fedc2b310214398caad1be8a5d60f3dce2a7be5f749b70a06e177bc345dc";
    private const string TargetManifestSha256 = "a1574a2daf4ca896a5c48250008bdc8e4479828edd3bc915d5c1b86cfa334ccd";
    private const string StarterName = "DFLegacy-ChapterTwo-v1.0.13-win-x64.zip";

    internal sealed record Package(Core.ReleaseSource Source, string Sha256, long Size, Core.ReleaseSource? Loopback = null)
    {
        internal Core.ReleaseSource InstalledSource => Loopback is null ? Source : Loopback with { Repository = Loopback.Repository + "/installed" };
    }

    internal static Package? FindPackage(string root)
    {
        var receipt = new Core.InstallationStore(root).ReadInstalled();
        return receipt is null ? null : FindPackage(receipt.Release);
    }

    internal static Package? FindPackage(Core.VerifiedRelease bridge)
    {
        if (bridge.Tag != BridgeVersion || bridge.Source is not { Channel: "chapter-two", Architecture: "x64", Flavor: "normal" })
            return null;
        bridge.Validate(bridge.Source);
        var manifest = bridge.Manifest;
        if (manifest.Sequence != 19 || manifest.MinimumSequence != 17
            || manifest.Files.SingleOrDefault(file => file.Path == BootstrapLayout.EntryName)?.Sha256 != BridgeEntrySha256)
            return null;
        var provider = bridge.Source.Provider;
        Core.ReleaseSource? local = null;
        if (!Core.ChapterTransitionSource.IsOfficialChapterTwo(bridge.Source))
        {
            if (provider != "local") return null;
            var feed = new Uri(bridge.Source.Repository);
            provider = feed.AbsolutePath switch { "/github" => "github", "/gitcode" => "gitcode", _ => "" };
            local = bridge.Source;
        }
        var target = bridge.Source with { Provider = provider, Repository = "firekula/DFLegacy" };
        return provider switch
        {
            "github" => new(target, "7ad9d33a3c6ae224555b87b46a7a977decbf44e29bbc26b50b091cb97b751996", 1135865180, local),
            "gitcode" => new(target, "54c047d72a686704f982c332ef8c9b851948efed40f43440d6829d0b86e0f024", 1135865182, local),
            _ => null
        };
    }

    internal static async Task<bool> RunAsync(string root, Package package, bool networkDisabled,
        string[] arguments, IProgress<Core.UpdateProgress>? progress, CancellationToken token)
    {
        try
        {
            if (ReadActive(root) is { } current)
            {
                RequireTarget(current, package, original: false);
                if (!IsOpen(current))
                    await LaunchAsync(current, arguments, null, token).ConfigureAwait(false);
                return true;
            }
        }
        catch (Exception error) when (CanRestoreOldLauncher(error))
        {
            Deactivate(root);
            throw;
        }

        if (networkDisabled) return false;
        var attempt = CreateAttempt(root);
        var keep = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(45));
            using var http = Core.ReleaseProvider.CreateHttpClient();
            using var response = await OpenPackageAsync(http, package, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != package.Size
                || response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                throw new InvalidDataException("完整启动包的下载响应与发行附件不符。");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var candidate = await ImportAsync(input, attempt, package.Size, package.Sha256, progress, timeout.Token).ConfigureAwait(false);
            RequireTarget(candidate, package, original: true);
            if (package.Loopback != null) BindLoopback(candidate, package.InstalledSource);
            using var maintenance = Core.ClientGameLease.EnterMaintenance(root);
            CopySettings(root, candidate);
            progress?.Report(new("install", "完整启动包", 1, 1, Detail: "正在检查新版登录器能否启动…"));
            await ActivateAsync(root, candidate, (confirm, cancel) => LaunchAsync(candidate, arguments, confirm, cancel), token).ConfigureAwait(false);
            keep = true;
            Program.WriteLauncherLog("Chapter Two full starter migration confirmed v1.0.13.");
            return true;
        }
        catch (BootstrapChildStillRunningException)
        {
            keep = true;
            throw;
        }
        finally
        {
            if (!keep)
            {
                try
                {
                    Core.PathPolicy.NoLinks(attempt);
                    Directory.Delete(attempt, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static Task<HttpResponseMessage> OpenPackageAsync(HttpClient http, Package package, CancellationToken token)
    {
        if (package.Loopback is {} local)
            return http.GetAsync(new Uri(local.Repository + "/releases/" + TargetVersion + "/" + StarterName),
                HttpCompletionOption.ResponseHeadersRead, token);
        return new Core.ReleaseProvider(http, package.Source).OpenAsync(
            new Core.ResourcePart(package.Sha256, package.Size, TargetVersion, StarterName), 0, null, token);
    }

    private static void RequireTarget(string directory, Package package, bool original)
    {
        var installed = new Core.InstallationStore(directory).ReadInstalled()
            ?? throw new InvalidDataException("完整启动包缺少安装记录。");
        var release = installed.Release;
        release.Validate(original ? package.Source : package.InstalledSource);
        var manifest = release.Manifest;
        if (manifest.Sequence < 19 || manifest.Files.SingleOrDefault(file => file.Path == BootstrapLayout.EntryName)?.Sha256 != TargetEntrySha256
            || original && release.Tag != TargetVersion
            || release.Tag == TargetVersion && (release.ManifestSha256 != TargetManifestSha256 || manifest.Commit != TargetBuild))
            throw new InvalidDataException("完整启动包与批准的目标版本不符。");
        BootstrapLayout.RequireReceiptLayout(directory, package.Source.Architecture, installed);
        BootstrapLayout.RequireStableEntry(directory, manifest);
        // Verify the App before it is allowed to execute and recheck the full installation.
        var app = manifest.Files.Single(file => file.Path == BootstrapLayout.AppName);
        if (!Core.FileVerifier.MatchesAsync(Core.PathPolicy.Resolve(directory, app.Path), app.Size, app.Sha256).GetAwaiter().GetResult())
            throw new InvalidDataException("完整启动包中的登录器校验失败。");
    }

    private static bool IsOpen(string directory)
    {
        try { using var lease = BootstrapLayout.AcquireAppLock(directory); return false; }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return true; }
    }

    private static Task LaunchAsync(string directory, string[] arguments, Action? confirmed, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.Combine(directory, BootstrapLayout.AppName))
        {
            WorkingDirectory = directory,
            UseShellExecute = false
        };
        start.ArgumentList.Add("--bootstrap-verified");
        start.ArgumentList.Add("--");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return BootstrapUpdateClient.LaunchAndConfirmAsync(start, token, confirmed, readinessTimeout: TimeSpan.FromMinutes(2));
    }

    internal static bool CanRestoreOldLauncher(Exception error) =>
        error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or TimeoutException
            or JsonException or HttpRequestException or OperationCanceledException or System.ComponentModel.Win32Exception;

    private static void BindLoopback(string candidate, Core.ReleaseSource source)
    {
        // A client explicitly installed against a loopback feed must keep using
        // the local feed after importing a pinned mirror's ZIP.
        var store = new Core.InstallationStore(candidate);
        var receipt = store.ReadInstalled() ?? throw new InvalidDataException("完整启动包缺少安装记录。");
        var release = receipt.Release with { Source = source };
        release.Validate(source);
        var path = Path.Combine(store.StateDirectory, "installed.json");
        Core.PathPolicy.NoLinks(path);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(receipt with { Release = release }, Core.ManifestCodec.Json));
    }
}
