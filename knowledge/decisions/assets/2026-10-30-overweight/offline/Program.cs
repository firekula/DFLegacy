using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DFLegacy.Protocol;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var root="G:/Game/DNF_ACT1/DFLegacy/.tmp/test-control-20261029";
var rows=new List<object>();var sources=new List<object>();var errors=new List<object>();
var rx=new Regex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) T=\d+ RX TCP (?<local>\S+) -> (?<remote>\S+) len=(?<length>\d+)(?<trunc> \(truncated\))? hex=(?<hex>[0-9A-Fa-f]*)\r?$");
foreach(var phase in new[]{"closed-tool-v1","closed-tool-v2s"})foreach(var role in "ABCD"){
 var path=$"{root}/{phase}/{role}-netdump.log";var raw=File.ReadAllBytes(path);var lines=Encoding.ASCII.GetString(raw).Split('\n');
 sources.Add(new{path,bytes=raw.Length,sha256=Convert.ToHexString(SHA256.HashData(raw))});
 var flows=new Dictionary<string,Flow>();int index=0;
 foreach(var line in lines){index++;var m=rx.Match(line);if(!m.Success||!m.Groups["remote"].Value.EndsWith(":7001"))continue;
 var key=m.Groups["local"].Value+"->"+m.Groups["remote"].Value;if(!flows.TryGetValue(key,out var f))flows[key]=f=new Flow(false);if(f.Failed)continue;
 var bytes=Convert.FromHexString(m.Groups["hex"].Value);if(m.Groups["trunc"].Success||bytes.Length!=int.Parse(m.Groups["length"].Value)){f.Failed=true;errors.Add(new{phase,role,line=index,reason="truncated_record"});continue;}
 try{foreach(var frame in f.Feed(bytes,DateTime.ParseExact(m.Groups["time"].Value,"yyyy-MM-dd HH:mm:ss.fff",null),index)){
 var type=frame.Bytes[0];var id=frame.Bytes[1];if(!((type==0&&id==12)||(type==1&&id==46)))continue;
 var b=frame.Bytes[6..];GamePayloadCipher.DecryptServerPayload(b);
 if(type==0){if(b.Length<7||b[0]!=16)continue;var n=BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(3));if(n>255||b.Length!=7+n)throw new InvalidDataException();var text=Encoding.GetEncoding(936).GetString(b,7,(int)n);if(!text.Contains("kg"))continue;
 rows.Add(new{phase,role,protocol="NOTI12",text,cp936_hex=Convert.ToHexString(b.AsSpan(7)),first_line=frame.FirstLine,last_line=frame.LastLine,time=frame.LastTime,wire_sha256=Convert.ToHexString(SHA256.HashData(frame.Bytes))});
 }else if(b.Length==2&&b[0]==0&&b[1]==0x15)rows.Add(new{phase,role,protocol="CMD46",body_hex="0015",first_line=frame.FirstLine,last_line=frame.LastLine,time=frame.LastTime});
 }}catch(InvalidDataException){f.Failed=true;errors.Add(new{phase,role,line=index,reason="invalid_stream_no_resync"});}
 }
 foreach(var kv in flows)if(kv.Value.Pending!=0)errors.Add(new{phase,role,reason="pending_tail",bytes=kv.Value.Pending});
}
var result=new{schema="overweight-only/v1",sources,rows,errors,scope="RX TCP7001 only, strict stream framing; emits only kg NOTI12 type16 and CMD46 0015. No other packet bodies retained."};
using(var output=new FileStream(args[0],FileMode.CreateNew))JsonSerializer.Serialize(output,result,new JsonSerializerOptions{WriteIndented=true});
Console.WriteLine(JsonSerializer.Serialize(new{rows,errors}));
sealed record Framed(byte[] Bytes,DateTime FirstTime,DateTime LastTime,int FirstLine,int LastLine,long Offset);
sealed class Segment(int count,DateTime time,int line){public int Count=count;public DateTime Time=time;public int Line=line;}
sealed class Flow(bool tx){
 public bool Tx=tx,Failed;public DateTime? Last;public GameClientCipherState Cipher=new();public int Frames;public int Pending=>buffer.Count;
 readonly List<byte> buffer=[];readonly Queue<Segment> segments=[];long offset;
 public IEnumerable<Framed> Feed(byte[] bytes,DateTime stamp,int line){
  buffer.AddRange(bytes);segments.Enqueue(new Segment(bytes.Length,stamp,line));var result=new List<Framed>();
  var header=Tx?10:6;
  while(buffer.Count>=header){
   var length=BinaryPrimitives.ReadUInt32LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buffer).Slice(2,4));
   if(length<header||length>4*1024*1024)throw new InvalidDataException();if(buffer.Count<length)break;
   var frame=buffer.GetRange(0,(int)length).ToArray();buffer.RemoveRange(0,(int)length);
   var first=segments.Peek();DateTime lastTime=first.Time;int lastLine=first.Line;int remaining=(int)length;
   while(remaining>0){var s=segments.Peek();int count=Math.Min(s.Count,remaining);remaining-=count;s.Count-=count;lastTime=s.Time;lastLine=s.Line;if(s.Count==0)segments.Dequeue();}
   result.Add(new Framed(frame,first.Time,lastTime,first.Line,lastLine,offset));offset+=length;Frames++;
  }return result;
 }
}
