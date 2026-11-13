using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DFLegacy.Launcher;

internal sealed class ChapterTwoDebugIcons
{
    private sealed record ImageEntry(string File,int Offset,int Size);
    private readonly Dictionary<string,ImageEntry> entries=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,BitmapSource?> cache=new(StringComparer.OrdinalIgnoreCase);
    internal ChapterTwoDebugIcons(string clientDirectory)
    {
        foreach(var path in Directory.EnumerateFiles(Path.Combine(clientDirectory,"ImagePacks2"),"sprite_item*.NPK"))
        {
            using var file=File.OpenRead(path);using var reader=new BinaryReader(file);
            if(Encoding.ASCII.GetString(reader.ReadBytes(16))!="NeoplePack_Bill\0") continue;
            int count=reader.ReadInt32();if(count<0||count>20000||(long)count*264+20>file.Length)continue;
            for(int i=0;i<count;i++)
            {
                int offset=reader.ReadInt32(),size=reader.ReadInt32();
                var name=Encoding.ASCII.GetString(reader.ReadBytes(256)).TrimEnd('\0');
                if(offset<20+count*264||size<32||(long)offset+size>file.Length||size>32*1024*1024)continue;
                entries.TryAdd(name.Replace('\\','/'),new(path,offset,size));
            }
        }
    }
    internal BitmapSource? Get(string icon,int frame)
    {
        if(string.IsNullOrWhiteSpace(icon)||frame<0)return null;
        var key=icon+":"+frame;if(cache.TryGetValue(key,out var found))return found;
        BitmapSource? result=null;
        if(entries.TryGetValue("sprite/"+icon.Replace('\\','/'),out var e))
        {
            using var stream=File.OpenRead(e.File);stream.Position=e.Offset;
            var bytes=new byte[e.Size];stream.ReadExactly(bytes);result=Decode(bytes,frame);
        }
        if(cache.Count>=256)cache.Clear();cache[key]=result;return result;
    }
    internal static BitmapSource? Decode(byte[] bytes,int requested)
    {
        static int N(byte[] data,int at)=>checked((int)ChapterTwoDebugResources.U32(data,at));
        if(bytes.Length<32||!bytes.AsSpan(0,17).SequenceEqual("Neople Image File"u8)||N(bytes,24)!=1)return null;
        int count=N(bytes,28);if(count<0||count>20000||requested<0||requested>=count)return null;
        var frames=new List<(int Color,int Compression,int Width,int Height,int At,int Size,int Link)>();
        int position=32;
        for(int i=0;i<count;i++)
        {
            if(position>bytes.Length-8)return null;
            int color=N(bytes,position),mode=N(bytes,position+4);
            if(color==17){frames.Add((color,0,0,0,0,0,mode));position+=8;continue;}
            if(position>bytes.Length-36||color is <14 or >16)return null;
            int w=N(bytes,position+8),h=N(bytes,position+12),size=N(bytes,position+16);
            position+=36;
            if(w<1||w>2048||h<1||h>2048||size<0||size>bytes.Length-position)return null;
            frames.Add((color,mode,w,h,position,size,-1));position+=size;
        }
        var visited=new HashSet<int>();var index=requested;
        while(index>=0&&index<count&&visited.Add(index))
        {
            var f=frames[index];if(f.Color==17){index=f.Link;continue;}
            int expected=checked(f.Width*f.Height*(f.Color==16?4:2));var pixels=new byte[expected];
            if(f.Compression==6)
            {
                using var compressed=new ZLibStream(new MemoryStream(bytes,f.At,f.Size),CompressionMode.Decompress);
                compressed.ReadExactly(pixels);if(compressed.ReadByte()!=-1)return null;
            }
            else if(f.Compression==5&&f.Size==expected)bytes.AsSpan(f.At,f.Size).CopyTo(pixels);
            else return null;
            var bgra=new byte[checked(f.Width*f.Height*4)];
            for(int p=0;p<f.Width*f.Height;p++)
            {
                if(f.Color==16){pixels.AsSpan(p*4,4).CopyTo(bgra.AsSpan(p*4,4));continue;}
                int v=pixels[p*2]|pixels[p*2+1]<<8;
                if(f.Color==14){bgra[p*4]=(byte)((v&31)*255/31);bgra[p*4+1]=(byte)((v>>5&31)*255/31);bgra[p*4+2]=(byte)((v>>10&31)*255/31);bgra[p*4+3]=(byte)((v&0x8000)!=0?255:0);}
                else {bgra[p*4]=(byte)((v&15)*17);bgra[p*4+1]=(byte)((v>>4&15)*17);bgra[p*4+2]=(byte)((v>>8&15)*17);bgra[p*4+3]=(byte)((v>>12&15)*17);}
            }
            var image=BitmapSource.Create(f.Width,f.Height,96,96,PixelFormats.Bgra32,null,bgra,f.Width*4);image.Freeze();return image;
        }
        return null;
    }
}
