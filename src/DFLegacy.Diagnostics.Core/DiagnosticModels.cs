using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DFLegacy.Diagnostics;

public sealed class DiagnosticOptions
{
    public bool Enabled { get; set; } = true;
    public string ServiceUrl { get; set; } = "";
}

// Local input contains structured evidence only. There is deliberately no exception/log/dump field.
public sealed record DiagnosticIncident
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = "error";
    public string Stage { get; init; } = "unknown";
    public string Evidence { get; init; } = "managed-error";
    public string Architecture { get; init; } = "unknown";
    public string ExceptionCode { get; init; } = "unknown";
    public string Module { get; init; } = "unknown";
    public string ModuleHash { get; init; } = "unknown";
    public string ModuleOffset { get; init; } = "unknown";
    public string[] Stack { get; init; } = [];
    public string? RelatedEventId { get; init; }
    public string Protocol { get; init; } = "unknown";
    public int Port { get; init; }
    public string? ClientPvfSha256 { get; init; }
    public int? SocketCode { get; init; }
}

public sealed record DiagnosticReport(string Format, string EventId, string SessionId, string Kind, DateTimeOffset OccurredAt,
    string ClientVersion, string Architecture, string Evidence, string Stage, string ExceptionCode, string Module,
    string ModuleHash, string ModuleOffset, string[] Stack, int Occurrences, Dictionary<string,string> System, string Context, string? ClientPvfSha256=null);

public static class DiagnosticJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 8, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static byte[] Serialize(DiagnosticReport report) => JsonSerializer.SerializeToUtf8Bytes(report, Options);
}

