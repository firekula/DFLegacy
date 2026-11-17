using DFLegacy.UpdateCore;

namespace DFLegacy.Launcher;

internal static class ChapterTwoGameSettings
{
    internal static string Prepare(string clientRoot)
    {
        var root=Path.GetFullPath(clientRoot);
        var target=ProfilePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        PathPolicy.NoLinks(target);
        if(File.Exists(target)){Read(target);return target;}
        var bytes=ReadSaved(root)??System.Text.Encoding.ASCII.GetBytes("[DNF COMMON]\r\nCONFIG VERSION=1\r\n");
        try {IniConfiguration.SaveExpected(target,null,bytes);}
        catch(IOException) when(File.Exists(target)){Read(target);}
        return target;
    }

    internal static void CopySaved(string oldRoot,string newRoot)
    {
        if(ReadSaved(oldRoot) is not { } bytes)return;
        var target=ProfilePath(newRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        IniConfiguration.SaveExpected(target,null,bytes);
    }

    private static string ProfilePath(string root)
    {
        var target=Path.Combine(root,".dflegacy","chapter-two","settings","DNF_CHINA.cfg");
        PathPolicy.NoLinks(target);
        return target;
    }

    private static byte[]? ReadSaved(string root)
    {
        var saved=ProfilePath(root);
        if(File.Exists(saved))return Read(saved);
        var sessions=Path.Combine(root,"Logs","DFLegacy","Sessions");
        // Only the prior sessions of this installation are eligible. Do not read
        // the shared Windows profile or another client's settings on migration.
        if(Directory.Exists(sessions))
        {
            PathPolicy.NoLinks(sessions);
            var candidates=Directory.EnumerateDirectories(sessions)
                .Where(p=>!ClientLogPaths.HasLink(p))
                .Select(p=>Path.Combine(p,"Temp","DNF_CHINA.cfg"))
                .Where(p=>File.Exists(p)&&!ClientLogPaths.HasLink(p))
                .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(p=>p,StringComparer.Ordinal);
            foreach(var candidate in candidates)
            {
                try {return Read(candidate);}
                catch(InvalidDataException){/* An incomplete attempt is not a saved profile. */}
            }
        }
        return null;
    }

    private static byte[] Read(string path)
    {
        PathPolicy.NoLinks(path);
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>1024*1024)throw new InvalidDataException("第二章游戏设置超过允许大小。");
        using var buffer=new MemoryStream();file.CopyTo(buffer);var bytes=buffer.ToArray();
        var ini=IniConfiguration.Parse(bytes);
        if(!ini.NativeEncodingSupported||!ini.HasSection)throw new InvalidDataException("第二章游戏设置文件不完整，原文件已保留。");
        return bytes;
    }
}
