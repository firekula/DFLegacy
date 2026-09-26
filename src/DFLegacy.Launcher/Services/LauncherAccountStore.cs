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
