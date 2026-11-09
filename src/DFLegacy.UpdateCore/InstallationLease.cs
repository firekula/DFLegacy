namespace DFLegacy.UpdateCore;

/// <summary>The same exclusive file lease is used by V3, V4, launch, and configuration writers.</summary>
public sealed class InstallationLease : IDisposable
{
    private readonly DirectoryPin pins;
    private readonly FileStream file;
    private InstallationLease(DirectoryPin pins, FileStream file) { this.pins = pins; this.file = file; }
    public static InstallationLease Acquire(string root)
    {
        var pins = new DirectoryPin();
        try
        {
            pins.Ensure(root);
            var directory = Path.Combine(Path.GetFullPath(root), ".dflegacy"); pins.Ensure(directory);
            var path = Path.Combine(directory, "update.lock"); PathPolicy.NoLinks(path);
            var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try { PathPolicy.NoLinks(path); return new(pins, file); } catch { file.Dispose(); throw; }
        }
        catch { pins.Dispose(); throw; }
    }
    public void Dispose() { file.Dispose(); pins.Dispose(); }
}
