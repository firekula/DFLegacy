using System.Text.Json.Serialization;
namespace DFLegacy.Launcher.Updates;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFile(string Path, string Sha256, long Size);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateManifest(int SchemaVersion, string Version, string Commit, UpdateFile[] Files, string Flavor)
{
    internal bool HasScriptPvf => Files.Any(file => file.Path == ClientPackageManifest.ScriptPvfName);
}
public sealed record UpdateRelease(long Id, string Version, DateTimeOffset PublishedAt, bool Prerelease, Uri DownloadUrl, long Size, string Sha256);
public sealed record UpdateProgress(long BytesReceived, long TotalBytes, double BytesPerSecond);
internal sealed record UpdateInstallProgress(double Fraction,string Message)
{
    internal static void Report(IProgress<UpdateInstallProgress>? progress,double fraction,string message)
    {
        // UI reporting cannot fail or roll back a disk transaction.
        try { progress?.Report(new(fraction,message)); } catch { }
    }
}
public sealed record PreparedUpdate(UpdateRelease Release, string Directory, UpdateManifest Manifest);
public sealed record UpdateState(long ReleaseId, string Version, UpdateFile[] Files);
public sealed record UpdateApplyResult(bool Success, bool Restarted, string Message, string? BackupDirectory = null);
internal sealed record UpdateRecovery(string TransactionId, string JournalSha256, string Version, UpdateFile OriginalLauncher, UpdateFile NewLauncher);