public static class DiagnosticProjection
{
    private static readonly Regex Token = new(@"\A[A-Za-z0-9_.+\-]{1,128}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Leaf = new(@"\A[A-Za-z0-9_.\-]{1,128}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hex = new(@"\A0x[0-9A-Fa-f]{1,16}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hash = new(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Frame = new(@"\A[A-Za-z0-9_.\-]{1,128}\+0x[0-9A-Fa-f]{1,16}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Unsafe = new(@"(?:[A-Za-z]:[\\/]|\\\\|://|(?:^|[\s""'=:(])/\S|(?:password|passwd|pwd|token|authorization|secret|account|username|api[_-]?key)[""']?\s*[:=]|\bBearer\s+\S+)", RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SystemKeys = new(StringComparer.Ordinal) { "osVersion","osBuild","osArchitecture","processArchitecture","language","cpuModel","logicalProcessors","totalMemoryBytes","availableMemoryBytes","gpuModel","gpuDriverVersion","gpuMemoryBytes","gameResolution","windowMode","renderer","adapter","dpi","uptimeSeconds" };
    internal static bool Id(string? value) => value is not null && value.Length==32 && Guid.TryParseExact(value,"N",out _) && value==value.ToLowerInvariant();
    private static string SafeToken(string? value,int limit=128) => value is not null && value.Length<=limit && Token.IsMatch(value)?value:"unknown";
    private static string SafeText(string? value) => value is not null && value.Length<=256 && !value.Any(char.IsControl) && !Unsafe.IsMatch(value)?value:"unknown";
    internal static bool Valid(DiagnosticReport report) => report.Format=="DFLegacy-client-diagnostic-1"&&Id(report.EventId)&&Id(report.SessionId)
        &&report.Kind is "error" or "crash" or "networkerror"&&report.OccurredAt!=default&&report.OccurredAt.Offset==TimeSpan.Zero
        &&SafeToken(report.ClientVersion)==report.ClientVersion&&SafeToken(report.Stage,64)==report.Stage&&SafeToken(report.Evidence,64)==report.Evidence
        &&report.Architecture is "x86" or "x64" or "arm64" or "unknown"&&report.ExceptionCode is not null&&(report.ExceptionCode=="unknown"||Hex.IsMatch(report.ExceptionCode))
        &&report.Module is not null&&Leaf.IsMatch(report.Module)&&!report.Module.Contains("..",StringComparison.Ordinal)
        &&report.ModuleHash is not null&&(report.ModuleHash=="unknown"||Hash.IsMatch(report.ModuleHash))
        &&(report.ClientPvfSha256 is null||report.ClientPvfSha256=="unknown"||Hash.IsMatch(report.ClientPvfSha256))
        &&report.ModuleOffset is not null&&(report.ModuleOffset=="unknown"||Hex.IsMatch(report.ModuleOffset))
        &&report.Stack is not null&&report.Stack.Length<=32&&report.Stack.All(f=>f is not null&&f.Length<=160&&!f.Contains("..",StringComparison.Ordinal)&&Frame.IsMatch(f))
        &&report.Occurrences is >=1 and <=1000000&&report.System is not null&&report.System.Count<=18&&report.System.All(p=>SystemKeys.Contains(p.Key)&&SafeText(p.Value)==p.Value)
        &&report.Context is not null&&(report.Context=="Structured diagnostic evidence"||Regex.IsMatch(report.Context,@"\Aprotocol (tcp|udp|unknown); port [0-9]{1,5}; socket (unknown|-?[0-9]{1,10})\z",RegexOptions.CultureInvariant));

    public static DiagnosticReport Create(DiagnosticIncident incident,string sessionId,string version,IReadOnlyDictionary<string,string>? system=null)
    {
        if(!Id(sessionId)||!Id(incident.EventId)) throw new ArgumentException("Invalid diagnostic identity.");
        var hardware = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var key in SystemKeys) hardware[key] = system is not null && system.TryGetValue(key,out var value)?SafeText(value):"unknown";
        var kind=incident.Kind is "crash" or "networkerror"?incident.Kind:"error";
        var protocol=incident.Protocol is "tcp" or "udp"?incident.Protocol:"unknown";
        // Context is generated from bounded enums/numbers. Never sanitize arbitrary logs into this field.
        var context=kind=="networkerror"?$"protocol {protocol}; port {Math.Clamp(incident.Port,0,65535)}; socket {incident.SocketCode?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"unknown"}":"Structured diagnostic evidence";
        return new("DFLegacy-client-diagnostic-1",incident.EventId,sessionId,kind,DateTimeOffset.UtcNow,SafeToken(version),
            incident.Architecture is "x86" or "x64" or "arm64"?incident.Architecture:RuntimeInformation.ProcessArchitecture switch { Architecture.X86=>"x86",Architecture.X64=>"x64",Architecture.Arm64=>"arm64",_=>"unknown" },
            SafeToken(incident.Evidence,64),SafeToken(incident.Stage,64),incident.ExceptionCode is { } code&&Hex.IsMatch(code)?code:"unknown",
            incident.Module is { } module&&Leaf.IsMatch(module)&&!module.Contains("..",StringComparison.Ordinal)?module:"unknown",
            incident.ModuleHash is { } hash&&Hash.IsMatch(hash)?hash:"unknown",incident.ModuleOffset is { } offset&&Hex.IsMatch(offset)?offset:"unknown",
            (incident.Stack??[]).Where(f=>f is not null&&f.Length<=160&&!f.Contains("..",StringComparison.Ordinal)&&Frame.IsMatch(f)).Take(32).ToArray(),1,hardware,context,incident.ClientPvfSha256 is { } pvf&&Hash.IsMatch(pvf)?pvf:null);
    }

    // Bounded framework/native queries; unavailable hardware values remain unknown.
    public static IReadOnlyDictionary<string,string> CollectSystem()
    {
        var values=new Dictionary<string,string> {
        ["osVersion"]=Environment.OSVersion.VersionString,["osBuild"]=Environment.OSVersion.Version.Build.ToString(),
        ["osArchitecture"]=RuntimeInformation.OSArchitecture.ToString(),["processArchitecture"]=RuntimeInformation.ProcessArchitecture.ToString(),
        ["logicalProcessors"]=Environment.ProcessorCount.ToString(),["language"]=System.Globalization.CultureInfo.CurrentUICulture.Name,
        ["uptimeSeconds"]=(Environment.TickCount64/1000).ToString()
        };
        DiagnosticHardware.ReadInto(values);return values;
    }
}
