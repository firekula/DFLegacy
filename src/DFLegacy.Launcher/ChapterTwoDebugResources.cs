using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DFLegacy.Launcher;

// A private debug view of the pinned native archive. Never edits the source.
internal sealed class ChapterTwoDebugResources
{
    private sealed record Entry(uint Id, byte[] Name, int Size, uint Crc, int Offset);
    private readonly byte[] archive;
    private readonly int body;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> stringFiles;
    private readonly Dictionary<int, Dictionary<string, string>> strings = [];
    internal static readonly Encoding TextEncoding = CreateEncoding();
    private static Encoding CreateEncoding() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(936); }
    internal string SourceHash { get; }
    internal ChapterTwoDebugResources(string path)
    {
        archive = File.ReadAllBytes(path);
        SourceHash = Convert.ToHexString(SHA256.HashData(archive));
        if (archive.Length < 56 || U32(archive, 0) != 36 || !Guid.TryParse(Encoding.ASCII.GetString(archive, 4, 36), out _))
            throw new InvalidDataException("不是受支持的第二章本地资源。");
        int length = checked((int)U32(archive, 44)), count = checked((int)U32(archive, 52));
        if (length <= 0 || length > 16*1024*1024 || length % 4 != 0 || count > 200000 || length > archive.Length-56)
            throw new InvalidDataException("本地资源目录越界。");
        body = 56+length;
        var tree = Crypt(archive.AsSpan(56,length), U32(archive,48), false);
        if (Crc(tree,(uint)count)!=U32(archive,48)) throw new InvalidDataException("本地资源目录校验失败。");
        using var reader = new BinaryReader(new MemoryStream(tree));
        for (int i=0;i<count;i++)
        {
            uint id=reader.ReadUInt32(); int n=reader.ReadInt32();
            if(n is < 1 or > 4096) throw new InvalidDataException("资源名称长度无效。");
            var name=reader.ReadBytes(n);
            int size=reader.ReadInt32(); uint crc=reader.ReadUInt32(); int offset=reader.ReadInt32();
            if(size<0 || offset<0 || (offset&3)!=0 || (long)offset+(((long)size+3)&~3)>archive.Length-body)
                throw new InvalidDataException("资源内容越界。");
            entries.Add(Normalize(TextEncoding.GetString(name).TrimEnd('\0')),new(id,name,size,crc,offset));
        }
        stringFiles=List(Read("string.lst"));
    }
    private static string Normalize(string path) => path.Replace('\\','/').ToLowerInvariant();
    internal byte[] Read(string path)
    {
        var e=entries[Normalize(path)];
        var bytes=Crypt(archive.AsSpan(body+e.Offset,(e.Size+3)&~3),e.Crc,false);
        if(Crc(bytes,e.Id)!=e.Crc) throw new InvalidDataException("本地资源校验失败："+path);
        return bytes[..e.Size];
    }
    internal static Dictionary<int,string> List(byte[] bytes) => Regex.Matches(TextEncoding.GetString(bytes),@"(?m)^\s*(\d+)\s+`([^`]+)`")
        .ToDictionary(m=>int.Parse(m.Groups[1].Value),m=>Normalize(m.Groups[2].Value));
    internal string Resolve(string value)
    {
        value=value.Trim();
        if(value.StartsWith('`')&&value.EndsWith('`')) return value[1..^1];
        var reference=Regex.Match(value,@"^<(\d+)::([^>]+)>$");
        if(!reference.Success) return value;
        int table=int.Parse(reference.Groups[1].Value);
        if(!strings.TryGetValue(table,out var text))
        {
            text=[];
            if(stringFiles.TryGetValue(table,out var path))
                foreach(Match row in Regex.Matches(TextEncoding.GetString(Read(path)),@"(?m)^([^>\r\n]+)>([^\r\n]*)")) text[row.Groups[1].Value]=row.Groups[2].Value;
            strings[table]=text;
        }
        return text.GetValueOrDefault(reference.Groups[2].Value,value);
    }
    private const string ValuePattern=@"(?:`[^`]*`|<\d+::[^>]+>)";
    internal string Field(string script,string tag)
    {
        var m=Regex.Match(script,@"(?m)^\s*\["+Regex.Escape(tag)+@"\]\s*("+ValuePattern+")");
        return m.Success?Resolve(m.Groups[1].Value):"";
    }
    internal (string Description,string Icon,int Frame) Describe(string path)
    {
        var script=TextEncoding.GetString(Read(path));
        var icon=Regex.Match(script,@"\[icon\]\s*`([^`]+)`\s+(\d+)");
        return (string.Join("\n\n",new[]{Field(script,"explain"),Field(script,"flavor text"),Field(script,"basic explain")}.Where(s=>!string.IsNullOrWhiteSpace(s))),
            icon.Success?icon.Groups[1].Value:"",icon.Success?int.Parse(icon.Groups[2].Value):0);
    }
    internal byte[] DecorateNames(byte[] script,int id)
    {
        // Latin-1 keeps every unrelated original byte, including Korean comments.
        var text=Encoding.Latin1.GetString(script);
        var after=Regex.Replace(text,@"(?m)(^\s*\[name2?\]\s*)("+ValuePattern+")",m=>
        {
            var value=Resolve(TextEncoding.GetString(Encoding.Latin1.GetBytes(m.Groups[2].Value)));
            var suffix=$" [ID:{id}]";
            if(value.EndsWith(suffix,StringComparison.Ordinal)) return m.Value;
            if(value.StartsWith('<')) value="未翻译 "+value;
            return m.Groups[1].Value+"`"+Encoding.Latin1.GetString(TextEncoding.GetBytes(value+suffix))+"`";
        });
        return Encoding.Latin1.GetBytes(after);
    }
    internal (string Path,int Count) BuildDebugView(string directory)
    {
        Directory.CreateDirectory(directory);
        var destination=Path.Combine(directory,"items-"+SourceHash[..16]+"-v2.pvf");
        var changes=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var family in new[]{"equipment","stackable"})
            foreach(var (id,path) in List(Read(family+"/"+family+".lst")))
            {
                var full=family+"/"+path;
                if(!entries.ContainsKey(full)) continue;
                var before=Read(full); var after=DecorateNames(before,id);
                if(!after.AsSpan().SequenceEqual(before)) changes.Add(full,after);
            }
        // Coin/item/creature shop rows can override the item's catalogue name.
        var shop=Read("etc/newcashshop.etc");
        changes["etc/newcashshop.etc"]=DecorateShopNames(shop);
        // Verify any cached copy against its full content hash, not just its name.
        if(File.Exists(destination)&&File.Exists(destination+".sha256")&&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)))==File.ReadAllText(destination+".sha256")) return(destination,changes.Count);
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using var newBody=new MemoryStream(); newBody.Write(archive.AsSpan(body));
            using var tree=new MemoryStream(); using var writer=new BinaryWriter(tree,Encoding.ASCII,true);
            foreach(var (path,e) in entries)
            {
                int offset=e.Offset,size=e.Size; uint crc=e.Crc;
                if(changes.TryGetValue(path,out var content))
                {
                    var padded=new byte[(content.Length+3)&~3];content.CopyTo(padded,0);
                    crc=Crc(padded,e.Id);offset=checked((int)newBody.Length);size=content.Length;
                    newBody.Write(Crypt(padded,crc,true));
                }
                writer.Write(e.Id);writer.Write(e.Name.Length);writer.Write(e.Name);writer.Write(size);writer.Write(crc);writer.Write(offset);
            }
            while(tree.Length%4!=0) writer.Write((byte)0);
            var table=tree.ToArray(); uint key=Crc(table,(uint)entries.Count);
            using(var file=new FileStream(temporary,FileMode.CreateNew))
            using(var output=new BinaryWriter(file))
            {
                output.Write(archive.AsSpan(0,44));output.Write(table.Length);output.Write(key);output.Write(entries.Count);
                output.Write(Crypt(table,key,true));newBody.Position=0;newBody.CopyTo(file);
            }
            File.Move(temporary,destination,true);
            File.WriteAllText(destination+".sha256",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))));
            return(destination,changes.Count);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    internal static byte[] DecorateShopNames(byte[] source)
    {
        var text=Encoding.Latin1.GetString(source);
        text=Regex.Replace(text,@"(?s)(\[(coin|item|creature)\])(.*?)(\[/\2\])",block=>
        {
            var rows=Regex.Replace(block.Groups[3].Value,@"(?m)^([ \t]*\d+[ \t]+(\d+)[ \t]+[^`\r\n]*`)([^`]*)(`)",row=>
            {
                var suffix=" [ID:"+row.Groups[2].Value+"]";
                return row.Groups[1].Value+row.Groups[3].Value+(row.Groups[3].Value.EndsWith(suffix,StringComparison.Ordinal)?"":suffix)+row.Groups[4].Value;
            });
            return block.Groups[1].Value+rows+block.Groups[4].Value;
        });
        return Encoding.Latin1.GetBytes(text);
    }
    internal static uint U32(ReadOnlySpan<byte> bytes,int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset,4));
    private static byte[] Crypt(ReadOnlySpan<byte> bytes,uint key,bool encode)
    {
        var output=new byte[bytes.Length];
        for(int i=0;i<bytes.Length;i+=4)
        {
            uint value=U32(bytes,i);
            value=encode?BitOperations.RotateLeft(value,6)^key:BitOperations.RotateRight(value^key,6);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(i,4),value);
        }
        return output;
    }
    private static readonly uint[] CrcTable=Enumerable.Range(0,256).Select(i=>{
        uint n=(uint)i;for(int b=0;b<8;b++)n=(n&1)!=0?(n>>1)^0xedb88320:n>>1;return n;
    }).ToArray();
    private static uint Crc(ReadOnlySpan<byte> bytes,uint seed)
    { uint crc=~seed;foreach(byte b in bytes)crc=CrcTable[(crc^b)&255]^(crc>>8);return ~crc; }
}
