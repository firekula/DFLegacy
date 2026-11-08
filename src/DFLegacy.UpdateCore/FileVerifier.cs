using System.Security.Cryptography;

namespace DFLegacy.UpdateCore;

public sealed record FileProblem(string Path, string Reason, string ExpectedSha256, string? ActualSha256, long ExpectedSize, long? ActualSize);
public sealed record VerificationResult(IReadOnlyList<FileProblem> Problems)
{
    public bool IsValid => Problems.Count == 0;
}
public static class FileVerifier
{
    public static async Task<VerificationResult> VerifyAsync(string root, ClientManifest manifest,
        IProgress<UpdateProgress>? progress = null, CancellationToken token = default)
    {
        ManifestCodec.Validate(manifest);
        var problems = new List<FileProblem>(); var completed = 0;
        var total = manifest.Files.Aggregate(0L, (sum, f) => checked(sum + f.Size));
        long read = 0;
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            var path = PathPolicy.Resolve(root, file.Path);
            string? actualHash = null; long? actualSize = null;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                actualSize = stream.Length;
                // Hash even when lengths differ: the report is evidence, not a timestamp/size shortcut.
                actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
                if (actualSize != file.Size || actualHash != file.Sha256)
                    problems.Add(new(file.Path, "content-mismatch", file.Sha256, actualHash, file.Size, actualSize));
            }
            catch (FileNotFoundException) { problems.Add(new(file.Path, "missing", file.Sha256, null, file.Size, null)); }
            catch (DirectoryNotFoundException) { problems.Add(new(file.Path, "missing", file.Sha256, null, file.Size, null)); }
            read = checked(read + file.Size);
            progress?.Report(new("verify", file.Path, ++completed, manifest.Files.Length, read, total));
        }
        return new(problems);
    }
    public static async Task<bool> MatchesAsync(string path, long size, string hash, CancellationToken token = default)
    {
        PathPolicy.NoLinks(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return stream.Length == size && Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) == hash;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
