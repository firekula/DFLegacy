using System.Text.Json;
namespace DFLegacy.Launcher.Updates;

internal sealed record UpdateReceipt(string TransactionId, UpdateState? PreviousState, UpdateState NewState, UpdateFile[] Originals, UpdateManifest Manifest);
internal static class UpdateTransaction
{
    internal static bool Matches(string path,UpdateFile file)
    {
        UpdateSafety.NoLinks(path);
        return File.Exists(path) && new FileInfo(path).Length==file.Size && UpdateSafety.Hash(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase);
    }
    internal static void ValidateStage(PreparedUpdate update)
    {
        UpdateSafety.ValidateManifest(update.Manifest);
        if(update.Release.Id<=0 || update.Manifest.Version!=update.Release.Version) throw new InvalidDataException("更新版本无效。");
        UpdateSafety.NoLinks(update.Directory);
        var manifestPath=Path.Combine(update.Directory,"update-manifest.json"); UpdateSafety.NoLinks(manifestPath);
        if(new FileInfo(manifestPath).Length>65536) throw new InvalidDataException("更新清单过大。");
        var disk=UpdateSafety.ReadManifest(File.ReadAllBytes(manifestPath));
        if(JsonSerializer.Serialize(disk,UpdateSafety.Json)!=JsonSerializer.Serialize(update.Manifest,UpdateSafety.Json)) throw new InvalidDataException("暂存更新清单已改变。");
        foreach(var file in update.Manifest.Files) if(!Matches(Path.Combine(update.Directory,file.Path),file)) throw new InvalidDataException("暂存更新文件已改变。");
    }
    internal static void WriteJson(string path,object value)
    {
        UpdateSafety.NoLinks(path);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(stream,value,UpdateSafety.Json); stream.Flush(true); }
            File.Move(temporary,path,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void ReplaceFrom(string source,string target)
    {
        UpdateSafety.NoLinks(source); UpdateSafety.NoLinks(target);
        var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { input.CopyTo(output); output.Flush(true); }
            File.Move(temporary,target,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Restore(string target,string backup,UpdateReceipt receipt)
    {
        foreach(var original in receipt.Originals)
        {
            var saved=Path.Combine(backup,original.Path);
            if(!Matches(saved,original)) throw new InvalidDataException("备份校验失败；已保留恢复资料。");
        }
        foreach(var original in receipt.Originals)
        {
            var destination=Path.Combine(target,original.Path);
            if(!Matches(destination,original)) ReplaceFrom(Path.Combine(backup,original.Path),destination);
        }
        if(receipt.Originals.Any(original=>!Matches(Path.Combine(target,original.Path),original))) throw new IOException("恢复后校验失败；已保留恢复资料。");
        UpdateSafety.ValidateInstalled(target,receipt.Manifest);
        var state=Path.Combine(target,".dflegacy","state.json");
        if(receipt.PreviousState==null) { UpdateSafety.NoLinks(state); if(File.Exists(state)) File.Delete(state); }
        else WriteJson(state,receipt.PreviousState);
        File.Delete(Path.Combine(target,".dflegacy","pending.json"));
    }
    // A durable receipt lets the next attempt recover after power loss/process termination.
    internal static UpdateRecovery? InspectRecovery(string target)
    {
        using var targetPin=UpdateSafety.PinDirectory(target);
        var state=Path.Combine(target,".dflegacy"); UpdateSafety.NoLinks(state);
        var pending=Path.Combine(state,"pending.json"); UpdateSafety.NoLinks(pending);
        if(!File.Exists(pending)) return null;
        using var statePin=UpdateSafety.PinDirectory(state);
        var lockPath=Path.Combine(state,"update.lock"); UpdateSafety.NoLinks(lockPath);
        using var exclusive=new FileStream(lockPath,FileMode.Open,FileAccess.Read,FileShare.None);
        var (receipt,digest)=ReadReceipt(target);
        var backup=Path.Combine(target,".dflegacy-backups",receipt.TransactionId);
        using var backupPin=UpdateSafety.PinDirectory(backup);
        var installed=UpdateSafety.ValidateInstalled(backup,receipt.Manifest);
        return new(receipt.TransactionId,digest,installed.Version,receipt.Originals.Single(f=>f.Path=="DFLegacy.Launcher.exe"),receipt.Manifest.Files.Single(f=>f.Path=="DFLegacy.Launcher.exe"));
    }
    internal static void ValidateRecovery(string target,UpdateRecovery recovery,string? executableHash=null)
    {
        if(InspectRecovery(target)!=recovery || executableHash!=null && executableHash!=recovery.OriginalLauncher.Sha256 && executableHash!=recovery.NewLauncher.Sha256)
            throw new InvalidDataException("恢复记录或登录器身份已改变。");
    }
    private static (UpdateReceipt Receipt,string Digest) ReadReceipt(string target)
    {
        var pending=Path.Combine(target,".dflegacy","pending.json"); UpdateSafety.NoLinks(pending);
        if(new FileInfo(pending).Length>65536) throw new InvalidDataException("恢复记录无效。");
        var bytes=File.ReadAllBytes(pending);
        var receipt=UpdateSafety.ReadJson<UpdateReceipt>(bytes);
        UpdateSafety.ValidateManifest(receipt.Manifest);
        var names=UpdateSafety.TransactionNames(receipt.Manifest).ToArray();
        if(!Guid.TryParseExact(receipt.TransactionId,"N",out _) || receipt.Originals is null || receipt.Originals.Length!=names.Length || receipt.Originals.Any(f=>f is null || f.Size<=0 || f.Size>UpdateSafety.MaxExpanded || !ClientPackageManifest.Hex(f.Sha256,64)) || receipt.Originals.Select(f=>f.Path).Order().SequenceEqual(names.Order())==false)
            throw new InvalidDataException("恢复记录路径无效。");
        var backup=Path.Combine(target,".dflegacy-backups",receipt.TransactionId);
        using var pin=UpdateSafety.PinDirectory(backup);
        UpdateSafety.ValidateInstalled(backup,receipt.Manifest);
        if(receipt.Originals.Any(original=>!Matches(Path.Combine(backup,original.Path),original))) throw new InvalidDataException("备份校验失败；已保留恢复资料。");
        return (receipt,Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }
    private static void Recover(string target)
    {
        var pending=Path.Combine(target,".dflegacy","pending.json"); UpdateSafety.NoLinks(pending);
        if(!File.Exists(pending)) return;
        var (receipt,_)=ReadReceipt(target);
        var backup=Path.Combine(target,".dflegacy-backups",receipt.TransactionId);
        using var pin=UpdateSafety.PinDirectory(backup);
        Restore(target,backup,receipt);
    }
    internal static UpdateApplyResult RecoverAndRestart(string target,UpdateRecovery recovery,string executableHash,Func<string,bool> restart)
    {
        string? backup=null;
        try
        {
            using var targetPin=UpdateSafety.PinDirectory(target);
            var state=Path.Combine(target,".dflegacy"); using var statePin=UpdateSafety.PinDirectory(state);
            var lockPath=Path.Combine(state,"update.lock"); UpdateSafety.NoLinks(lockPath);
            using var exclusive=new FileStream(lockPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            var (receipt,digest)=ReadReceipt(target);
            if(receipt.TransactionId!=recovery.TransactionId || digest!=recovery.JournalSha256 ||
               executableHash!=receipt.Originals.Single(f=>f.Path=="DFLegacy.Launcher.exe").Sha256 && executableHash!=receipt.Manifest.Files.Single(f=>f.Path=="DFLegacy.Launcher.exe").Sha256)
                throw new InvalidDataException("恢复记录或登录器身份已改变。");
            backup=Path.Combine(target,".dflegacy-backups",receipt.TransactionId);
            using var backupPin=UpdateSafety.PinDirectory(backup);
            Restore(target,backup,receipt);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return new(false,false,"恢复未完成；请关闭占用文件的游戏后重试。原文件和恢复资料已保留。",backup); }
        bool restarted; try { restarted=restart(Path.Combine(target,"DFLegacy.Launcher.exe")); } catch { restarted=false; }
        return new(true,restarted,restarted?"已恢复上次更新前的完整客户端。":"恢复已完成，但自动重启失败；请手动启动登录器。",backup);
    }
    internal static UpdateApplyResult Apply(string target,PreparedUpdate update,Func<string,bool> restart)
    {
        string? backup=null; UpdateReceipt? receipt=null;
        try
        {
            using var targetPin=UpdateSafety.PinDirectory(target);
            var stateDirectory=Path.Combine(target,".dflegacy"); UpdateSafety.NoLinks(stateDirectory); Directory.CreateDirectory(stateDirectory);
            using var statePin=UpdateSafety.PinDirectory(stateDirectory);
            var lockPath=Path.Combine(stateDirectory,"update.lock"); UpdateSafety.NoLinks(lockPath);
            using var exclusive=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            Recover(target);
            using var stagePin=UpdateSafety.PinDirectory(update.Directory);
            ValidateStage(update);
            UpdateSafety.ValidateInstalled(target,update.Manifest);
            var installer=new UpdateInstaller(target); var previous=installer.ReadState();
            if(previous!=null && update.Release.Id<=previous.ReleaseId) return new(false,false,"该版本已安装或比当前版本更旧。");
            if(installer.IsCurrent(update))
            {
                WriteJson(Path.Combine(stateDirectory,"state.json"),new UpdateState(update.Release.Id,update.Manifest.Version,update.Manifest.Files));
                return new(true,false,"文件已是最新版本，无需替换。");
            }
            var held=new List<FileStream>();
            try
            {
                // Keep staged bytes and metadata immutable until the complete transaction finishes.
                foreach(var file in update.Manifest.Files) held.Add(new FileStream(Path.Combine(update.Directory,file.Path),FileMode.Open,FileAccess.Read,FileShare.Read));
                held.Add(new FileStream(Path.Combine(update.Directory,"update-manifest.json"),FileMode.Open,FileAccess.Read,FileShare.Read));
                ValidateStage(update);
                foreach(var name in UpdateSafety.TransactionNames(update.Manifest))
                {
                    var path=Path.Combine(target,name); UpdateSafety.NoLinks(path);
                    // Detect the game's usual read-only sharing without terminating any process.
                    using var probe=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
                }
                var backupRoot=Path.Combine(target,".dflegacy-backups"); UpdateSafety.NoLinks(backupRoot); Directory.CreateDirectory(backupRoot);
                using var rootPin=UpdateSafety.PinDirectory(backupRoot);
                var id=Guid.NewGuid().ToString("N"); backup=Path.Combine(backupRoot,id); Directory.CreateDirectory(backup);
                using var backupPin=UpdateSafety.PinDirectory(backup);
                var originals=new List<UpdateFile>();
                foreach(var name in UpdateSafety.TransactionNames(update.Manifest))
                {
                    var path=Path.Combine(target,name); var original=new UpdateFile(name,UpdateSafety.Hash(path),new FileInfo(path).Length);
                    // Backup data must reach disk before either durable recovery record.
                    using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                    using(var output=new FileStream(Path.Combine(backup,name),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    { input.CopyTo(output); output.Flush(true); }
                    if(!Matches(Path.Combine(backup,name),original)) throw new IOException("更新备份未通过校验。");
                    originals.Add(original);
                }
                // Reject a mixed original snapshot before the recovery journal can authorize writes.
                UpdateSafety.ValidateInstalled(backup,update.Manifest);
                receipt=new(id,previous,new(update.Release.Id,update.Manifest.Version,update.Manifest.Files),originals.ToArray(),update.Manifest);
                WriteJson(Path.Combine(backup,"receipt.json"),receipt); WriteJson(Path.Combine(stateDirectory,"pending.json"),receipt);
                try
                {
                    foreach(var file in update.Manifest.Files) ReplaceFrom(Path.Combine(update.Directory,file.Path),Path.Combine(target,file.Path));
                    WriteJson(Path.Combine(target,UpdateSafety.InstalledManifestName),update.Manifest);
                    foreach(var file in update.Manifest.Files) if(!Matches(Path.Combine(target,file.Path),file)) throw new IOException("安装后校验失败。");
                    UpdateSafety.ValidateInstalled(target,update.Manifest);
                    WriteJson(Path.Combine(stateDirectory,"state.json"),receipt.NewState);
                    File.Delete(Path.Combine(stateDirectory,"pending.json"));
                }
                catch
                {
                    Restore(target,backup,receipt);
                    throw;
                }
            }
            finally { foreach(var file in held) file.Dispose(); }
            bool restarted;
            try { restarted=restart(Path.Combine(target,"DFLegacy.Launcher.exe")); } catch { restarted=false; }
            var result=new UpdateApplyResult(true,restarted,restarted?"更新完成。":"更新已完成，但自动重启失败；请手动启动登录器。备份已保留。",backup);
            // The binaries and release state have committed. A receipt failure cannot
            // undo that outcome or change whether the restart actually succeeded.
            try { WriteJson(Path.Combine(stateDirectory,"last-result.json"),result); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
            { result=result with { Message=result.Message+" 更新结果记录未能保存；已安装版本与上述重启结果不受影响。" }; }
            return result;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            return new(false,false,"更新未完成；请关闭占用文件的游戏后重试。原文件或恢复备份已保留。",backup);
        }
    }
}
