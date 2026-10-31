using System.Diagnostics;
using System.Text.Json;
namespace DFLegacy.Launcher.Updates;

public sealed partial class UpdateInstaller
{
    private readonly string target;
    public UpdateInstaller(string installationDirectory) => target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
    public UpdateState? ReadState()
    {
        var path=Path.Combine(target,".dflegacy","state.json"); UpdateSafety.NoLinks(path);
        if(!File.Exists(path)) return null;
        if(new FileInfo(path).Length>65536) throw new InvalidDataException("更新状态过大。");
        var state=UpdateSafety.ReadJson<UpdateState>(File.ReadAllBytes(path));
        if(state.ReleaseId<0) throw new InvalidDataException("更新状态无效。");
        return state;
    }
    public bool IsCurrent(PreparedUpdate update)
    {
        UpdateSafety.ValidateManifest(update.Manifest);
        using var pin=UpdateSafety.PinDirectory(target);
        var installed=UpdateSafety.ValidateInstalled(target,update.Manifest);
        return installed.Version==update.Manifest.Version && installed.Commit==update.Manifest.Commit && update.Manifest.Files.All(file=>UpdateTransaction.Matches(Path.Combine(target,file.Path),file));
    }
    public bool RecordCurrentIfMatching(PreparedUpdate update)
    {
        using var targetPin=UpdateSafety.PinDirectory(target);
        using var stagePin=UpdateSafety.PinDirectory(update.Directory);
        UpdateTransaction.ValidateStage(update);
        var directory=Path.Combine(target,".dflegacy"); UpdateSafety.NoLinks(directory); Directory.CreateDirectory(directory);
        using var statePin=UpdateSafety.PinDirectory(directory);
        var lockPath=Path.Combine(directory,"update.lock"); UpdateSafety.NoLinks(lockPath);
        using var exclusive=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(Path.Combine(directory,"pending.json"))) return false;
        var state=ReadState();
        if(state!=null && state.ReleaseId>update.Release.Id || !IsCurrent(update)) return false;
        UpdateTransaction.WriteJson(Path.Combine(directory,"state.json"),new UpdateState(update.Release.Id,update.Manifest.Version,update.Manifest.Files));
        return true;
    }
    public UpdateApplyResult? ReadLastResult()
    {
        var path=Path.Combine(target,".dflegacy","last-result.json"); UpdateSafety.NoLinks(path);
        return File.Exists(path) && new FileInfo(path).Length<=65536 ? UpdateSafety.ReadJson<UpdateApplyResult>(File.ReadAllBytes(path)) : null;
    }
    internal static bool Restart(string executable)
    {
        try { using var child=Process.Start(new ProcessStartInfo(executable) { UseShellExecute=false, WorkingDirectory=Path.GetDirectoryName(executable)! }); return child!=null; }
        catch { return false; }
    }
}
