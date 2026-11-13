using System.Diagnostics;

namespace DFLegacy.UpdateCore;

/// <summary>Concurrent games share a root; maintenance excludes games for its entire lifetime.</summary>
public sealed class ClientGameLease : IDisposable
{
    private readonly DirectoryPin pins;
    private readonly FileStream file;
    private ClientGameLease(DirectoryPin pins,FileStream file){this.pins=pins;this.file=file;}

    public static ClientGameLease AcquireShared(string root)=>Open(root,false);
    public static ClientGameLease EnterMaintenance(string root)=>Open(root,true);

    private static ClientGameLease Open(string root,bool maintenance)
    {
        root=Path.GetFullPath(root);
        var pins=new DirectoryPin();FileStream? file=null;
        try
        {
            pins.Ensure(root);
            var directory=Path.Combine(root,".dflegacy");pins.Ensure(directory);
            var path=Path.Combine(directory,"game-use.lock");PathPolicy.NoLinks(path);
            if(!maintenance&&!File.Exists(path))
            {
                try { using var created=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read); }
                catch(IOException) when(File.Exists(path)) { }
            }
            file=new FileStream(path,maintenance?FileMode.OpenOrCreate:FileMode.Open,
                maintenance?FileAccess.ReadWrite:FileAccess.Read,maintenance?FileShare.None:FileShare.Read);
            PathPolicy.NoLinks(path);
            if(maintenance)RequireNoOrphanGame(root);
            return new(pins,file);
        }
        catch {file?.Dispose();pins.Dispose();throw;}
    }

    // Also cover games that outlived a crashed launcher or came from an older build.
    private static void RequireNoOrphanGame(string root)
    {
        foreach(var process in Process.GetProcesses())
        using(process)
        {
            try
            {
                var name=process.ProcessName;
                if(!name.Equals("DNF",StringComparison.OrdinalIgnoreCase)
                    &&!name.StartsWith("s1c2_chapter2_host-",StringComparison.OrdinalIgnoreCase))continue;
                var path=process.MainModule?.FileName;
                if(path!=null&&string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),Path.TrimEndingDirectorySeparator(root),StringComparison.OrdinalIgnoreCase))
                    throw new IOException("请先退出此目录中的游戏，再进行章节更新或回退。");
            }
            catch(System.ComponentModel.Win32Exception) { }
            catch(InvalidOperationException) { }
        }
    }

    public void Dispose(){file.Dispose();pins.Dispose();}
}
