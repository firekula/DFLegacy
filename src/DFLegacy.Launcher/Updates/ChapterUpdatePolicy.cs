using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal static class ChapterUpdatePolicy
{
    internal static bool IsManaged(string root)
    {
        var store = new Core.InstallationStore(root);
        var marker = Path.Combine(store.Root, Core.ClientChapterIdentity.FileName);
        var receiptPath = Path.Combine(store.StateDirectory, "installed.json");
        var checkpoint = Path.Combine(store.Root, ".dflegacy", "chapter-transition", "checkpoint.json");
        Core.PathPolicy.NoLinks(marker);
        Core.PathPolicy.NoLinks(receiptPath);
        Core.PathPolicy.NoLinks(checkpoint);
        if (File.Exists(checkpoint) || Directory.Exists(checkpoint)
            || (File.Exists(marker) || Directory.Exists(marker)) && (File.Exists(receiptPath) || Directory.Exists(receiptPath)))
            return true;
        try
        {
            return store.ReadInstalled()?.Release.Manifest.Files.Any(file =>
                file.Path.Equals(Core.ClientChapterIdentity.FileName, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (InvalidDataException) { return false; }
    }

    internal static Core.ClientChapterIdentity? Validate(string root, Core.VerifiedRelease release)
    {
        if (!release.Manifest.Files.Any(f => f.Path == Core.ClientChapterIdentity.FileName)) return null;
        var identity = Core.ClientChapterIdentity.Read(root, release);
        if (identity.Chapter == Core.ClientChapterIdentity.ChapterTwo)
            foreach (var old in new[] { "DFLegacy.Client.dll", "DFLegacy.Debug.dll", "debug.dll" })
            {
                var path = Core.PathPolicy.Resolve(root, old);
                if (release.Manifest.Files.Any(f => f.Path.Equals(old, StringComparison.OrdinalIgnoreCase))
                    || File.Exists(path) || Directory.Exists(path))
                    throw new InvalidDataException("第二章目录仍有第一章专用组件，请先完成已验证的章节迁移：" + old);
            }
        return identity;
    }
}
