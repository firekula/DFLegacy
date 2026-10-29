using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DFLegacy.Protocol;

if(args.Length%2!=0)throw new ArgumentException("Use named argument/value pairs");
var a=Enumerable.Range(0,args.Length/2).ToDictionary(i=>args[i*2],i=>args[i*2+1]);
var input=a["--input"];var output=a["--output"];var client=a["--client"];
var port=a.GetValueOrDefault("--port");var self=a.GetValueOrDefault("--self-uid");
DateTime? from=a.TryGetValue("--from",out var f)?DateTime.Parse(f):null,to=a.TryGetValue("--to",out var t)?DateTime.Parse(t):null;
byte[] snapshot;long captureLength;
using(var fs=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
 captureLength=fs.Length;if(captureLength>256*1024*1024)throw new InvalidDataException("Input exceeds 256 MiB bound");
 snapshot=new byte[(int)captureLength];fs.ReadExactly(snapshot);
}
var text=Encoding.ASCII.GetString(snapshot);var completeEnd=text.LastIndexOf('\n');
var lines=(completeEnd>=0?text[..(completeEnd+1)]:"").Split('\n');
var rx=new Regex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) T=\d+ (?<dir>TX|RX) TCP (?<local>\S+) -> (?<remote>\S+) len=(?<length>\d+)(?<trunc> \(truncated\))? hex=(?<hex>[0-9A-Fa-f]*)\r?$",RegexOptions.Compiled);
var inventory=new Dictionary<string,int>();var flows=new Dictionary<string,Flow>();var rows=new List<object>();var errors=new List<object>();int selectedErrors=0,otherDecodeFailures=0;
for(var index=0;index<lines.Length;index++){
 var m=rx.Match(lines[index]);if(!m.Success)continue;
 var remote=m.Groups["remote"].Value;var direction=m.Groups["dir"].Value;var key=direction+" "+m.Groups["local"].Value+" -> "+remote;
 inventory.TryGetValue(key,out var n);inventory[key]=n+1;
 if(port==null||!remote.EndsWith(":"+port,StringComparison.Ordinal))continue;
 if(!flows.TryGetValue(key,out var flow))flows[key]=flow=new Flow(direction=="TX");
 if(flow.Failed)continue;
 var stamp=DateTime.ParseExact(m.Groups["time"].Value,"yyyy-MM-dd HH:mm:ss.fff",null);
 var raw=Convert.FromHexString(m.Groups["hex"].Value);var declared=int.Parse(m.Groups["length"].Value);
 if(m.Groups["trunc"].Success||raw.Length!=declared){flow.Failed=true;errors.Add(new{flow=key,line=index+1,reason="truncated_socket_record"});continue;}
 if(flow.Last is DateTime previous&&stamp<previous){flow.Failed=true;errors.Add(new{flow=key,line=index+1,reason="timestamp_regressed_no_resynchronization"});continue;}flow.Last=stamp;
 try{
 foreach(var frame in flow.Feed(raw,stamp,index+1)){
  var type=frame.Bytes[0];var id=frame.Bytes[1];bool target=flow.Tx?(type==1&&id==30):(((type==0&&id==19)||(type==1&&id==30)));
  byte[]? body=null;bool valid=true;string mode;
  if(flow.Tx){
   var request=new PacketFrame(type,id,BinaryPrimitives.ReadUInt32LittleEndian(frame.Bytes.AsSpan(6,4)),frame.Bytes[10..]);
   valid=flow.Cipher.TryDecode(request,out var decoded);mode="GameClientCipherState+CRC32";
   if(valid)body=decoded.Body;
   if(!valid){if(target)selectedErrors++;else otherDecodeFailures++;}
   if(target&&valid&&(request.DeclaredCrc32==0||Crc32.Compute(body!)!=request.DeclaredCrc32)){valid=false;selectedErrors++;}
  }else{mode="GamePayloadCipher.DecryptServerPayload";if(target){body=frame.Bytes[6..];GamePayloadCipher.DecryptServerPayload(body);}}
  if(!target)continue;
  if(!valid||body==null){errors.Add(new{flow=key,line=frame.LastLine,reason="selected_cipher_failed"});continue;}
  if((from!=null&&frame.LastTime<from)||(to!=null&&frame.FirstTime>to))continue;
  if(flow.Tx){if(body.Length!=4){errors.Add(new{flow=key,line=frame.LastLine,reason="CMD30_request_length"});continue;}
   rows.Add(new{client,direction,protocol="CMD30",sequence=BinaryPrimitives.ReadUInt16LittleEndian(body),source=(int)body[2],destination=(int)body[3],first_line=frame.FirstLine,last_line=frame.LastLine,time=frame.LastTime,wire_sha256=Hash(frame.Bytes),decode=mode});
  }else if(id==30){rows.Add(new{client,direction,protocol="CMD30",payload_hex=Convert.ToHexString(body),first_line=frame.FirstLine,last_line=frame.LastLine,time=frame.LastTime,wire_sha256=Hash(frame.Bytes),decode=mode});}
  else{if(body.Length<4||body.Length!=4+3*body[2]||body[^1]!=0){errors.Add(new{flow=key,line=frame.LastLine,reason="NOTI19_shape"});continue;}
   rows.Add(new{client,direction,protocol="NOTI19",sp=BinaryPrimitives.ReadUInt16LittleEndian(body),skills=Enumerable.Range(0,body[2]).Select(i=>new{slot=body[3+i*3],id=body[4+i*3],level=body[5+i*3]}).ToArray(),first_line=frame.FirstLine,last_line=frame.LastLine,time=frame.LastTime,wire_sha256=Hash(frame.Bytes),decode=mode});
  }
 }
 }catch(InvalidDataException){flow.Failed=true;errors.Add(new{flow=key,line=index+1,reason="invalid_tcp_frame_no_resynchronization"});}
}
var result=new{schema="skillbar-only/v1",client,input,input_captured_length=captureLength,input_captured_sha256=Hash(snapshot),complete_line_bytes=completeEnd+1,partial_final_line_bytes=text.Length-completeEnd-1,port,from,to,endpoint_inventory=inventory,rows,errors,selected_decode_failures=selectedErrors,other_decode_failures=otherDecodeFailures,streams=flows.Select(k=>new{flow=k.Key,k.Value.Failed,pending_bytes=k.Value.Pending,frames=k.Value.Frames}),scope="Only CMD30 slot requests/ACK and NOTI19 skill triples emitted. Socket return log timestamps are not native apply or wire transmission times. Last partial live record/frame remains pending; no scan resync."};
using(var dest=new FileStream(output,FileMode.CreateNew))JsonSerializer.Serialize(dest,result,new JsonSerializerOptions{WriteIndented=true});
Console.WriteLine(JsonSerializer.Serialize(new{client,port,input_captured_length=captureLength,input_captured_sha256=Hash(snapshot),selected_rows=rows.Count,errors=errors.Count,selectedErrors,otherDecodeFailures,endpoint_inventory=port==null?inventory:null,output}));
static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
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
