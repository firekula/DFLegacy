using System.Diagnostics;

namespace DFLegacy.Diagnostics;

public sealed record DiagnosticRegistration(string SessionId,string Capability,string UserRoot,string ClientRoot,string Site,string Installation,string Queue,
    DateTimeOffset StartedAt,int LauncherPid,long LauncherCreatedUtcTicks,int GamePid=0,long GameCreatedUtcTicks=0,string ClientVersion="unknown",bool LocalOnly=false,bool LocalSmallDumps=true,string? ClientPvfSha256=null);
internal sealed record DiagnosticCollection(string SessionId,int LauncherPid,long LauncherCreatedUtcTicks,string[] Ids);
internal sealed record DiagnosticLocalEvent(string SessionId,string Capability,string State,DiagnosticIncident Incident,int ProducerPid,long ProducerCreatedUtcTicks);

public sealed class DiagnosticSession
{
    public string DirectoryPath { get; }
    public DiagnosticRegistration Registration { get; private set; }
    private readonly HashSet<string> collected=new(StringComparer.Ordinal);
    private readonly object completionGate=new();
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
        var settings=new DiagnosticSettings(userRoot);var localOnly=string.IsNullOrWhiteSpace(serviceUrl);var site=localOnly?"local-only":NormalizeSite(serviceUrl);var scope=settings.SelectScope(clientRoot,site);
        using var sessionLease=DiagnosticFiles.Lease(settings.Root,"sessions.lock");
        var sessions=Path.Combine(settings.Root,"sessions");DiagnosticFiles.Ensure(sessions);
        foreach(var previous in Directory.EnumerateDirectories(sessions).Take(101))
        {
            try {
                var old=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(previous,"session.json"),16384);
                if(old is not null&&!Alive(old.GamePid,old.GameCreatedUtcTicks)&&(!Alive(old.LauncherPid,old.LauncherCreatedUtcTicks)||DiagnosticFiles.Read<string>(Path.Combine(previous,"closed.json"),128)==old.Capability))new DiagnosticSession(previous,old).CleanupManagedFiles();
            }catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
        }
        // Logical producer receipts survive registration replacement and helper
        // cleanup. Reclaim only after every original owner is demonstrably dead.
        try {
            using var queueLease=DiagnosticFiles.Lease(settings.Root,"queue.lock");
            var receipts=Path.Combine(settings.Root,"collections");DiagnosticFiles.NoLinks(receipts);
            var liveGames=Directory.EnumerateDirectories(sessions).Take(101).Select(p=>DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(p,"session.json"),16384)).Where(r=>r is not null&&Alive(r.GamePid,r.GameCreatedUtcTicks)).Select(r=>r!.SessionId).ToHashSet(StringComparer.Ordinal);
            if(Directory.Exists(receipts))foreach(var path in Directory.EnumerateFiles(receipts,"*.json").Take(101)) {
                var receipt=DiagnosticFiles.Read<DiagnosticCollection>(path,16384);
                if(receipt is not null&&DiagnosticProjection.Id(receipt.SessionId)&&Path.GetFileNameWithoutExtension(path)==receipt.SessionId&&!Alive(receipt.LauncherPid,receipt.LauncherCreatedUtcTicks)&&!liveGames.Contains(receipt.SessionId))File.Delete(path);
            }
        }catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
        if(Directory.EnumerateDirectories(sessions).Take(101).Count()>=100){settings.RecordDrop();throw new IOException("Diagnostic session budget exhausted.");}
        var directory=Path.Combine(settings.Root,"sessions",Guid.NewGuid().ToString("N"));DiagnosticFiles.Ensure(Path.Combine(directory,"events"));
        using var launcher=Process.GetCurrentProcess();var registration=new DiagnosticRegistration(sessionId,Guid.NewGuid().ToString("N"),settings.Root,Path.GetFullPath(clientRoot),site,scope.Installation,scope.Queue,DateTimeOffset.UtcNow,launcher.Id,launcher.StartTime.ToUniversalTime().Ticks,ClientVersion:clientVersion,LocalOnly:localOnly,LocalSmallDumps:settings.Read().LocalSmallDumps);
        DiagnosticFiles.Write(Path.Combine(directory,"session.json"),registration,false);return new(directory,registration);
    }
    public static DiagnosticSession Open(string directory,string capability,string? userRoot=null)
    {
        var root=DiagnosticFiles.Root(userRoot);var path=Path.GetFullPath(directory);var sessions=Path.Combine(root,"sessions")+Path.DirectorySeparatorChar;
        if(!path.StartsWith(sessions,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(path)!=sessions.TrimEnd(Path.DirectorySeparatorChar))throw new IOException("Session is outside current-user registration.");
        var registration=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(path,"session.json"),16384)??throw new InvalidDataException("Missing session registration.");
        if(!DiagnosticProjection.Id(capability)||registration.Capability!=capability||registration.UserRoot!=root||!DiagnosticProjection.Id(registration.SessionId)||(registration.StartedAt<DateTimeOffset.UtcNow.AddDays(-1)&&!Alive(registration.LauncherPid,registration.LauncherCreatedUtcTicks)&&!Alive(registration.GamePid,registration.GameCreatedUtcTicks))||registration.StartedAt>DateTimeOffset.UtcNow.AddMinutes(1))throw new InvalidDataException("Invalid diagnostic registration.");
        var installation=Path.Combine(root,"installations",DiagnosticFiles.Hash(Path.GetFullPath(registration.ClientRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()));
        if(registration.Installation!=installation||Path.GetDirectoryName(registration.Queue)!=Path.Combine(installation,"queues")||!DiagnosticProjection.Id(Path.GetFileName(registration.Queue))||(registration.LocalOnly?registration.Site!="local-only":registration.Site!=NormalizeSite(registration.Site)))throw new InvalidDataException("Invalid diagnostic scope.");
        return new(path,registration);
    }
    public void BindProcess(Process process)
    {
        Registration=Registration with {GamePid=process.Id,GameCreatedUtcTicks=process.StartTime.ToUniversalTime().Ticks};
        DiagnosticFiles.Write(Path.Combine(DirectoryPath,"session.json"),Registration);
    }
    public void SetPvfIdentity(string? hash)
    {
        Registration=Registration with {ClientPvfSha256=hash is not null&&System.Text.RegularExpressions.Regex.IsMatch(hash,@"\A[a-f0-9]{64}\z")?hash:null};
        try {DiagnosticFiles.Write(Path.Combine(DirectoryPath,"session.json"),Registration);}
        catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ } // Optional identity must not block starting the game.
    }
    // The bounded receipt set outlives queue reclamation. Claim before writing a
    // queue body: a failed write may lose this incident, never refreeze its ID.
    private string CollectionPath=>Path.Combine(Registration.UserRoot,"collections",SessionId+".json");
    internal bool WasCollected(string id) => ReadCollected().Contains(id,StringComparer.Ordinal);
    private string[] ReadCollected()
    {
        var receipt=DiagnosticFiles.Read<DiagnosticCollection>(CollectionPath,16384);
        if(receipt is null)return [];
        if(receipt.SessionId!=SessionId||receipt.LauncherPid!=Registration.LauncherPid||receipt.LauncherCreatedUtcTicks!=Registration.LauncherCreatedUtcTicks||receipt.Ids is null||receipt.Ids.Length>256||receipt.Ids.Any(id=>!DiagnosticProjection.Id(id)))throw new InvalidDataException("Invalid collection receipts.");
        return receipt.Ids;
    }
    internal bool ClaimCollection(string id)
    {
        var ids=ReadCollected();if(ids.Contains(id,StringComparer.Ordinal))return false;
        if(ids.Length>=256)return false;
        var directory=Path.GetDirectoryName(CollectionPath)!;DiagnosticFiles.Ensure(directory);
        if(!File.Exists(CollectionPath)&&Directory.EnumerateFiles(directory,"*.json").Take(101).Count()>=100)return false;
        DiagnosticFiles.Write(CollectionPath,new DiagnosticCollection(SessionId,Registration.LauncherPid,Registration.LauncherCreatedUtcTicks,ids.Append(id).ToArray()));return true;
    }
    public void ApplyEnvironment(ProcessStartInfo start)
    {
        start.Environment["DFL_DIAGNOSTIC_SESSION_DIR"]=DirectoryPath;start.Environment["DFL_DIAGNOSTIC_SESSION_ID"]=SessionId;
        start.Environment["DFL_DIAGNOSTIC_CAPABILITY"]=Registration.Capability;
        start.Environment["DFL_DIAGNOSTIC_SMALL_DUMPS"]=Registration.LocalSmallDumps?"1":"0";
        var helper=Path.Combine(AppContext.BaseDirectory,"DFLegacy.CrashCapture.exe");
        if(File.Exists(helper)){DiagnosticFiles.NoLinks(helper);start.Environment["DFL_DIAGNOSTIC_CAPTURE_HELPER"]=helper;}
    }
    public bool OwnerAlive()
    {
        var current=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(DirectoryPath,"session.json"),16384)??Registration;
        return Alive(current.LauncherPid,current.LauncherCreatedUtcTicks)||Alive(current.GamePid,current.GameCreatedUtcTicks);
    }
    public bool GameAlive()
    {
        // An uploader may have opened this session before BindProcess wrote the
        // game identity. Always check the latest PID and creation-time pair.
        var current=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(DirectoryPath,"session.json"),16384)??Registration;
        return Alive(current.GamePid,current.GameCreatedUtcTicks);
    }
    public void Complete()
    {
        // A valid own-session marker is immutable. Replacing it a second time
        // can collide with a reader during update coordinator/controller handoff.
        lock(completionGate)
        {
            var complete=false;
            try {complete=IsClosed;}
            catch(Exception error) when(error is System.Text.Json.JsonException or InvalidDataException){ }
            if(!complete)DiagnosticFiles.Write(Path.Combine(DirectoryPath,"closed.json"),Registration.Capability);
        }
    }
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
        using var lease=DiagnosticFiles.Lease(events,"events.lock");
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
        FileStream lease;try{lease=DiagnosticFiles.Lease(DirectoryPath,"collect.lock");}catch(IOException){return 0;}
        using(lease) {
        // Confirm native causes before linked exit summaries regardless of directory enumeration order.
        var events=ConfirmedEvents();IReadOnlyDictionary<string,string>? system=null;
        var nativeCause=events.FirstOrDefault(e=>e.Incident.Kind=="crash"&&e.Incident.Evidence is "confirmed-unhandled-exception" or "original-exception-context");
        var exitAlreadyFrozen=events.Any(e=>e.Incident.Stage=="game-exit"&&queue.ContainsEvent(e.Incident.EventId));
        foreach(var value in events.OrderBy(e=>e.Incident.RelatedEventId is null?0:1))
        {
            try {
                var incident=value.Incident;
                // An exceptionally stalled local writer can miss the six-second
                // drain. Preserve its richer evidence locally, without a second
                // automatic report after the unknown exit was already frozen.
                if(exitAlreadyFrozen&&incident.Kind=="crash"&&incident.Evidence is "confirmed-unhandled-exception" or "original-exception-context")continue;
                if(incident.Stage=="game-exit") {
                    if(nativeCause is not null)continue;
                    var eventPath=Path.Combine(DirectoryPath,"events",incident.EventId+"-confirmed.json");
                    // The external native helper can finalize after process exit.
                    // Do not freeze an unlinked summary until that bounded drain ends.
                    if(!File.Exists(eventPath)||File.GetLastWriteTimeUtc(eventPath)>DateTime.UtcNow.AddSeconds(-6))continue;
                }
                if(collected.Contains(incident.EventId)||queue.ContainsEvent(incident.EventId))continue;
                if(incident.RelatedEventId is not null&&(queue.ContainsEvent(incident.RelatedEventId)||events.Any(e=>e.Incident.EventId==incident.RelatedEventId)))continue;
                var current=DiagnosticFiles.Read<DiagnosticRegistration>(Path.Combine(DirectoryPath,"session.json"),16384)??Registration;
                incident=incident with {ClientPvfSha256=incident.ClientPvfSha256??current.ClientPvfSha256};
                system??=DiagnosticProjection.CollectSystem();
                if(queue.Enqueue(DiagnosticProjection.Create(incident,SessionId,Registration.ClientVersion,system))){collected.Add(incident.EventId);count++;}
            } catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
        }
        return count;
        }
    }
    internal void CleanupManagedFiles()
    {
        // Closing/revoking upload does not revoke a running game's local capture.
        if(GameAlive())return;
        // Remove only our bounded structured metadata. Unknown files and Task3 local captures are preserved.
        var events=Path.Combine(DirectoryPath,"events");DiagnosticFiles.NoLinks(events);
        if(Directory.Exists(events)) {
            foreach(var path in Directory.EnumerateFiles(events,"*.json").Take(300)){DiagnosticFiles.NoLinks(path);File.Delete(path);}
            var eventsLock=Path.Combine(events,"events.lock");DiagnosticFiles.NoLinks(eventsLock);if(File.Exists(eventsLock))File.Delete(eventsLock);
            if(!Directory.EnumerateFileSystemEntries(events).Any())Directory.Delete(events);
        }
        DiagnosticFiles.NoLinks(DirectoryPath);
        if(!Directory.EnumerateDirectories(DirectoryPath).Any()&&Directory.EnumerateFiles(DirectoryPath).All(p=>Path.GetFileName(p) is "session.json" or "closed.json" or "collect.lock" or "collected.json")) {
            foreach(var path in Directory.EnumerateFiles(DirectoryPath)){DiagnosticFiles.NoLinks(path);File.Delete(path);}Directory.Delete(DirectoryPath);
        }
    }
}
