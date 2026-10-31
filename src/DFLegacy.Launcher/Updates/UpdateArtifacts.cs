using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace DFLegacy.Launcher.Updates;

// No paths from a mutable receipt are used: only bounded GUID names under fixed roots.
// The exclusive lease covers the complete download/handoff operation, including cleanup.
internal sealed class UpdateArtifacts : IDisposable
{
    private sealed record Receipt(string Owner, string? Helper = null, int ProcessId = 0, long StartTicks = 0);
    private static readonly string[] StageFiles=["package.zip","update-manifest.json","DFLegacy.Launcher.exe","ijl15.dll"];
    private readonly string owner;
    private FileStream? lease;
    private Receipt receipt;
    internal string Directory { get; }
    private UpdateArtifacts(string directory,string owner,FileStream lease,Receipt receipt)
    { Directory=directory; this.owner=owner; this.lease=lease; this.receipt=receipt; }
    private static string Root(string installation)
    {
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation)).ToUpperInvariant())));
        return Path.Combine(Path.GetTempPath(),"dflegacy-updates",key);
    }
    private static bool GuidName(string name,string prefix)=>name.StartsWith(prefix,StringComparison.Ordinal) && Guid.TryParseExact(name[prefix.Length..],"N",out _);
    internal static UpdateArtifacts Create(string installation)
    {
        var root=Root(installation); UpdateSafety.NoLinks(root); System.IO.Directory.CreateDirectory(root);
        using var rootPin=UpdateSafety.PinDirectory(root);
        var name="attempt-"+Guid.NewGuid().ToString("N"); var directory=Path.Combine(root,name); System.IO.Directory.CreateDirectory(directory);
        var held=new FileStream(Path.Combine(directory,"lease.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        var owner=Path.GetFileName(root)+"/"+name;
        var result=new UpdateArtifacts(directory,owner,held,new(owner));
        try { result.Save(); return result; } catch { result.Dispose(); throw; }
    }
    private void Save()
    {
        var path=Path.Combine(Directory,"cleanup.json");
        var next=Path.Combine(Directory,"cleanup.next");
        UpdateSafety.NoLinks(path); UpdateSafety.NoLinks(next);
        try
        {
            using(var stream=new FileStream(next,FileMode.Create,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(stream,receipt,UpdateSafety.Json); stream.Flush(true); }
            // Keep the prior valid receipt if writing or replacement fails.
            File.Move(next,path,true);
        }
        finally
        {
            try { UpdateSafety.NoLinks(next); if(File.Exists(next)) File.Delete(next); }
            catch(Exception ex) when(IsExpected(ex)) { }
        }
    }
    internal void Track(PreparedUpdate update)
    {
        if(!Path.GetDirectoryName(Path.GetFullPath(update.Directory))!.Equals(Directory,StringComparison.OrdinalIgnoreCase) || !GuidName(Path.GetFileName(update.Directory),"dflegacy-update-"))
            throw new InvalidDataException("更新下载不属于本次临时目录。");
    }
    internal void TrackHelper(UpdateHandoff handoff)
    {
        var directory=Path.GetDirectoryName(Path.GetFullPath(handoff.HelperPath))!;
        if(!GuidName(Path.GetFileName(directory),"dflegacy-helper-") || !Path.GetDirectoryName(directory)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) || Path.GetFileName(handoff.HelperPath)!="DFLegacy.Launcher.exe")
            throw new InvalidDataException("安装助手不属于允许的临时目录。");
        var markerOwned=false;
        try
        {
            using var pin=UpdateSafety.PinDirectory(directory);
            var marker=Path.Combine(directory,"dflegacy-owner"); UpdateSafety.NoLinks(marker);
            using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                markerOwned=true;
                var bytes=Encoding.UTF8.GetBytes(owner); stream.Write(bytes); stream.Flush(true);
            }
            receipt=receipt with { Helper=Path.GetFileName(directory) }; Save();
        }
        catch
        {
            // The handoff is known in memory and StartHelperAsync has not been called.
            // Do not rely on a receipt whose write just failed to recover this copy.
            DeleteUnstartedHelper(handoff.HelperPath,markerOwned);
            throw;
        }
    }
    internal void MarkStarted(UpdateHelperHandle helper)
    {
        // Readiness already committed. A metadata problem must never delete a running helper.
        try
        {
            using var process=Process.GetProcessById(helper.ProcessId);
            receipt=receipt with { ProcessId=helper.ProcessId,StartTicks=process.StartTime.ToUniversalTime().Ticks }; Save();
        }
        catch(Exception ex) when(IsExpected(ex)) { }
    }
    internal void Release() { lease?.Dispose(); lease=null; }
    public void Dispose()
    {
        // While holding this lease, in-memory ownership remains authoritative even when
        // its receipt is locked or damaged. Released/orphaned instances use disk only.
        var owned=lease!=null?receipt:null;
        Release(); TryClean(Directory,owner,owned);
    }
    internal static void Sweep(string installation)
    {
        try
        {
            var root=Root(installation); UpdateSafety.NoLinks(root); if(!System.IO.Directory.Exists(root)) return;
            using var rootPin=UpdateSafety.PinDirectory(root);
            foreach(var path in System.IO.Directory.EnumerateDirectories(root).Take(64))
                if(GuidName(Path.GetFileName(path),"attempt-")) TryClean(path,Path.GetFileName(root)+"/"+Path.GetFileName(path));
        }
        catch(Exception ex) when(IsExpected(ex)) { }
    }
    private static bool IsExpected(Exception ex)=>ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException;
    private static void TryClean(string directory,string owner,Receipt? owned=null)
    {
        try
        {
            if(!System.IO.Directory.Exists(directory)) return;
            UpdateSafety.NoLinks(directory);
            // FileMode.Open: an orphan must have the lease marker created by this caller.
            UpdateSafety.NoLinks(Path.Combine(directory,"lease.lock"));
            using(var held=new FileStream(Path.Combine(directory,"lease.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            using(var pin=UpdateSafety.PinDirectory(directory))
            {
                var record=Path.Combine(directory,"cleanup.json"); UpdateSafety.NoLinks(record);
                if(owned==null && new FileInfo(record).Length>4096) return;
                var receipt=owned??UpdateSafety.ReadJson<Receipt>(File.ReadAllBytes(record)); if(receipt.Owner!=owner) return;
                if(System.IO.Directory.EnumerateFiles(directory).Any(p=>Path.GetFileName(p) is not ("lease.lock" or "cleanup.json" or "cleanup.next"))) return;
                var stages=System.IO.Directory.EnumerateDirectories(directory).ToArray();
                if(stages.Any(p=>!GuidName(Path.GetFileName(p),"dflegacy-update-"))) return;
                if(receipt.Helper!=null)
                {
                    if(!GuidName(receipt.Helper,"dflegacy-helper-")) return;
                    var helper=Path.Combine(Path.GetTempPath(),receipt.Helper);
                    if(System.IO.Directory.Exists(helper))
                    {
                        using(var hp=UpdateSafety.PinDirectory(helper))
                        {
                            var marker=Path.Combine(helper,"dflegacy-owner"); UpdateSafety.NoLinks(marker);
                            if(!File.Exists(marker) || new FileInfo(marker).Length>256 || File.ReadAllText(marker)!=owner || HelperActive(receipt)) return;
                            DeleteFiles(helper,["DFLegacy.Launcher.exe","dflegacy-owner"]);
                        }
                        System.IO.Directory.Delete(helper,false);
                    }
                }
                foreach(var stage in stages) { DeleteStage(stage); if(System.IO.Directory.Exists(stage)) return; }
                var next=Path.Combine(directory,"cleanup.next"); UpdateSafety.NoLinks(next);
                if(File.Exists(next)) File.Delete(next);
                File.Delete(record);
            }
            File.Delete(Path.Combine(directory,"lease.lock")); System.IO.Directory.Delete(directory,false);
        }
        catch(Exception ex) when(IsExpected(ex)) { /* Defer locked, changed or unowned material. */ }
    }
    private static bool HelperActive(Receipt receipt)
    {
        if(receipt.ProcessId==0) return false;
        try
        {
            using var process=Process.GetProcessById(receipt.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==receipt.StartTicks;
        }
        catch(ArgumentException) { return false; }
        catch(InvalidOperationException) { return false; }
        catch(System.ComponentModel.Win32Exception) { return true; }
    }
    private static void DeleteFiles(string directory,string[] allowed)
    {
        if(System.IO.Directory.EnumerateDirectories(directory).Any()) throw new InvalidDataException("临时目录包含未拥有的目录。");
        var files=System.IO.Directory.EnumerateFiles(directory).ToArray();
        if(files.Any(p=>!allowed.Contains(Path.GetFileName(p),StringComparer.Ordinal))) throw new InvalidDataException("临时目录包含未拥有的文件。");
        // Check all files before deleting any; a mapped/running EXE refuses the write handle.
        foreach(var file in files) { UpdateSafety.NoLinks(file); using var check=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
        foreach(var file in files) File.Delete(file);
    }
    internal static void DeleteUnstartedHelper(string helperPath,bool ownsMarker=false)
    {
        try
        {
            var full=Path.GetFullPath(helperPath); var directory=Path.GetDirectoryName(full)!;
            if(Path.GetFileName(full)!="DFLegacy.Launcher.exe" || !GuidName(Path.GetFileName(directory),"dflegacy-helper-") ||
               !Path.GetDirectoryName(directory)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) return;
            if(!System.IO.Directory.Exists(directory)) return;
            using(var pin=UpdateSafety.PinDirectory(directory))
            {
                // This entry point owns the exact unstarted executable, not arbitrary
                // neighboring entries. Unknown marker content must remain untouched.
                var files=new List<string>();
                if(File.Exists(full)) files.Add(full);
                var marker=Path.Combine(directory,"dflegacy-owner");
                if(ownsMarker && File.Exists(marker)) files.Add(marker);
                foreach(var file in files) { UpdateSafety.NoLinks(file); using var probe=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
                foreach(var file in files) File.Delete(file);
            }
            System.IO.Directory.Delete(directory,false);
        }
        catch(Exception ex) when(IsExpected(ex)) { /* A live image, link or unknown entry is never removed. */ }
    }
    internal static void DeleteStage(string directory)
    {
        try
        {
            if(!GuidName(Path.GetFileName(directory),"dflegacy-update-")) return;
            using(var pin=UpdateSafety.PinDirectory(directory)) DeleteFiles(directory,StageFiles);
            System.IO.Directory.Delete(directory,false);
        }
        catch(Exception ex) when(IsExpected(ex)) { }
    }
    internal static bool PresentOnce(string installation,string message)
    {
        try
        {
            var root=Root(installation); UpdateSafety.NoLinks(root); System.IO.Directory.CreateDirectory(root);
            using var pin=UpdateSafety.PinDirectory(root);
            var path=Path.Combine(root,"last-presented.sha256"); UpdateSafety.NoLinks(path);
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)));
            if(File.Exists(path) && new FileInfo(path).Length==64 && File.ReadAllText(path)==hash) return false;
            File.WriteAllText(path,hash); return true;
        }
        catch(Exception ex) when(IsExpected(ex)) { return false; }
    }
}
