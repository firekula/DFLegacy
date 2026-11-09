using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DFLegacy.Launcher.Diagnostics;

internal enum IncidentKind { Error, NetworkError, Crash, Update }

// Rich records remain local; the optional sink receives a separate structured projection only.
internal sealed class IncidentReporter
{
    internal Action<DFLegacy.Diagnostics.DiagnosticIncident>? AutomaticReport { get; set; }
    private readonly object gate = new();
    private readonly Queue<string> context = new();
    private readonly HashSet<string> secrets = new(StringComparer.Ordinal);
    private readonly Dictionary<string,(string Path,string EventId,int Count,DateTimeOffset First)> repeated = new();
    private readonly string directory, version, architecture, installation;
    private readonly int maxFiles;
    private readonly long maxBytes;
    private string clientPvfSha256="unknown";
    internal string SessionId { get; } = Guid.NewGuid().ToString("N");
    internal IncidentReporter(string directory, string? version=null, string? architecture=null, int maxFiles=100, long maxBytes=16*1024*1024)
    {
        this.directory=Path.GetFullPath(directory);
        installation=Path.GetFullPath(Path.Combine(directory,"..","..",".."));
        this.version=version ?? typeof(IncidentReporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        this.architecture=architecture ?? RuntimeInformation.ProcessArchitecture.ToString();
        this.maxFiles=Math.Max(1,maxFiles); this.maxBytes=Math.Max(4096,maxBytes);
    }
    internal void RegisterSecret(string? value) { if(!string.IsNullOrEmpty(value)) lock(gate) { if(secrets.Count<512) secrets.Add(value); } }
    internal string CapturePvfIdentity(string path)
    {
        try { using var file=File.OpenRead(path); lock(gate) return clientPvfSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file)); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { lock(gate) return clientPvfSha256="unknown"; }
    }
    internal string Redact(string value)
    {
        lock(gate)
        {
            foreach(var secret in secrets.OrderByDescending(x=>x.Length)) value=value.Replace(secret,"[redacted]",StringComparison.Ordinal);
            value=Regex.Replace(value,@"(?i)(https?://)[^\s/@]+:[^\s/@]+@","$1[redacted]@");
            value=Regex.Replace(value,@"(https?://[^\s?]+)\?[^\s]+","$1?[redacted]",RegexOptions.IgnoreCase);
            value=Regex.Replace(value,@"(?im)(authorization\s*[:=]\s*).*$","$1[redacted]");
            return Regex.Replace(value,"(?i)([\\\"]?(?:password|passwd|pwd|token|secret|account|username|api[_-]?key|key)[\\\"]?\\s*[:=]\\s*)(?:\\\"[^\\\"]*\\\"|[^\\s,;]+)","$1[redacted]");
        }
    }
    internal void AddContext(string message)
    {
        lock(gate) { context.Enqueue($"{DateTimeOffset.Now:O} {Limit(Redact(message),1024)}"); while(context.Count>64) context.Dequeue(); }
    }
    internal string? Report(IncidentKind kind,string stage,Exception? error=null,string? details=null,string? updateId=null,string? repetitionKey=null,DFLegacy.Diagnostics.DiagnosticIncident? structured=null)
    {
        if(error is OperationCanceledException) return null;
        lock(gate)
        {
            try
            {
                var now=DateTimeOffset.Now;
                var budget=kind==IncidentKind.Update ? 2*1024*1024 : 32768;
                var body=Limit(Redact($"stage={stage}\nerrorCode={(error is null ? "none" : $"0x{error.HResult:X8}")}\n{details}\n{error}"),budget);
                var key=$"{kind}|{repetitionKey ?? body}|{updateId}";
                // Successful runs and independent update operations always receive distinct reports.
                repeated.TryGetValue(key,out var previous);
                var grouped=kind!=IncidentKind.Update && previous.Path is not null && now-previous.First<TimeSpan.FromMinutes(1) && File.Exists(previous.Path);
                var eventId=grouped ? previous.EventId : Guid.NewGuid().ToString("N");
                var count=grouped ? previous.Count+1 : 1;
                var first=grouped ? previous.First : now;
                if(kind!=IncidentKind.Update)
                {
                    var evidence=structured??new DFLegacy.Diagnostics.DiagnosticIncident {
                        Kind=kind==IncidentKind.NetworkError?"networkerror":kind==IncidentKind.Crash&&stage!="game-exit"?"crash":"error",
                        Stage=stage,Evidence=stage=="game-exit"?"abnormal-process-exit":kind==IncidentKind.Crash?"managed-unhandled-exception":"managed-error",
                        ExceptionCode=error is null?"unknown":$"0x{error.HResult:X8}"
                    };
                    // Freeze the first event identity even when the rich local file later aggregates occurrences.
                    try { AutomaticReport?.Invoke(evidence with {EventId=eventId,ClientPvfSha256=clientPvfSha256}); }
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
                }
                ClientLogPaths.EnsureDirectory(directory);
                var path=grouped ? previous.Path! : Path.Combine(directory,$"{kind.ToString().ToLowerInvariant()}-{now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{eventId}.log");
                if(ClientLogPaths.HasLink(path)) return null;
                var report=$"format=DFLegacy-incident-1\nkind={kind}\nsessionId={SessionId}\neventId={eventId}\nversion={version}\narchitecture={architecture}\ninstallation={installation}\npid={Environment.ProcessId}\ntime={first:O}\nlastSeen={now:O}\ntimezone={TimeZoneInfo.Local.Id}\noccurrences={count}\nclientPvfSha256={clientPvfSha256}\nserverVersion=unknown\n";
                if(updateId is not null) report+=$"updateId={updateId}\n";
                report+=body+"\n--- recent context ---\n"+string.Join('\n',context);
                report=Limit(Redact(report),(int)Math.Min(kind==IncidentKind.Update ? 4*1024*1024 : 65536,maxBytes/2));
                var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { var bytes=Encoding.UTF8.GetBytes(report); stream.Write(bytes); } File.Move(temp,path,true); }
                finally { if(File.Exists(temp)) File.Delete(temp); }
                if(repeated.Count>=128) repeated.Clear();
                repeated[key]=(path,eventId,count,first);
                Retain(); return path;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        }
    }
    private void Retain()
    {
        var files=new DirectoryInfo(directory).EnumerateFiles("*.log").Where(f=>Regex.IsMatch(f.Name,@"\A(?:error|networkerror|crash|update)-\d{8}-\d{6}-\d{3}-\d+-[a-f0-9]{32}\.log\z") && !ClientLogPaths.HasLink(f.FullName)).OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();
        long total=0;
        for(var i=0;i<files.Length;i++) { total+=files[i].Length; if(i>=maxFiles || total>maxBytes || files[i].LastWriteTimeUtc<DateTime.UtcNow.AddDays(-14)) try { files[i].Delete(); } catch(IOException) { } }
    }
    internal string? ReportExit(int processId,int exitCode,bool cancelled)
    {
        if(exitCode==0||cancelled)return null;
        string? native=null;
        try
        {
            if(Directory.Exists(directory))
                foreach(var path in Directory.EnumerateFiles(directory,$"crash-*-{processId}-*.log"))
                {
                    if(ClientLogPaths.HasLink(path)||new FileInfo(path).Length>65536)continue;
                    var lines=File.ReadAllLines(path);
                    if(lines.Contains($"sessionId={SessionId}")&&lines.Contains($"pid={processId}")&&lines.Contains("evidence=unhandled-exception-filter")){native=Path.GetFileName(path);break;}
                }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
        return Report(IncidentKind.Crash,"game-exit",details:$"evidence=abnormal-process-exit\ngamePid={processId}\nexitCode=0x{exitCode:X8}\nnativeCause={(native is null ? "unknown" : "see-linked-native-record")}\nnativeReport={native ?? "missing"}\nnativeScene={(native is null ? "missing; a nonzero exit does not establish an unhandled exception" : "available")}");
    }
    internal string? ReportNetwork(DFLegacy.Relay.RelayTermination value) => !value.IsIncident ? null : Report(IncidentKind.NetworkError,"relay-"+value.Phase,
        details:$"evidence=relay-termination\nreason={value.Reason}\nprotocol={value.Protocol}\ndirection={value.Direction}\nport={value.Port}\nconnectionId={value.ConnectionId}\nelapsedMs={value.ElapsedMilliseconds:F0}\nclientToServerBytes={value.ClientToServerBytes}\nserverToClientBytes={value.ServerToClientBytes}\nsocketError={value.SocketError}\nsocketCode={(int?)value.SocketError}\nnativeError={value.NativeError}\nattempts={value.Attempts}",repetitionKey:$"{value.Reason}|{value.Protocol}|{value.Phase}|{value.Port}|{value.Direction}|{value.SocketError}",
        structured:new DFLegacy.Diagnostics.DiagnosticIncident {Kind="networkerror",Stage="relay-"+value.Phase,Evidence="relay-termination",Protocol=value.Protocol.ToLowerInvariant(),Port=value.Port,SocketCode=(int?)value.SocketError});
    internal UpdateIncidentScope BeginUpdate(string stage) => new(this,stage);
    private static string Limit(string value,int max) { if(Encoding.UTF8.GetByteCount(value)<=max) return value; var chars=Math.Min(value.Length,max/3); return value[..chars]+"\n[truncated by diagnostic budget]"; }
}

internal sealed class UpdateIncidentScope(IncidentReporter reporter,string stage) : IDisposable
{
    private readonly List<string> results=[];
    private int omitted;
    private bool complete;
    internal string Id { get; }=Guid.NewGuid().ToString("N");
    internal void AddResult(string result) { if(results.Count<4096) results.Add(result.Length>4096 ? result[..4096]+"[truncated]" : result); else omitted++; }
    internal void Complete(string status,Exception? error=null)
    {
        if(complete) return; complete=true;
        reporter.Report(IncidentKind.Update,stage,details:$"result={status}\nomittedResults={omitted}\n"+string.Join('\n',results),updateId:Id);
        if(error is not null) reporter.Report(IncidentKind.Error,stage,error,updateId:Id);
    }
    public void Dispose() { if(!complete) Complete("incomplete-or-cancelled"); }
}
