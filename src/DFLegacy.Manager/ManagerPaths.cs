namespace DFLegacy.Manager;

public sealed class ManagerPaths
{
    public ManagerPaths(string root, string? dataDirectory = null)
    {
        Root = Path.GetFullPath(root);
        Data = Path.GetFullPath(dataDirectory ?? "data", Root);
        foreach (var directory in new[] { Data, Path.Combine(Data, "config"), Management, Backups, Logs, Exports, Path.GetDirectoryName(GameData)! })
            Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Management, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public string Root { get; }
    public string Data { get; }
    public string Management => Path.Combine(Data, "management");
    public string IdentityFile => Path.Combine(Management, "identity.json");
    public string SetupTokenFile => Path.Combine(Management, "setup-token.txt");
    public string ServerConfig => Path.Combine(Data, "config", "server.json");
    public string SettingsFile => Path.Combine(Data, "config", "manager.json");
    public string GameData => Path.Combine(Data, "game", "state.json");
    public string Backups => Path.Combine(Data, "backups");
    public string Logs => Path.Combine(Data, "logs");
    public string Exports => Path.Combine(Data, "exports");
    public string ServerDirectory => Path.Combine(Root, "runtime", "server");
    public string AssetsDirectory => Path.Combine(Root, "client-assets");
}

internal static class AtomicFile
{
    internal static void Write(string path, string contents, bool privateFile = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (privateFile && !OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
            {
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
