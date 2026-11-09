using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using DFLegacy.UpdateCore;

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
    private readonly ConditionalWeakTable<LauncherOptions, Snapshot> snapshots = new();
    private sealed class Snapshot(JsonObject value) { internal JsonObject Value = value; }
    public LauncherAccountStore(string path) => _path = path;

    public LauncherOptions Load()
    {
        PathPolicy.NoLinks(_path);
        var options=LoadCurrent(); Track(options); return options;
    }
    private LauncherOptions LoadCurrent()=>!File.Exists(_path)?new LauncherOptions():
        JsonSerializer.Deserialize<LauncherOptions>(File.ReadAllText(_path),JsonOptions)??throw new InvalidDataException("launcher.json 不能为空。");
    private static JsonObject Node(LauncherOptions options)=>JsonSerializer.SerializeToNode(options,JsonOptions)!.AsObject();
    private void Track(LauncherOptions options)
    { snapshots.Remove(options); snapshots.Add(options,new(Node(options))); }

    internal sealed class ReadLease(LauncherOptions options, FileStream stream, IDisposable installationLease) : IDisposable
    {
        internal LauncherOptions Options { get; } = options;
        public void Dispose() { stream.Dispose(); installationLease.Dispose(); }
    }

    // Hold the saved target stable while a different file (Config.ini) is saved.
    // Windows sharing denies writes/replacement until the lease is disposed.
    internal ReadLease AcquireReadLease()
    {
        var held=InstallationLease.Acquire(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        FileStream? stream=null;
        try
        {
            PathPolicy.NoLinks(_path);
            stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
            var options = JsonSerializer.Deserialize<LauncherOptions>(reader.ReadToEnd(), JsonOptions)
                ?? throw new InvalidDataException("launcher.json 不能为空。");
            Track(options); return new(options, stream,held);
        }
        catch { stream?.Dispose(); held.Dispose(); throw; }
    }

    public void Save(LauncherOptions options)
    {
        var dir=Path.GetDirectoryName(Path.GetFullPath(_path))!;
        using var held=InstallationLease.Acquire(dir);
        SaveCore(options,dir);
    }
    private void SaveCore(LauncherOptions options,string directory)
    {
        if(InstallTransaction.HasPending(directory)) throw new IOException("存在未完成更新，请先恢复再保存配置。");
        PathPolicy.NoLinks(_path);
        var current=Node(LoadCurrent()); var desired=Node(options);
        var baseline=snapshots.TryGetValue(options,out var snapshot)?snapshot.Value:new JsonObject();
        MergeChanges(current,baseline,desired);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(current,JsonOptions);
        var installed=new InstallationStore(directory).ReadInstalled();
        if(installed!=null) bytes=ConfigurationMigration.Apply(bytes,installed.Release.Manifest.Migrations);
        var temporary=Path.Combine(directory,".launcher-save-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { output.Write(bytes); output.Flush(true); }
            // Retain the existing read-lease contract: a read-only UI snapshot blocks replacement.
            if(File.Exists(_path)) { using var probe=new FileStream(_path,FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
            PathPolicy.NoLinks(_path); File.Move(temporary,_path,true);
            // Track what this caller actually saw, not fields merged from another writer.
            Track(options);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void MergeChanges(JsonObject latest,JsonObject baseline,JsonObject desired)
    {
        foreach(var pair in desired)
        {
            baseline.TryGetPropertyValue(pair.Key,out var old);
            if(JsonNode.DeepEquals(old,pair.Value)) continue;
            if(pair.Value is JsonObject value && old is JsonObject prior && latest[pair.Key] is JsonObject existing)
                MergeChanges(existing,prior,value);
            else latest[pair.Key]=pair.Value?.DeepClone();
        }
        foreach(var pair in baseline)
            if(!desired.ContainsKey(pair.Key)) latest.Remove(pair.Key);
    }

    public void Update(Action<LauncherOptions> mutate)
    {
        var directory=Path.GetDirectoryName(Path.GetFullPath(_path))!;
        using var held=InstallationLease.Acquire(directory);
        var options = Load();
        mutate(options);
        SaveCore(options,directory);
    }
}
