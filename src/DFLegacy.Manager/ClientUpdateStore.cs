using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Manager;

public sealed record ClientUpdateRelease(string Version, long Id, DateTimeOffset PublishedAt, long Size, string Sha256);

public sealed class ClientUpdateStore
{
    public const int Port = 18083;
    private static readonly DateTimeOffset ZipTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _directory;
    public string PublicUrl { get; }

    public ClientUpdateStore(ManagerPaths paths, string publicUrl)
    {
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.PathAndQuery != "/" || uri.Fragment.Length != 0)
            throw new ArgumentException("DFLEGACY_UPDATE_PUBLIC_URL 必须为 HTTP 或 HTTPS 地址，可带端口。");
        PublicUrl = uri.GetLeftPart(UriPartial.Authority);
        _directory = Path.Combine(paths.Data, "client-updates");
    }

    private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool IsFlavor(string flavor) => flavor is "normal" or "test";

    private string IndexPath(string flavor) => Path.Combine(_directory, flavor + ".json");
    private string PackagePath(string arch, string flavor, string hash) => Path.Combine(_directory, $"{arch}-{flavor}-{hash}.zip");

    private Dictionary<string, ClientUpdateRelease> ReadIndex(string flavor)
    {
        try
        {
            using var index = File.Open(IndexPath(flavor), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return JsonSerializer.Deserialize<Dictionary<string, ClientUpdateRelease>>(index) ?? [];
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    public ClientUpdateRelease? Latest(string arch, string flavor) =>
        (arch is "x86" or "x64") && IsFlavor(flavor) ? ReadIndex(flavor).GetValueOrDefault(arch) : null;

    public string? Package(string arch, string flavor, string hash)
    {
        if (arch is not ("x86" or "x64") || !IsFlavor(flavor) || !IsHash(hash)) return null;
        var path = PackagePath(arch, flavor, hash);
        return File.Exists(path) ? path : null;
    }

    internal async Task PublishAsync(string x86, string x64, bool testBuild, CancellationToken token)
    {
        var flavor = testBuild ? "test" : "normal";
        Directory.CreateDirectory(_directory);
        var previous = ReadIndex(flavor);
        var now = DateTimeOffset.UtcNow;
        var id = Math.Max(now.ToUnixTimeMilliseconds(), previous.Values.Select(release => release.Id).DefaultIfEmpty(0).Max() + 1);
        var next = new Dictionary<string, ClientUpdateRelease>();
        foreach (var (arch, path) in new[] { ("x86", x86), ("x64", x64) })
        {
            var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using var installer = ZipFile.OpenRead(path);
                using var manifestReader = installer.GetEntry("client-package.json")!.Open();
                using var manifest = await JsonDocument.ParseAsync(manifestReader, cancellationToken: token);
                using (var update = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    foreach (var row in manifest.RootElement.GetProperty("files").EnumerateArray())
                    {
                        var name = row.GetProperty("path").GetString()!;
                        await using var source = installer.GetEntry(name)!.Open();
                        var entry = update.CreateEntry(name, CompressionLevel.Fastest);
                        entry.LastWriteTime = ZipTimestamp;
                        await using var destination = entry.Open();
                        await source.CopyToAsync(destination, token);
                    }
                    var manifestEntry = update.CreateEntry("update-manifest.json");
                    manifestEntry.LastWriteTime = ZipTimestamp;
                    await using var target = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(target, manifest.RootElement, cancellationToken: token);
                }
                string hash;
                long size;
                await using (var content = File.OpenRead(temporary))
                {
                    size = content.Length;
                    hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, token));
                }
                var final = PackagePath(arch, flavor, hash);
                if (!File.Exists(final)) File.Move(temporary, final);
                next.Add(arch, new(manifest.RootElement.GetProperty("version").GetString()!, id, now, size, hash));
            }
            finally { File.Delete(temporary); }
        }
        token.ThrowIfCancellationRequested();
        if (next.All(item => previous.GetValueOrDefault(item.Key)?.Sha256 == item.Value.Sha256)) return;
        AtomicFile.Write(IndexPath(flavor), JsonSerializer.Serialize(next));
    }

    internal IResult Serve(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) return Results.NotFound();
        var parts = context.Request.Path.Value?.Split('/');
        if (parts is not ["", "v1", var arch, var flavor, var resource]) return Results.NotFound();
        if (resource == "latest") return Latest(arch, flavor) is { } release ? Results.Ok(release) : Results.NotFound();
        return resource.EndsWith(".zip", StringComparison.Ordinal) && Package(arch, flavor, resource[..^4]) is { } path
            ? Results.File(path, "application/zip", enableRangeProcessing: true) : Results.NotFound();
    }
}
