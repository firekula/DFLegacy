using System.Diagnostics;

namespace DFLegacy.Diagnostics;

public sealed record DiagnosticRegistration(string SessionId,string Capability,string UserRoot,string ClientRoot,string Site,string Installation,string Queue,
    DateTimeOffset StartedAt,int LauncherPid,long LauncherCreatedUtcTicks,int GamePid=0,long GameCreatedUtcTicks=0,string ClientVersion="unknown");
internal sealed record DiagnosticLocalEvent(string SessionId,string Capability,string State,DiagnosticIncident Incident,int ProducerPid,long ProducerCreatedUtcTicks);

public sealed class DiagnosticSession
{
    public string DirectoryPath { get; }
    public DiagnosticRegistration Registration { get; private set; }
    private readonly HashSet<string> collected=new(StringComparer.Ordinal);
    public string SessionId=>Registration.SessionId;
    private DiagnosticSession(string directory,DiagnosticRegistration registration) {DirectoryPath=directory;Registration=registration;}
    internal static string NormalizeSite(string serviceUrl)
    {
        if(!Uri.TryCreate(serviceUrl,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||uri.AbsolutePath!="/")throw new ArgumentException("Diagnostic service requires an HTTP(S) account API origin.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    public static DiagnosticSession Start(string clientRoot,string serviceUrl,string sessionId,string? userRoot=null,string clientVersion="unknown")
    {
        if(!DiagnosticProjection.Id(sessionId))throw new ArgumentException("Invalid session identity.");
        var settings=new DiagnosticSettings(userRoot);var site=NormalizeSite(serviceUrl);var scope=settings.SelectScope(clientRoot,site);
        using var sessionLease=DiagnosticFiles.Lease(settings.Root,"sessions.lock");
        var sessions=Path.Combine(settings.Root,"sessions");DiagnosticFiles.Ensure(sessions);
        foreach(var previous in Directory.EnumerateDirectories(sessions).Take(101))
        {
            try {
                var old=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(previous,"session.json"),16384);
                if(old is not null&&old.StartedAt<DateTimeOffset.UtcNow.AddDays(-14)&&!Alive(old.LauncherPid,old.LauncherCreatedUtcTicks)&&!Alive(old.GamePid,old.GameCreatedUtcTicks))new DiagnosticSession(previous,old).CleanupManagedFiles();
            }catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
        }
        if(Directory.EnumerateDirectories(sessions).Take(101).Count()>=100){settings.RecordDrop();throw new IOException("Diagnostic session budget exhausted.");}
        var directory=Path.Combine(settings.Root,"sessions",Guid.NewGuid().ToString("N"));DiagnosticFiles.Ensure(Path.Combine(directory,"events"));
        using var launcher=Process.GetCurrentProcess();var registration=new DiagnosticRegistration(sessionId,Guid.NewGuid().ToString("N"),settings.Root,Path.GetFullPath(clientRoot),site,scope.Installation,scope.Queue,DateTimeOffset.UtcNow,launcher.Id,launcher.StartTime.ToUniversalTime().Ticks,ClientVersion:clientVersion);
        DiagnosticFiles.Write(Path.Combine(directory,"session.json"),registration,false);return new(directory,registration);
    }
    public static DiagnosticSession Open(string directory,string capability,string? userRoot=null)
    {
        var root=DiagnosticFiles.Root(userRoot);var path=Path.GetFullPath(directory);var sessions=Path.Combine(root,"sessions")+Path.DirectorySeparatorChar;
        if(!path.StartsWith(sessions,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(path)!=sessions.TrimEnd(Path.DirectorySeparatorChar))throw new IOException("Session is outside current-user registration.");
        var registration=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(path,"session.json"),16384)??throw new InvalidDataException("Missing session registration.");
        if(!DiagnosticProjection.Id(capability)||registration.Capability!=capability||registration.UserRoot!=root||!DiagnosticProjection.Id(registration.SessionId)||registration.StartedAt<DateTimeOffset.UtcNow.AddDays(-1)||registration.StartedAt>DateTimeOffset.UtcNow.AddMinutes(1))throw new InvalidDataException("Invalid diagnostic registration.");
        var installation=Path.Combine(root,"installations",DiagnosticFiles.Hash(Path.GetFullPath(registration.ClientRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()));
        if(registration.Installation!=installation||Path.GetDirectoryName(registration.Queue)!=Path.Combine(installation,"queues")||!DiagnosticProjection.Id(Path.GetFileName(registration.Queue))||registration.Site!=NormalizeSite(registration.Site))throw new InvalidDataException("Invalid diagnostic scope.");
        return new(path,registration);
    }
    public void BindProcess(Process process)
    {
        Registration=Registration with {GamePid=process.Id,GameCreatedUtcTicks=process.StartTime.ToUniversalTime().Ticks};
        DiagnosticFiles.Write(Path.Combine(DirectoryPath,"session.json"),Registration);
    }
    public void ApplyEnvironment(ProcessStartInfo start)
    {
        start.Environment["DFL_DIAGNOSTIC_SESSION_DIR"]=DirectoryPath;start.Environment["DFL_DIAGNOSTIC_SESSION_ID"]=SessionId;
        start.Environment["DFL_DIAGNOSTIC_CAPABILITY"]=Registration.Capability;
    }
    public bool OwnerAlive()
    {
        var current=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(DirectoryPath,"session.json"),16384)??Registration;
        return Alive(current.LauncherPid,current.LauncherCreatedUtcTicks)||Alive(current.GamePid,current.GameCreatedUtcTicks);
    }
    public void Complete() => DiagnosticFiles.Write(Path.Combine(DirectoryPath,"closed.json"),Registration.Capability);
    public bool IsClosed => DiagnosticFiles.Read<string>(Path.Combine(DirectoryPath,"closed.json"),128)==Registration.Capability;
    private static bool Alive(int pid,long created)
    {
        if(pid<=0||created<=0)return false;
        try {using var process=Process.GetProcessById(pid);return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks==created;}
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
    }
    public void WriteIncident(DiagnosticIncident incident,string state)
    {
        if(state is not ("provisional" or "confirmed" or "cancelled")||!DiagnosticProjection.Id(incident.EventId))throw new ArgumentException("Invalid diagnostic event state.");
        // One file per state transition; provisional events are never interpreted as crashes.
        var events=Path.Combine(DirectoryPath,"events");DiagnosticFiles.NoLinks(events);
        var value=new DiagnosticLocalEvent(SessionId,Registration.Capability,state,incident,Registration.LauncherPid,Registration.LauncherCreatedUtcTicks);
        var bytes=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value,DiagnosticJson.Options);
        if(bytes.Length>16384||Directory.EnumerateFiles(events,"*.json").Take(101).Count()>=100){new DiagnosticSettings(Registration.UserRoot).RecordDrop();return;}
        DiagnosticFiles.Write(Path.Combine(events,incident.EventId+"-"+state+".json"),value);
    }
    private List<DiagnosticLocalEvent> ConfirmedEvents()
    {
        var result=new List<DiagnosticLocalEvent>();var events=Path.Combine(DirectoryPath,"events");DiagnosticFiles.NoLinks(events);
        var owner=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(DirectoryPath,"session.json"),16384)??Registration;
        foreach(var path in Directory.EnumerateFiles(events,"*.json").Take(300))
        {
            try {
                var info=new FileInfo(path);if(info.LastWriteTimeUtc<Registration.StartedAt.UtcDateTime||info.Length>16384)continue;
                var value=DiagnosticFiles.Read<DiagnosticLocalEvent>(path,16384);
                if(value is null||value.SessionId!=SessionId||value.Capability!=Registration.Capability||value.State!="confirmed"||value.Incident is null)continue;
                if(!((value.ProducerPid==owner.LauncherPid&&value.ProducerCreatedUtcTicks==owner.LauncherCreatedUtcTicks)||(owner.GamePid>0&&value.ProducerPid==owner.GamePid&&value.ProducerCreatedUtcTicks==owner.GameCreatedUtcTicks)))continue;
                if(!DiagnosticProjection.Id(value.Incident.EventId)||File.Exists(Path.Combine(events,value.Incident.EventId+"-cancelled.json")))continue;
                result.Add(value);
            }catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
        }
        return result;
    }
    public string? FindConfirmedNativeCause() => ConfirmedEvents().FirstOrDefault(e=>e.Incident.Kind=="crash"&&e.Incident.Evidence is "confirmed-unhandled-exception" or "original-exception-context")?.Incident.EventId;
    public int CollectConfirmed()
    {
        var queue=new DiagnosticQueue(this);if(!queue.IsEligible)return 0;var count=0;
        // Confirm native causes before linked exit summaries regardless of directory enumeration order.
        var events=ConfirmedEvents();IReadOnlyDictionary<string,string>? system=null;
        foreach(var value in events.OrderBy(e=>e.Incident.RelatedEventId is null?0:1))
        {
            try {
                var incident=value.Incident;
                if(collected.Contains(incident.EventId)||queue.ContainsEvent(incident.EventId))continue;
                if(incident.RelatedEventId is not null&&(queue.ContainsEvent(incident.RelatedEventId)||events.Any(e=>e.Incident.EventId==incident.RelatedEventId)))continue;
                system??=DiagnosticProjection.CollectSystem();
                if(queue.Enqueue(DiagnosticProjection.Create(incident,SessionId,Registration.ClientVersion,system))){collected.Add(incident.EventId);count++;}
            } catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
        }
        return count;
    }
    internal void CleanupManagedFiles()
    {
        // Remove only our bounded structured metadata. Unknown files and Task3 local captures are preserved.
        var events=Path.Combine(DirectoryPath,"events");DiagnosticFiles.NoLinks(events);
        if(Directory.Exists(events)) {
            foreach(var path in Directory.EnumerateFiles(events,"*.json").Take(300)){DiagnosticFiles.NoLinks(path);File.Delete(path);}
            if(!Directory.EnumerateFileSystemEntries(events).Any())Directory.Delete(events);
        }
        DiagnosticFiles.NoLinks(DirectoryPath);
        if(!Directory.EnumerateDirectories(DirectoryPath).Any()&&Directory.EnumerateFiles(DirectoryPath).All(p=>Path.GetFileName(p) is "session.json" or "closed.json")) {
            foreach(var path in Directory.EnumerateFiles(DirectoryPath)){DiagnosticFiles.NoLinks(path);File.Delete(path);}Directory.Delete(DirectoryPath);
        }
    }
}
