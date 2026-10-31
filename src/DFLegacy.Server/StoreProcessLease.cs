namespace DFLegacy.Server;

/// <summary>An OS-backed exclusive file handle held for the entire writer process lifetime.</summary>
public sealed class StoreProcessLease : IDisposable
{
    private readonly FileStream _stream;

    private StoreProcessLease(FileStream stream) => _stream = stream;

    public static StoreProcessLease Acquire(string dataPath)
    {
        var path = Path.GetFullPath(dataPath, AppContext.BaseDirectory) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            // FileShare.None uses OS sharing locks on Windows and flock on Unix.
            // Keep the lock file: deleting it would permit two distinct locked inodes.
            return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception)
        {
            throw new IOException("The game store is in use by another process.", exception);
        }
    }

    public void Dispose() => _stream.Dispose();
}
