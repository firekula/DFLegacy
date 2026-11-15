using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;
using System.IO.Compression;
using System.Security.Cryptography;

var directory = Path.Combine(Path.GetTempPath(), "dflegacy-real-package-parser-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    foreach (var argument in args.Skip(2))
    {
        var archive = Path.GetFullPath(argument);
        var output = Path.Combine(directory, Path.GetFileNameWithoutExtension(archive));
        Directory.CreateDirectory(output);
        var flavor = archive.Contains("-test-") ? "test" : "normal";
        if (archive.Contains("-tools-"))
        {
            ZipFile.ExtractToDirectory(archive, output);
            var manifest = ClientPackageManifest.LoadAndValidate(output);
            if (manifest.Version != args[0] || manifest.Commit != args[1] || manifest.Flavor != flavor) throw new Exception("Installed identity differs");
            Console.WriteLine("actual ClientPackageManifest parser accepted " + Path.GetFileName(archive));
        }
        else
        {
            using var stream = File.OpenRead(archive);
            var release = new UpdateRelease(1, args[0], DateTimeOffset.UtcNow, false,
                new Uri("https://github.com/firekula/DFLegacy/releases/download/" + args[0] + "/" + Path.GetFileName(archive)),
                stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
            var prepared = UpdatePackage.Extract(release, output, archive, expectedFlavor: flavor);
            if (prepared.Manifest.Commit != args[1]) throw new Exception("Source commit differs");
            Console.WriteLine("actual UpdatePackage parser accepted " + Path.GetFileName(archive));
        }
    }
}
finally { Directory.Delete(directory, recursive: true); }
