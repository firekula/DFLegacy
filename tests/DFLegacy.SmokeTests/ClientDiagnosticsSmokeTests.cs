using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;

internal static class ClientDiagnosticsSmokeTests
{
    internal static void Run(Action<bool, string> check)
    {
        Func<PacketFrame, ILogger, Guid?, string?, bool> handle = ClientDiagnosticHandler.TryHandle;
        var characterId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        // Catches ignoring CMD125, reading sequence bytes as fields, and accidentally logging raw data.
        var bossBody = new byte[] { 0x34, 0x12, 2, 9, 0xDE, 0xAD };
        var bossLogger = new RecordingLogger();
        check(handle(new PacketFrame(1, 125, 0, bossBody), bossLogger, characterId, "诊断角色"),
            "ACT1 CMD125 type 2 is consumed without falling through to a command reply");
        CheckLog(bossLogger, check, 125, "Parsed", 6, characterId, "诊断角色", 2, 9);
        check(bossBody.SequenceEqual(new byte[] { 0x34, 0x12, 2, 9, 0xDE, 0xAD }),
            "handling a diagnostic leaves the supplied packet body untouched");

        // Catches incorrectly requiring an error byte for type3 or requiring a selected character.
        var type3Logger = new RecordingLogger();
        check(handle(new PacketFrame(1, 125, 0, [0x02, 0xEF, 3]), type3Logger, null, null),
            "ACT1 CMD125 type 3 is consumed before character selection");
        CheckLog(type3Logger, check, 125, "Parsed", 3, null, null, 3, null);

        // Catches out-of-bounds reads, accepting a type2 report without its error, and sequence confusion.
        foreach (var body in new byte[][] { [], [2], [2, 9], [0x34, 0x12, 2] })
        {
            var logger = new RecordingLogger();
            check(handle(new PacketFrame(1, 125, 0, body), logger, null, null),
                $"malformed ACT1 CMD125 body of length {body.Length} is consumed silently");
            CheckLog(logger, check, 125, "Malformed", body.Length, null, null,
                body.Length == 3 ? (byte)2 : null, null);
        }

        var unknownLogger = new RecordingLogger();
        check(handle(new PacketFrame(1, 125, 0, [0x02, 0x03, 0x7F, 0xAA]), unknownLogger, null, null),
            "unknown ACT1 CMD125 type is consumed as a diagnostic");
        CheckLog(unknownLogger, check, 125, "UnknownType", 4, null, null, 0x7F, null);

        // Captured historical reports must keep their separate code/flag interpretation.
        foreach (var body in new byte[][]
                 { [0x3B, 0, 8, 1, 0, 0, 0], [0x1F, 0, 8, 1, 0, 0, 0] })
        {
            var logger = new RecordingLogger();
            check(handle(new PacketFrame(1, 118, 0, body), logger, characterId, "历史角色"),
                "captured historical CMD118 report is still consumed");
            check(logger.Entries is [var entry]
                  && entry.Level == LogLevel.Warning
                  && Has(entry, "Command", (byte)118) && Has(entry, "Status", "Historical")
                  && Has(entry, "Code", (byte)8) && Has(entry, "Flag", (byte)1)
                  && Has(entry, "BodyLength", 7) && Has(entry, "CharacterId", characterId)
                  && Has(entry, "Name", "历史角色")
                  && OnlyFields(entry, "Command", "Status", "Code", "Flag", "BodyLength", "CharacterId", "Name"),
                "historical CMD118 logs code 8 / flag 1 without claiming ACT1 trace-error semantics or dumping the body");
        }

        foreach (var body in new byte[][] { [], [0x1F, 0], [0x1F, 0, 8] })
        {
            var logger = new RecordingLogger();
            check(handle(new PacketFrame(1, 118, 0, body), logger, null, null),
                "short historical CMD118 report remains consumed without throwing");
            check(logger.Entries is [var entry] && Has(entry, "Status", "Malformed")
                  && Has(entry, "Command", (byte)118) && Has(entry, "BodyLength", body.Length),
                "short historical report is logged as malformed");
        }

        // Catches overbroad routing that would swallow unrelated gameplay or notification packets.
        foreach (var frame in new[]
                 {
                     new PacketFrame(0, 125, 0, [0, 0, 2, 9]),
                     new PacketFrame(0, 118, 0, [0, 0, 8, 1]),
                     new PacketFrame(2, 125, 0, [0, 0, 2, 9]),
                     new PacketFrame(1, 124, 0, [0, 0, 2, 9]),
                     new PacketFrame(1, 126, 0, [0, 0, 2, 9])
                 })
        {
            var logger = new RecordingLogger();
            check(!handle(frame, logger, characterId, "诊断角色") && logger.Entries.Count == 0,
                $"non-diagnostic packet type {frame.Type} / command {frame.ProtocolId} keeps its original routing");
        }
    }

    private static void CheckLog(RecordingLogger logger, Action<bool, string> check,
        byte command, string status, int length, Guid? characterId, string? name, byte? type, byte? error)
    {
        check(logger.Entries is [var entry]
              && entry.Level == LogLevel.Warning
              && Has(entry, "Command", command) && Has(entry, "Status", status)
              && Has(entry, "Type", type) && Has(entry, "Error", error)
              && Has(entry, "BodyLength", length) && Has(entry, "CharacterId", characterId)
              && Has(entry, "Name", name)
              && OnlyFields(entry, "Command", "Status", "Type", "Error", "BodyLength", "CharacterId", "Name"),
            $"CMD{command} {status} log preserves parsed fields, length and optional identity without a raw body");
    }

    private static bool Has(LogEntry entry, string name, object? value) =>
        entry.Fields.TryGetValue(name, out var actual) && Equals(actual, value);

    private static bool OnlyFields(LogEntry entry, params string[] names) =>
        entry.Fields.Keys.All(key => key == "{OriginalFormat}" || names.Contains(key));

    private sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Fields);

    private sealed class RecordingLogger : ILogger
    {
        internal List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                Entries.Add(new LogEntry(logLevel, fields.ToDictionary(pair => pair.Key, pair => pair.Value)));
        }
    }
}
