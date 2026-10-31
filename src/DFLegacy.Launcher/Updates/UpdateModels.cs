using System.Text.Json.Serialization;
namespace DFLegacy.Launcher.Updates;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFile(string Path, string Sha256, long Size);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateManifest(int SchemaVersion, string Version, string Commit, UpdateFile[] Files);
public sealed record UpdateRelease(long Id, string Version, DateTimeOffset PublishedAt, bool Prerelease, Uri DownloadUrl, long Size, string Sha256);
public sealed record UpdateProgress(long BytesReceived, long TotalBytes, double BytesPerSecond);
public sealed record PreparedUpdate(UpdateRelease Release, string Directory, UpdateManifest Manifest);
public sealed record UpdateState(long ReleaseId, string Version, UpdateFile[] Files);
public sealed record UpdateApplyResult(bool Success, bool Restarted, string Message, string? BackupDirectory = null);
