namespace DFLegacy.UpdateCore;

/// <summary>Only hash/length-addressed files under this directory are owned by the cache.</summary>
public sealed class ContentCache
{
    public string DirectoryPath { get; }
    public ContentCache(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory); PathPolicy.NoLinks(DirectoryPath); Directory.CreateDirectory(DirectoryPath);
    }
    public string ObjectPath(string hash, long size)
    {
        if (!ManifestCodec.IsHash(hash) || size < 0) throw new InvalidDataException("缓存身份无效。");
        var path = Path.Combine(DirectoryPath, hash + "-" + size + ".blob"); PathPolicy.NoLinks(path); return path;
    }
    public string PartialPath(ResourcePart part) => ObjectPath(part.Sha256, part.Size) + ".partial";
    internal async Task<FileStream> LockAsync(string path, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            token.ThrowIfCancellationRequested(); PathPolicy.NoLinks(path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < until) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    public async Task<string> MaterializeAsync(ManagedFile file, ResourceDownloader downloader,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        var target = ObjectPath(file.Sha256, file.Size);
        if (await FileVerifier.MatchesAsync(target, file.Size, file.Sha256, token).ConfigureAwait(false)) return target;
        var parts = new List<string>();
        foreach (var part in file.Parts) parts.Add(await downloader.FetchAsync(part, this, progress, token).ConfigureAwait(false));
        if (parts.Count == 1 && parts[0] == target) return target;
        using var lease = await LockAsync(target + ".lock", token).ConfigureAwait(false);
        if (await FileVerifier.MatchesAsync(target, file.Size, file.Sha256, token).ConfigureAwait(false)) return target;
        var temporary = target + ".assembling"; PathPolicy.NoLinks(temporary);
        try
        {
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                foreach (var path in parts)
                {
                    PathPolicy.NoLinks(path);
                    using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
                    await source.CopyToAsync(output, token).ConfigureAwait(false);
                }
                output.Flush(true);
            }
            if (!await FileVerifier.MatchesAsync(temporary, file.Size, file.Sha256, token).ConfigureAwait(false))
                throw new InvalidDataException("分卷合并后校验失败：" + file.Path);
            PathPolicy.NoLinks(target); File.Move(temporary, target, true);
            return target;
        }
        finally { PathPolicy.NoLinks(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
