using System.Text.Json;

namespace DFLegacy.Launcher;

internal sealed class LauncherAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly string _path;
    public LauncherAccountStore(string path) => _path = path;

    public LauncherOptions Load()
    {
        if (!File.Exists(_path))
            return new LauncherOptions();
        return JsonSerializer.Deserialize<LauncherOptions>(File.ReadAllText(_path), JsonOptions)
            ?? throw new InvalidDataException("launcher.json 不能为空。");
    }

    internal sealed class ReadLease(LauncherOptions options, FileStream stream) : IDisposable
    {
        internal LauncherOptions Options { get; } = options;
        public void Dispose() => stream.Dispose();
    }

    // Hold the saved target stable while a different file (Config.ini) is saved.
    // Windows sharing denies writes/replacement until the lease is disposed.
    internal ReadLease AcquireReadLease()
    {
        var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
            var options = JsonSerializer.Deserialize<LauncherOptions>(reader.ReadToEnd(), JsonOptions)
                ?? throw new InvalidDataException("launcher.json 不能为空。");
            return new(options, stream);
        }
        catch { stream.Dispose(); throw; }
    }

    public void Save(LauncherOptions options)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(options, JsonOptions));
    }

    public void Update(Action<LauncherOptions> mutate)
    {
        var options = Load();
        mutate(options);
        Save(options);
    }
}
