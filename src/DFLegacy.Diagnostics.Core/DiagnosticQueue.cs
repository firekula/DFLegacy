using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Diagnostics;

public sealed record PendingDiagnostic(string EventId,byte[] Body);
public sealed record DiagnosticUploadResult(string Status,string? ReceiptId=null);
public sealed class DiagnosticUploadOptions
{
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ExhaustedRetryDelay { get; init; } = TimeSpan.FromMinutes(1);
}
internal sealed record FrozenDiagnostic(string EventId,string Scope,string Body,string Hash,DateTimeOffset CreatedAt,int Attempts=0,
    DateTimeOffset NextAttempt=default,string Status="pending",string? ReceiptId=null,string AttemptRun="");
internal sealed record DiagnosticSiteBackoff(string Site,DateTimeOffset Until);

public sealed class DiagnosticQueue(DiagnosticSession session)
{
    private DiagnosticRegistration Registration=>session.Registration;
    private string Run=>$"{Registration.LauncherPid}:{Registration.LauncherCreatedUtcTicks}";
    private const long MaxBytes=32L*1024*1024;
    public bool IsEligible
    {
        get {
            try {
                if(Registration.LocalOnly)return false;
                var preference=new DiagnosticSettings(Registration.UserRoot).Read();
                var active=DiagnosticFiles.Read<DiagnosticScope>(Path.Combine(Registration.Installation,"active.json"),4096);
                return preference.Enabled&&active is not null&&active.PreferenceEpoch==preference.Epoch&&active.Site==Registration.Site&&active.Epoch==Path.GetFileName(Registration.Queue);
            } catch(Exception error) when(DiagnosticFiles.Recoverable(error)){return false;}
        }
    }
    private string Storage=>Path.Combine(Registration.UserRoot,"queue");
    private string BackoffPath=>Path.Combine(Registration.Installation,"site-backoff.json");
    private string PathFor(string id)=>Path.Combine(Storage,id+".json");
    internal bool ContainsEvent(string id)=>DiagnosticProjection.Id(id)&&(session.WasCollected(id)||(File.Exists(PathFor(id))&&Read(PathFor(id))?.Scope==Registration.Queue));
    public bool Enqueue(DiagnosticReport report)
    {
        try {
            if(!IsEligible||report.SessionId!=session.SessionId||!DiagnosticProjection.Valid(report))return false;
            var body=DiagnosticJson.Serialize(report);if(body.Length>256*1024)return false;
            using var held=DiagnosticFiles.Lease(Registration.UserRoot,"queue.lock");
            if(!IsEligible)return false;
            DiagnosticFiles.Ensure(Storage);
            // Never delete a frozen body while another process is sending it.
            try {using var upload=DiagnosticFiles.Lease(Registration.UserRoot,"upload.lock");Maintain(body.Length*2);}catch(IOException){ }
            var path=PathFor(report.EventId);if(File.Exists(path)||session.WasCollected(report.EventId))return false;
            var files=Files();if(files.Length>=100||files.Sum(f=>new FileInfo(f).Length)+body.Length*2>MaxBytes){new DiagnosticSettings(Registration.UserRoot).RecordDrop();return false;}
            if(!session.ClaimCollection(report.EventId)){new DiagnosticSettings(Registration.UserRoot).RecordDrop();return false;}
            try {DiagnosticFiles.Write(path,new FrozenDiagnostic(report.EventId,Registration.Queue,Convert.ToBase64String(body),Convert.ToHexStringLower(SHA256.HashData(body)),DateTimeOffset.UtcNow),false);return true;}
            catch(Exception error) when(DiagnosticFiles.Recoverable(error)){new DiagnosticSettings(Registration.UserRoot).RecordDrop();throw;}
        }catch(Exception error) when(DiagnosticFiles.Recoverable(error)){return false;}
    }
    private string[] Files()
    {
        DiagnosticFiles.NoLinks(Storage);
        return Directory.Exists(Storage)?Directory.EnumerateFiles(Storage,"*.json").Where(p=>DiagnosticProjection.Id(Path.GetFileNameWithoutExtension(p))).Take(101).ToArray():[];
    }
    private void Maintain(long admissionBytes=0)
    {
        var remaining=new List<(string Path,FrozenDiagnostic Value,long Size)>();
        foreach(var path in Files()) {
            DiagnosticFiles.NoLinks(path);
            var value=DiagnosticFiles.Read<FrozenDiagnostic>(path,400000);
            if(value is null)continue;
            if(value.CreatedAt<DateTimeOffset.UtcNow.AddDays(-14)) {
                PreserveCollectionIdentity(value);File.Delete(path);
                if(value.Status!="uploaded")new DiagnosticSettings(Registration.UserRoot).RecordDrop();
            }else remaining.Add((path,value,new FileInfo(path).Length));
        }
        if(admissionBytes==0)return;
        var count=remaining.Count;var bytes=remaining.Sum(f=>f.Size);
        foreach(var sent in remaining.Where(f=>f.Value.Status=="uploaded").OrderBy(f=>f.Value.CreatedAt)) {
            if(count<100&&bytes+admissionBytes<=MaxBytes)break;
            PreserveCollectionIdentity(sent.Value);
            File.Delete(sent.Path);count--;bytes-=sent.Size;
        }
    }
    private void PreserveCollectionIdentity(FrozenDiagnostic frozen)
    {
        // Backfill receipts for registrations created by an earlier helper.
        // No matching registration means there is no remaining event source.
        var body=Body(frozen);if(body is null)return;
        var report=JsonSerializer.Deserialize<DiagnosticReport>(body,DiagnosticJson.Options)!;
        foreach(var directory in Directory.EnumerateDirectories(Path.Combine(Registration.UserRoot,"sessions")).Take(101)) {
            var owner=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(directory,"session.json"),16384);
            if(owner is not null&&owner.SessionId==report.SessionId&&owner.Queue==frozen.Scope) {
                var source=DiagnosticSession.Open(directory,owner.Capability,Registration.UserRoot);
                if(!source.WasCollected(report.EventId)&&!source.ClaimCollection(report.EventId))throw new IOException("Collection receipt budget exhausted.");
            }
        }
    }
    private FrozenDiagnostic? Read(string path)
    {
        var value=DiagnosticFiles.Read<FrozenDiagnostic>(path,400000);
        if(value is null||!DiagnosticProjection.Id(value.EventId)||PathFor(value.EventId)!=path||value.Attempts is <0 or >3||value.CreatedAt<DateTimeOffset.UtcNow.AddDays(-14)||value.CreatedAt>DateTimeOffset.UtcNow.AddMinutes(1))return null;
        return value;
    }
    private static byte[]? Body(FrozenDiagnostic value)
    {
        try {
            var bytes=Convert.FromBase64String(value.Body);if(bytes.Length>256*1024||Convert.ToHexStringLower(SHA256.HashData(bytes))!=value.Hash)return null;
            var report=JsonSerializer.Deserialize<DiagnosticReport>(bytes,DiagnosticJson.Options);
            return report is not null&&report.EventId==value.EventId&&DiagnosticProjection.Valid(report)?bytes:null;
        }catch(Exception error) when(error is JsonException or FormatException or ArgumentException){return null;}
    }
    public IReadOnlyList<PendingDiagnostic> ReadPending()
    {
        var result=new List<PendingDiagnostic>();if(!IsEligible)return result;
        foreach(var path in Files()) {
            try {var value=Read(path);if(value is not null&&value.Scope==Registration.Queue
                &&(value.Status is "pending" or "retry"||(value.Status=="exhausted"&&value.AttemptRun!=Run))
                &&(value.Attempts<3||value.AttemptRun!=Run)&&Body(value) is { } body)result.Add(new(value.EventId,body));}
            catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
        }
        return result;
    }
    public async Task<DiagnosticUploadResult> UploadPendingAsync(DiagnosticUploadOptions? options=null,CancellationToken cancellationToken=default)
    {
        options??=new();if(!IsEligible)return new("disabled");FileStream lease;
        try {lease=DiagnosticFiles.Lease(Registration.UserRoot,"upload.lock");}
        catch(IOException){return new("busy");}
        using(lease)
        using(var revoked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var monitor=MonitorRevocation(revoked);
            try {
                using(var queueLease=DiagnosticFiles.Lease(Registration.UserRoot,"queue.lock"))Maintain();
                var backoff=DiagnosticFiles.Read<DiagnosticSiteBackoff>(BackoffPath,4096);
                if(backoff is not null&&backoff.Site==Registration.Site&&backoff.Until>DateTimeOffset.UtcNow)return new("retry");
                using var handler=new SocketsHttpHandler {AllowAutoRedirect=false,UseCookies=false,UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(5),MaxConnectionsPerServer=1};
                using var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
                var result=new DiagnosticUploadResult("idle");
                foreach(var pending in ReadPending()) {
                    if(!IsEligible)return new("disabled");var path=PathFor(pending.EventId);var item=Read(path);if(item is null||item.NextAttempt>DateTimeOffset.UtcNow)continue;
                    var bytes=Body(item);if(bytes is null)continue;
                    if(item.AttemptRun!=Run)item=item with {Attempts=0,Status="pending",AttemptRun=Run};
                    while(item.Attempts<3) {
                        revoked.Token.ThrowIfCancellationRequested();if(!IsEligible)return new("disabled");
                        item=item with {Attempts=item.Attempts+1};DiagnosticFiles.Write(path,item);
                        string status;string? receipt=null;DateTimeOffset next=default;var rateLimited=false;
                        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(revoked.Token);
                        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(options.RequestTimeout.TotalMilliseconds,100,30000)));
                        try {
                            using var request=new HttpRequestMessage(HttpMethod.Post,Registration.Site+"/api/client-diagnostics/reports") {Content=new ThrottledJsonContent(bytes)};
                            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
                            var code=(int)response.StatusCode;
                            if(code is 200 or 201) {
                                await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                                using var memory=new MemoryStream();var buffer=new byte[1024];int count;
                                while((count=await stream.ReadAsync(buffer,timeout.Token).ConfigureAwait(false))>0) {if(memory.Length+count>4096)throw new InvalidDataException("Oversized diagnostic receipt.");await memory.WriteAsync(buffer.AsMemory(0,count),timeout.Token).ConfigureAwait(false);}
                                using var json=JsonDocument.Parse(memory.ToArray());var value=json.RootElement;
                                receipt=value.GetProperty("receiptId").GetString();
                                status=DiagnosticProjection.Id(receipt)&&value.GetProperty("eventId").GetString()==item.EventId&&value.GetProperty("status").GetString()=="stored"?"uploaded":"retry";
                            } else status=code switch {404=>"unsupported",410=>"deleted",409=>"conflict",>=300 and <400=>"redirect-rejected",429=>"retry",>=500=>"retry",_=>"rejected"};
                            if(code==429) {
                                rateLimited=true;
                                var delay=response.Headers.RetryAfter?.Delta??(response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow)??TimeSpan.FromSeconds(60);
                                next=DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(delay.TotalSeconds,1,86400));
                                DiagnosticFiles.Write(BackoffPath,new DiagnosticSiteBackoff(Registration.Site,next));
                            }
                        }catch(Exception error) when(error is HttpRequestException or IOException or JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException) {
                            if(revoked.IsCancellationRequested)throw new OperationCanceledException(revoked.Token);status="retry";
                        }
                        if(status=="retry"&&item.Attempts>=3){status="exhausted";if(next==default)next=DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(options.ExhaustedRetryDelay.TotalMilliseconds,0,3600000));}
                        item=item with {Status=status,ReceiptId=status=="uploaded"?receipt:null,NextAttempt=next};DiagnosticFiles.Write(path,item);
                        result=new(status,item.ReceiptId);
                        if(rateLimited)return result;
                        if(status!="retry"||next!=default)break;
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(options.RetryDelay.TotalMilliseconds,0,30000)),revoked.Token).ConfigureAwait(false);
                    }
                }
                return result;
            }catch(OperationCanceledException){return new(IsEligible?"cancelled":"disabled");}
            catch(Exception error) when(DiagnosticFiles.Recoverable(error)){return new("local-error");}
            finally {revoked.Cancel();await monitor.ConfigureAwait(false);}
        }
    }
    private async Task MonitorRevocation(CancellationTokenSource revoked)
    {
        try {while(true){await Task.Delay(50,revoked.Token).ConfigureAwait(false);if(!IsEligible){revoked.Cancel();return;}}}
        catch(OperationCanceledException){ }
    }
    private sealed class ThrottledJsonContent:HttpContent
    {
        private readonly byte[] bytes;
        public ThrottledJsonContent(byte[] bytes){this.bytes=bytes;Headers.ContentType=new MediaTypeHeaderValue("application/json");}
        protected override bool TryComputeLength(out long length){length=bytes.Length;return true;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>SerializeToStreamAsync(stream,context,CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken token)
        {
            for(var offset=0;offset<bytes.Length;offset+=4096) {var count=Math.Min(4096,bytes.Length-offset);await Task.Delay(TimeSpan.FromSeconds(count/65536d),token).ConfigureAwait(false);await stream.WriteAsync(bytes.AsMemory(offset,count),token).ConfigureAwait(false);}
        }
    }
}
