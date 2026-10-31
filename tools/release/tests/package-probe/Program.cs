using DFLegacy.Launcher.Updates;
using System.Security.Cryptography;

var archive = Path.GetFullPath(args[0]);
var directory = Path.Combine(Path.GetTempPath(), "dflegacy-real-update-parser-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    using var stream = File.OpenRead(archive);
    var release = new UpdateRelease(1, args[1], DateTimeOffset.UtcNow, false,
        new Uri("https://github.com/firekula/DFLegacy/releases/download/" + args[1] + "/DFLegacy-client-update-win-x64.zip"),
        stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    var prepared = UpdatePackage.Extract(release, directory, archive);
    if (prepared.Manifest.Commit != args[2]) throw new Exception("Source commit differs");
    Console.WriteLine("actual UpdatePackage parser accepted " + prepared.Manifest.Version);
}
finally { Directory.Delete(directory, recursive: true); }
