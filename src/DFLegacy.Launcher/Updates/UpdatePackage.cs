using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
namespace DFLegacy.Launcher.Updates;

internal static class UpdatePackage
{
    internal static PreparedUpdate Extract(UpdateRelease release, string directory, string archive, CancellationToken cancellationToken=default, string? expectedFlavor=null)
    {
        using var archiveStream=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (archiveStream.Length!=release.Size || !Convert.ToHexString(SHA256.HashData(archiveStream)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新压缩包 SHA256 不匹配。");
        archiveStream.Position=0;
        using var zip = new ZipArchive(archiveStream,ZipArchiveMode.Read);
        var manifests=zip.Entries.Where(e=>e.FullName=="update-manifest.json").ToArray();
        if(manifests.Length!=1) throw new InvalidDataException("更新包缺少清单或清单重复。");
        var manifestEntry=manifests[0];
        if(manifestEntry.Length is <=0 or >16384) throw new InvalidDataException("更新清单过大。");
        using var memory=new MemoryStream(); CopyEntry(manifestEntry,memory,cancellationToken);
        var manifest=UpdateSafety.ReadManifest(memory.ToArray());
        UpdateSafety.ValidatePackageFormat(release,manifest);
        if(manifest.Version!=release.Version || expectedFlavor!=null && manifest.Flavor!=expectedFlavor) throw new InvalidDataException("更新版本或类型不匹配。");
        var expected = new HashSet<string>(manifest.Files.Select(f=>f.Path).Append("update-manifest.json"),StringComparer.Ordinal);
        long total=0;
        foreach(var entry in zip.Entries)
        {
            var type=(entry.ExternalAttributes >> 16) & 0xF000;
            if(!expected.Remove(entry.FullName) || (type != 0 && type != 0x8000) || (entry.ExternalAttributes & (int)(FileAttributes.ReparsePoint|FileAttributes.Directory)) != 0 || entry.Length<=0 || entry.Length>UpdateSafety.MaxExpanded)
                throw new InvalidDataException("更新包包含未允许的文件或链接。");
            total+=entry.Length;
        }
        if(expected.Count!=0 || total>UpdateSafety.MaxExpanded) throw new InvalidDataException("更新包缺少文件或大小超限。");
        foreach(var file in manifest.Files)
        {
            var entry=zip.GetEntry(file.Path)!;
            if(entry.Length!=file.Size) throw new InvalidDataException("更新文件大小不匹配。");
            var destination=Path.Combine(directory,file.Path);
            using(var dst=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)) CopyEntry(entry,dst,cancellationToken);
            if(new FileInfo(destination).Length!=file.Size || !UpdateSafety.Hash(destination).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新文件 SHA256 不匹配。");
        }
        File.WriteAllBytes(Path.Combine(directory,"update-manifest.json"),memory.ToArray());
        return new(release,directory,manifest);
    }
    private static readonly uint[] CrcTable=BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var table=new uint[256];
        for(uint index=0;index<table.Length;index++)
        { var value=index; for(int bit=0;bit<8;bit++) value=(value&1)!=0 ? 0xedb88320^(value>>1) : value>>1; table[index]=value; }
        return table;
    }
    private static void CopyEntry(ZipArchiveEntry entry,Stream output,CancellationToken token)
    {
        using var input=entry.Open(); var bytes=new byte[65536]; long total=0; uint crc=uint.MaxValue;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            var count=input.Read(bytes); if(count==0) break;
            total+=count; if(total>entry.Length) throw new InvalidDataException("更新解压长度超出清单。");
            for(int index=0;index<count;index++) crc=CrcTable[(crc^bytes[index])&255]^(crc>>8);
            output.Write(bytes,0,count);
        }
        if(total!=entry.Length || ~crc!=entry.Crc32) throw new InvalidDataException("更新压缩条目校验失败。");
    }
}
