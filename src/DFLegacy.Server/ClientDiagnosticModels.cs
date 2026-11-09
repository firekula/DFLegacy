using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DFLegacy.Server;

public sealed class ClientDiagnosticsOptions
{
    public bool Enabled { get; set; } = true;
    public string StoragePath { get; set; } = "";
    public int RetentionDays { get; set; } = 14;
    public long MaxStorageBytes { get; set; } = 256L * 1024 * 1024;
    public int MaxReportCount { get; set; } = 2000;
    public int MaxDeduplicationEntries { get; set; } = 50000;
    public int SourcePerMinute { get; set; } = 5;
    public int SourcePerHour { get; set; } = 30;
    public int GlobalPerMinute { get; set; } = 10;
    public int GlobalPerHour { get; set; } = 120;
}

// This whitelist is the complete wire contract. Unknown members and duplicate JSON keys are rejected.
public sealed record ClientDiagnosticReport(
    string Format, string EventId, string SessionId, string Kind, DateTimeOffset OccurredAt,
    string ClientVersion, string Architecture, string Evidence, string Stage, string ExceptionCode,
    string Module, string ModuleHash, string ModuleOffset, string[] Stack, int Occurrences,
    Dictionary<string, string> System, string Context, string? ClientPvfSha256 = null)
{
    public const int MaxWireBytes = 256 * 1024;
    public const int MaxExpandedBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, MaxDepth = 8 };
    private static readonly Regex Token = new(@"^[A-Za-z0-9_.+\-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex Leaf = new(@"^[A-Za-z0-9_.\-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex = new(@"^0x[0-9a-fA-F]{1,16}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hash = new(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Frame = new(@"^[A-Za-z0-9_.\-]{1,128}\+0x[0-9a-fA-F]{1,16}$", RegexOptions.CultureInvariant);
    private static readonly Regex Unsafe = new(@"(?:[A-Za-z]:[\\/]|\\\\|://|(?:^|[\s""'=:(])/\S|(?:password|passwd|token|authorization|secret|account|username|apikey)[""']?\s*[:=]|\bBearer\s+\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static readonly IReadOnlySet<string> SystemKeys = new HashSet<string>(StringComparer.Ordinal) {
        "osVersion", "osBuild", "osArchitecture", "processArchitecture", "language", "cpuModel", "logicalProcessors",
        "totalMemoryBytes", "availableMemoryBytes", "gpuModel", "gpuDriverVersion", "gpuMemoryBytes", "gameResolution",
        "windowMode", "renderer", "adapter", "dpi", "uptimeSeconds"
    };
    public static bool TryParse(byte[] bytes, out ClientDiagnosticReport? report)
    {
        report = null;
        try
        {
            if (bytes.Length > MaxExpandedBytes) return false;
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
            if (!UniqueKeys(document.RootElement)) return false;
            var value = JsonSerializer.Deserialize<ClientDiagnosticReport>(bytes, Wire);
            if (value is null || value.Format != "DFLegacy-client-diagnostic-1" || !Id(value.EventId) || !Id(value.SessionId)
                || value.Kind is not ("error" or "networkerror" or "crash") || value.OccurredAt == default || value.OccurredAt.Offset != TimeSpan.Zero
                || !SafeToken(value.ClientVersion, 128) || !SafeToken(value.Evidence, 64) || !SafeToken(value.Stage, 64)
                || value.Architecture is not ("x86" or "x64" or "arm64" or "unknown")
                || !HexOrUnknown(value.ExceptionCode) || !HexOrUnknown(value.ModuleOffset)
                || !ModuleLeaf(value.Module) || !HashOrUnknown(value.ModuleHash) || (value.ClientPvfSha256 is not null && !HashOrUnknown(value.ClientPvfSha256))
                || value.Stack is null || value.Stack.Length > 32 || value.Stack.Any(frame => frame is null || frame.Length > 160 || !Frame.IsMatch(frame) || frame.Contains("..", StringComparison.Ordinal))
                || value.Occurrences is < 1 or > 1000000 || value.System is null || value.System.Count > SystemKeys.Count
                || value.System.Any(pair => !SystemKeys.Contains(pair.Key) || !SafeText(pair.Value, 256)) || !SafeText(value.Context, 2048)) return false;
            report = value; return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return false; }
    }
    private static bool UniqueKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) if (!names.Add(property.Name) || !UniqueKeys(property.Value)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) if (!UniqueKeys(item)) return false;
        return true;
    }
    public static bool Id(string? value) => value is not null && value.Length == 32 && Guid.TryParseExact(value, "N", out _) && value == value.ToLowerInvariant();
    private static bool SafeToken(string? value, int maximum) => value is not null && value.Length <= maximum && Token.IsMatch(value);
    private static bool ModuleLeaf(string? value) => value is not null && Leaf.IsMatch(value) && !value.Contains("..", StringComparison.Ordinal);
    private static bool HexOrUnknown(string? value) => value == "unknown" || value is not null && Hex.IsMatch(value);
    private static bool HashOrUnknown(string? value) => value == "unknown" || value is not null && Hash.IsMatch(value);
    private static bool SafeText(string? value, int maximum) => value is not null && value.Length <= maximum && !value.Any(char.IsControl) && !Unsafe.IsMatch(value);
    public string Fingerprint()
    {
        // Unknown faults retain stage/evidence/module/stack distinctions instead of becoming a single known cause.
        var summary = Kind == "networkerror" || Module == "unknown" || ModuleOffset == "unknown" ? Context : "";
        var key = string.Join('|', Kind, ClientVersion, ClientPvfSha256 ?? "unknown", Module, ModuleHash, ExceptionCode, ModuleOffset, Stage, Evidence, string.Join(',', Stack.Take(6)), summary);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}

public sealed record ClientDiagnosticReceipt(string ReceiptId, string EventId, string Status);
public sealed record ClientDiagnosticReceiveResult(int StatusCode, ClientDiagnosticReceipt? Receipt = null);
public sealed record ClientDiagnosticQuery(int Page = 1, int PageSize = 25, string? Kind = null, string? Version = null, string? Status = null, string? System = null, string? Gpu = null, DateTimeOffset? From = null, DateTimeOffset? To = null);
public sealed record ClientDiagnosticGroup(string Id, string Kind, string ClientVersion, string Evidence, string Module, string Status, long Occurrences, int ReportCount, DateTimeOffset LastSeen, string[] ReportIds, string? UpdatedBy, DateTimeOffset? UpdatedAt);
public sealed record ClientDiagnosticPage(int Total, int Page, int PageSize, ClientDiagnosticGroup[] Items);
public sealed record ClientDiagnosticDetail(string ReceiptId, string Fingerprint, string Status, DateTimeOffset ReceivedAt, ClientDiagnosticReport Report);
public sealed record ClientDiagnosticStatusRequest(string Status);
